using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The Delaunay triangulation of distinct points (ADR 0029) as a half-edge mesh. Triangle <c>t</c> has the vertices
/// <c>Triangles[3t..3t+2]</c>, counter-clockwise, and the half-edge <c>e</c> runs from <c>Triangles[e]</c> to the next vertex of
/// its triangle; <c>HalfEdges[e]</c> is the opposite half-edge of the neighbour or −1 on the hull.
/// </summary>
/// <remarks>
/// The points are inserted in order of their distance from the circumcentre of a seed triangle, so that a new point lies
/// outside the hull of those before it (the sweep of Mapbox's Delaunator, here with exact predicates). A point that is not
/// outside (it lies on a hull edge or inside) is inserted by locating its triangle and splitting it. Every insertion restores
/// the Delaunay property by edge flips with the exact <see cref="InCircle"/> test; <see cref="Orientation"/> decides the
/// visibility and the location. Points on a common circle give an equally valid, deterministic triangulation.
/// </remarks>
internal sealed partial class DelaunayMesh
{
    private readonly Coordinate[] _points;
    private readonly int[] _triangles;
    private readonly int[] _halfEdges;
    private int _triangleCount;

    private readonly int[] _hullNext;
    private readonly int[] _hullPrev;
    private readonly int[] _hullEdge;     // the half-edge of the hull edge that starts at the vertex
    private int _hullStart;
    private int _hullSize;

    private readonly int[] _hash;
    private readonly int _hashSize;
    private double _centreX, _centreY;
    private int _lastTriangle;

    private readonly Stack<int> _stack = new();
    private readonly List<int> _chain = new();

    private DelaunayMesh(Coordinate[] points)
    {
        _points = points;
        int n = points.Length;
        _triangles = new int[Math.Max(2 * n - 2, 1) * 3];
        _halfEdges = new int[_triangles.Length];
        _hullNext = new int[n];
        _hullPrev = new int[n];
        _hullEdge = new int[n];
        Array.Fill(_hullNext, -1);
        Array.Fill(_hullPrev, -1);
        _hashSize = (int)Math.Ceiling(Math.Sqrt(n));
        _hash = new int[_hashSize];
        Array.Fill(_hash, -1);
    }

    private DelaunayMesh(Coordinate[] points, int[] triangles, int[] halfEdges)
    {
        _points = points;
        _triangles = triangles;
        _halfEdges = halfEdges;
        _triangleCount = triangles.Length / 3;
        _hullNext = [];
        _hullPrev = [];
        _hullEdge = new int[points.Length];   // Link writes into it
        _hash = [];
    }

    /// <summary>The distinct points; the vertex ids are indices into this array.</summary>
    public Coordinate[] Points => _points;

    public int TriangleCount => _triangleCount;

    public int[] Triangles => _triangles;

    public int[] HalfEdges => _halfEdges;

    /// <summary>The hull vertices, counter-clockwise (vertices in the middle of a straight hull edge included).</summary>
    public int[] Hull()
    {
        var hull = new List<int>(_hullSize);
        int v = _hullStart;
        do
        {
            hull.Add(v);
            v = _hullNext[v];
        }
        while (v != _hullStart);
        return hull.ToArray();
    }

    public static int Next(int edge) => edge % 3 == 2 ? edge - 2 : edge + 1;

    public static int Prev(int edge) => edge % 3 == 0 ? edge + 2 : edge - 1;

    /// <summary>
    /// Triangulates distinct points. <c>null</c> if there are fewer than three or all are collinear.
    /// </summary>
    /// <param name="points">Pairwise different points (by X and Y), all finite.</param>
    /// <param name="sortRadially">
    /// <c>false</c> inserts the points in the given order after the seed (for tests: most points then take the path for points on or
    /// inside the hull).
    /// </param>
    public static DelaunayMesh? Create(Coordinate[] points, bool sortRadially = true)
    {
        if (points.Length < 3)
        {
            return null;
        }

        var mesh = new DelaunayMesh(points);
        return mesh.Build(sortRadially) ? mesh : null;
    }

    private bool Build(bool sortRadially)
    {
        var p = _points;
        int n = p.Length;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var c in p)
        {
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
        }

        double midX = minX / 2 + maxX / 2, midY = minY / 2 + maxY / 2;

        // seed triangle: the point nearest to the middle, its nearest neighbour, and the point with the smallest circumcircle
        int i0 = Nearest(midX, midY, -1, -1);
        int i1 = Nearest(p[i0].X, p[i0].Y, i0, -1);
        int i2 = -1;
        double bestRadius = double.PositiveInfinity;
        for (int k = 0; k < n; k++)
        {
            if (k == i0 || k == i1 || Orientation.Orient2D(p[i0], p[i1], p[k]) == 0)
            {
                continue;
            }

            double r = CircumradiusSquared(p[i0], p[i1], p[k]);
            if (r < bestRadius || i2 == -1)
            {
                bestRadius = r;
                i2 = k;
            }
        }

        if (i2 == -1)
        {
            return false; // all collinear
        }

        if (Orientation.Orient2D(p[i0], p[i1], p[i2]) < 0)
        {
            (i1, i2) = (i2, i1);
        }

        (_centreX, _centreY) = Circumcentre(p[i0], p[i1], p[i2]);
        if (!double.IsFinite(_centreX) || !double.IsFinite(_centreY))
        {
            (_centreX, _centreY) = (midX, midY);
        }

        // order of the other points
        var ids = new List<int>(n - 3);
        for (int k = 0; k < n; k++)
        {
            if (k != i0 && k != i1 && k != i2)
            {
                ids.Add(k);
            }
        }

        var order = ids.ToArray();
        if (sortRadially)
        {
            var keys = new double[order.Length];
            for (int k = 0; k < order.Length; k++)
            {
                double dx = p[order[k]].X - _centreX, dy = p[order[k]].Y - _centreY;
                keys[k] = dx * dx + dy * dy;
            }

            Array.Sort(keys, order);
            for (int start = 0; start < order.Length;)
            {
                int end = start + 1;
                while (end < order.Length && keys[end] == keys[start]) end++;
                if (end - start > 1) Array.Sort(order, start, end - start); // equal distance: by index, for a deterministic result
                start = end;
            }
        }

        // the seed triangle is the whole mesh and the hull
        _triangles[0] = i0;
        _triangles[1] = i1;
        _triangles[2] = i2;
        _halfEdges[0] = _halfEdges[1] = _halfEdges[2] = -1;
        _triangleCount = 1;
        _hullNext[i0] = i1; _hullNext[i1] = i2; _hullNext[i2] = i0;
        _hullPrev[i1] = i0; _hullPrev[i2] = i1; _hullPrev[i0] = i2;
        _hullEdge[i0] = 0; _hullEdge[i1] = 1; _hullEdge[i2] = 2;
        _hullStart = i0;
        _hullSize = 3;
        _hash[HashKey(p[i0])] = i0;
        _hash[HashKey(p[i1])] = i1;
        _hash[HashKey(p[i2])] = i2;
        _lastTriangle = 0;

        foreach (int id in order)
        {
            if (!InsertOutsideHull(id))
            {
                InsertInsideOrOnHull(id);
            }
        }

        return true;
    }

    private int Nearest(double x, double y, int skip1, int skip2)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        for (int k = 0; k < _points.Length; k++)
        {
            if (k == skip1 || k == skip2)
            {
                continue;
            }

            double dx = _points[k].X - x, dy = _points[k].Y - y, d = dx * dx + dy * dy;
            if (d < bestDistance || best == -1)
            {
                bestDistance = d;
                best = k;
            }
        }

        return best;
    }

    private static double CircumradiusSquared(Coordinate a, Coordinate b, Coordinate c)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, ex = c.X - a.X, ey = c.Y - a.Y;
        double bl = dx * dx + dy * dy, cl = ex * ex + ey * ey;
        double d = 0.5 / (dx * ey - dy * ex);
        double x = (ey * bl - dy * cl) * d, y = (dx * cl - ex * bl) * d;
        double r = x * x + y * y;
        return double.IsNaN(r) ? double.PositiveInfinity : r;
    }

    internal static (double X, double Y) Circumcentre(Coordinate a, Coordinate b, Coordinate c)
    {
        // The differences from the origin lose what two close points say about the third: of (0, 0), (5e-20, 0) and (30, 46) seen from the
        // third, the first two are the same. So the origin is an end of the shortest edge, from which that edge is exact.
        double lab = Sq(b.X - a.X, b.Y - a.Y), lbc = Sq(c.X - b.X, c.Y - b.Y), lca = Sq(a.X - c.X, a.Y - c.Y);
        if (lbc < lab && lbc <= lca)
        {
            (a, b, c) = (b, c, a);
        }
        else if (lca < lab && lca < lbc)
        {
            (a, b, c) = (c, a, b);
        }

        double dx = b.X - a.X, dy = b.Y - a.Y, ex = c.X - a.X, ey = c.Y - a.Y;
        double bl = dx * dx + dy * dy, cl = ex * ex + ey * ey;
        double d = 0.5 / (dx * ey - dy * ex);
        return (a.X + (ey * bl - dy * cl) * d, a.Y + (dx * cl - ex * bl) * d);
    }

    private static double Sq(double dx, double dy) => dx * dx + dy * dy;

    private int HashKey(Coordinate c)
    {
        double dx = c.X - _centreX, dy = c.Y - _centreY;
        double p = dx / (Math.Abs(dx) + Math.Abs(dy));
        double angle = double.IsNaN(p) ? 0 : (dy > 0 ? 3 - p : 1 + p) / 4; // 0..1, increasing with the angle
        return Math.Clamp((int)(angle * _hashSize), 0, _hashSize - 1);
    }

    private bool Visible(int a, int b, Coordinate p) => Orientation.Orient2D(_points[a], _points[b], p) < 0;

    // ---------- a point outside the hull: a fan of triangles on the visible hull edges ----------

    private bool InsertOutsideHull(int pi)
    {
        var p = _points[pi];

        int start = -1;
        int key = HashKey(p);
        for (int j = 0; j < _hashSize; j++)
        {
            int s = _hash[(key + j) % _hashSize];
            if (s != -1 && _hullNext[s] != -1)
            {
                start = s;
                break;
            }
        }

        if (start == -1)
        {
            start = _hullStart;
        }

        // any visible hull edge
        int first = -1;
        int e = _hullPrev[start];
        for (int step = 0; step < _hullSize; step++)
        {
            if (Visible(e, _hullNext[e], p))
            {
                first = e;
                break;
            }

            e = _hullNext[e];
        }

        if (first == -1)
        {
            return false;
        }

        // the visible edges form one run of the hull: its start ...
        int s0 = first;
        for (int step = 0; step < _hullSize; step++)
        {
            int before = _hullPrev[s0];
            if (!Visible(before, s0, p)) break;
            s0 = before;
        }

        // ... and its vertices
        _chain.Clear();
        int v = s0;
        for (int step = 0; step < _hullSize && Visible(v, _hullNext[v], p); step++)
        {
            _chain.Add(v);
            v = _hullNext[v];
        }

        int end = v;
        int count = _chain.Count;
        var created = new int[count];
        int previous = -1;
        for (int i = 0; i < count; i++)
        {
            int a = _chain[i];
            int b = i + 1 < count ? _chain[i + 1] : end;
            int t = _triangleCount * 3;
            _triangleCount++;
            _triangles[t] = a;
            _triangles[t + 1] = pi;
            _triangles[t + 2] = b;
            _halfEdges[t] = previous == -1 ? -1 : previous + 1; // a → p meets p → a of the triangle before
            if (previous != -1) _halfEdges[previous + 1] = t;
            _halfEdges[t + 1] = -1;
            int outer = _hullEdge[a];                           // b → a meets the old hull edge a → b
            _halfEdges[t + 2] = outer;
            _halfEdges[outer] = t + 2;
            created[i] = t;
            previous = t;
        }

        // the hull: ... s → p → end ...
        _hullNext[s0] = pi;
        _hullPrev[pi] = s0;
        _hullNext[pi] = end;
        _hullPrev[end] = pi;
        _hullEdge[s0] = created[0];
        _hullEdge[pi] = created[count - 1] + 1;
        for (int i = 1; i < count; i++)
        {
            _hullNext[_chain[i]] = -1;
            _hullPrev[_chain[i]] = -1;
        }

        _hullSize += 2 - count;
        _hullStart = pi;
        _hash[HashKey(p)] = pi;
        _hash[HashKey(_points[s0])] = s0;
        _lastTriangle = created[count - 1] / 3;

        foreach (int t in created)
        {
            Legalize(t + 2);
        }

        return true;
    }

    // ---------- a point on the hull or inside: locate the triangle and split it ----------

    private void InsertInsideOrOnHull(int pi)
    {
        var p = _points[pi];
        int tri = Locate(p, out int zeros, out int zeroEdge);
        if (zeros >= 2)
        {
            return; // equal to a vertex: the points are distinct, so this cannot happen
        }

        int b = tri * 3;
        if (zeros == 0)
        {
            SplitInside(pi, b);
        }
        else
        {
            SplitEdge(pi, b + zeroEdge);
        }
    }

    /// <summary>The triangle that contains p (inside or on its border); <paramref name="zeroEdge"/> says on which edge, if on one.</summary>
    private int Locate(Coordinate p, out int zeros, out int zeroEdge)
    {
        int tri = _lastTriangle;
        int limit = 3 * _triangleCount + 10;
        for (int step = 0; step < limit; step++)
        {
            if (TryClassify(tri, p, out zeros, out zeroEdge, out int across))
            {
                return tri;
            }

            if (across == -1)
            {
                break; // outside the hull: cannot be here, search everything instead
            }

            tri = across / 3;
        }

        // the walk did not end (it cannot cycle in a Delaunay triangulation, but exact degeneracy is not worth a risk)
        for (int t = 0; t < _triangleCount; t++)
        {
            if (TryClassify(t, p, out zeros, out zeroEdge, out _))
            {
                return t;
            }
        }

        throw new InvalidOperationException("The point lies outside the triangulation.");
    }

    private bool TryClassify(int tri, Coordinate p, out int zeros, out int zeroEdge, out int across)
    {
        int b = tri * 3;
        zeros = 0;
        zeroEdge = 0;
        across = -2;
        for (int k = 0; k < 3; k++)
        {
            int o = Orientation.Orient2D(_points[_triangles[b + k]], _points[_triangles[b + (k + 1) % 3]], p);
            if (o < 0)
            {
                across = _halfEdges[b + k];
                return false;
            }

            if (o == 0)
            {
                zeros++;
                zeroEdge = k;
            }
        }

        return true;
    }

    private void SplitInside(int pi, int b)
    {
        int a = _triangles[b], bv = _triangles[b + 1], c = _triangles[b + 2];
        int o0 = _halfEdges[b], o1 = _halfEdges[b + 1], o2 = _halfEdges[b + 2];

        int t2 = _triangleCount * 3, t3 = t2 + 3;
        _triangleCount += 2;
        Set(b, a, bv, pi);        // (A, B, p)
        Set(t2, bv, c, pi);       // (B, C, p)
        Set(t3, c, a, pi);        // (C, A, p)

        Link(b, o0, a);           // A → B stays
        Link(t2, o1, bv);         // B → C
        Link(t3, o2, c);          // C → A
        Pair(b + 1, t2 + 2);      // B → p  with  p → B
        Pair(b + 2, t3 + 1);      // p → A  with  A → p
        Pair(t2 + 1, t3 + 2);     // C → p  with  p → C

        _lastTriangle = _triangleCount - 1;
        Legalize(b);
        Legalize(t2);
        Legalize(t3);
    }

    private void SplitEdge(int pi, int e)
    {
        int h = _halfEdges[e];
        int t = e - e % 3;
        int p0 = _triangles[e], p1 = _triangles[Next(e)], p2 = _triangles[Prev(e)];
        int oT1 = _halfEdges[Next(e)];   // P1 → P2
        int oT2 = _halfEdges[Prev(e)];   // P2 → P0

        if (h == -1)
        {
            // on a hull edge: two triangles, p joins the hull between P0 and P1
            int x = _triangleCount * 3;
            _triangleCount++;
            Set(t, p0, pi, p2);          // (P0, p, P2)
            Set(x, pi, p1, p2);          // (p, P1, P2)
            _halfEdges[t] = -1;          // P0 → p: hull
            _halfEdges[x] = -1;          // p → P1: hull
            Pair(t + 1, x + 2);          // p → P2  with  P2 → p
            Link(t + 2, oT2, p2);        // P2 → P0
            Link(x + 1, oT1, p1);        // P1 → P2
            _hullEdge[p0] = t;
            _hullEdge[pi] = x;
            _hullNext[p0] = pi;
            _hullPrev[pi] = p0;
            _hullNext[pi] = p1;
            _hullPrev[p1] = pi;
            _hullSize++;
            _hash[HashKey(_points[pi])] = pi;
            _lastTriangle = x / 3;
            Legalize(t + 2);
            Legalize(x + 1);
            return;
        }

        int nb = h - h % 3;
        int d = _triangles[Prev(h)];
        int oN1 = _halfEdges[Next(h)];   // P0 → D
        int oN2 = _halfEdges[Prev(h)];   // D → P1

        int tx = _triangleCount * 3, ny = tx + 3;
        _triangleCount += 2;
        Set(t, p0, pi, p2);              // (P0, p, P2)
        Set(tx, pi, p1, p2);             // (p, P1, P2)
        Set(nb, p1, pi, d);              // (P1, p, D)
        Set(ny, pi, p0, d);              // (p, P0, D)

        Pair(t, ny);                     // P0 → p  with  p → P0
        Pair(t + 1, tx + 2);             // p → P2  with  P2 → p
        Pair(tx, nb);                    // p → P1  with  P1 → p
        Pair(nb + 1, ny + 2);            // p → D  with  D → p
        Link(t + 2, oT2, p2);            // P2 → P0
        Link(tx + 1, oT1, p1);           // P1 → P2
        Link(nb + 2, oN2, d);            // D → P1
        Link(ny + 1, oN1, p0);           // P0 → D

        _lastTriangle = ny / 3;
        Legalize(t + 2);
        Legalize(tx + 1);
        Legalize(nb + 2);
        Legalize(ny + 1);
    }

    private void Set(int b, int v0, int v1, int v2)
    {
        _triangles[b] = v0;
        _triangles[b + 1] = v1;
        _triangles[b + 2] = v2;
    }

    private void Pair(int x, int y)
    {
        _halfEdges[x] = y;
        _halfEdges[y] = x;
    }

    /// <summary>The half-edge <paramref name="edge"/> (starting at <paramref name="start"/>) meets <paramref name="outer"/>, or is a hull edge.</summary>
    private void Link(int edge, int outer, int start)
    {
        _halfEdges[edge] = outer;
        if (outer != -1)
        {
            _halfEdges[outer] = edge;
        }
        else
        {
            _hullEdge[start] = edge;
        }
    }

    // ---------- the Delaunay property: flip every edge opposite the new point whose neighbour's apex lies in the circumcircle ----------

    /// <summary>
    /// Legalizes the edge <paramref name="start"/>, which lies opposite the newly inserted point in its triangle, and every edge that a
    /// flip makes opposite to that point.
    /// </summary>
    private void Legalize(int start)
    {
        _stack.Push(start);
        while (_stack.Count > 0)
        {
            int a = _stack.Pop();
            int b = _halfEdges[a];
            if (b == -1)
            {
                continue;
            }

            int a1 = Next(a), a2 = Prev(a), b1 = Next(b), b2 = Prev(b);
            int u = _triangles[a], v = _triangles[a1], pa = _triangles[a2], pb = _triangles[b2];
            if (InCircle.Sign(_points[u], _points[v], _points[pa], _points[pb]) <= 0)
            {
                continue;
            }

            Flip(a, b);
            _stack.Push(a);
            _stack.Push(b);
        }
    }

    /// <summary>
    /// Flips the edge between the half-edges <paramref name="a"/> and its opposite <paramref name="b"/>: the triangles (u, v, pa) and
    /// (v, u, pb) become (u, pb, pa) and (pb, v, pa). The new diagonal is <c>a1</c> (pb → pa) and <c>b2</c> (pa → pb).
    /// </summary>
    private void Flip(int a, int b)
    {
        int a1 = Next(a), a2 = Prev(a), b1 = Next(b), b2 = Prev(b);
        int u = _triangles[a], v = _triangles[a1], pa = _triangles[a2], pb = _triangles[b2];
        int oA1 = _halfEdges[a1], oB1 = _halfEdges[b1], oB2 = _halfEdges[b2];
        _triangles[a] = u;
        _triangles[a1] = pb;
        _triangles[a2] = pa;
        _triangles[b] = pb;
        _triangles[b1] = v;
        _triangles[b2] = pa;

        Link(a, oB1, u);            // u → pb
        Pair(a1, b2);               // pb → pa  with  pa → pb
        Link(b, oB2, pb);           // pb → v
        Link(b1, oA1, v);           // v → pa
        // pa → u keeps its place and its neighbour

        if (_vertexEdge is not null)
        {
            _vertexEdge[u] = a;
            _vertexEdge[pb] = b;
            _vertexEdge[v] = b1;
            _vertexEdge[pa] = a2;
        }
    }
}
