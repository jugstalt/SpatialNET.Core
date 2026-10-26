using System.Numerics;
using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The cells of a Voronoi diagram (ADR 0036) from the Delaunay triangulation: the corners of the cell of a site are the circumcentres
/// of the triangles around it, so two cells share their corners bit for bit. The cells at the hull are unbounded; they are closed
/// far away, outside the clip rectangle, and cut with it. Where an edge of a cell is cut by the rectangle the crossing is computed
/// from the end that lies inside, so both cells that share the edge get the same point.
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
            var (x, y) = Centre(mesh.Points[mesh.Triangles[3 * t]], mesh.Points[mesh.Triangles[3 * t + 1]], mesh.Points[mesh.Triangles[3 * t + 2]]);
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

        var polygon = new List<Corner>();
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
            // every edge of the ring is a piece of the bisector of the site and one neighbour, remembered for the clipping
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                int next = i > 0 ? i - 1 : (closed ? chain.Count - 1 : -1);
                polygon.Add(new Corner(centres[chain[i]], next >= 0 ? (sites[v], sites[CommonNeighbour(mesh, chain[i], chain[next], v)]) : null));
            }

            if (!closed)
            {
                var n1 = OutwardNormal(mesh, DelaunayMesh.Prev(e0));   // the hull edge that ends at the vertex
                var n2 = OutwardNormal(mesh, e);                       // the hull edge that starts at it
                var c1 = centres[chain[0]];
                var ck = centres[chain[^1]];
                polygon.Add(new Corner(new Coordinate(c1.X + n1.X * far, c1.Y + n1.Y * far), null));
                polygon[^2] = polygon[^2] with { Edge = (sites[v], sites[mesh.Triangles[DelaunayMesh.Prev(e0)]]) };

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
                        polygon.Add(new Corner(new Coordinate(rcx + 1.5 * far * Math.Cos(angle), rcy + 1.5 * far * Math.Sin(angle)), null));
                    }
                }

                polygon.Add(new Corner(new Coordinate(ck.X + n2.X * far, ck.Y + n2.Y * far), (sites[v], sites[mesh.Triangles[DelaunayMesh.Next(e)]])));
            }

            cells[v] = Finish(polygon, rect);
        }

        return cells;
    }

    /// <summary>
    /// The circumcentre. For an almost flat triangle the formula divides a difference that is lost in rounding (the direction of a centre far
    /// away is wrong, or the result is infinite), so that is computed exactly with integers and rounded once. A centre beyond
    /// <see cref="Limit"/> is shortened, in the same direction.
    /// </summary>
    private static (double X, double Y) Centre(Coordinate a, Coordinate b, Coordinate c)
    {
        var (x, y) = DelaunayMesh.Circumcentre(a, b, c);
        double twiceArea = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
        double terms = Math.Abs((b.X - a.X) * (c.Y - a.Y)) + Math.Abs((b.Y - a.Y) * (c.X - a.X));
        if (!double.IsFinite(x) || !double.IsFinite(y) || twiceArea <= 1e-6 * terms)
        {
            (x, y) = ExactCircumcentre(a, b, c);
        }

        double m = Math.Max(Math.Abs(x), Math.Abs(y));
        if (m > Limit)
        {
            (x, y) = (x / m * Limit, y / m * Limit);
        }

        return (x, y);
    }

    private static (double X, double Y) ExactCircumcentre(Coordinate a, Coordinate b, Coordinate c)
    {
        // every number is an integer times 2^shift
        int shift = int.MaxValue;
        foreach (double v in new[] { a.X, a.Y, b.X, b.Y, c.X, c.Y })
        {
            if (v != 0)
            {
                shift = Math.Min(shift, Mantissa(v).Exponent);
            }
        }

        if (shift == int.MaxValue)
        {
            return (double.NaN, double.NaN);
        }

        BigInteger Units(double v)
        {
            var (m, e) = Mantissa(v);
            return v == 0 ? BigInteger.Zero : m << (e - shift);
        }

        BigInteger ax = Units(a.X), ay = Units(a.Y);
        BigInteger dx = Units(b.X) - ax, dy = Units(b.Y) - ay, ex = Units(c.X) - ax, ey = Units(c.Y) - ay;
        BigInteger bl = dx * dx + dy * dy, cl = ex * ex + ey * ey;
        BigInteger d = 2 * (dx * ey - dy * ex);
        if (d.IsZero)
        {
            return (double.NaN, double.NaN);
        }

        // the offset from a is (…)/d times the unit; 128 more bits keep the quotient exact enough for one rounding
        double Offset(BigInteger numerator) => (double)((numerator << 128) / d) * Math.ScaleB(1.0, shift - 128);
        double ox = Offset(ey * bl - dy * cl), oy = Offset(dx * cl - ex * bl);
        return (a.X + ox, a.Y + oy);
    }

    /// <summary>The value as an integer mantissa times a power of two.</summary>
    private static (BigInteger Mantissa, int Exponent) Mantissa(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        long mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        exponent = exponent == 0 ? -1074 : exponent - 1075;
        return (bits < 0 ? -mantissa : mantissa, exponent);
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

    /// <summary>A corner of a cell and the two sites whose bisector the edge to the next corner lies on (<c>null</c> for an edge that does not).</summary>
    private readonly record struct Corner(Coordinate Point, (Coordinate A, Coordinate B)? Edge);

    /// <summary>The vertex, other than <paramref name="v"/>, that two triangles around it have in common.</summary>
    private static int CommonNeighbour(DelaunayMesh mesh, int t1, int t2, int v)
    {
        for (int i = 0; i < 3; i++)
        {
            int w = mesh.Triangles[3 * t1 + i];
            if (w != v && (mesh.Triangles[3 * t2] == w || mesh.Triangles[3 * t2 + 1] == w || mesh.Triangles[3 * t2 + 2] == w))
            {
                return w;
            }
        }

        return v;
    }

    private static Coordinate[] Finish(List<Corner> polygon, Envelope2D rect)
    {
        var clipped = ClipToRectangle(polygon, rect);
        return clipped.Count >= 3 ? Oriented(clipped.Select(c => c.Point).ToList()) : [];
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

    /// <summary>
    /// Sutherland–Hodgman against the four sides in a fixed order. Where an edge of the diagram crosses a side, the point is computed from the
    /// bisector of the two sites (which both cells that share the edge know exactly), not from the ends of the edge: those can be far outside
    /// and have lost the position to rounding (the circumcentre of almost collinear sites at the hull is 1e17 away or more). An edge without a
    /// bisector (an arc of the closure) is cut from its end inside.
    /// </summary>
    private static List<Corner> ClipToRectangle(List<Corner> polygon, Envelope2D r)
    {
        var p = Clip(polygon, true, r.MinX, true);
        p = Clip(p, false, r.MinY, true);
        p = Clip(p, true, r.MaxX, false);
        return Clip(p, false, r.MaxY, false);
    }

    private static List<Corner> Clip(List<Corner> input, bool xAxis, double bound, bool keepGreater)
    {
        var output = new List<Corner>(input.Count + 2);
        int n = input.Count;
        double V(Coordinate c) => xAxis ? c.X : c.Y;
        bool Inside(Coordinate c) => keepGreater ? V(c) >= bound : V(c) <= bound;

        for (int i = 0; i < n; i++)
        {
            var cur = input[i];
            var prev = input[(i + n - 1) % n];
            bool curIn = Inside(cur.Point), prevIn = Inside(prev.Point);
            if (curIn != prevIn)
            {
                var crossing = Cross(prev, cur, curIn, xAxis, bound);
                // leaving: the edge goes on along the side, which is not an edge of the diagram; entering: the rest of the edge to cur
                output.Add(curIn ? new Corner(crossing, prev.Edge) : new Corner(crossing, null));
            }

            if (curIn)
            {
                output.Add(cur);
            }
        }

        return output;
    }

    private static Coordinate Cross(Corner prev, Corner cur, bool curIn, bool xAxis, double bound)
    {
        if (prev.Edge is { } edge)
        {
            var (p, q) = edge;
            double mx = (p.X + q.X) / 2, my = (p.Y + q.Y) / 2, dx = q.X - p.X, dy = q.Y - p.Y;
            if (xAxis && dy != 0)
            {
                return new Coordinate(bound, my - (bound - mx) * (dx / dy));
            }

            if (!xAxis && dx != 0)
            {
                return new Coordinate(mx - (bound - my) * (dy / dx), bound);
            }
        }

        // from the end inside: both cells that share the edge classify its ends alike, so they get the same point
        var (a, b) = curIn ? (cur.Point, prev.Point) : (prev.Point, cur.Point);
        double v0 = xAxis ? a.X : a.Y, v1 = xAxis ? b.X : b.Y;
        double t = (bound - v0) / (v1 - v0);
        return xAxis
            ? new Coordinate(bound, a.Y + t * (b.Y - a.Y))
            : new Coordinate(a.X + t * (b.X - a.X), bound);
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
