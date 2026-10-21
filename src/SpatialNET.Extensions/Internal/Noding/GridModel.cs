using System.Numerics;

namespace SpatialNET.Internal.Noding;

/// <summary>A point on the grid in integer grid units; orientation on it is exact (Int128).</summary>
internal readonly record struct GridPoint(long X, long Y) : IComparable<GridPoint>, Topology.IExactPoint<GridPoint>
{
    public static int CompareX(GridPoint a, GridPoint b) => a.X.CompareTo(b.X);

    public static int CompareY(GridPoint a, GridPoint b) => a.Y.CompareTo(b.Y);

    /// <summary>+1 if c lies to the left of a→b, −1 to the right, 0 collinear – exact.</summary>
    public static int Orient(GridPoint a, GridPoint b, GridPoint c)
    {
        Int128 det = (Int128)(b.X - a.X) * (c.Y - a.Y) - (Int128)(b.Y - a.Y) * (c.X - a.X);
        return det.CompareTo(Int128.Zero);
    }

    public int CompareTo(GridPoint other) => X != other.X ? X.CompareTo(other.X) : Y.CompareTo(other.Y);
}

/// <summary>
/// The grid to which overlays round their results (ADR 0005). Grid size from the cascade
/// <c>gridSize</c> → <see cref="SpatialNET.Precision.DefaultGridSize"/> → automatic from the coordinate magnitude.
/// </summary>
internal sealed class GridModel
{
    /// <summary>Largest permitted magnitude in grid units (leaves room for exact Int128 products).</summary>
    private const double MaxUnits = 4e18;

    private readonly double _inverse; // 1 / Size, exakt ganzzahlig für Size < 1 (Zehnerpotenz)

    public GridModel(double size)
    {
        if (!double.IsFinite(size) || size <= 0)
        {
            throw new ArgumentOutOfRangeException("gridSize", size, "The grid size must be finite and greater than 0.");
        }

        Size = size;
        _inverse = size < 1 ? Math.Round(1 / size) : 0;
    }

    public double Size { get; }

    /// <summary>Resolves the grid size cascade; the automatic size uses all inputs (a ∩ b uses the same grid as b ∩ a).</summary>
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

    /// <summary>max|coordinate| · 1e-12, rounded down to a power of ten (about 12 significant digits).</summary>
    public static double AutoSize(double maxAbs)
    {
        if (maxAbs <= 0)
        {
            return 1e-12;
        }

        int exponent = (int)Math.Floor(Math.Log10(maxAbs * 1e-12));
        return exponent < 0 ? 1 / Math.Pow(10, -exponent) : Math.Pow(10, exponent);
    }

    /// <summary>Value in continuous grid units.</summary>
    public double ToUnits(double value)
    {
        double units = _inverse > 0 ? value * _inverse : value / Size;
        if (!(Math.Abs(units) <= MaxUnits))
        {
            throw new ArgumentOutOfRangeException("gridSize", Size,
                $"The grid size {Size} is too small for the coordinate {value}, or the coordinate is not finite.");
        }

        return units;
    }

    /// <summary>Rounds to the nearest grid point; half units upwards (floor(x + ½)), as in JTS/NTS.</summary>
    public static long RoundUnits(double units) => (long)Math.Floor(units + 0.5);

    public GridPoint Round(Coordinate c) => new(RoundUnits(ToUnits(c.X)), RoundUnits(ToUnits(c.Y)));

    /// <summary>
    /// Rounds the exact intersection point of the lines p0p1 and q0q1 (segments cross properly) to the grid.
    /// Computes rationally over the exact values of the doubles, so that the rounding does not depend on the order of computation.
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

    /// <summary>Rounds (num / den) · 2^exponent to grid units: floor(x + ½), exact.</summary>
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
