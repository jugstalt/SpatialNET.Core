using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

// Quality meshes (ADR 0041): triangles without sharp angles, by Delaunay refinement (Ruppert 1995).
public sealed partial class Triangulation
{
    private const double MaxMinAngle = 34;      // above this the refinement is not known to stop
    private const int MaxRounds = 400;

    /// <summary>
    /// The smallest angle of a triangle in degrees (ADR 0041): 60 for an equilateral triangle, near 0 for a sliver.
    /// </summary>
    /// <param name="triangle">The triangle.</param>
    /// <returns>The smallest angle, in degrees.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public double MinAngle(int triangle)
    {
        CheckTriangle(triangle);
        return MinAngleOf(triangle);
    }

    /// <summary>The smallest angle of all triangles in degrees; <see cref="double.NaN"/> for a triangulation without triangles.</summary>
    public double MinAngle()
    {
        double min = double.NaN;
        for (int t = 0; t < TriangleCount; t++)
        {
            double angle = MinAngleOf(t);
            if (double.IsNaN(min) || angle < min)
            {
                min = angle;
            }
        }

        return min;
    }

    private double MinAngleOf(int t)
    {
        var p = new[] { _vertices[_triangles[3 * t]], _vertices[_triangles[3 * t + 1]], _vertices[_triangles[3 * t + 2]] };
        double min = double.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            var a = p[k];
            var b = p[(k + 1) % 3];
            var c = p[(k + 2) % 3];
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - a.X, vy = c.Y - a.Y;
            min = Math.Min(min, Math.Atan2(Math.Abs(ux * vy - uy * vx), ux * vx + uy * vy));
        }

        return min * 180 / Math.PI;
    }

    /// <summary>
    /// A finer triangulation without sharp triangles (ADR 0041), by Delaunay refinement after Ruppert: points are added (the circumcentres of the
    /// triangles that are too sharp or too large, and the middles of edges that would be encroached by them) until no angle is smaller than
    /// <paramref name="minAngle"/> and no triangle larger than <paramref name="maxArea"/>. The shape of the surface is kept: the constraints
    /// (break lines, the rings of a polygon) and the border stay where they are, only cut into shorter edges, and the added points get their
    /// height from this surface, so they lie on it. A mesh for a finite element or flow calculation, or for a smoother contour map, starts here.
    /// </summary>
    /// <remarks>
    /// The method is guaranteed to stop for angles up to about 20.7 degrees, and in practice up to about 30, as long as the input has no angle
    /// smaller than that between two constraints. Where it has (the tip of a thin wedge) the sharp triangles at the tip cannot be avoided, and
    /// they are left as they are once their circumcircle is smaller than 1e-7 of the extent of the surface. <paramref name="maxSteinerPoints"/>
    /// ends the work in any case; the result then still is a valid triangulation, only not as fine as asked. A triangulation without triangles is
    /// returned as it is.
    /// </remarks>
    /// <param name="minAngle">The smallest angle wanted, in degrees, from 0 (only the size counts) to 34; the default 20 is safe.</param>
    /// <param name="maxArea">The largest area of a triangle in the plane, or <c>null</c> for no limit.</param>
    /// <param name="maxSteinerPoints">The most points that are added.</param>
    /// <returns>The refined triangulation; the same instance if nothing had to be done.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minAngle"/> is not from 0 to 34, <paramref name="maxArea"/> is not greater than 0, or <paramref name="maxSteinerPoints"/> is negative.</exception>
    public Triangulation Refine(double minAngle = 20, double? maxArea = null, int maxSteinerPoints = 100_000)
    {
        if (!(minAngle >= 0 && minAngle <= MaxMinAngle))
        {
            throw new ArgumentOutOfRangeException(nameof(minAngle), minAngle, $"The angle must be from 0 to {MaxMinAngle} degrees.");
        }

        if (maxArea is { } limit && !(limit > 0 && double.IsFinite(limit)))
        {
            throw new ArgumentOutOfRangeException(nameof(maxArea), maxArea, "The area must be finite and greater than 0.");
        }

        if (maxSteinerPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSteinerPoints), maxSteinerPoints, "The number must not be negative.");
        }

        if (TriangleCount == 0)
        {
            return this;
        }

        var points = new List<Coordinate>(_vertices);
        var known = new HashSet<(double, double)>(_vertices.Select(v => (v.X, v.Y)));
        var segments = BorderAndConstraintSegments();

        double minX = _vertices.Min(v => v.X), maxX = _vertices.Max(v => v.X), minY = _vertices.Min(v => v.Y), maxY = _vertices.Max(v => v.Y);
        double floor = 1e-7 * Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        var current = this;
        int added = 0;
        for (int round = 0; round < MaxRounds && added < maxSteinerPoints; round++)
        {
            // 1. the edges of the border and the constraints come first: split those that a vertex lies in the diametral circle of
            var split = new HashSet<(int, int)>();
            for (int t = 0; t < current.TriangleCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int u = current._triangles[3 * t + k], v = current._triangles[3 * t + (k + 1) % 3];
                    var key = u < v ? (u, v) : (v, u);
                    if (segments.ContainsKey(key) && Encroaches(points[key.Item1], points[key.Item2], current._vertices[current._triangles[3 * t + (k + 2) % 3]], floor))
                    {
                        split.Add(key);
                    }
                }
            }

            var inserts = new List<Coordinate>();
            if (split.Count == 0)
            {
                // 2. the triangles that are too sharp or too large: the circumcentre, unless it encroaches an edge, which is split instead
                var index = new SpatialIndex<(int, int)>(segments.Keys.Select(key =>
                {
                    var a = points[key.Item1];
                    var b = points[key.Item2];
                    double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2, r = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) / 2;
                    return (new Envelope2D(cx - r, cy - r, cx + r, cy + r), key);
                }));

                var bad = current.BadTriangles(minAngle, maxArea, floor);
                var touched = new HashSet<int>();
                foreach (int t in bad)
                {
                    if (touched.Contains(t))
                    {
                        continue;
                    }

                    var (cx, cy) = VoronoiBuilder.Centre(current._vertices[current._triangles[3 * t]], current._vertices[current._triangles[3 * t + 1]], current._vertices[current._triangles[3 * t + 2]]);
                    var centre = new Coordinate(cx, cy);
                    if (!double.IsFinite(cx) || !double.IsFinite(cy) || !known.Add((cx, cy)))
                    {
                        continue;
                    }

                    known.Remove((cx, cy));
                    bool encroaching = false;
                    foreach (var key in index.Query(new Envelope2D(cx, cy, cx, cy)))
                    {
                        var a = points[key.Item1];
                        var b = points[key.Item2];
                        if ((cx - a.X) * (cx - b.X) + (cy - a.Y) * (cy - b.Y) < 0)
                        {
                            encroaching = true;
                            if (Length(a, b) >= 2 * floor)
                            {
                                split.Add(key);
                            }
                        }
                    }

                    if (!encroaching && Locate(centre) < 0)
                    {
                        // a sliver at the border has its centre outside (or far away): the edge that is in the way is split instead
                        encroaching = true;
                        if (current.EdgeInTheWay(t, cx, cy, segments) is { } blocking && Length(points[blocking.Item1], points[blocking.Item2]) >= 2 * floor)
                        {
                            split.Add(blocking);
                        }
                    }

                    touched.Add(t);
                    for (int k = 0; k < 3; k++)
                    {
                        int n = current._neighbours[3 * t + k];
                        if (n >= 0)
                        {
                            touched.Add(n);
                        }
                    }

                    if (!encroaching)
                    {
                        double z = InterpolateZ(centre);
                        inserts.Add(double.IsNaN(z) ? centre : new Coordinate(cx, cy, z));
                    }
                }
            }

            if (split.Count == 0 && inserts.Count == 0)
            {
                break;
            }

            foreach (var key in split)
            {
                var a = points[key.Item1];
                var b = points[key.Item2];
                if (!segments.TryGetValue(key, out var segment))
                {
                    continue;
                }

                var middle = Middle(a, b, segment.From >= 0 ? points[segment.From] : null, segment.From >= 0 ? points[segment.To] : null);
                if (!known.Add((middle.X, middle.Y)))
                {
                    continue;
                }

                int id = points.Count;
                points.Add(middle);
                segments.Remove(key);
                // the halves keep the direction of a border edge, with the middle in place of the end that it replaces
                int from = segment.From, to = segment.To;
                segments[key.Item1 < id ? (key.Item1, id) : (id, key.Item1)] = new Segment(segment.Constrained, from >= 0 ? (from == key.Item1 ? key.Item1 : id) : -1, from >= 0 ? (from == key.Item1 ? id : key.Item1) : -1);
                segments[key.Item2 < id ? (key.Item2, id) : (id, key.Item2)] = new Segment(segment.Constrained, from >= 0 ? (from == key.Item2 ? key.Item2 : id) : -1, from >= 0 ? (from == key.Item2 ? id : key.Item2) : -1);
                added++;
            }

            foreach (var p in inserts)
            {
                if (known.Add((p.X, p.Y)))
                {
                    points.Add(p);
                    added++;
                }
            }

            var next = Rebuild(points, segments);
            if (next is null)
            {
                break;
            }

            current = next;
        }

        return current;
    }

    /// <summary>The first edge of the border or of a constraint on the straight way from the centre of a triangle to a point, or <c>null</c> if there is none.</summary>
    private (int, int)? EdgeInTheWay(int triangle, double x, double y, Dictionary<(int, int), Segment> segments)
    {
        var a0 = _vertices[_triangles[3 * triangle]];
        var b0 = _vertices[_triangles[3 * triangle + 1]];
        var c0 = _vertices[_triangles[3 * triangle + 2]];
        var from = new Coordinate((a0.X + b0.X + c0.X) / 3, (a0.Y + b0.Y + c0.Y) / 3);
        var to = new Coordinate(x, y);
        int t = triangle;
        for (int step = 0; step <= TriangleCount; step++)
        {
            int next = -1;
            for (int k = 0; k < 3; k++)
            {
                int u = _triangles[3 * t + k], v = _triangles[3 * t + (k + 1) % 3];
                var pu = _vertices[u];
                var pv = _vertices[v];
                // the way leaves the triangle through this edge: the target is on the outer side, and the edge separates the two ends of the way
                if (Orientation.Orient2D(pu, pv, to) < 0 && Orientation.Orient2D(from, to, pu) * Orientation.Orient2D(from, to, pv) <= 0)
                {
                    var key = u < v ? (u, v) : (v, u);
                    if (_neighbours[3 * t + k] < 0 || segments.ContainsKey(key))
                    {
                        return key;
                    }

                    next = _neighbours[3 * t + k];
                    break;
                }
            }

            if (next < 0)
            {
                return null;
            }

            t = next;
        }

        return null;
    }

    /// <summary>The edges of the border and the constraints, with the direction of a border edge that has the inside on its left.</summary>
    private Dictionary<(int, int), Segment> BorderAndConstraintSegments()
    {
        var segments = new Dictionary<(int, int), Segment>();
        for (int t = 0; t < TriangleCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                bool constrained = _constrained[3 * t + k];
                if (_neighbours[3 * t + k] < 0 || constrained)
                {
                    int u = _triangles[3 * t + k], v = _triangles[3 * t + (k + 1) % 3];
                    var key = u < v ? (u, v) : (v, u);
                    bool isBorder = _neighbours[3 * t + k] < 0;
                    segments.TryGetValue(key, out var before);
                    // the triangle lies on the left of its edge u → v: for the border that is the inside
                    segments[key] = isBorder ? new Segment(constrained || before.Constrained, u, v) : new Segment(true, -1, -1);
                }
            }
        }

        return segments;
    }

    /// <summary>An edge of the border or a constraint; <c>From</c> and <c>To</c> are its ends in the direction that has the inside on the left, or −1 for an edge that is not on the border.</summary>
    private readonly record struct Segment(bool Constrained, int From, int To);

    /// <summary>
    /// The middle of an edge. On the border it is moved to the inside by as many units in the last place as it takes to lie on that side of the
    /// edge, so that the rounding never puts it outside: a middle outside would make a triangle of the edge with the chord that no refinement can improve.
    /// </summary>
    private static Coordinate Middle(Coordinate a, Coordinate b, Coordinate? from, Coordinate? to)
    {
        double x = (a.X + b.X) / 2, y = (a.Y + b.Y) / 2;
        if (from is { } start && to is { } end)
        {
            double nx = -(end.Y - start.Y), ny = end.X - start.X, length = Math.Sqrt(nx * nx + ny * ny);
            nx /= length;
            ny /= length;
            double size = Math.Max(Math.Abs(x), Math.Abs(y));
            double step = Math.BitIncrement(size) - size;
            double mx = x, my = y;
            for (int i = 0; i < 80 && Orientation.Orient2D(start, end, new Coordinate(mx, my)) < 0; i++)
            {
                mx = x + nx * step;
                my = y + ny * step;
                step *= 2;
            }

            x = mx;
            y = my;
        }

        return a.HasZ && b.HasZ ? new Coordinate(x, y, (a.Z + b.Z) / 2) : new Coordinate(x, y);
    }

    /// <summary>Whether a point lies strictly inside the circle with the segment as its diameter, and the segment is long enough to be split.</summary>
    private static bool Encroaches(Coordinate a, Coordinate b, Coordinate p, double floor) =>
        (p.X - a.X) * (p.X - b.X) + (p.Y - a.Y) * (p.Y - b.Y) < 0 && Length(a, b) >= 2 * floor;

    private static double Length(Coordinate a, Coordinate b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The triangles that are too sharp or too large, the worst first; those whose circumcircle is smaller than the floor are left out.</summary>
    private List<int> BadTriangles(double minAngle, double? maxArea, double floor)
    {
        var bad = new List<(double Key, int T)>();
        for (int t = 0; t < TriangleCount; t++)
        {
            var a = _vertices[_triangles[3 * t]];
            var b = _vertices[_triangles[3 * t + 1]];
            var c = _vertices[_triangles[3 * t + 2]];
            double area = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            double radius = area > 0 ? Length(a, b) * Length(b, c) * Length(c, a) / (4 * area) : double.PositiveInfinity;
            if (radius < floor)
            {
                continue;
            }

            if (maxArea is { } limit && area > limit)
            {
                bad.Add((-area / limit, t));
                continue;
            }

            double angle = MinAngleOf(t);
            if (angle < minAngle)
            {
                bad.Add((angle / minAngle, t));
            }
        }

        bad.Sort((x, y) => x.Key.CompareTo(y.Key));
        return bad.Select(b => b.T).ToList();
    }

    /// <summary>The constrained triangulation of the points with the segments, inside the domain of this triangulation; <c>null</c> if it cannot be made.</summary>
    private Triangulation? Rebuild(List<Coordinate> points, Dictionary<(int, int), Segment> segments)
    {
        var mesh = DelaunayMesh.Create(points.ToArray());
        if (mesh is null)
        {
            return null;
        }

        try
        {
            mesh.InsertConstraints(segments.Keys);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        // The inside is what can be reached from the inner side of the border edges without crossing one (a border edge has the inside on its
        // left). That is topology, not geometry: where the middle of a split edge is a hair off the line, the slivers between the edge and its
        // chord are outside, whatever a test of their position would say.
        var keep = new bool[mesh.TriangleCount];
        var queue = new Queue<int>();
        for (int e = 0; e < keep.Length * 3; e++)
        {
            int u = mesh.Triangles[e], v = mesh.Triangles[DelaunayMesh.Next(e)];
            if (segments.TryGetValue(u < v ? (u, v) : (v, u), out var seed) && seed.From == u && seed.To == v && !keep[e / 3])
            {
                keep[e / 3] = true;
                queue.Enqueue(e / 3);
            }
        }

        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            for (int k = 0; k < 3; k++)
            {
                int e = 3 * t + k, h = mesh.HalfEdges[e];
                if (h == -1 || keep[h / 3])
                {
                    continue;
                }

                int u = mesh.Triangles[e], v = mesh.Triangles[DelaunayMesh.Next(e)];
                if (segments.TryGetValue(u < v ? (u, v) : (v, u), out var segment) && segment.From >= 0)
                {
                    continue;   // a border edge: the other side is outside
                }

                keep[h / 3] = true;
                queue.Enqueue(h / 3);
            }
        }

        return From(mesh, keep, borderConstrained: false, (u, v) => segments.TryGetValue(u < v ? (u, v) : (v, u), out var segment) && segment.Constrained);
    }
}
