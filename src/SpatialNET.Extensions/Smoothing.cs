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

    /// <summary>
    /// Smooths a polyline until it is smooth to a distance (ADR 0043) instead of by a number of iterations: the segments are halved as often as it takes
    /// for no vertex to lie further than <paramref name="tolerance"/> from the straight line between its two neighbours, so that the result looks like a
    /// curve at the scale of the tolerance, and no finer. A line that is that smooth already is returned as it is. Everything else is as for
    /// <see cref="Smooth(IPolyline2D, int, SmoothingMethod, IGeometryFactory?)"/>.
    /// </summary>
    /// <remarks>
    /// At most 10 iterations are made for <see cref="SmoothingMethod.Chaikin"/>, and for <see cref="SmoothingMethod.CatmullRom"/> every segment of the
    /// line is cut into at most 1024 pieces, and the work stops where the result would exceed the vertex budget of <c>Densify</c>; a very small
    /// tolerance gives the finest that is allowed. The tolerance is in the units of the coordinates (give it in metres for a map in metres).
    /// </remarks>
    /// <param name="line">The polyline.</param>
    /// <param name="tolerance">The largest distance of a vertex from the line between its neighbours; finite and greater than 0.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D SmoothToTolerance(this IPolyline2D line, double tolerance, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        Check(tolerance, method);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(SmoothPathTo(p, tolerance, method, ref budget), f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Smooths the rings of a polygon until they are smooth to a distance, see
    /// <see cref="SmoothToTolerance(IPolyline2D, double, SmoothingMethod, IGeometryFactory?)"/>. The rings are normalized first (shells
    /// counter-clockwise, holes clockwise); a polygon whose parts are closer together than the curve moves can become invalid.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="tolerance">The largest distance of a vertex from the line between its neighbours; finite and greater than 0.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D SmoothToTolerance(this IPolygon2D polygon, double tolerance, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        Check(tolerance, method);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var parts = PolygonStructure.Build(polygon).Select(part => new PolygonPart(
            GeometryAccess.Restrict(SmoothPathTo(part.Shell, tolerance, method, ref budget), f),
            part.Holes.Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(SmoothPathTo(h, tolerance, method, ref budget), f)).ToArray())).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    /// <summary>Smooths a polyline or polygon to a distance; a point or multipoint is copied unchanged.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="tolerance">The largest distance of a vertex from the line between its neighbours; finite and greater than 0.</param>
    /// <param name="method">The kind of curve.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="method"/> is not a known method.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D SmoothToTolerance(this IGeometry2D geometry, double tolerance, SmoothingMethod method = SmoothingMethod.Chaikin, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        Check(tolerance, method);
        var f = GeometryFactory.Resolve(factory);
        return geometry switch
        {
            IPolygon2D polygon => polygon.SmoothToTolerance(tolerance, method, f),
            IPolyline2D line => line.SmoothToTolerance(tolerance, method, f),
            IMultiPoint2D multiPoint => f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint), f), geometry),
            _ => f.CreatePoint(GeometryAccess.Restrict(((IPoint2D)geometry).ToCoordinate(), f), geometry),
        };
    }

    private static void Check(double tolerance, SmoothingMethod method)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and greater than 0.");
        }

        Check(0, method);
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

    /// <summary>The points of a path without consecutive repeats, and whether it is closed (first and last point the same, with three different points); the closing point is dropped.</summary>
    private static (List<Coordinate> Points, bool Closed) Prepare(Coordinate[] path)
    {
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

        return (points, closed);
    }

    private static Coordinate[] Finish(List<Coordinate> result, bool closed)
    {
        if (closed)
        {
            result.Add(result[0]);
        }

        return result.ToArray();
    }

    private static Coordinate[] SmoothPath(Coordinate[] path, int iterations, SmoothingMethod method, ref long budget)
    {
        var (points, closed) = Prepare(path);
        if (iterations == 0 || points.Count < 3)
        {
            return path;   // nothing to round: a copy (two points, or no iterations)
        }

        budget -= (long)points.Count << iterations;
        if (budget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "The number of iterations is so large that the result would be unreasonably large.");
        }

        var result = method == SmoothingMethod.Chaikin ? Chaikin(points, closed, iterations, double.NaN, ref budget) : CatmullRom(points, closed, _ => 1 << iterations);
        return Finish(result, closed);
    }

    private static Coordinate[] SmoothPathTo(Coordinate[] path, double tolerance, SmoothingMethod method, ref long budget)
    {
        var (points, closed) = Prepare(path);
        if (points.Count < 3)
        {
            return path;
        }

        List<Coordinate> result;
        if (method == SmoothingMethod.Chaikin)
        {
            result = Chaikin(points, closed, MaxIterations, tolerance, ref budget);
        }
        else
        {
            // the same number of pieces for every span, doubled until the whole line is flat, so that the corner at a vertex counts as well
            result = points;
            int pieces = 1;
            while (Flatness(result, closed) > tolerance && pieces < 1024)
            {
                pieces *= 2;
                if (budget < (long)points.Count * pieces)
                {
                    break;
                }

                result = CatmullRom(points, closed, _ => pieces);
            }

            budget -= result.Count;
        }

        return Finish(result, closed);
    }

    /// <summary>
    /// Corner cutting, <paramref name="iterations"/> times, or in the tolerance mode (<paramref name="tolerance"/> is a number) as many times as it
    /// takes for the line to be flat to that distance, as long as the budget lasts.
    /// </summary>
    private static List<Coordinate> Chaikin(List<Coordinate> points, bool closed, int iterations, double tolerance, ref long budget)
    {
        var current = points;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (!double.IsNaN(tolerance))
            {
                if (Flatness(current, closed) <= tolerance || budget < 4L * current.Count)
                {
                    break;
                }

                budget -= 2L * current.Count;
            }

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

    /// <summary>The largest distance of a vertex from the segment between its two neighbours (for an open path without its ends).</summary>
    private static double Flatness(IReadOnlyList<Coordinate> points, bool closed)
    {
        int n = points.Count;
        double worst = 0;
        for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
        {
            var a = points[(i + n - 1) % n];
            var b = points[(i + 1) % n];
            worst = Math.Max(worst, DistanceToSegment(points[i], a, b));
        }

        return worst;
    }

    private static double DistanceToSegment(Coordinate p, Coordinate a, Coordinate b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, length2 = ex * ex + ey * ey;
        double t = length2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * ex + (p.Y - a.Y) * ey) / length2, 0, 1);
        double dx = a.X + t * ex - p.X, dy = a.Y + t * ey - p.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The four points around a span of a Catmull–Rom spline: the neighbour before, the two ends, the neighbour after.</summary>
    private readonly record struct Span(Coordinate P0, Coordinate P1, Coordinate P2, Coordinate P3);

    private static List<Coordinate> CatmullRom(List<Coordinate> points, bool closed, Func<Span, int> piecesOf)
    {
        int n = points.Count;
        var result = new List<Coordinate>(n * 4 + 1);
        int segments = closed ? n : n - 1;
        for (int i = 0; i < segments; i++)
        {
            var p1 = points[i];
            var p2 = points[(i + 1) % n];
            var p0 = closed ? points[(i + n - 1) % n] : i > 0 ? points[i - 1] : Reflect(p2, p1);
            var p3 = closed ? points[(i + 2) % n] : i + 2 < n ? points[i + 2] : Reflect(p1, p2);
            var span = new Span(p0, p1, p2, p3);
            AddSpan(result, span, piecesOf(span));
        }

        if (!closed)
        {
            result.Add(points[n - 1]);
        }

        return result;
    }

    /// <summary>The start of a span and the points between it and its end, which the next span adds.</summary>
    private static void AddSpan(List<Coordinate> result, Span span, int pieces)
    {
        var (p0, p1, p2, p3) = (span.P0, span.P1, span.P2, span.P3);
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

    /// <summary>The point mirrored at <paramref name="centre"/>: the missing neighbour at an end of an open path.</summary>
    private static Coordinate Reflect(Coordinate other, Coordinate centre) => new(2 * centre.X - other.X, 2 * centre.Y - other.Y);

    /// <summary>The centripetal knot spacing: the square root of the distance.</summary>
    private static double Knot(Coordinate a, Coordinate b) => Math.Sqrt(Rings.Distance(a, b));

    /// <summary><paramref name="weightA"/> · a + (1 − weightA) · b, in X and Y.</summary>
    private static Coordinate Mix(Coordinate a, Coordinate b, double weightA) => new(weightA * a.X + (1 - weightA) * b.X, weightA * a.Y + (1 - weightA) * b.Y);
}
