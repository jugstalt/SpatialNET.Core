using SpatialNET.Internal.Noding;

namespace SpatialNET;

// Height bands (ADR 0042): the areas between two heights of the surface, the filled version of the contour lines.
public sealed partial class Triangulation
{
    /// <summary>
    /// The areas of the surface between heights (ADR 0042): the surface is cut along the contour lines of the given heights, and the pieces between two
    /// of them are united to one polygon per band. Band 0 is everything below the first height, band <c>i</c> from height <c>i − 1</c> up to (not
    /// including) height <c>i</c>, and the last band everything from the last height up; so there is one more band than there are heights, and a band
    /// without area is an empty polygon. The bands tile the surface, share their boundaries along the contour lines exactly, and the vertices on a
    /// contour line have the height as Z. Triangles with a vertex without Z belong to no band.
    /// </summary>
    /// <remarks>
    /// Over a triangle the surface is a plane, so a band is a convex piece of it, and the cut points on an edge are computed from the two vertices of
    /// the edge in a fixed order: the two triangles that share the edge get the same point to the last bit, and the pieces fit without a gap and without
    /// an overlay. A triangle that is flat at exactly the height of a contour line belongs to the band above it.
    /// </remarks>
    /// <param name="levels">The heights, strictly ascending.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The polygons, one per band.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="levels"/> are not finite or not strictly ascending.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IReadOnlyList<IPolygon2D> ContourBands(IReadOnlyList<double> levels, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(levels);
        for (int i = 0; i < levels.Count; i++)
        {
            if (!double.IsFinite(levels[i]) || (i > 0 && levels[i] <= levels[i - 1]))
            {
                throw new ArgumentException("The heights must be finite and strictly ascending.", nameof(levels));
            }
        }

        var f = GeometryFactory.Resolve(factory);
        int bands = levels.Count + 1;
        var vertices = new List<Coordinate>[bands];
        var ids = new Dictionary<(double, double), int>[bands];
        var edges = new Dictionary<(int, int), int>[bands];
        for (int k = 0; k < bands; k++)
        {
            vertices[k] = [];
            ids[k] = [];
            edges[k] = [];
        }

        var piece = new List<Corner>(8);
        var cut = new List<Corner>(8);
        for (int t = 0; t < TriangleCount; t++)
        {
            int ia = _triangles[3 * t], ib = _triangles[3 * t + 1], ic = _triangles[3 * t + 2];
            var a = _vertices[ia];
            var b = _vertices[ib];
            var c = _vertices[ic];
            if (!a.HasZ || !b.HasZ || !c.HasZ)
            {
                continue;
            }

            int[] triangleIds = [ia, ib, ic];
            int first = BandOf(levels, Math.Min(a.Z, Math.Min(b.Z, c.Z))), last = BandOf(levels, Math.Max(a.Z, Math.Max(b.Z, c.Z)));
            for (int k = first; k <= last; k++)
            {
                // the original triangle every time, so that the cut points on its edges come from the whole edge
                piece.Clear();
                piece.Add(new Corner(a.X, a.Y, a.Z, 0b101));   // the vertex j lies on the edges j and j − 1
                piece.Add(new Corner(b.X, b.Y, b.Z, 0b011));
                piece.Add(new Corner(c.X, c.Y, c.Z, 0b110));
                if (k > 0)
                {
                    ClipHeight(piece, cut, levels[k - 1], keepAbove: true, triangleIds);
                    (piece, cut) = (cut, piece);
                }

                if (k < levels.Count && piece.Count >= 3)
                {
                    ClipHeight(piece, cut, levels[k], keepAbove: false, triangleIds);
                    (piece, cut) = (cut, piece);
                }

                if (piece.Count < 3 || (k < levels.Count && piece.All(p => p.Z == levels[k])))
                {
                    continue;   // nothing left, or flat at the upper height: that belongs to the band above
                }

                // dropped only if it is exactly degenerate (all its corners on one line): a sliver of a few units in the last place still has its
                // edges, which the pieces next to it need to cancel against; a test of the area in floating point would get its sign wrong
                if (IsDegenerate(piece))
                {
                    continue;
                }

                int previous = Index(vertices[k], ids[k], piece[^1]);
                foreach (var corner in piece)
                {
                    int current = Index(vertices[k], ids[k], corner);
                    if (current != previous)
                    {
                        edges[k][(previous, current)] = edges[k].GetValueOrDefault((previous, current)) + 1;
                    }

                    previous = current;
                }
            }
        }

        var result = new IPolygon2D[bands];
        for (int k = 0; k < bands; k++)
        {
            // an edge that the neighbouring piece has in the opposite direction is inside the band
            var from = new List<int>();
            var to = new List<int>();
            foreach (var ((u, v), count) in edges[k])
            {
                int net = count - edges[k].GetValueOrDefault((v, u));
                for (int i = 0; i < net; i++)
                {
                    from.Add(u);
                    to.Add(v);
                }
            }

            result[k] = from.Count == 0 ? f.CreatePolygon([], null) : AreaFromEdges(vertices[k], from, to, f, repair: HasSliver);
        }

        return result;
    }

    /// <summary>A corner of a piece of a triangle: where it is, and on which edges of the triangle it lies (a bit for each of the three).</summary>
    private readonly record struct Corner(double X, double Y, double Z, int Edges);

    private static bool IsDegenerate(List<Corner> piece)
    {
        var origin = new Coordinate(piece[0].X, piece[0].Y);
        for (int i = 1; i + 1 < piece.Count; i++)
        {
            if (Orientation.Orient2D(origin, new Coordinate(piece[i].X, piece[i].Y), new Coordinate(piece[i + 1].X, piece[i + 1].Y)) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int BandOf(IReadOnlyList<double> levels, double z)
    {
        int band = 0;
        while (band < levels.Count && z >= levels[band])
        {
            band++;
        }

        return band;
    }

    private static int Index(List<Coordinate> vertices, Dictionary<(double, double), int> ids, Corner corner)
    {
        if (!ids.TryGetValue((corner.X, corner.Y), out int id))
        {
            id = vertices.Count;
            ids[(corner.X, corner.Y)] = id;
            vertices.Add(new Coordinate(corner.X, corner.Y, corner.Z));
        }

        return id;
    }

    /// <summary>Keeps the part of a convex piece at or above (or at or below) a height.</summary>
    private void ClipHeight(List<Corner> piece, List<Corner> output, double level, bool keepAbove, int[] triangleIds)
    {
        output.Clear();
        for (int i = 0; i < piece.Count; i++)
        {
            var cur = piece[i];
            var prev = piece[(i + piece.Count - 1) % piece.Count];
            bool curIn = keepAbove ? cur.Z >= level : cur.Z <= level;
            bool prevIn = keepAbove ? prev.Z >= level : prev.Z <= level;
            if (curIn != prevIn)
            {
                output.Add(Crossing(prev, cur, level, triangleIds));
            }

            if (curIn)
            {
                output.Add(cur);
            }
        }
    }

    /// <summary>
    /// Where the way from one corner to another crosses a height. Along an edge of the triangle it is computed from the whole edge, from the vertex
    /// with the smaller id to the other, so that the triangle on the other side of the edge gets the same point; elsewhere it is inside the triangle.
    /// </summary>
    private Corner Crossing(Corner p, Corner q, double level, int[] triangleIds)
    {
        int common = p.Edges & q.Edges;
        if (common != 0)
        {
            int j = (common & 1) != 0 ? 0 : (common & 2) != 0 ? 1 : 2;
            int u = triangleIds[j], v = triangleIds[(j + 1) % 3];
            if (u > v)
            {
                (u, v) = (v, u);
            }

            var a = _vertices[u];
            var b = _vertices[v];
            // a vertex that lies exactly on the height is the point itself: the formula would miss it by a rounding error, and the vertex and its
            // near copy would make a sliver
            if (a.Z == level)
            {
                return new Corner(a.X, a.Y, level, 1 << j);
            }

            if (b.Z == level)
            {
                return new Corner(b.X, b.Y, level, 1 << j);
            }

            double s = (level - a.Z) / (b.Z - a.Z);
            return new Corner(a.X + s * (b.X - a.X), a.Y + s * (b.Y - a.Y), level, 1 << j);
        }

        if (p.Z == level)
        {
            return p with { Edges = 0 };
        }

        if (q.Z == level)
        {
            return q with { Edges = 0 };
        }

        double t = (level - p.Z) / (q.Z - p.Z);
        return new Corner(p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y), level, 0);
    }
}
