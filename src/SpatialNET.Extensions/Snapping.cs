using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>What <see cref="Snapping.SnapTo(IPolyline2D, IGeometry2D, double, SnapMode, SnapEnds, IGeometryFactory?)"/> moves (ADR 0046).</summary>
public enum SnapMode
{
    /// <summary>
    /// Only the loose ends of the paths of a line: the start and the end of every open path. A closed path has no loose end and stays as it is,
    /// and a polygon has none at all. This is what a cutting line needs before <c>Cut</c>.
    /// </summary>
    Ends = 0,

    /// <summary>
    /// Every vertex that lies within the tolerance of the target, and the vertices of the target that lie within the tolerance of a segment
    /// are inserted into it, so that two neighbours share their common boundary afterwards.
    /// </summary>
    Vertices = 1,
}

/// <summary>How a loose end reaches the target in <see cref="SnapMode.Ends"/> (ADR 0046).</summary>
public enum SnapEnds
{
    /// <summary>The end moves to the nearest point of the target.</summary>
    Nearest = 0,

    /// <summary>The last segment is extended in its own direction up to the target; an end whose extension misses the target within the tolerance stays.</summary>
    Extend = 1,

    /// <summary>The last segment is extended if that meets the target within the tolerance, otherwise the end moves to the nearest point.</summary>
    ExtendOrNearest = 2,
}

/// <summary>
/// Snapping to a target (ADR 0046): <c>SnapTo</c> moves the vertices of a geometry that lie within a tolerance of another geometry onto it, so that
/// data with small errors (digitised by hand, projected, rounded) meets where it is meant to meet. The exact operations (<c>Cut</c>, the overlays,
/// the predicates) stay exact; snapping is the step before them: <c>cutter.SnapTo(polygon, 0.01).Cut(polygon)</c>.
/// </summary>
/// <remarks>
/// <para>A vertex of the target within the tolerance always wins over a point on an edge, so that a point does not land just beside a corner (which
/// would leave a sliver). Among equally near candidates the first one of the target wins.</para>
/// <para>A moved point keeps its own Z and M: the target decides only where it lies. A vertex of the target that is inserted into a segment gets Z
/// and M interpolated on that segment.</para>
/// <para>Vertices that fall together are merged, paths with fewer than two and rings with fewer than three distinct vertices are dropped (as for
/// <see cref="Cleaning.SnapToGrid"/>). The result <b>may be invalid</b> (moving vertices can make edges cross); <c>MakeValid</c> repairs it.</para>
/// <para>The tolerance is in the units of the coordinates. A point lies on the target after snapping within the rounding of its computation (about
/// 1e-16 relative), far below the grid of the overlays (ADR 0005), so that those see it as touching.</para>
/// </remarks>
public static class Snapping
{
    /// <summary>
    /// Snaps a point to the target: to the nearest vertex of the target within <paramref name="tolerance"/>, otherwise to the nearest point of an
    /// edge within it. A point with nothing within the tolerance stays as it is.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="target">The geometry to snap to; any kind, a polygon by its rings.</param>
    /// <param name="tolerance">The largest distance a point is moved; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="target"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPoint2D SnapTo(this IPoint2D point, IGeometry2D target, double tolerance, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(point);
        var snapper = Prepare(target, tolerance);
        var f = GeometryFactory.Resolve(factory);
        return f.CreatePoint(GeometryAccess.Restrict(snapper.SnapPoint(point.ToCoordinate()), f), point);
    }

    /// <summary>Snaps every point of a multipoint to the target, as <see cref="SnapTo(IPoint2D, IGeometry2D, double, IGeometryFactory?)"/>; all points stay, also if they now coincide.</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="target">The geometry to snap to.</param>
    /// <param name="tolerance">The largest distance a point is moved; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="target"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D SnapTo(this IMultiPoint2D multiPoint, IGeometry2D target, double tolerance, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(multiPoint);
        var snapper = Prepare(target, tolerance);
        var f = GeometryFactory.Resolve(factory);
        return f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint).Select(snapper.SnapPoint).ToArray(), f), multiPoint);
    }

    /// <summary>
    /// Snaps a polyline to the target (ADR 0046). With <see cref="SnapMode.Ends"/> (the default) only the loose ends move: an end that runs over the
    /// target by less than the tolerance (measured along the line) is cut back to where it meets it, an end short of it is moved or extended to it
    /// as <paramref name="ends"/> says, and a vertex of the target within the tolerance of the end wins. With <see cref="SnapMode.Vertices"/> every
    /// vertex is snapped as a point, and vertices of the target near a segment are inserted into it; <paramref name="ends"/> is not used then.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="target">The geometry to snap to; any kind, a polygon by its rings.</param>
    /// <param name="tolerance">The largest distance a vertex is moved; finite and greater than 0.</param>
    /// <param name="mode">What is moved.</param>
    /// <param name="ends">How a loose end reaches the target (only for <see cref="SnapMode.Ends"/>).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="mode"/> or <paramref name="ends"/> is not a known value.</exception>
    /// <exception cref="NotSupportedException"><paramref name="target"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D SnapTo(this IPolyline2D line, IGeometry2D target, double tolerance, SnapMode mode = SnapMode.Ends,
        SnapEnds ends = SnapEnds.Nearest, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        Check(mode, ends);
        var snapper = Prepare(target, tolerance);
        var f = GeometryFactory.Resolve(factory);
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Select(p => WithoutRepeats(mode == SnapMode.Ends ? snapper.SnapPathEnds(p, ends) : snapper.SnapVertices(p)))
            .Where(p => p.Length >= 2)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Snaps the rings of a polygon to the target (ADR 0046), with <see cref="SnapMode.Vertices"/> (the default) as for a polyline. A polygon has no
    /// loose ends, so <see cref="SnapMode.Ends"/> gives a copy. The rings are normalized (shells counter-clockwise, holes clockwise); a part whose
    /// shell collapses goes with its holes.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="target">The geometry to snap to; any kind, a polygon by its rings.</param>
    /// <param name="tolerance">The largest distance a vertex is moved; finite and greater than 0.</param>
    /// <param name="mode">What is moved.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="mode"/> is not a known value.</exception>
    /// <exception cref="NotSupportedException"><paramref name="target"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D SnapTo(this IPolygon2D polygon, IGeometry2D target, double tolerance, SnapMode mode = SnapMode.Vertices, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        Check(mode, SnapEnds.Nearest);
        var snapper = Prepare(target, tolerance);
        var f = GeometryFactory.Resolve(factory);
        var parts = new List<PolygonPart>();
        foreach (var part in PolygonStructure.Build(polygon))
        {
            var shell = mode == SnapMode.Ends ? part.Shell : SnapRing(part.Shell, snapper);
            if (shell is null)
            {
                continue;
            }

            var holes = part.Holes.Select(h => mode == SnapMode.Ends ? h : SnapRing(h, snapper)).Where(h => h is not null)
                .Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(PolygonStructure.Oriented(h!, counterClockwise: false), f)).ToArray();
            parts.Add(new PolygonPart(GeometryAccess.Restrict(PolygonStructure.Oriented(shell, counterClockwise: true), f), holes));
        }

        return f.CreatePolygon(parts.ToArray(), polygon);
    }

    /// <summary>
    /// Snaps any geometry to the target, by the overloads for its kind. Without <paramref name="mode"/> a polyline snaps its
    /// <see cref="SnapMode.Ends"/> and a polygon its <see cref="SnapMode.Vertices"/>; points snap as points.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="target">The geometry to snap to; any kind, a polygon by its rings.</param>
    /// <param name="tolerance">The largest distance a vertex is moved; finite and greater than 0.</param>
    /// <param name="mode">What is moved; <c>null</c> = the default of the kind.</param>
    /// <param name="ends">How a loose end reaches the target (only for a polyline with <see cref="SnapMode.Ends"/>).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0; <paramref name="mode"/> or <paramref name="ends"/> is not a known value.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> or <paramref name="target"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D SnapTo(this IGeometry2D geometry, IGeometry2D target, double tolerance, SnapMode? mode = null,
        SnapEnds ends = SnapEnds.Nearest, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IPolygon2D polygon => polygon.SnapTo(target, tolerance, mode ?? SnapMode.Vertices, factory),
            IPolyline2D line => line.SnapTo(target, tolerance, mode ?? SnapMode.Ends, ends, factory),
            IMultiPoint2D multiPoint => multiPoint.SnapTo(target, tolerance, factory),
            _ => ((IPoint2D)geometry).SnapTo(target, tolerance, factory),
        };
    }

    // ---------- Helpers ----------

    private static Snapper Prepare(IGeometry2D target, double tolerance)
    {
        GeometryAccess.EnsureKnown(target, nameof(target));
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and greater than 0.");
        }

        return new Snapper(target, tolerance);
    }

    private static void Check(SnapMode mode, SnapEnds ends)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown snap mode.");
        }

        if (!Enum.IsDefined(ends))
        {
            throw new ArgumentOutOfRangeException(nameof(ends), ends, "Unknown way of snapping the ends.");
        }
    }

    private static Coordinate[]? SnapRing(Coordinate[] closed, Snapper snapper)
    {
        var ring = WithoutRepeats(snapper.SnapVertices(closed));
        return ring.Length >= 4 && Rings.SameXY(ring[0], ring[^1]) ? ring : null;
    }

    private static Coordinate[] WithoutRepeats(IEnumerable<Coordinate> path)
    {
        var result = new List<Coordinate>();
        foreach (var c in path)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], c))
            {
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    /// <summary>The target with an index of its vertices and segments; candidates are chosen by distance, ties by their order in the target.</summary>
    private sealed class Snapper
    {
        private readonly double _tolerance;
        private readonly Coordinate[] _vertices;
        private readonly (Coordinate A, Coordinate B)[] _segments;
        private readonly SpatialIndex<int> _vertexIndex;
        private readonly SpatialIndex<int> _segmentIndex;

        public Snapper(IGeometry2D target, double tolerance)
        {
            _tolerance = tolerance;
            var segments = GeometryAccess.Segments(target).ToList();
            _vertices = GeometryAccess.AllPoints(target).Select(p => p.ToCoordinate()).ToArray();
            _segments = segments.Where(s => !Rings.SameXY(s.A, s.B)).ToArray();
            _vertexIndex = new SpatialIndex<int>(_vertices.Select((v, i) => (new Envelope2D(v.X, v.Y, v.X, v.Y), i)));
            _segmentIndex = new SpatialIndex<int>(_segments.Select((s, i) => (Envelope(s.A, s.B), i)));
        }

        /// <summary>The nearest vertex within the tolerance, otherwise the nearest point of an edge within it; the point keeps its Z and M.</summary>
        public Coordinate SnapPoint(Coordinate p)
        {
            if (NearestVertex(p) is { } v)
            {
                return p with { X = v.X, Y = v.Y };
            }

            return NearestOnEdge(p) is { } q ? p with { X = q.X, Y = q.Y } : p;
        }

        /// <summary>Every vertex snapped as a point, then the vertices of the target near a segment inserted into it.</summary>
        public Coordinate[] SnapVertices(Coordinate[] path)
        {
            var snapped = path.Select(SnapPoint).ToArray();
            var result = new List<Coordinate>(snapped.Length);
            for (int i = 0; i < snapped.Length; i++)
            {
                if (i > 0)
                {
                    result.AddRange(Inserted(snapped[i - 1], snapped[i]));
                }

                result.Add(snapped[i]);
            }

            return result.ToArray();
        }

        /// <summary>The start and the end of an open path snapped; a closed path stays.</summary>
        public Coordinate[] SnapPathEnds(Coordinate[] path, SnapEnds ends)
        {
            var distinct = WithoutRepeats(path);
            if (distinct.Length < 2 || (distinct.Length >= 4 && Rings.SameXY(distinct[0], distinct[^1])))
            {
                return path;
            }

            var list = SnapEnd(distinct.ToList(), ends);
            list.Reverse();
            list = SnapEnd(list, ends);
            list.Reverse();
            return list.ToArray();
        }

        /// <summary>Snaps the last point of a path: cut back an overshoot, then a vertex, then on the target, then extend or move.</summary>
        private List<Coordinate> SnapEnd(List<Coordinate> path, SnapEnds ends)
        {
            if (path.Count < 2)
            {
                return path;
            }

            if (Overshoot(path) is { } trimmed)
            {
                path = trimmed;
            }

            var end = path[^1];
            if (NearestVertex(end) is { } v)
            {
                path[^1] = end with { X = v.X, Y = v.Y };
                return path;
            }

            if (IsOnTarget(end))
            {
                return path;
            }

            if (ends != SnapEnds.Nearest && Extension(path[^2], end) is { } hit)
            {
                path[^1] = end with { X = hit.X, Y = hit.Y };
                return path;
            }

            if (ends != SnapEnds.Extend && NearestOnEdge(end) is { } q)
            {
                path[^1] = end with { X = q.X, Y = q.Y };
            }

            return path;
        }

        /// <summary>
        /// The path cut back to its last contact with the target, if that lies within the tolerance of the end measured along the path and leaves
        /// at least two distinct points; <c>null</c> if there is nothing to cut back.
        /// </summary>
        private List<Coordinate>? Overshoot(List<Coordinate> path)
        {
            double behind = 0; // length from the end of the current segment to the end of the path
            for (int i = path.Count - 1; i > 0 && behind <= _tolerance; i--)
            {
                Coordinate a = path[i - 1], b = path[i];
                double bestRest = double.PositiveInfinity, bestT = 0;
                int bestIndex = int.MaxValue;
                foreach (int s in _segmentIndex.Query(Envelope(a, b)))
                {
                    var (c, d) = _segments[s];
                    var (distance, onPath, _) = Segments.Nearest(a, b, c, d);
                    if (distance != 0)
                    {
                        continue;
                    }

                    double rest = behind + Rings.Distance(onPath, b);
                    if (rest < bestRest || (rest == bestRest && s < bestIndex))
                    {
                        (bestRest, bestIndex) = (rest, s);
                        double length = Rings.Distance(a, b);
                        bestT = length == 0 ? 0 : 1 - Rings.Distance(onPath, b) / length;
                    }
                }

                foreach (int k in _vertexIndex.Query(Envelope(a, b)))
                {
                    var w = _vertices[k];
                    if (!Segments.IsOnSegment(w, a, b))
                    {
                        continue;
                    }

                    double rest = behind + Rings.Distance(w, b);
                    if (rest < bestRest)
                    {
                        bestRest = rest;
                        double length = Rings.Distance(a, b);
                        bestT = length == 0 ? 0 : 1 - Rings.Distance(w, b) / length;
                    }
                }

                if (double.IsFinite(bestRest))
                {
                    if (bestRest == 0 || bestRest > _tolerance)
                    {
                        return null; // the end lies on the target, or the contact is too far back
                    }

                    var contact = Segments.Lerp(a, b, bestT);
                    var result = path.Take(i).ToList();
                    if (!Rings.SameXY(result[^1], contact))
                    {
                        result.Add(contact);
                    }

                    return WithoutRepeats(result).Length >= 2 ? result : null;
                }

                behind += Rings.Distance(a, b);
            }

            return null;
        }

        /// <summary>The first point where the ray from <paramref name="end"/> in the direction from <paramref name="previous"/> meets the target within the tolerance.</summary>
        private Coordinate? Extension(Coordinate previous, Coordinate end)
        {
            double dx = end.X - previous.X, dy = end.Y - previous.Y, length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0)
            {
                return null;
            }

            double ux = dx / length, uy = dy / length;
            var far = new Coordinate(end.X + ux * _tolerance, end.Y + uy * _tolerance);
            double best = double.PositiveInfinity;
            int bestIndex = int.MaxValue;
            foreach (int s in _segmentIndex.Query(Envelope(end, far)))
            {
                var (c, d) = _segments[s];
                double ex = d.X - c.X, ey = d.Y - c.Y;
                double denominator = ux * ey - uy * ex;
                if (denominator == 0)
                {
                    continue; // parallel: an extension along an edge does not meet it in one point
                }

                double wx = c.X - end.X, wy = c.Y - end.Y;
                double along = (wx * ey - wy * ex) / denominator;   // distance along the ray
                double u = (wx * uy - wy * ux) / denominator;       // parameter on the edge
                if (along > 0 && along <= _tolerance && u >= 0 && u <= 1 && (along < best || (along == best && s < bestIndex)))
                {
                    (best, bestIndex) = (along, s);
                }
            }

            return double.IsFinite(best) ? new Coordinate(end.X + ux * best, end.Y + uy * best) : null;
        }

        /// <summary>The vertices of the target within the tolerance of the segment a-b and strictly between its ends, in the order along it.</summary>
        private IEnumerable<Coordinate> Inserted(Coordinate a, Coordinate b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 == 0)
            {
                return [];
            }

            var found = new List<(double T, int Index, Coordinate Point)>();
            foreach (int k in _vertexIndex.Query(Envelope(a, b).Expand(_tolerance)))
            {
                var w = _vertices[k];
                if (Rings.SameXY(w, a) || Rings.SameXY(w, b))
                {
                    continue;
                }

                double t = ((w.X - a.X) * dx + (w.Y - a.Y) * dy) / len2;
                if (t <= 0 || t >= 1 || Segments.PointDistance(w, a, b) > _tolerance)
                {
                    continue;
                }

                var point = Segments.Lerp(a, b, t);
                found.Add((t, k, point with { X = w.X, Y = w.Y }));
            }

            return found.OrderBy(x => x.T).ThenBy(x => x.Index).Select(x => x.Point)
                .DistinctBy(p => (p.X, p.Y));
        }

        private Coordinate? NearestVertex(Coordinate p)
        {
            double best = double.PositiveInfinity;
            int bestIndex = int.MaxValue;
            foreach (int k in _vertexIndex.Query(Around(p)))
            {
                double distance = Rings.Distance(p, _vertices[k]);
                if (distance <= _tolerance && (distance < best || (distance == best && k < bestIndex)))
                {
                    (best, bestIndex) = (distance, k);
                }
            }

            return bestIndex == int.MaxValue ? null : _vertices[bestIndex];
        }

        private Coordinate? NearestOnEdge(Coordinate p)
        {
            double best = double.PositiveInfinity;
            int bestIndex = int.MaxValue;
            Coordinate bestPoint = default;
            foreach (int s in _segmentIndex.Query(Around(p)))
            {
                var (c, d) = _segments[s];
                var q = Project(p, c, d);
                double distance = Rings.Distance(p, q);
                if (distance <= _tolerance && (distance < best || (distance == best && s < bestIndex)))
                {
                    (best, bestIndex, bestPoint) = (distance, s, q);
                }
            }

            return bestIndex == int.MaxValue ? null : bestPoint;
        }

        private bool IsOnTarget(Coordinate p) =>
            _segmentIndex.Query(Around(p)).Any(s => Segments.IsOnSegment(p, _segments[s].A, _segments[s].B)) ||
            _vertexIndex.Query(Around(p)).Any(k => Rings.SameXY(p, _vertices[k]));

        private static Coordinate Project(Coordinate p, Coordinate c, Coordinate d)
        {
            if (Segments.IsOnSegment(p, c, d))
            {
                return p;
            }

            double dx = d.X - c.X, dy = d.Y - c.Y;
            double t = Math.Clamp(((p.X - c.X) * dx + (p.Y - c.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            return t == 0 ? c : t == 1 ? d : new Coordinate(c.X + t * dx, c.Y + t * dy);
        }

        private Envelope2D Around(Coordinate p) => new(p.X - _tolerance, p.Y - _tolerance, p.X + _tolerance, p.Y + _tolerance);

        private static Envelope2D Envelope(Coordinate a, Coordinate b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
