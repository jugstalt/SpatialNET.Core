using System.Numerics;

namespace SpatialNET.Internal.Noding;

/// <summary>
/// Exaktes Orientierungsprädikat orient2d (ADR 0005): erst ein Gleitkomma-Filter mit Shewchuks
/// Fehlerschranke, nur bei unsicherem Vorzeichen exakte Rechnung. Das Ergebnis ist immer das exakte
/// Vorzeichen der Determinante der Eingabe-Doubles.
/// </summary>
internal static class Orientation
{
    // Shewchuk, "Adaptive Precision Floating-Point Arithmetic …": ccwerrboundA = (3 + 16ε)ε, ε = 2^-53
    private const double Epsilon = 1.1102230246251565e-16;
    private const double ErrorBound = (3.0 + 16.0 * Epsilon) * Epsilon;

    /// <summary>+1, wenn c links von a→b liegt, −1 rechts, 0 kollinear – exakt.</summary>
    public static int Orient2D(double ax, double ay, double bx, double by, double cx, double cy)
    {
        double acx = ax - cx, bcy = by - cy, acy = ay - cy, bcx = bx - cx;

        // Ist ein Faktor exakt 0 (gemeinsamer Vertex, gleiche X bzw. Y), ist ein Produkt exakt 0 und das
        // Vorzeichen des anderen folgt exakt aus den Vorzeichen der Differenzen (Differenzen von Doubles
        // haben immer das exakte Vorzeichen). Häufig beim Noding benachbarter Segmente – ohne exakten Pfad.
        if (acx == 0 || bcy == 0)
        {
            return acy == 0 || bcx == 0 ? 0 : -Math.Sign(acy) * Math.Sign(bcx);
        }

        if (acy == 0 || bcx == 0)
        {
            return Math.Sign(acx) * Math.Sign(bcy);
        }

        double detLeft = acx * bcy;
        double detRight = acy * bcx;
        double det = detLeft - detRight;

        // Ein Produkt, das auf 0 unterläuft, oder Werte im subnormalen Bereich machen Filter und Abkürzungen
        // ungültig (Shewchuk setzt keinen Unterlauf voraus) – dann exakt.
        if (detLeft == 0 || detRight == 0 || Math.Abs(detLeft) + Math.Abs(detRight) < 1e-280)
        {
            return Exact(ax, ay, bx, by, cx, cy);
        }

        double detSum;
        if (detLeft > 0)
        {
            if (detRight <= 0) return Math.Sign(det);
            detSum = detLeft + detRight;
        }
        else if (detLeft < 0)
        {
            if (detRight >= 0) return Math.Sign(det);
            detSum = -detLeft - detRight;
        }
        else
        {
            return Math.Sign(det);
        }

        if (Math.Abs(det) >= ErrorBound * detSum && double.IsFinite(det))
        {
            return Math.Sign(det);
        }

        return Exact(ax, ay, bx, by, cx, cy);
    }

    public static int Orient2D(Coordinate a, Coordinate b, Coordinate c) => Orient2D(a.X, a.Y, b.X, b.Y, c.X, c.Y);

    /// <summary>Exakte Determinante über ganze Zahlen: jedes Double ist m·2^e.</summary>
    internal static int Exact(double ax, double ay, double bx, double by, double cx, double cy)
    {
        Span<double> values = [ax, ay, bx, by, cx, cy];
        int minExponent = int.MaxValue;
        var decomposed = new (BigInteger Mantissa, int Exponent)[6];
        for (int i = 0; i < 6; i++)
        {
            decomposed[i] = Decompose(values[i]);
            if (!decomposed[i].Mantissa.IsZero)
            {
                minExponent = Math.Min(minExponent, decomposed[i].Exponent);
            }
        }

        if (minExponent == int.MaxValue)
        {
            return 0; // alle Werte 0
        }

        var v = new BigInteger[6];
        for (int i = 0; i < 6; i++)
        {
            v[i] = decomposed[i].Mantissa << (decomposed[i].Exponent - minExponent);
        }

        var det = (v[0] - v[4]) * (v[3] - v[5]) - (v[1] - v[5]) * (v[2] - v[4]);
        return det.Sign;
    }

    /// <summary>Zerlegt ein endliches Double exakt in Mantisse und Exponent: value = mantissa · 2^exponent.</summary>
    internal static (BigInteger Mantissa, int Exponent) Decompose(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Koordinaten müssen endlich sein.");
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
        {
            exponent++; // subnormal
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        return (negative ? -new BigInteger(mantissa) : new BigInteger(mantissa), exponent);
    }
}
