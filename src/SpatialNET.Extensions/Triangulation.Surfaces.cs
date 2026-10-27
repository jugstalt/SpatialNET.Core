using SpatialNET.Internal;

namespace SpatialNET;

// Questions about whole surfaces (ADR 0041): the volume between two surfaces, the zones of equal slope and the line of sight.
public sealed partial class Triangulation
{
    // ---------- the volume between two surfaces ----------

    /// <summary>
    /// The volume between this surface and another one (ADR 0041), over the area that both cover: <c>Above</c> is where this surface lies above the
    /// other, <c>Below</c> where it lies below, both not negative; their difference is the net volume. With the ground before and after an excavation
    /// <c>Above</c> is what is to be filled and <c>Below</c> what is to be cut away (or the other way round, as the surfaces are given). Both
    /// surfaces are linear in their triangles, so the result is exact: the triangles are cut with each other, and the difference of two planes is
    /// again a plane over every piece. Triangles with a vertex without Z are not counted. X, Y and Z are taken to be in the same unit.
    /// </summary>
    /// <param name="other">The other surface.</param>
    /// <returns>The volume above and below it; (0, 0) if the surfaces have no area in common.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <c>null</c>.</exception>
    public (double Above, double Below) Volume(Triangulation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        double above = 0, below = 0;
        if (TriangleCount == 0 || other.TriangleCount == 0)
        {
            return (0, 0);
        }

        var piece = new List<(double X, double Y)>();
        var next = new List<(double X, double Y)>();
        for (int t = 0; t < TriangleCount; t++)
        {
            var (dxT, dyT) = GradientOf(t);
            if (double.IsNaN(dxT))
            {
                continue;
            }

            var a = _vertices[_triangles[3 * t]];
            var b = _vertices[_triangles[3 * t + 1]];
            var c = _vertices[_triangles[3 * t + 2]];
            var box = new Envelope2D(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            foreach (int u in other._index.Value.Query(box))
            {
                var (dxU, dyU) = other.GradientOf(u);
                if (double.IsNaN(dxU))
                {
                    continue;
                }

                // this triangle cut with the other one: a convex polygon, by the three half-planes of the other
                piece.Clear();
                piece.Add((a.X, a.Y));
                piece.Add((b.X, b.Y));
                piece.Add((c.X, c.Y));
                for (int k = 0; k < 3 && piece.Count >= 3; k++)
                {
                    var p = other._vertices[other._triangles[3 * u + k]];
                    var q = other._vertices[other._triangles[3 * u + (k + 1) % 3]];
                    CutByHalfPlane(piece, next, p, q);
                    (piece, next) = (next, piece);
                }

                if (piece.Count < 3)
                {
                    continue;
                }

                // the difference of the heights is linear over the piece: split it into triangles
                var originT = NearestVertex(t, piece[0].X, piece[0].Y);
                var originU = other.NearestVertex(u, piece[0].X, piece[0].Y);
                double Difference((double X, double Y) v) =>
                    originT.Z + dxT * (v.X - originT.X) + dyT * (v.Y - originT.Y) - (originU.Z + dxU * (v.X - originU.X) + dyU * (v.Y - originU.Y));

                double h0 = Difference(piece[0]);
                for (int i = 1; i + 1 < piece.Count; i++)
                {
                    var p0 = piece[0];
                    var p1 = piece[i];
                    var p2 = piece[i + 1];
                    double area = ((p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y)) / 2;
                    if (area <= 0)
                    {
                        continue;
                    }

                    double h1 = Difference(p1), h2 = Difference(p2);
                    above += Positive(h0, h1, h2, area);
                    below += Positive(-h0, -h1, -h2, area);
                }
            }
        }

        return (above, below);
    }

    /// <summary>Keeps the part of a convex polygon that lies on the left of (or on) the line p → q.</summary>
    private static void CutByHalfPlane(List<(double X, double Y)> polygon, List<(double X, double Y)> output, Coordinate p, Coordinate q)
    {
        output.Clear();
        double ex = q.X - p.X, ey = q.Y - p.Y;
        for (int i = 0; i < polygon.Count; i++)
        {
            var cur = polygon[i];
            var prev = polygon[(i + polygon.Count - 1) % polygon.Count];
            double fc = ex * (cur.Y - p.Y) - ey * (cur.X - p.X), fp = ex * (prev.Y - p.Y) - ey * (prev.X - p.X);
            if ((fc >= 0) != (fp >= 0))
            {
                double s = fp / (fp - fc);
                output.Add((prev.X + s * (cur.X - prev.X), prev.Y + s * (cur.Y - prev.Y)));
            }

            if (fc >= 0)
            {
                output.Add(cur);
            }
        }
    }

    // ---------- zones of equal slope ----------

    /// <summary>
    /// The areas of the surface by slope class (ADR 0041): the triangles whose <see cref="Slope"/> lies between two limits are united to one polygon
    /// per class. Class 0 is everything below the first limit, class <c>i</c> from limit <c>i − 1</c> up to (not including) limit <c>i</c>, and the
    /// last class everything from the last limit up; so there is one more class than there are limits, and a class without area is an empty polygon.
    /// Triangles with a vertex without Z belong to no class.
    /// </summary>
    /// <param name="limits">The limits in degrees, strictly ascending.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The polygons, one per class.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="limits"/> are not finite or not strictly ascending.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IReadOnlyList<IPolygon2D> SlopeZones(IReadOnlyList<double> limits, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        for (int i = 0; i < limits.Count; i++)
        {
            if (!double.IsFinite(limits[i]) || (i > 0 && limits[i] <= limits[i - 1]))
            {
                throw new ArgumentException("The limits must be finite and strictly ascending.", nameof(limits));
            }
        }

        var f = GeometryFactory.Resolve(factory);
        var classes = new int[TriangleCount];
        var members = new List<int>[limits.Count + 1];
        for (int i = 0; i < members.Length; i++)
        {
            members[i] = [];
        }

        for (int t = 0; t < TriangleCount; t++)
        {
            double slope = Slope(t);
            if (double.IsNaN(slope))
            {
                classes[t] = -1;
                continue;
            }

            int cls = 0;
            while (cls < limits.Count && slope >= limits[cls])
            {
                cls++;
            }

            classes[t] = cls;
            members[cls].Add(t);
        }

        return members.Select((triangles, cls) => triangles.Count == 0 ? f.CreatePolygon([], null) : Zone(triangles, classes, cls, f)).ToArray();
    }

    /// <summary>
    /// The polygon of the triangles of one class: the edges that have a triangle of another class (or none) on the other side are the boundary,
    /// chained to rings with the area on the left; clockwise rings are holes and belong to the smallest ring around them. No overlay is needed, since
    /// the triangles of a triangulation never overlap.
    /// </summary>
    private IPolygon2D Zone(List<int> triangles, int[] classes, int cls, IGeometryFactory f)
    {
        var from = new List<int>();
        var to = new List<int>();
        foreach (int t in triangles)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = _neighbours[3 * t + k];
                if (n >= 0 && classes[n] == cls)
                {
                    continue;
                }

                from.Add(_triangles[3 * t + k]);
                to.Add(_triangles[3 * t + (k + 1) % 3]);
            }
        }

        return AreaFromEdges(_vertices, from, to, f, repair: HasSliver);
    }

    private int _sliverState;   // 0 = not known yet, 1 = no sliver, 2 = a sliver; racing threads compute the same value

    /// <summary>Whether a triangle has an angle of less than a millionth of a degree: its corners lie on one line to within a rounding error.</summary>
    private bool HasSliver
    {
        get
        {
            if (_sliverState == 0)
            {
                _sliverState = TriangleCount > 0 && MinAngle() < 1e-6 ? 2 : 1;
            }

            return _sliverState == 2;
        }
    }

    /// <summary>
    /// The polygon whose boundary is a set of directed edges between vertices (<paramref name="from"/>[i] → <paramref name="to"/>[i], the area on the
    /// left): the edges are chained to rings, a ring that touches itself in a vertex is cut there, clockwise rings are holes and belong to the smallest
    /// ring around them.
    /// </summary>
    private static IPolygon2D AreaFromEdges(IReadOnlyList<Coordinate> vertices, List<int> from, List<int> to, IGeometryFactory f, bool repair)
    {
        var outgoing = new Dictionary<int, List<int>>();
        for (int e = 0; e < from.Count; e++)
        {
            if (!outgoing.TryGetValue(from[e], out var list))
            {
                outgoing[from[e]] = list = [];
            }

            list.Add(e);
        }

        var used = new bool[from.Count];
        var shells = new List<(Coordinate[] Ring, double Area, Envelope2D Box)>();
        var holes = new List<(Coordinate[] Ring, Envelope2D Box)>();
        for (int start = 0; start < from.Count; start++)
        {
            if (used[start])
            {
                continue;
            }

            var chain = new List<int>();
            int edge = start;
            while (true)
            {
                used[edge] = true;
                chain.Add(from[edge]);
                int next = NextBoundaryEdge(vertices, edge, from, to, outgoing, used, start);
                if (next < 0)
                {
                    break;
                }

                edge = next;
            }

            // a vertex where the boundary touches itself (a pocket that meets the border in a point, two parts that meet in a corner): the chain
            // visits it twice and is cut there, so that no ring touches itself; a clockwise piece is a hole, a counter-clockwise one a shell
            foreach (var piece in SplitAtRepeatedVertices(chain))
            {
                if (piece.Count < 3)
                {
                    continue;
                }

                var ring = piece.Select(id => vertices[id]).ToList();
                ring.Add(ring[0]);
                double area = 0, minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (int i = 0; i + 1 < ring.Count; i++)
                {
                    area += (ring[i].X - ring[0].X) * (ring[i + 1].Y - ring[0].Y) - (ring[i + 1].X - ring[0].X) * (ring[i].Y - ring[0].Y);
                    minX = Math.Min(minX, ring[i].X);
                    maxX = Math.Max(maxX, ring[i].X);
                    minY = Math.Min(minY, ring[i].Y);
                    maxY = Math.Max(maxY, ring[i].Y);
                }

                var box = new Envelope2D(minX, minY, maxX, maxY);
                if (area > 0)
                {
                    shells.Add((ring.ToArray(), area / 2, box));
                }
                else if (area < 0)
                {
                    holes.Add((ring.ToArray(), box));
                }
            }
        }

        // every hole to the smallest shell that holds it: the shells that have the hole in their envelope, then the position of the vertices of the
        // hole (a hole may touch its shell in a vertex)
        var index = new SpatialIndex<int>(shells.Select((shell, i) => (shell.Box, i)));
        var holesOf = new List<ReadOnlyMemory<Coordinate>>[shells.Count];
        bool uncertain = repair;   // set also by a hole that needed the tolerance, or that no shell holds: the rings may overlap by a rounding error
        foreach (var (ring, box) in holes)
        {
            int best = -1;
            foreach (int i in index.Query(box))
            {
                if ((best < 0 || shells[i].Area < shells[best].Area) && Holds(shells[i].Ring, ring, out bool tolerated))
                {
                    best = i;
                    uncertain |= tolerated;
                }
            }

            uncertain |= best < 0;

            if (best >= 0)
            {
                (holesOf[best] ??= []).Add(GeometryAccess.Restrict(ring, f));
            }
        }

        var parts = shells.Select((shell, i) => new PolygonPart(GeometryAccess.Restrict(shell.Ring, f), (holesOf[i] ?? []).ToArray())).ToArray();
        var polygon = f.CreatePolygon(parts, null);

        // Along a sliver of a triangle (three corners that lie on one line to within a rounding error) the rings of a hole and its shell may overlap by
        // that error; the structure of a polygon is read from the position of its rings, so such a polygon is made valid (only then, since the check
        // of a large polygon is not free)
        return uncertain && !polygon.IsValid() ? polygon.MakeValid(factory: f) : polygon;
    }

    /// <summary>Cuts a closed chain of vertex ids into pieces in which no vertex occurs twice: where it comes back to a vertex, the loop is a piece of its own.</summary>
    private static List<List<int>> SplitAtRepeatedVertices(List<int> chain)
    {
        var pieces = new List<List<int>>();
        var stack = new List<int>();
        var position = new Dictionary<int, int>();
        foreach (int id in chain)
        {
            if (position.TryGetValue(id, out int at))
            {
                var loop = stack.GetRange(at, stack.Count - at);
                pieces.Add(loop);
                for (int i = at + 1; i < stack.Count; i++)
                {
                    position.Remove(stack[i]);
                }

                stack.RemoveRange(at + 1, stack.Count - at - 1);
            }
            else
            {
                position[id] = stack.Count;
                stack.Add(id);
            }
        }

        pieces.Add(stack);
        return pieces;
    }

    /// <summary>
    /// The boundary edge that follows <paramref name="edge"/> in its ring: of the edges that start where it ends, the first one met when turning
    /// clockwise from the way back, which keeps the area of the class on the left and separates the rings at a vertex where two parts touch.
    /// −1 when the ring is closed.
    /// </summary>
    private static int NextBoundaryEdge(IReadOnlyList<Coordinate> vertices, int edge, List<int> from, List<int> to, Dictionary<int, List<int>> outgoing, bool[] used, int start)
    {
        int end = to[edge];
        if (!outgoing.TryGetValue(end, out var candidates))
        {
            return -1;
        }

        var here = vertices[end];
        var back = vertices[from[edge]];
        double bx = back.X - here.X, by = back.Y - here.Y;
        int best = -1;
        double bestAngle = double.PositiveInfinity;
        foreach (int c in candidates)
        {
            if (used[c] && c != start)
            {
                continue;
            }

            var target = vertices[to[c]];
            double dx = target.X - here.X, dy = target.Y - here.Y;
            double clockwise = -Math.Atan2(bx * dy - by * dx, bx * dx + by * dy);
            if (clockwise <= 0)
            {
                clockwise += 2 * Math.PI;
            }

            if (clockwise < bestAngle)
            {
                bestAngle = clockwise;
                best = c;
            }
        }

        return best == start || best < 0 ? -1 : best;
    }

    /// <summary>
    /// Whether a closed ring lies within another: no vertex outside, and one inside. A vertex on the other ring tells nothing, and neither does one that
    /// lies outside by less than a rounding error of the size of the ring (a hole that touches its shell along an edge, with a sliver of a triangle
    /// between them, has corners that are computed a hair off the shell).
    /// </summary>
    private static bool Holds(Coordinate[] shell, Coordinate[] ring, out bool tolerated)
    {
        tolerated = false;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var c in shell)
        {
            minX = Math.Min(minX, c.X);
            maxX = Math.Max(maxX, c.X);
            minY = Math.Min(minY, c.Y);
            maxY = Math.Max(maxY, c.Y);
        }

        double tolerance = 1e-9 * Math.Max(maxX - minX, maxY - minY);
        bool inside = false;
        for (int i = 0; i + 1 < ring.Length; i++)
        {
            switch (Rings.Locate(ring[i], shell))
            {
                case Location.Outside:
                    if (DistanceToRing(ring[i], shell) > tolerance)
                    {
                        return false;
                    }

                    tolerated = true;
                    break;
                case Location.Inside:
                    inside = true;
                    break;
            }
        }

        return inside;
    }

    private static double DistanceToRing(Coordinate p, Coordinate[] closed)
    {
        double best = double.PositiveInfinity;
        for (int i = 1; i < closed.Length; i++)
        {
            var a = closed[i - 1];
            var b = closed[i];
            double ex = b.X - a.X, ey = b.Y - a.Y, length2 = ex * ex + ey * ey;
            double t = length2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * ex + (p.Y - a.Y) * ey) / length2, 0, 1);
            double dx = a.X + t * ex - p.X, dy = a.Y + t * ey - p.Y;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }

        return best;
    }

    // ---------- line of sight ----------

    /// <summary>
    /// Whether one point can be seen from another over the surface (ADR 0041): the straight line from the eye, at the height of the surface plus
    /// <paramref name="observerHeight"/>, to the target, at the surface plus <paramref name="targetHeight"/>, must not pass below the surface. The
    /// surface is linear between the places where the line crosses an edge or a vertex, so it is enough to look there. A line that just touches the
    /// surface (a ridge exactly in the way, flat ground at height 0) is not blocked. Where the line leaves the surface (a notch of a polygon) there
    /// is no ground to block it.
    /// </summary>
    /// <param name="from">The observer.</param>
    /// <param name="to">The target.</param>
    /// <param name="observerHeight">The height of the eye above the surface (a person: 1.7).</param>
    /// <param name="targetHeight">The height of the target above the surface.</param>
    /// <returns><c>true</c> if the line is free; <c>false</c> if it is blocked, or if either point is outside the surface or has no height.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of a point or a height are not finite.</exception>
    public bool LineOfSight(Coordinate from, Coordinate to, double observerHeight = 0, double targetHeight = 0)
    {
        EnsurePoint(from);
        EnsurePoint(to);
        if (!double.IsFinite(observerHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(observerHeight), observerHeight, "The height must be finite.");
        }

        if (!double.IsFinite(targetHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), targetHeight, "The height must be finite.");
        }

        double z0 = InterpolateZ(from), z1 = InterpolateZ(to);
        if (double.IsNaN(z0) || double.IsNaN(z1))
        {
            return false;
        }

        if (from.X == to.X && from.Y == to.Y)
        {
            return true;
        }

        double eye = z0 + observerHeight, goal = z1 + targetHeight;
        double tolerance = 1e-9 * (1 + Math.Max(Math.Abs(eye), Math.Abs(goal)));
        foreach (var (t, z) in CutParameters(from, to))
        {
            if (t > 0 && t < 1 && !double.IsNaN(z) && z > eye + (goal - eye) * t + tolerance)
            {
                return false;
            }
        }

        return true;
    }
}
