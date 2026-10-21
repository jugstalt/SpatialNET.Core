using SpatialNET.Internal;
using SpatialNET.Internal.Buffer;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

/// <summary>Options for <see cref="OffsetCurves.OffsetCurve(IPolyline2D, double, OffsetOptions?, IGeometryFactory?, double?)"/> (ADR 0006).</summary>
public sealed record OffsetOptions
{
    /// <summary>The default options.</summary>
    public static OffsetOptions Default { get; } = new();

    /// <summary>Corners on the outside; default <see cref="JoinStyle.Round"/>.</summary>
    public JoinStyle JoinStyle { get; init; } = JoinStyle.Round;

    /// <summary>Segments per quarter circle; default 8.</summary>
    public int QuadrantSegments { get; init; } = 8;

    /// <summary>Largest chord deviation of the arcs; if set, it takes precedence over <see cref="QuadrantSegments"/>.</summary>
    public double? ArcTolerance { get; init; }

    /// <summary>Mitre limit; default 5.</summary>
    public double MitreLimit { get; init; } = 5;

    /// <summary>Factor of the input simplification; default 0.01.</summary>
    public double SimplifyFactor { get; init; } = 0.01;

    /// <summary>Join the pieces into one path (JTS "joined"); default off.</summary>
    public bool Joined { get; init; }

    internal BufferOptions ToBuffer() => new()
    {
        JoinStyle = JoinStyle,
        CapStyle = CapStyle.Flat,
        QuadrantSegments = QuadrantSegments,
        ArcTolerance = ArcTolerance,
        MitreLimit = MitreLimit,
        SimplifyFactor = SimplifyFactor,
    };
}

/// <summary>
/// Offset curves following JTS OffsetCurve (ADR 0006): buffer with flat ends, raw offset of the side,
/// matched against the buffer boundary. The result is guaranteed to lie on the buffer boundary and, at narrow
/// spots, is a polyline with several paths. Z and M are interpolated on the nearest source segment.
/// </summary>
public static class OffsetCurves
{
    private const double MatchDistanceFactor = 10000;

    /// <summary>The offset curve of a polyline; positive distance to the left, negative to the right of the direction of travel.</summary>
    /// <param name="polyline">The polyline.</param>
    /// <param name="distance">The distance.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="OffsetOptions.Default"/>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distance not finite or invalid options.</exception>
    public static IPolyline2D OffsetCurve(this IPolyline2D polyline, double distance, OffsetOptions? options = null,
        IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        options ??= OffsetOptions.Default;
        var bufferOptions = options.ToBuffer();
        Buffering.Validate(distance, bufferOptions);
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, polyline);

        var result = new List<Coordinate[]>();
        for (int i = 0; i < polyline.PathCount; i++)
        {
            var path = Rings.Coordinates(polyline[i]);
            foreach (var curve in PathCurve(path, distance, options, bufferOptions, grid))
            {
                result.Add(WithZM(curve, [path], f));
            }
        }

        return f.CreatePolyline(result.Select(c => (ReadOnlyMemory<Coordinate>)c).ToArray(), polyline);
    }

    /// <summary>
    /// The offset curve of a polygon: the boundary of <c>Buffer(distance)</c> as closed paths – positive outside,
    /// negative inside, regardless of ring orientation.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="distance">The distance.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="OffsetOptions.Default"/>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distance not finite or invalid options.</exception>
    public static IPolyline2D OffsetCurve(this IPolygon2D polygon, double distance, OffsetOptions? options = null,
        IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        options ??= OffsetOptions.Default;
        var bufferOptions = options.ToBuffer() with { CapStyle = CapStyle.Round };
        Buffering.Validate(distance, bufferOptions);
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, polygon);

        var sources = Enumerable.Range(0, polygon.RingCount).Select(i => Rings.Closed(polygon[i])).ToList();
        var paths = new List<ReadOnlyMemory<Coordinate>>();
        foreach (var part in Buffering.Parts(polygon, distance, bufferOptions, grid))
        {
            foreach (var ring in part.Holes.ToArray().Prepend(part.Shell))
            {
                paths.Add(WithZM(ring.ToArray(), sources, f));
            }
        }

        return f.CreatePolyline(paths.ToArray(), polygon);
    }

    // ---------- Linien: Abgleich Roh-Offset ↔ Buffer-Rand (JTS OffsetCurve) ----------

    private static IEnumerable<Coordinate[]> PathCurve(Coordinate[] path, double distance, OffsetOptions options, BufferOptions bufferOptions, GridModel grid)
    {
        var points = path.Select(c => new Coordinate(c.X, c.Y)).Where((c, i) => i == 0 || !Rings.SameXY(c, path[i - 1])).ToArray();
        if (points.Length < 2) yield break;
        if (distance == 0)
        {
            yield return points;
            yield break;
        }

        if (points.Length == 2)
        {
            yield return OffsetSegment(points[0], points[1], distance);
            yield break;
        }

        var raw = new OffsetBuilder(bufferOptions, distance).RawOffset(points);
        if (raw.Length < 2) yield break;

        var parts = Buffering.Parts(new PathLine(points), Math.Abs(distance), bufferOptions, grid);
        if (parts.Count == 0) yield break;
        var largest = parts.MaxBy(p => Math.Abs(Rings.SignedArea(p.Shell.Span)))!;

        // Richtung wie der Roh-Offset: links (d > 0) im Uhrzeigersinn, rechts gegen den Uhrzeigersinn
        var rings = largest.Holes.ToArray().Prepend(largest.Shell).Select(r => r.ToArray()).ToList();
        if (distance > 0) rings = rings.Select(r => r.Reverse().ToArray()).ToList();

        double matchDistance = Math.Max(Math.Abs(distance) / MatchDistanceFactor, 2 * grid.Size);
        var sections = new List<(double Location, Coordinate[] Points)>();
        foreach (var ring in rings)
        {
            Sections(ring, raw, matchDistance, options.Joined, sections);
        }

        sections.Sort((a, b) => a.Location.CompareTo(b.Location));
        if (options.Joined)
        {
            var joined = new List<Coordinate>();
            foreach (var (_, pts) in sections)
            {
                foreach (var p in pts)
                {
                    if (joined.Count == 0 || !Rings.SameXY(joined[^1], p)) joined.Add(p);
                }
            }

            if (joined.Count >= 2) yield return joined.ToArray();
            yield break;
        }

        foreach (var (_, pts) in sections)
        {
            yield return pts;
        }
    }

    private static void Sections(Coordinate[] ring, Coordinate[] raw, double matchDistance, bool joined, List<(double, Coordinate[])> sections)
    {
        int n = ring.Length - 1;
        var position = new double[n];
        Array.Fill(position, -1);

        int first = -1;
        double minPosition = -1;
        for (int r = 0; r < raw.Length - 1; r++)
        {
            int minIndex = -1;
            double minFrac = -1;
            for (int i = 0; i < n; i++)
            {
                if (Segments.PointDistance(ring[i], raw[r], raw[r + 1]) > matchDistance
                    || Segments.PointDistance(ring[i + 1], raw[r], raw[r + 1]) > matchDistance)
                {
                    continue;
                }

                double frac = r + Fraction(raw[r], raw[r + 1], ring[i]);
                position[i] = frac; // wie JTS: der zuletzt passende Roh-Abschnitt bestimmt die Lage
                if (minIndex < 0 || frac < minFrac)
                {
                    minIndex = i;
                    minFrac = frac;
                }
            }

            if (minIndex >= 0 && (first < 0 || position[minIndex] < minPosition))
            {
                minPosition = position[minIndex];
                first = minIndex;
            }
        }

        if (first < 0) return;

        int start = first, count = 0;
        int end;
        do
        {
            end = SectionEnd(position, start, first, joined);
            double location = position[start];
            var pts = new List<Coordinate>();
            int i = start;
            do
            {
                pts.Add(ring[i]);
                i = (i + 1) % n;
            }
            while (i != end);

            pts.Add(ring[end]);
            sections.Add((location, pts.ToArray()));
            start = SectionStart(position, end, joined);
            if (count++ > n) break;
        }
        while (start != first && end != first);
    }

    private static int SectionStart(double[] position, int end, bool joined)
    {
        int n = position.Length, start = end;
        do
        {
            int next = (start + 1) % n;
            if (position[start] < 0)
            {
                start = next;
                continue;
            }

            int prev = (start - 1 + n) % n;
            if (position[prev] < 0) return start;
            if (joined && Math.Abs(position[start] - position[prev]) > 1) return start;
            start = next;
        }
        while (start != end);

        return start;
    }

    private static int SectionEnd(double[] position, int start, int first, bool joined)
    {
        int n = position.Length, end = start, next;
        do
        {
            next = (end + 1) % n;
            if (position[next] < 0) return next;
            if (joined && Math.Abs(position[next] - position[end]) > 1) return next;
            end = next;
        }
        while (end != start && end != first);

        return end;
    }

    private static double Fraction(Coordinate a, Coordinate b, Coordinate p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 == 0) return 0;
        return Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
    }

    private static Coordinate[] OffsetSegment(Coordinate a, Coordinate b, double distance)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / len * distance, ny = dx / len * distance; // links bei positiver Distanz
        return [new(a.X + nx, a.Y + ny), new(b.X + nx, b.Y + ny)];
    }

    /// <summary>
    /// Z and M per point, each interpolated from the nearest source segment whose ends carry it (only if the
    /// factory supports it, ADR 0015).
    /// </summary>
    private static Coordinate[] WithZM(Coordinate[] curve, IReadOnlyList<Coordinate[]> sources, IGeometryFactory f)
    {
        bool useZ = f.SupportsZ && sources.Any(s => s.Any(c => c.HasZ));
        bool useM = f.SupportsM && sources.Any(s => s.Any(c => c.HasM));
        if (!useZ && !useM)
        {
            return curve.Select(c => new Coordinate(c.X, c.Y)).ToArray();
        }

        return curve.Select(c =>
        {
            double bestZ = double.PositiveInfinity, bestM = double.PositiveInfinity, z = double.NaN, m = double.NaN;
            foreach (var s in sources)
            {
                for (int i = 1; i < s.Length; i++)
                {
                    double d = Segments.PointDistance(c, s[i - 1], s[i]);
                    if (useZ && d < bestZ && s[i - 1].HasZ && s[i].HasZ)
                    {
                        bestZ = d;
                        z = s[i - 1].Z + Fraction(s[i - 1], s[i], c) * (s[i].Z - s[i - 1].Z);
                    }

                    if (useM && d < bestM && s[i - 1].HasM && s[i].HasM)
                    {
                        bestM = d;
                        m = s[i - 1].M + Fraction(s[i - 1], s[i], c) * (s[i].M - s[i - 1].M);
                    }
                }
            }

            return new Coordinate(c.X, c.Y, z, m);
        }).ToArray();
    }

    private sealed class PathLine(Coordinate[] points) : IPolyline2D
    {
        private readonly IPath2D[] _paths = [new PathPoints(points)];
        public int PathCount => 1;
        public IPath2D this[int index] => _paths[index];
    }

    private sealed class PathPoints(Coordinate[] points) : IPath2D
    {
        public int PointCount => points.Length;
        public IPoint2D this[int index] => new P(points[index]);
    }

    private sealed class P(Coordinate c) : IPoint2D
    {
        public double X => c.X;
        public double Y => c.Y;
    }
}
