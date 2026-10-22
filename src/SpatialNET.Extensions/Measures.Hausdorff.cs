using SpatialNET.Internal;

namespace SpatialNET;

public static partial class Measures
{
    /// <summary>The most sample points <c>HausdorffDistance</c> examines before it refuses the parameter.</summary>
    private const long MaxHausdorffSamples = 50_000_000;

    /// <summary>
    /// The discrete Hausdorff distance (ADR 0025): the larger of the two directed distances, where the directed distance from A to
    /// B is the greatest distance from a <b>sample point</b> of A to the line work of B. The sample points are the vertices of A
    /// and, with <paramref name="densifyFraction"/> below 1, extra points that divide every segment into equal pieces. The
    /// value is exact for the samples and a <b>lower bound</b> of the true Hausdorff distance; a smaller fraction approaches it.
    /// A polygon counts by its rings (the line work), not by its area, as in JTS.
    /// </summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="densifyFraction">
    /// In (0, 1]: every segment is divided into <c>round(1 / densifyFraction)</c> pieces; 1 (default) uses the vertices only.
    /// </param>
    /// <returns>The distance, or <see cref="double.NaN"/> if one geometry is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="densifyFraction"/> is not in (0, 1], or so small that the number of samples would be unreasonably large.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static double HausdorffDistance(this IGeometry2D a, IGeometry2D b, double densifyFraction = 1)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        if (!(densifyFraction > 0 && densifyFraction <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(densifyFraction), densifyFraction, "The densify fraction must be in (0, 1].");
        }

        if (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b))
        {
            return double.NaN;
        }

        int pieces = (int)Math.Round(1 / densifyFraction);
        return Math.Max(Directed(a, b, pieces), Directed(b, a, pieces));
    }

    /// <summary>The greatest distance from a sample point of <paramref name="from"/> to the line work of <paramref name="to"/>.</summary>
    private static double Directed(IGeometry2D from, IGeometry2D to, int pieces)
    {
        var segments = GeometryAccess.Segments(to).ToArray();
        var index = new SpatialIndex<int>(segments.Select((s, i) => (
            new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)));
        double Nearest(Coordinate p)
        {
            var (a, b) = segments[index.Nearest(p, 1, (i, q) => Segments.PointDistance(q, segments[i].A, segments[i].B))[0]];
            return Segments.PointDistance(p, a, b);
        }

        double best = 0;
        long budget = MaxHausdorffSamples;
        foreach (var (start, end) in GeometryAccess.Segments(from))
        {
            budget -= pieces;
            if (budget < 0)
            {
                throw new ArgumentOutOfRangeException("densifyFraction", "The densify fraction is so small that the number of samples would be unreasonably large.");
            }

            best = Math.Max(best, Nearest(start));
            if (start.X != end.X || start.Y != end.Y)
            {
                best = Math.Max(best, Nearest(end));
                for (int k = 1; k < pieces; k++)
                {
                    best = Math.Max(best, Nearest(Segments.Lerp(start, end, k / (double)pieces)));
                }
            }
        }

        return best;
    }
}
