using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// Yes/no questions about the form of a geometry (ADR 0025): <c>IsSimple</c>, <c>IsClosed</c>, <c>IsRing</c> and <c>EqualsExact</c>.
/// </summary>
public static class Characteristics
{
    // ---------- IsSimple ----------

    /// <summary>
    /// Whether a geometry is simple in the sense of the OGC (ADR 0025). A point and a polygon are always simple (whether a polygon is
    /// a valid area is <see cref="Validation.IsValid"/>); a multipoint is simple if no point occurs twice; a polyline is simple if
    /// it does not intersect itself except at end points: two segments of different paths may meet only in a shared vertex that is
    /// an end point of both paths, and both paths must be open there (the closing point of a closed path is an interior point,
    /// so no other path may touch it). Any number of open paths may end in one point. Consecutive segments of one path and the
    /// two ends of a closed path are no violation, a path that doubles back on itself is.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static bool IsSimple(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IMultiPoint2D multiPoint => IsSimple(multiPoint),
            IPolyline2D line => IsSimple(line),
            _ => true,
        };
    }

    private static bool IsSimple(IMultiPoint2D multiPoint)
    {
        var seen = new HashSet<(double, double)>();
        return Rings.Coordinates(multiPoint).All(c => seen.Add((c.X, c.Y)));
    }

    private static bool IsSimple(IPolyline2D line)
    {
        // paths without repeated consecutive points; paths of one point have no segment
        var paths = Enumerable.Range(0, line.PathCount).Select(i => WithoutRepeats(Rings.Coordinates(line[i]))).Where(p => p.Length >= 2).ToArray();
        if (paths.Length == 0)
        {
            return true;
        }

        var closed = paths.Select(p => Rings.SameXY(p[0], p[^1])).ToArray();

        var segments = new List<(int Path, int Index)>();
        for (int p = 0; p < paths.Length; p++)
        {
            for (int k = 0; k + 1 < paths[p].Length; k++)
            {
                segments.Add((p, k));
            }
        }

        Envelope2D EnvelopeOf((int Path, int Index) s)
        {
            var (p, q) = (paths[s.Path][s.Index], paths[s.Path][s.Index + 1]);
            return new Envelope2D(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
        }

        var index = new SpatialIndex<(int Path, int Index)>(segments.Select(s => (EnvelopeOf(s), s)));

        foreach (var (first, second) in index.SelfJoin())
        {
            var path1 = paths[first.Path];
            var path2 = paths[second.Path];
            var (a, b) = (path1[first.Index], path1[first.Index + 1]);
            var (c, d) = (path2[second.Index], path2[second.Index + 1]);
            int o1 = Segments.Orient(a, b, c), o2 = Segments.Orient(a, b, d), o3 = Segments.Orient(c, d, a), o4 = Segments.Orient(c, d, b);

            // not even a touch
            if (!Segments.Intersects(a, b, c, d))
            {
                continue;
            }

            Coordinate shared;
            if (o1 == 0 && o2 == 0)
            {
                // collinear: an overlap of positive length is a violation; otherwise the segments meet at one end point
                var (lo, hi) = Overlap(a, b, c, d);
                if (lo.Position < hi.Position)
                {
                    return false;
                }

                shared = lo.Point;
            }
            else if (o1 * o2 < 0 && o3 * o4 < 0)
            {
                return false; // proper crossing
            }
            else
            {
                shared = o1 == 0 && Segments.IsOnSegment(c, a, b) ? c
                    : o2 == 0 && Segments.IsOnSegment(d, a, b) ? d
                    : o3 == 0 && Segments.IsOnSegment(a, c, d) ? a
                    : b;
            }

            // the touch must be a vertex of both segments (otherwise a vertex lies inside the other segment)
            int vertex1 = VertexIndex(first.Index, a, b, shared);
            int vertex2 = VertexIndex(second.Index, c, d, shared);
            if (vertex1 < 0 || vertex2 < 0)
            {
                return false;
            }

            if (first.Path == second.Path)
            {
                int last = path1.Length - 1;
                bool sameVertex = vertex1 == vertex2;
                bool closingPair = closed[first.Path] && ((vertex1 == 0 && vertex2 == last) || (vertex1 == last && vertex2 == 0));
                if (sameVertex || closingPair)
                {
                    continue;
                }

                return false;
            }

            bool endOfBoth = (vertex1 == 0 || vertex1 == path1.Length - 1) && (vertex2 == 0 || vertex2 == path2.Length - 1);
            if (!endOfBoth || closed[first.Path] || closed[second.Path])
            {
                return false;
            }
        }

        // two different segments of one path can also meet at the same vertex only when adjacent; paths that touch themselves
        // at a vertex that is an end of the path are handled above, a repeated vertex inside a path is a self-touch
        return true;
    }

    private static Coordinate[] WithoutRepeats(Coordinate[] path)
    {
        var result = new List<Coordinate>(path.Length);
        foreach (var c in path)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], c))
            {
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    /// <summary>The vertex index of a shared point on a segment (segment k has the vertices k and k+1), or −1 if it is none of its ends.</summary>
    private static int VertexIndex(int segment, Coordinate start, Coordinate end, Coordinate shared) =>
        Rings.SameXY(start, shared) ? segment : Rings.SameXY(end, shared) ? segment + 1 : -1;

    /// <summary>The overlap of two collinear segments along their longer axis, as (low, high) positions; low = high is a single point.</summary>
    private static ((double Position, Coordinate Point) Low, (double Position, Coordinate Point) High) Overlap(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        bool useX = Math.Abs(b.X - a.X) + Math.Abs(d.X - c.X) >= Math.Abs(b.Y - a.Y) + Math.Abs(d.Y - c.Y);
        double Pos(Coordinate p) => useX ? p.X : p.Y;
        var first = Pos(a) <= Pos(b) ? (a, b) : (b, a);
        var second = Pos(c) <= Pos(d) ? (c, d) : (d, c);
        var low = Pos(first.Item1) >= Pos(second.Item1) ? first.Item1 : second.Item1;
        var high = Pos(first.Item2) <= Pos(second.Item2) ? first.Item2 : second.Item2;
        return ((Pos(low), low), (Pos(high), high));
    }

    // ---------- IsClosed / IsRing ----------

    /// <summary>
    /// Whether every non-empty path of the polyline is closed (its first and last point have the same X and Y). A polyline without
    /// a non-empty path is not closed.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    public static bool IsClosed(this IPolyline2D line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(p => p.Length > 0).ToArray();
        return paths.Length > 0 && paths.All(p => p.Length >= 2 && Rings.SameXY(p[0], p[^1]));
    }

    /// <summary>
    /// Whether the polyline is a ring: exactly one non-empty path, closed, with at least four points, and simple (it does not
    /// touch itself except at the closing point).
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    public static bool IsRing(this IPolyline2D line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(p => p.Length > 0).ToArray();
        return paths.Length == 1 && paths[0].Length >= 4 && Rings.SameXY(paths[0][0], paths[0][^1]) && IsSimple(line);
    }

    // ---------- EqualsExact ----------

    /// <summary>
    /// Whether two geometries are of the same kind and have the same structure and the same coordinates, vertex by vertex and
    /// <b>as given</b> (no normalization: the order of points, paths and rings, the start of a ring and its direction all count; a
    /// ring counts as closed whether or not its closing point is stored). Only X and Y are compared, within
    /// <paramref name="tolerance"/> (a vertex pair is equal if its distance is at most the tolerance);
    /// Z and M are ignored. For the topological equality use <see cref="Relations.EqualsTopo"/>.
    /// </summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="tolerance">The tolerance in the unit of the coordinates; finite and not negative. The default 0 means exact equality.</param>
    /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative, <c>NaN</c> or infinite.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static bool EqualsExact(this IGeometry2D a, IGeometry2D b, double tolerance = 0)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and not negative.");
        }

        bool Same(Coordinate[] p, Coordinate[] q) =>
            p.Length == q.Length && p.Zip(q).All(t => Rings.Distance(t.First, t.Second) <= tolerance);

        bool SameAll(IEnumerable<Coordinate[]> p, IEnumerable<Coordinate[]> q)
        {
            var (left, right) = (p.ToArray(), q.ToArray());
            return left.Length == right.Length && left.Zip(right).All(t => Same(t.First, t.Second));
        }

        return (a, b) switch
        {
            (IPoint2D p, IPoint2D q) => Same([p.ToCoordinate()], [q.ToCoordinate()]),
            (IMultiPoint2D p, IMultiPoint2D q) => Same(Rings.Coordinates(p), Rings.Coordinates(q)),
            (IPolyline2D p, IPolyline2D q) => SameAll(
                Enumerable.Range(0, p.PathCount).Select(i => Rings.Coordinates(p[i])),
                Enumerable.Range(0, q.PathCount).Select(i => Rings.Coordinates(q[i]))),
            (IPolygon2D p, IPolygon2D q) => SameAll(
                Enumerable.Range(0, p.RingCount).Select(i => Rings.Closed(p[i])),
                Enumerable.Range(0, q.RingCount).Select(i => Rings.Closed(q[i]))),
            _ => false,
        };
    }
}
