using System.Numerics;
using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Topology;

/// <summary>Ein Punkttyp mit exakten Prädikaten – Grundlage des generischen planaren Graphen.</summary>
internal interface IExactPoint<T> : IEquatable<T> where T : struct, IExactPoint<T>
{
    /// <summary>+1, wenn c links von a→b liegt, −1 rechts, 0 kollinear – exakt.</summary>
    static abstract int Orient(T a, T b, T c);

    static abstract int CompareX(T a, T b);

    static abstract int CompareY(T a, T b);
}

/// <summary>
/// Ein exakter Punkt in homogenen Koordinaten (X/W, Y/W) mit ganzzahligen Werten, W &gt; 0, gekürzt.
/// Eingabe-Doubles werden über einen gemeinsamen Exponenten exakt zu ganzen Zahlen (W = 1);
/// Schnittpunkte sind exakte Brüche. Grundlage der Prädikate (exakt ohne Gitter, ADR 0005/0013).
/// </summary>
internal readonly struct HPoint : IExactPoint<HPoint>
{
    public readonly BigInteger X, Y, W;

    public HPoint(BigInteger x, BigInteger y, BigInteger w)
    {
        if (w.Sign < 0)
        {
            (x, y, w) = (-x, -y, -w);
        }

        if (!w.IsOne)
        {
            var g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(x, y), w);
            if (!g.IsOne && !g.IsZero)
            {
                (x, y, w) = (x / g, y / g, w / g);
            }
        }

        (X, Y, W) = (x, y, w);
    }

    public static int Orient(HPoint a, HPoint b, HPoint c)
    {
        var det = a.X * (b.Y * c.W - c.Y * b.W) - a.Y * (b.X * c.W - c.X * b.W) + a.W * (b.X * c.Y - c.X * b.Y);
        return det.Sign;
    }

    public static int CompareX(HPoint a, HPoint b) => (a.X * b.W).CompareTo(b.X * a.W);

    public static int CompareY(HPoint a, HPoint b) => (a.Y * b.W).CompareTo(b.Y * a.W);

    public bool Equals(HPoint other) => X == other.X && Y == other.Y && W == other.W;

    public override bool Equals(object? obj) => obj is HPoint p && Equals(p);

    public override int GetHashCode() => HashCode.Combine(X, Y, W);

    public override string ToString() => W.IsOne ? $"({X} {Y})" : $"({X}/{W} {Y}/{W})";

    /// <summary>Exakter Schnittpunkt der Geraden durch p0p1 und q0q1 (ganzzahlige Endpunkte, W = 1).</summary>
    public static HPoint Intersection(HPoint p0, HPoint p1, HPoint q0, HPoint q1)
    {
        BigInteger rx = p1.X - p0.X, ry = p1.Y - p0.Y, sx = q1.X - q0.X, sy = q1.Y - q0.Y;
        BigInteger den = rx * sy - ry * sx;
        BigInteger num = (q0.X - p0.X) * sy - (q0.Y - p0.Y) * sx;
        return new HPoint(p0.X * den + num * rx, p0.Y * den + num * ry, den);
    }
}

/// <summary>Skaliert Doubles exakt auf ganze Zahlen über den kleinsten gemeinsamen Exponenten.</summary>
internal sealed class ExactScale
{
    private readonly int _minExponent;

    public ExactScale(IEnumerable<double> values)
    {
        int min = int.MaxValue;
        foreach (var v in values)
        {
            if (v != 0 && double.IsFinite(v))
            {
                min = Math.Min(min, Orientation.Decompose(v).Exponent);
            }
        }

        _minExponent = min == int.MaxValue ? 0 : min;
    }

    public BigInteger ToInteger(double value)
    {
        var (m, e) = Orientation.Decompose(value);
        return m << (e - _minExponent);
    }

    public HPoint ToPoint(Coordinate c) => new(ToInteger(c.X), ToInteger(c.Y), BigInteger.One);
}

/// <summary>Grundoperationen über <see cref="IExactPoint{T}"/>.</summary>
internal static class ExactGeometry
{
    public static int Compare<T>(T a, T b) where T : struct, IExactPoint<T>
    {
        int c = T.CompareX(a, b);
        return c != 0 ? c : T.CompareY(a, b);
    }

    /// <summary>Exakter Winkelvergleich zweier Richtungen um <paramref name="o"/> (Halbebene, dann Orientierung).</summary>
    public static int CompareAngle<T>(T o, T a, T b) where T : struct, IExactPoint<T>
    {
        int ha = Half(o, a), hb = Half(o, b);
        if (ha != hb) return ha.CompareTo(hb);
        return -T.Orient(o, a, b);
    }

    /// <summary>0 für Winkel in [0°, 180°), 1 für [180°, 360°).</summary>
    public static int Half<T>(T o, T a) where T : struct, IExactPoint<T>
    {
        int cy = T.CompareY(a, o);
        return cy > 0 || (cy == 0 && T.CompareX(a, o) > 0) ? 0 : 1;
    }
}
