using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The cells of a Voronoi diagram (ADR 0036) from the Delaunay triangulation: the corners of the cell of a site are the circumcentres
/// of the triangles around it, so two cells share their corners bit for bit. The cells at the hull are unbounded; they are closed
/// far away, outside the clip rectangle, and cut with it. Where an edge of a cell is cut by the rectangle the crossing is computed
/// from the two end points in a fixed order, so both cells that share the edge get the same point.
/// </summary>
internal static class VoronoiBuilder
{
    private const double Limit = 1e100;   // circumcentres of slivers can be far away; they are only clamped to stay finite

    /// <summary>
    /// The cell of every site as an open ring, counter-clockwise, cut to the rectangle; an empty array for a cell that vanishes.
    /// </summary>
    /// <param name="sites">The distinct sites; the result is in the same order.</param>
    /// <param name="mesh">The Delaunay triangulation of the sites, <c>null</c> for fewer than three or collinear sites.</param>
    /// <param name="rect">The rectangle; positive width and height.</param>
    public static Coordinate[][] Cells(Coordinate[] sites, DelaunayMesh? mesh, Envelope2D rect)
    {
        var cells = new Coordinate[sites.Length][];
        if (mesh is null)
        {
            DegenerateCells(sites, rect, cells);
            return cells;
        }

        int triangles = mesh.TriangleCount;
        var centres = new Coordinate[triangles];
        double rcx = (rect.MinX + rect.MaxX) / 2, rcy = (rect.MinY + rect.MaxY) / 2;
        double reach = 0;
        for (int t = 0; t < triangles; t++)
        {
            var (x, y) = DelaunayMesh.Circumcentre(mesh.Points[mesh.Triangles[3 * t]], mesh.Points[mesh.Triangles[3 * t + 1]], mesh.Points[mesh.Triangles[3 * t + 2]]);
            x = double.IsNaN(x) ? rcx : Math.Clamp(x, -Limit, Limit);
            y = double.IsNaN(y) ? rcy : Math.Clamp(y, -Limit, Limit);
            centres[t] = new Coordinate(x, y);
            reach = Math.Max(reach, Math.Max(Math.Abs(x - rcx), Math.Abs(y - rcy)));
        }

        double far = 4 * (Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) + 2 * reach) + 1;

        // for every vertex an outgoing half-edge: on the hull the one where the walk around the vertex starts
        var start = new int[sites.Length];
        Array.Fill(start, -1);
        var halfEdges = mesh.HalfEdges;
        var tri = mesh.Triangles;
        for (int e = 0; e < triangles * 3; e++)
        {
            int v = tri[e];
            if (start[v] == -1 || halfEdges[DelaunayMesh.Prev(e)] == -1)
            {
                start[v] = e;
            }
        }

        var polygon = new List<Coordinate>();
        for (int v = 0; v < sites.Length; v++)
        {
            polygon.Clear();
            int e0 = start[v];
            var chain = new List<int>();
            int e = e0;
            bool closed = false;
            while (true)
            {
                chain.Add(e / 3);
                int h = halfEdges[e];
                if (h == -1)
                {
                    break;
                }

                e = DelaunayMesh.Next(h);
                if (e == e0)
                {
                    closed = true;
                    break;
                }
            }

            // the chain runs clockwise around the vertex; the ring counter-clockwise
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                polygon.Add(centres[chain[i]]);
            }

            if (!closed)
            {
                var n1 = OutwardNormal(mesh, DelaunayMesh.Prev(e0));   // the hull edge that ends at the vertex
                var n2 = OutwardNormal(mesh, e);                       // the hull edge that starts at it
                var c1 = centres[chain[0]];
                var ck = centres[chain[^1]];
                polygon.Add(new Coordinate(c1.X + n1.X * far, c1.Y + n1.Y * far));

                double theta = Math.Atan2(n1.Y, n1.X);
                double psi = Math.Atan2(n1.X * n2.Y - n1.Y * n2.X, n1.X * n2.X + n1.Y * n2.Y);
                if (psi < 0)
                {
                    psi += 2 * Math.PI;
                }

                if (psi > 1e-12 && psi < 2 * Math.PI - 1e-12)
                {
                    int steps = (int)Math.Ceiling(psi / (Math.PI / 3));
                    for (int j = 1; j < steps; j++)
                    {
                        double angle = theta + j * psi / steps;
                        polygon.Add(new Coordinate(rcx + 1.5 * far * Math.Cos(angle), rcy + 1.5 * far * Math.Sin(angle)));
                    }
                }

                polygon.Add(new Coordinate(ck.X + n2.X * far, ck.Y + n2.Y * far));
            }

            cells[v] = Finish(polygon, rect);
        }

        return cells;
    }

    /// <summary>The unit normal of a hull half-edge that points away from the triangulation (to the right of the edge).</summary>
    private static (double X, double Y) OutwardNormal(DelaunayMesh mesh, int edge)
    {
        var a = mesh.Points[mesh.Triangles[edge]];
        var b = mesh.Points[mesh.Triangles[DelaunayMesh.Next(edge)]];
        double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
        return (dy / length, -dx / length);
    }

    /// <summary>One site, two sites or collinear sites: every cell is the rectangle cut by the bisectors with its neighbours along the line.</summary>
    private static void DegenerateCells(Coordinate[] sites, Envelope2D rect, Coordinate[][] cells)
    {
        var order = Enumerable.Range(0, sites.Length).OrderBy(i => sites[i].X).ThenBy(i => sites[i].Y).ToArray();
        for (int k = 0; k < order.Length; k++)
        {
            var polygon = new List<Coordinate> { new(rect.MinX, rect.MinY), new(rect.MaxX, rect.MinY), new(rect.MaxX, rect.MaxY), new(rect.MinX, rect.MaxY) };
            var site = sites[order[k]];
            if (k > 0)
            {
                polygon = CutByBisector(polygon, site, sites[order[k - 1]]);
            }

            if (k + 1 < order.Length)
            {
                polygon = CutByBisector(polygon, site, sites[order[k + 1]]);
            }

            cells[order[k]] = polygon.Count >= 3 ? Oriented(polygon) : [];
        }
    }

    /// <summary>Keeps what is nearer to <paramref name="site"/> than to <paramref name="other"/>.</summary>
    private static List<Coordinate> CutByBisector(List<Coordinate> polygon, Coordinate site, Coordinate other)
    {
        double mx = (site.X + other.X) / 2, my = (site.Y + other.Y) / 2, dx = other.X - site.X, dy = other.Y - site.Y;
        double F(Coordinate p) => (p.X - mx) * dx + (p.Y - my) * dy;

        var output = new List<Coordinate>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var cur = polygon[i];
            var prev = polygon[(i + polygon.Count - 1) % polygon.Count];
            double fc = F(cur), fp = F(prev);
            bool curIn = fc <= 0, prevIn = fp <= 0;
            if (curIn != prevIn)
            {
                // the same point for both sites that share the bisector: the end points in a fixed order, the sign of F does not matter
                var (a, b, fa, fb) = Compare(prev, cur) <= 0 ? (prev, cur, fp, fc) : (cur, prev, fc, fp);
                double t = fa / (fa - fb);
                output.Add(new Coordinate(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
            }

            if (curIn)
            {
                output.Add(cur);
            }
        }

        return output;
    }

    private static Coordinate[] Finish(List<Coordinate> polygon, Envelope2D rect)
    {
        var clipped = ClipToRectangle(polygon, rect);
        return clipped.Count >= 3 ? Oriented(clipped) : [];
    }

    private static Coordinate[] Oriented(List<Coordinate> ring)
    {
        var array = ring.ToArray();
        double area = 0;
        for (int i = 0; i < array.Length; i++)
        {
            var a = array[i];
            var b = array[(i + 1) % array.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        if (area < 0)
        {
            Array.Reverse(array);
        }

        return array;
    }

    private static int Compare(Coordinate a, Coordinate b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);

    /// <summary>Sutherland–Hodgman against the four sides in a fixed order; the crossing of an edge does not depend on its direction.</summary>
    private static List<Coordinate> ClipToRectangle(List<Coordinate> polygon, Envelope2D r)
    {
        var p = Clip(polygon, true, r.MinX, true);
        p = Clip(p, false, r.MinY, true);
        p = Clip(p, true, r.MaxX, false);
        return Clip(p, false, r.MaxY, false);
    }

    private static List<Coordinate> Clip(List<Coordinate> input, bool xAxis, double bound, bool keepGreater)
    {
        var output = new List<Coordinate>(input.Count + 2);
        int n = input.Count;
        double V(Coordinate c) => xAxis ? c.X : c.Y;
        bool Inside(Coordinate c) => keepGreater ? V(c) >= bound : V(c) <= bound;

        for (int i = 0; i < n; i++)
        {
            var cur = input[i];
            var prev = input[(i + n - 1) % n];
            bool curIn = Inside(cur), prevIn = Inside(prev);
            if (curIn != prevIn)
            {
                var (a, b) = Compare(prev, cur) <= 0 ? (prev, cur) : (cur, prev);
                double t = (bound - V(a)) / (V(b) - V(a));
                output.Add(xAxis
                    ? new Coordinate(bound, a.Y + t * (b.Y - a.Y))
                    : new Coordinate(a.X + t * (b.X - a.X), bound));
            }

            if (curIn)
            {
                output.Add(cur);
            }
        }

        return output;
    }

    /// <summary>
    /// Rounds a convex ring to the grid, drops repeated vertices and checks that it is still convex and has an area.
    /// <c>null</c> if it is not (a cell of almost cocircular sites can collapse or fold at the size of the grid).
    /// </summary>
    public static Coordinate[]? Round(Coordinate[] ring, GridModel grid)
    {
        var rounded = new List<Coordinate>(ring.Length);
        foreach (var c in ring)
        {
            var r = new Coordinate(grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(c.X))), grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(c.Y))));
            if (rounded.Count == 0 || rounded[^1].X != r.X || rounded[^1].Y != r.Y)
            {
                rounded.Add(r);
            }
        }

        while (rounded.Count > 1 && rounded[0].X == rounded[^1].X && rounded[0].Y == rounded[^1].Y)
        {
            rounded.RemoveAt(rounded.Count - 1);
        }

        if (rounded.Count < 3)
        {
            return null;
        }

        for (int i = 0; i < rounded.Count; i++)
        {
            if (Orientation.Orient2D(rounded[i], rounded[(i + 1) % rounded.Count], rounded[(i + 2) % rounded.Count]) < 0)
            {
                return null;
            }
        }

        double area = 0;
        for (int i = 0; i < rounded.Count; i++)
        {
            var a = rounded[i];
            var b = rounded[(i + 1) % rounded.Count];
            area += (a.X - rounded[0].X) * (b.Y - rounded[0].Y) - (b.X - rounded[0].X) * (a.Y - rounded[0].Y);
        }

        return area > 0 ? rounded.ToArray() : null;
    }

    /// <summary>
    /// The edges between neighbouring cells (not the edges on the rectangle), each once, from the unrounded cells: two cells share the
    /// vertices of their common edge bit for bit.
    /// </summary>
    public static List<(Coordinate A, Coordinate B)> Edges(Coordinate[][] cells, Envelope2D rect)
    {
        var seen = new HashSet<(double, double, double, double)>();
        var edges = new List<(Coordinate, Coordinate)>();
        foreach (var ring in cells)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                if ((a.X == rect.MinX && b.X == rect.MinX) || (a.X == rect.MaxX && b.X == rect.MaxX)
                    || (a.Y == rect.MinY && b.Y == rect.MinY) || (a.Y == rect.MaxY && b.Y == rect.MaxY))
                {
                    continue;   // on the rectangle: not an edge of the diagram
                }

                (a, b) = Compare(a, b) <= 0 ? (a, b) : (b, a);
                if (seen.Add((a.X, a.Y, b.X, b.Y)))
                {
                    edges.Add((a, b));
                }
            }
        }

        return edges;
    }
}
