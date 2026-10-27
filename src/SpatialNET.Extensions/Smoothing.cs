using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>How <see cref="Smoothing.Smooth(IPolyline2D, int, SmoothingMethod, IGeometryFactory?)"/> rounds a line (ADR 0043).</summary>
public enum SmoothingMethod
{
    /// <summary>
    /// Corner cutting after Chaikin: every corner is replaced by two points at a quarter of the adjacent segments. The curve is a quadratic
    /// B-spline, it stays inside the convex hull of the vertices, never overshoots, and moves away from the vertices (it does not pass through them).
    /// </summary>
    Chaikin = 0,

    /// <summary>
    /// A centripetal Catmull–Rom spline (alpha 0.5): the curve passes through every vertex, without the loops and the cusps that the uniform
    /// variant makes at uneven spacing. It can overshoot a little between two vertices.
    /// </summary>
    CatmullRom = 1,
}

/// <summary>
/// Rounding of lines (ADR 0043): <c>Smooth</c> replaces the corners of a polyline, or the rings of a polygon, by curves. It is meant for contour lines
/// and everything else that was digitised or computed as a chain of straight pieces.
/// </summary>
public static class Smoothing
{
    private const int MaxIterations = 10;
    private const long MaxVertices = 50_000_000;

    /// <summary>
    /// Smooths a polyline (ADR 0043): every path is replaced by a curve with <c>2^iterations</c> times as many segments. The ends of an open path stay
    /// where they are, a closed path (the first point equals the last) is smoothed all the way round, and Z and M are interpolated linearly along the
    /// segments (so a contour line keeps its height, and M stays monotonic). A path of two points has nothing to smooth.
    /// </summary>
    /// <remarks>
    /// The result follows the input within a part of the longest segment (for <see cref="SmoothingMethod.Chaikin"/> within half of it), but two lines
    /// that run closer to each other than that can cross after smoothing. Contour lines of a surface can be kept apart by smoothing the surface instead,
    /// <see cref="Triangulation.Resample"/>.
    /// </remarks>
    /// <param name="line">The polyline.</param>
    /// <param name="iterations">How often the segments are halved, from 0 (a copy) to 10; 2 to 3 look smooth.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is not from 0 to 10, or the result would be unreasonably large; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Smooth(this IPolyline2D line, int iterations = 2, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        Check(iterations, method);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(SmoothPath(p, iterations, method, ref budget), f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Smooths the rings of a polygon after their normalization (shells counter-clockwise, holes clockwise), every ring all the way round. See
    /// <see cref="Smooth(IPolyline2D, int, SmoothingMethod, IGeometryFactory?)"/>. A ring is cut inwards at its convex corners and outwards at the
    /// reflex ones, so a polygon whose parts lie closer to each other than the curve moves can become invalid; <c>MakeValid</c> repairs it.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="iterations">How often the segments are halved, from 0 (a copy) to 10.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is not from 0 to 10, or the result would be unreasonably large; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D Smooth(this IPolygon2D polygon, int iterations = 2, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        Check(iterations, method);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var parts = PolygonStructure.Build(polygon).Select(part => new PolygonPart(
            GeometryAccess.Restrict(SmoothPath(part.Shell, iterations, method, ref budget), f),
            part.Holes.Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(SmoothPath(h, iterations, method, ref budget), f)).ToArray())).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    /// <summary>Smooths a polyline or polygon; a point or multipoint is copied unchanged.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="iterations">How often the segments are halved, from 0 (a copy) to 10.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is not from 0 to 10, or the result would be unreasonably large; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D Smooth(this IGeometry2D geometry, int iterations = 2, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        Check(iterations, method);
        var f = GeometryFactory.Resolve(factory);
        return geometry switch
        {
            IPolygon2D polygon => polygon.Smooth(iterations, method, f),
            IPolyline2D line => line.Smooth(iterations, method, f),
            IMultiPoint2D multiPoint => f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint), f), geometry),
            _ => f.CreatePoint(GeometryAccess.Restrict(((IPoint2D)geometry).ToCoordinate(), f), geometry),
        };
    }

    private static void Check(int iterations, SmoothingMethod method)
    {
        if (iterations < 0 || iterations > MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, $"The number of iterations must be from 0 to {MaxIterations}.");
        }

        if (method is not (SmoothingMethod.Chaikin or SmoothingMethod.CatmullRom))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "The method is not known.");
        }
    }

    private static Coordinate[] SmoothPath(Coordinate[] path, int iterations, SmoothingMethod method, ref long budget)
    {
        // consecutive points with the same X and Y are one point; a path whose first and last point are the same is closed
        var points = new List<Coordinate>(path.Length);
        foreach (var c in path)
        {
            if (points.Count == 0 || !Rings.SameXY(points[^1], c))
            {
                points.Add(c);
            }
        }

        bool closed = points.Count >= 4 && Rings.SameXY(points[0], points[^1]);
        if (closed)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (iterations == 0 || points.Count < 3)
        {
            return path;   // nothing to round: a copy (two points, or no iterations)
        }

        budget -= (long)points.Count << iterations;
        if (budget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "The number of iterations is so large that the result would be unreasonably large.");
        }

        var result = method == SmoothingMethod.Chaikin ? Chaikin(points, closed, iterations) : CatmullRom(points, closed, 1 << iterations);
        if (closed)
        {
            result.Add(result[0]);
        }

        return result.ToArray();
    }

    private static List<Coordinate> Chaikin(List<Coordinate> points, bool closed, int iterations)
    {
        var current = points;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int n = current.Count;
            var next = new List<Coordinate>(2 * n);
            if (closed)
            {
                // every vertex becomes the point at three quarters of the segment before it and the point at a quarter of the one after it
                for (int i = 0; i < n; i++)
                {
                    var previous = current[(i + n - 1) % n];
                    var here = current[i];
                    var after = current[(i + 1) % n];
                    next.Add(Segments.Lerp(previous, here, 0.75));
                    next.Add(Segments.Lerp(here, after, 0.25));
                }
            }
            else
            {
                // the ends stay; every other vertex is cut
                next.Add(current[0]);
                for (int i = 1; i < n - 1; i++)
                {
                    next.Add(Segments.Lerp(current[i - 1], current[i], 0.75));
                    next.Add(Segments.Lerp(current[i], current[i + 1], 0.25));
                }

                next.Add(current[n - 1]);
            }

            current = next;
        }

        return current;
    }

    private static List<Coordinate> CatmullRom(List<Coordinate> points, bool closed, int pieces)
    {
        int n = points.Count;
        var result = new List<Coordinate>(n * pieces + 1);
        int segments = closed ? n : n - 1;
        for (int i = 0; i < segments; i++)
        {
            var p1 = points[i];
            var p2 = points[(i + 1) % n];
            var p0 = closed ? points[(i + n - 1) % n] : i > 0 ? points[i - 1] : Reflect(p2, p1);
            var p3 = closed ? points[(i + 2) % n] : i + 2 < n ? points[i + 2] : Reflect(p1, p2);
            result.Add(p1);
            double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            // points so close together that the sum of the knots cannot tell them apart (a spacing of 1e-69 next to one of 1) have no curve between them
            bool degenerate = !(t1 > t0 && t2 > t1 && t3 > t2 && double.IsFinite(t3));
            for (int k = 1; k < pieces; k++)
            {
                double u = k / (double)pieces, t = t1 + (t2 - t1) * u;
                if (degenerate)
                {
                    result.Add(Segments.Lerp(p1, p2, u));
                    continue;
                }

                var a1 = Mix(p0, p1, (t1 - t) / (t1 - t0));
                var a2 = Mix(p1, p2, (t2 - t) / (t2 - t1));
                var a3 = Mix(p2, p3, (t3 - t) / (t3 - t2));
                var b1 = Mix(a1, a2, (t2 - t) / (t2 - t0));
                var b2 = Mix(a2, a3, (t3 - t) / (t3 - t1));
                var c = Mix(b1, b2, (t2 - t) / (t2 - t1));
                var along = Segments.Lerp(p1, p2, u);   // Z and M are linear along the span, only X and Y follow the curve
                result.Add(double.IsFinite(c.X) && double.IsFinite(c.Y) ? new Coordinate(c.X, c.Y, along.Z, along.M) : along);
            }
        }

        if (!closed)
        {
            result.Add(points[n - 1]);
        }

        return result;
    }

    /// <summary>The point mirrored at <paramref name="centre"/>: the missing neighbour at an end of an open path.</summary>
    private static Coordinate Reflect(Coordinate other, Coordinate centre) => new(2 * centre.X - other.X, 2 * centre.Y - other.Y);

    /// <summary>The centripetal knot spacing: the square root of the distance.</summary>
    private static double Knot(Coordinate a, Coordinate b) => Math.Sqrt(Rings.Distance(a, b));

    /// <summary><paramref name="weightA"/> · a + (1 − weightA) · b, in X and Y.</summary>
    private static Coordinate Mix(Coordinate a, Coordinate b, double weightA) => new(weightA * a.X + (1 - weightA) * b.X, weightA * a.Y + (1 - weightA) * b.Y);
}
