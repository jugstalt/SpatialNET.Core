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
        var groups = new List<IPolygon2D>[limits.Count + 1];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = [];
        }

        var polygons = ToPolygons(f);
        for (int t = 0; t < TriangleCount; t++)
        {
            double slope = Slope(t);
            if (double.IsNaN(slope))
            {
                continue;
            }

            int cls = 0;
            while (cls < limits.Count && slope >= limits[cls])
            {
                cls++;
            }

            groups[cls].Add(polygons[t]);
        }

        return groups.Select(g => g.Count == 0 ? f.CreatePolygon([], null) : g.UnionAll(factory: f)).ToArray();
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
