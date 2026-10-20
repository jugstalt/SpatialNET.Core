using SpatialNET.Internal;
using SpatialNET.Internal.Buffer;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

/// <summary>Optionen für <see cref="OffsetCurves.OffsetCurve(IPolyline2D, double, OffsetOptions?, IGeometryFactory?, double?)"/> (ADR 0006).</summary>
public sealed record OffsetOptions
{
    /// <summary>Die Standard-Optionen.</summary>
    public static OffsetOptions Default { get; } = new();

    /// <summary>Ecken an der Außenseite; Standard <see cref="JoinStyle.Round"/>.</summary>
    public JoinStyle JoinStyle { get; init; } = JoinStyle.Round;

    /// <summary>Segmente je Viertelkreis; Standard 8.</summary>
    public int QuadrantSegments { get; init; } = 8;

    /// <summary>Größte Sehnenabweichung der Bögen; gesetzt hat sie Vorrang vor <see cref="QuadrantSegments"/>.</summary>
    public double? ArcTolerance { get; init; }

    /// <summary>Mitre-Limit; Standard 5.</summary>
    public double MitreLimit { get; init; } = 5;

    /// <summary>Faktor der Eingabe-Vereinfachung; Standard 0,01.</summary>
    public double SimplifyFactor { get; init; } = 0.01;

    /// <summary>Teilstücke zu einem Pfad verbinden (JTS „joined“); Standard aus.</summary>
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
/// Offset-Kurven nach dem Muster von JTS OffsetCurve (ADR 0006): Buffer mit flachen Enden, Roh-Offset der Seite,
/// Abgleich mit dem Buffer-Rand. Das Ergebnis liegt garantiert auf dem Buffer-Rand und ist bei Engstellen eine
/// Polylinie mit mehreren Pfaden. Z wird auf dem nächsten Quellsegment interpoliert.
/// </summary>
public static class OffsetCurves
{
    private const double MatchDistanceFactor = 10000;

    /// <summary>Die Offset-Kurve einer Polylinie; positive Distanz links, negative rechts der Laufrichtung.</summary>
    /// <param name="polyline">Die Polylinie.</param>
    /// <param name="distance">Die Distanz.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="OffsetOptions.Default"/>.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distanz nicht endlich oder ungültige Optionen.</exception>
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
                result.Add(WithZ(curve, [path], f.SupportsZ));
            }
        }

        return f.CreatePolyline(result.Select(c => (ReadOnlyMemory<Coordinate>)c).ToArray(), polyline);
    }

    /// <summary>
    /// Die Offset-Kurve eines Polygons: der Rand von <c>Buffer(distance)</c> als geschlossene Pfade – positiv außen,
    /// negativ innen, unabhängig von der Ring-Orientierung.
    /// </summary>
    /// <param name="polygon">Das Polygon.</param>
    /// <param name="distance">Die Distanz.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="OffsetOptions.Default"/>.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distanz nicht endlich oder ungültige Optionen.</exception>
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
                paths.Add(WithZ(ring.ToArray(), sources, f.SupportsZ));
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

    /// <summary>Z je Punkt vom nächsten Quellsegment interpoliert (nur, wenn die Quelle Z trägt).</summary>
    private static Coordinate[] WithZ(Coordinate[] curve, IReadOnlyList<Coordinate[]> sources, bool supportsZ)
    {
        if (!supportsZ || !sources.Any(s => s.Any(c => c.HasZ)))
        {
            return curve.Select(c => new Coordinate(c.X, c.Y)).ToArray();
        }

        return curve.Select(c =>
        {
            double best = double.PositiveInfinity, z = double.NaN;
            foreach (var s in sources)
            {
                for (int i = 1; i < s.Length; i++)
                {
                    double d = Segments.PointDistance(c, s[i - 1], s[i]);
                    if (d < best && s[i - 1].HasZ && s[i].HasZ)
                    {
                        best = d;
                        z = s[i - 1].Z + Fraction(s[i - 1], s[i], c) * (s[i].Z - s[i - 1].Z);
                    }
                }
            }

            return new Coordinate(c.X, c.Y, z);
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
