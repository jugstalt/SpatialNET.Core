using System.Numerics;

namespace SpatialNET.Internal.Noding;

/// <summary>Ein Punkt auf dem Gitter in ganzzahligen Gittereinheiten; Orientierung darauf ist exakt (Int128).</summary>
internal readonly record struct GridPoint(long X, long Y) : IComparable<GridPoint>
{
    /// <summary>+1, wenn c links von a→b liegt, −1 rechts, 0 kollinear – exakt.</summary>
    public static int Orient(GridPoint a, GridPoint b, GridPoint c)
    {
        Int128 det = (Int128)(b.X - a.X) * (c.Y - a.Y) - (Int128)(b.Y - a.Y) * (c.X - a.X);
        return det.CompareTo(Int128.Zero);
    }

    public int CompareTo(GridPoint other) => X != other.X ? X.CompareTo(other.X) : Y.CompareTo(other.Y);
}

/// <summary>
/// Das Gitter, auf das Overlays ihre Ergebnisse runden (ADR 0005). Gitterweite aus der Kaskade
/// <c>gridSize</c> → <see cref="SpatialNET.Precision.DefaultGridSize"/> → automatisch aus der Koordinatengröße.
/// </summary>
internal sealed class GridModel
{
    /// <summary>Größter erlaubter Betrag in Gittereinheiten (lässt Platz für exakte Int128-Produkte).</summary>
    private const double MaxUnits = 4e18;

    private readonly double _inverse; // 1 / Size, exakt ganzzahlig für Size < 1 (Zehnerpotenz)

    public GridModel(double size)
    {
        if (!double.IsFinite(size) || size <= 0)
        {
            throw new ArgumentOutOfRangeException("gridSize", size, "Die Gitterweite muss endlich und größer als 0 sein.");
        }

        Size = size;
        _inverse = size < 1 ? Math.Round(1 / size) : 0;
    }

    public double Size { get; }

    /// <summary>Löst die Gitterweite-Kaskade auf; die automatische Weite nutzt alle Eingaben (a ∩ b nutzt dasselbe Gitter wie b ∩ a).</summary>
    public static GridModel Resolve(double? gridSize, params IGeometry2D?[] inputs)
    {
        if (gridSize is { } explicitSize)
        {
            return new GridModel(explicitSize);
        }

        if (SpatialNET.Precision.DefaultGridSize is { } defaultSize)
        {
            return new GridModel(defaultSize);
        }

        double max = 0;
        foreach (var input in inputs)
        {
            if (input is null) continue;
            foreach (var p in GeometryAccess.AllPoints(input))
            {
                if (double.IsFinite(p.X)) max = Math.Max(max, Math.Abs(p.X));
                if (double.IsFinite(p.Y)) max = Math.Max(max, Math.Abs(p.Y));
            }
        }

        return new GridModel(AutoSize(max));
    }

    /// <summary>max|Koordinate| · 1e-12, abgerundet auf eine Zehnerpotenz (etwa 12 signifikante Stellen).</summary>
    public static double AutoSize(double maxAbs)
    {
        if (maxAbs <= 0)
        {
            return 1e-12;
        }

        int exponent = (int)Math.Floor(Math.Log10(maxAbs * 1e-12));
        return exponent < 0 ? 1 / Math.Pow(10, -exponent) : Math.Pow(10, exponent);
    }

    /// <summary>Wert in kontinuierlichen Gittereinheiten.</summary>
    public double ToUnits(double value)
    {
        double units = _inverse > 0 ? value * _inverse : value / Size;
        if (!(Math.Abs(units) <= MaxUnits))
        {
            throw new ArgumentOutOfRangeException("gridSize", Size,
                $"Die Gitterweite {Size} ist für die Koordinate {value} zu klein bzw. die Koordinate ist nicht endlich.");
        }

        return units;
    }

    /// <summary>Rundet auf den nächsten Gitterpunkt; halbe Einheiten nach oben (floor(x + ½)), wie JTS/NTS.</summary>
    public static long RoundUnits(double units) => (long)Math.Floor(units + 0.5);

    public GridPoint Round(Coordinate c) => new(RoundUnits(ToUnits(c.X)), RoundUnits(ToUnits(c.Y)));

    /// <summary>
    /// Rundet den exakten Schnittpunkt der Geraden p0p1 und q0q1 (Segmente kreuzen sich echt) auf das Gitter.
    /// Rechnet rational über die exakten Werte der Doubles, damit die Rundung nicht vom Rechenweg abhängt.
    /// </summary>
    public GridPoint RoundExactIntersection(Coordinate p0, Coordinate p1, Coordinate q0, Coordinate q1)
    {
        Span<double> values = [p0.X, p0.Y, p1.X, p1.Y, q0.X, q0.Y, q1.X, q1.Y];
        var parts = new (BigInteger M, int E)[8];
        int minE = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            parts[i] = Orientation.Decompose(values[i]);
            if (!parts[i].M.IsZero) minE = Math.Min(minE, parts[i].E);
        }

        if (minE == int.MaxValue) minE = 0;
        var v = new BigInteger[8];
        for (int i = 0; i < 8; i++) v[i] = parts[i].M << (parts[i].E - minE);

        BigInteger rx = v[2] - v[0], ry = v[3] - v[1], sx = v[6] - v[4], sy = v[7] - v[5];
        BigInteger den = rx * sy - ry * sx;
        BigInteger num = (v[4] - v[0]) * sy - (v[5] - v[1]) * sx;
        // Punkt = p0 + num/den · r, in Einheiten 2^minE
        BigInteger xNum = v[0] * den + num * rx, yNum = v[1] * den + num * ry;
        return new GridPoint(RoundRational(xNum, den, minE), RoundRational(yNum, den, minE));
    }

    /// <summary>Rundet (num / den) · 2^exponent in Gittereinheiten: floor(x + ½), exakt.</summary>
    private long RoundRational(BigInteger num, BigInteger den, int exponent)
    {
        if (exponent >= 0) num <<= exponent;
        else den <<= -exponent;

        // in Gittereinheiten: · inverse (Size < 1) bzw. / Size (Size ≥ 1, ganzzahlige Zehnerpotenz)
        if (_inverse > 0) num *= new BigInteger(_inverse);
        else den *= new BigInteger(Size);

        if (den.Sign < 0)
        {
            num = -num;
            den = -den;
        }

        // floor((2·num + den) / (2·den)) mit den > 0; DivRem schneidet zur 0 hin ab, daher Korrektur für negative Werte
        var n = num * 2 + den;
        var d = den * 2;
        var quotient = BigInteger.DivRem(n, d, out var remainder);
        if (remainder.Sign < 0) quotient -= 1;
        return (long)quotient;
    }

    public double FromUnits(long units) => _inverse > 0 ? units / _inverse : units * Size;

    public Coordinate ToCoordinate(GridPoint p, double z = double.NaN) => new(FromUnits(p.X), FromUnits(p.Y), z);
}
