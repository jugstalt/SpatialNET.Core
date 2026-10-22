using System.Numerics;
using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// Exact incircle predicate (ADR 0029): first a floating-point filter with Shewchuk's error bound, exact computation
/// over integers only if the sign is uncertain. The result is always the exact sign of the determinant of the input doubles.
/// </summary>
internal static class InCircle
{
    // Shewchuk: iccerrboundA = (10 + 96ε)ε, ε = 2^-53
    private const double Epsilon = 1.1102230246251565e-16;
    private const double ErrorBound = (10.0 + 96.0 * Epsilon) * Epsilon;

    /// <summary>
    /// +1 if d lies inside the circle through a, b, c (counter-clockwise), −1 outside, 0 on the circle. If a, b, c are clockwise
    /// the signs are reversed.
    /// </summary>
    public static int Sign(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        double adx = a.X - d.X, ady = a.Y - d.Y;
        double bdx = b.X - d.X, bdy = b.Y - d.Y;
        double cdx = c.X - d.X, cdy = c.Y - d.Y;

        double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy;
        double alift = adx * adx + ady * ady;
        double cdxady = cdx * ady, adxcdy = adx * cdy;
        double blift = bdx * bdx + bdy * bdy;
        double adxbdy = adx * bdy, bdxady = bdx * ady;
        double clift = cdx * cdx + cdy * cdy;

        double det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady);
        double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift
            + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * blift
            + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * clift;
        double bound = ErrorBound * permanent;

        // an overflow or underflow makes the filter invalid (NaN or infinity compare false): exact then
        if (double.IsFinite(det) && double.IsFinite(permanent) && permanent > 1e-250 && (det > bound || -det > bound))
        {
            return det > 0 ? 1 : -1;
        }

        return Exact(a, b, c, d);
    }

    private static int Exact(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        double[] values = [a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y];
        var decomposed = new (BigInteger Mantissa, int Exponent)[8];
        int minExponent = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            decomposed[i] = Orientation.Decompose(values[i]);
            if (!decomposed[i].Mantissa.IsZero)
            {
                minExponent = Math.Min(minExponent, decomposed[i].Exponent);
            }
        }

        if (minExponent == int.MaxValue)
        {
            return 0;
        }

        var v = new BigInteger[8];
        for (int i = 0; i < 8; i++)
        {
            v[i] = decomposed[i].Mantissa << (decomposed[i].Exponent - minExponent);
        }

        BigInteger adx = v[0] - v[6], ady = v[1] - v[7];
        BigInteger bdx = v[2] - v[6], bdy = v[3] - v[7];
        BigInteger cdx = v[4] - v[6], cdy = v[5] - v[7];
        BigInteger alift = adx * adx + ady * ady, blift = bdx * bdx + bdy * bdy, clift = cdx * cdx + cdy * cdy;

        var det = alift * (bdx * cdy - cdx * bdy) + blift * (cdx * ady - adx * cdy) + clift * (adx * bdy - bdx * ady);
        return det.Sign;
    }
}
