using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

// Quality meshes (ADR 0041): triangles without sharp angles, by Delaunay refinement (Ruppert 1995).
public sealed partial class Triangulation
{
    private const double MaxMinAngle = 34;      // above this the refinement is not known to stop

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

    private double MinAngleOf(int t) => MinAngleDegrees(_vertices[_triangles[3 * t]], _vertices[_triangles[3 * t + 1]], _vertices[_triangles[3 * t + 2]]);

    private static double MinAngleDegrees(Coordinate p0, Coordinate p1, Coordinate p2)
    {
        var p = new[] { p0, p1, p2 };
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

        if (TriangleCount == 0 || maxSteinerPoints == 0)
        {
            return this;
        }

        var mesh = new RefinementMesh(_vertices, _triangles, _neighbours, _constrained);
        double minX = _vertices.Min(v => v.X), maxX = _vertices.Max(v => v.X), minY = _vertices.Min(v => v.Y), maxY = _vertices.Max(v => v.Y);
        double floor = 1e-7 * Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        var segments = new Queue<(int A, int B, bool Forced)>();
        var queued = new HashSet<(int, int, bool)>();
        var bad = new PriorityQueue<(int T, int Stamp), double>();
        var touched = new List<int>();
        int added = 0;

        // forced: a point that is to be inserted lies in the diametral circle of the edge, so that no vertex that is there already has to
        void QueueSegment(int a, int b, bool forced = false)
        {
            if (queued.Add(a < b ? (a, b, forced) : (b, a, forced)))
            {
                segments.Enqueue((a, b, forced));
            }
        }

        // the triangles that are too sharp or too large: the worst come first; those whose circumcircle is smaller than the floor are left
        void CheckTriangle(int t)
        {
            var a = mesh.Corner(t, 0);
            var b = mesh.Corner(t, 1);
            var c = mesh.Corner(t, 2);
            double area = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            double radius = area > 0 ? Length(a, b) * Length(b, c) * Length(c, a) / (4 * area) : double.PositiveInfinity;
            if (radius < floor)
            {
                return;
            }

            if (maxArea is { } limit && area > limit)
            {
                bad.Enqueue((t, mesh.Stamp(t)), -area / limit);
                return;
            }

            double angle = MinAngleDegrees(a, b, c);
            if (angle < minAngle)
            {
                bad.Enqueue((t, mesh.Stamp(t)), angle / Math.Max(minAngle, 1e-300));
            }
        }

        // the protected edges of a triangle that the vertex opposite lies in the diametral circle of
        void CheckSegments(int t)
        {
            for (int k = 0; k < 3; k++)
            {
                if (mesh.IsProtected(t, k) && Encroaches(mesh.Corner(t, k), mesh.Corner(t, k + 1), mesh.Corner(t, k + 2), floor))
                {
                    QueueSegment(mesh.Vertex(t, k), mesh.Vertex(t, k + 1));
                }
            }
        }

        void Examine(List<int> changed)
        {
            foreach (int t in changed.Distinct())
            {
                CheckSegments(t);
                CheckTriangle(t);
            }

            changed.Clear();
        }

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            CheckSegments(t);
            CheckTriangle(t);
        }

        long guard = 100L * (maxSteinerPoints + TriangleCount) + 1000;
        while (added < maxSteinerPoints && guard-- > 0)
        {
            if (segments.Count > 0)
            {
                // an edge of the border or of a constraint that a vertex lies in the diametral circle of: split in the middle
                var (a, b, forced) = segments.Dequeue();
                queued.Remove(a < b ? (a, b, forced) : (b, a, forced));
                var (t, k) = mesh.FindEdge(a, b);
                if (t < 0 || (forced ? Length(mesh.Corner(t, k), mesh.Corner(t, k + 1)) < 2 * floor : !EdgeEncroached(mesh, t, k, floor)))
                {
                    continue;
                }

                var pa = mesh.Points[mesh.Vertex(t, k)];
                var pb = mesh.Points[mesh.Vertex(t, k + 1)];
                var middle = pa.HasZ && pb.HasZ ? new Coordinate((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, (pa.Z + pb.Z) / 2) : new Coordinate((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2);
                if (mesh.Split(middle, t, k, touched) >= 0)
                {
                    added++;
                }

                Examine(touched);
                continue;
            }

            if (bad.Count == 0)
            {
                break;
            }

            var (triangle, stamp) = bad.Dequeue();
            if (mesh.Stamp(triangle) != stamp)
            {
                continue;   // changed since: it was examined again
            }

            var c0 = mesh.Corner(triangle, 0);
            var c1 = mesh.Corner(triangle, 1);
            var c2 = mesh.Corner(triangle, 2);
            var (cx, cy) = VoronoiBuilder.Centre(c0, c1, c2);
            if (!double.IsFinite(cx) || !double.IsFinite(cy))
            {
                continue;
            }

            var centre = new Coordinate(cx, cy);
            var from = new Coordinate((c0.X + c1.X + c2.X) / 3, (c0.Y + c1.Y + c2.Y) / 3);
            if (!mesh.Walk(triangle, from, centre, out int target, out int blockedSide))
            {
                // a sliver at the border or next to a constraint has its centre beyond it: the edge in the way is split instead
                if (blockedSide >= 0 && Length(mesh.Corner(target, blockedSide), mesh.Corner(target, blockedSide + 1)) >= 2 * floor)
                {
                    QueueSegment(mesh.Vertex(target, blockedSide), mesh.Vertex(target, blockedSide + 1), forced: true);
                    bad.Enqueue((triangle, stamp), 0);   // and tried again afterwards
                }

                continue;
            }

            var encroached = EncroachedBy(mesh, target, centre, floor);
            if (encroached.Count > 0)
            {
                // the centre lies in the diametral circle of an edge: those edges are split, and the triangle is tried again
                foreach (var (a, b) in encroached)
                {
                    QueueSegment(a, b, forced: true);
                }

                bad.Enqueue((triangle, stamp), 0);
                continue;
            }

            double z = InterpolateZ(centre);
            if (mesh.Insert(double.IsNaN(z) ? centre : new Coordinate(cx, cy, z), target, touched) >= 0)
            {
                added++;
            }

            Examine(touched);
        }

        if (added == 0)
        {
            return this;
        }

        var (vertices, triangles, neighbours, constrained) = mesh.ToArrays();
        return new Triangulation(vertices, triangles, neighbours, constrained);
    }

    /// <summary>Whether a vertex lies in the diametral circle of the edge of a side, on either side of it.</summary>
    private static bool EdgeEncroached(RefinementMesh mesh, int t, int k, double floor)
    {
        var a = mesh.Corner(t, k);
        var b = mesh.Corner(t, k + 1);
        if (Encroaches(a, b, mesh.Corner(t, k + 2), floor))
        {
            return true;
        }

        int u = mesh.Neighbour(t, k);
        if (u < 0)
        {
            return false;
        }

        for (int j = 0; j < 3; j++)
        {
            if (mesh.Vertex(u, j) != mesh.Vertex(t, k) && mesh.Vertex(u, j) != mesh.Vertex(t, k + 1))
            {
                return Encroaches(a, b, mesh.Corner(u, j), floor);
            }
        }

        return false;
    }

    /// <summary>
    /// The protected edges that a point would encroach upon: those of the triangles whose circumcircle holds the point (what it would cut out)
    /// and of their neighbours, that have it in their diametral circle.
    /// </summary>
    private static List<(int A, int B)> EncroachedBy(RefinementMesh mesh, int start, Coordinate p, double floor)
    {
        var found = new List<(int, int)>();
        var cavity = new HashSet<int> { start };
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int t = stack.Pop();
            for (int k = 0; k < 3; k++)
            {
                int n = mesh.Neighbour(t, k);
                if (!mesh.IsProtected(t, k) && n >= 0 && !cavity.Contains(n)
                    && InCircle.Sign(mesh.Corner(n, 0), mesh.Corner(n, 1), mesh.Corner(n, 2), p) > 0)
                {
                    cavity.Add(n);
                    stack.Push(n);
                }
            }
        }

        var seen = new HashSet<int>(cavity);
        foreach (int t in cavity.ToArray())
        {
            for (int k = 0; k < 3; k++)
            {
                int n = mesh.Neighbour(t, k);
                if (n >= 0)
                {
                    seen.Add(n);
                }
            }
        }

        foreach (int t in seen)
        {
            for (int k = 0; k < 3; k++)
            {
                if (mesh.IsProtected(t, k))
                {
                    var a = mesh.Corner(t, k);
                    var b = mesh.Corner(t, k + 1);
                    if (Encroaches(a, b, p, floor))
                    {
                        found.Add((mesh.Vertex(t, k), mesh.Vertex(t, k + 1)));
                    }
                }
            }
        }

        return found;
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

    /// <summary>Whether a point lies strictly inside the circle with the segment as its diameter, and the segment is long enough to be split.</summary>
    private static bool Encroaches(Coordinate a, Coordinate b, Coordinate p, double floor) =>
        (p.X - a.X) * (p.X - b.X) + (p.Y - a.Y) * (p.Y - b.Y) < 0 && Length(a, b) >= 2 * floor;

    private static double Length(Coordinate a, Coordinate b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
