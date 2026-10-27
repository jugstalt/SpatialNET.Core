using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// A mutable triangulation for Delaunay refinement (ADR 0045): points are inserted one at a time into a triangle or onto an edge, and the edges around
/// them are made Delaunay again by flips. Edges of the border and the constraints are <b>protected</b>: they are never flipped, only split.
/// </summary>
/// <remarks>
/// Triangles are never removed (an insertion divides a triangle and keeps its index for one of the parts), so an index stays valid and a version number
/// tells whether a triangle has changed. Every decision uses the exact <see cref="Orientation"/> and <see cref="InCircle"/>. The structure is the one of
/// <c>Triangulation</c>: three vertex ids per triangle, counter-clockwise, and the neighbour across the edge from vertex <c>k</c> to <c>k + 1</c>.
/// </remarks>
internal sealed class RefinementMesh
{
    private readonly List<Coordinate> _points;
    private int[] _v;
    private int[] _n;
    private bool[] _protected;   // the edge is on the border or a constraint: split it, never flip it
    private bool[] _user;        // the edge is a constraint that the result reports
    private int[] _stamp;
    private int[] _vertexTriangle;
    private int _count;

    public RefinementMesh(IReadOnlyList<Coordinate> vertices, int[] triangles, int[] neighbours, bool[] constrained)
    {
        _points = new List<Coordinate>(vertices);
        _count = triangles.Length / 3;
        int capacity = Math.Max(_count * 3, 12);
        _v = new int[capacity];
        _n = new int[capacity];
        _protected = new bool[capacity];
        _user = new bool[capacity];
        _stamp = new int[Math.Max(_count, 4)];
        Array.Copy(triangles, _v, triangles.Length);
        Array.Copy(neighbours, _n, neighbours.Length);
        for (int e = 0; e < triangles.Length; e++)
        {
            _user[e] = constrained[e];
            _protected[e] = neighbours[e] < 0 || constrained[e];
        }

        _vertexTriangle = new int[vertices.Count];
        Array.Fill(_vertexTriangle, -1);
        for (int t = 0; t < _count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                _vertexTriangle[_v[3 * t + k]] = t;
            }
        }
    }

    public int TriangleCount => _count;

    public IReadOnlyList<Coordinate> Points => _points;

    /// <summary>The number of the change that last touched a triangle: an entry of a queue is stale if the number has moved on.</summary>
    public int Stamp(int t) => _stamp[t];

    public int Vertex(int t, int k) => _v[3 * t + k % 3];

    public int Neighbour(int t, int k) => _n[3 * t + k];

    public bool IsProtected(int t, int k) => _protected[3 * t + k];

    public Coordinate Corner(int t, int k) => _points[_v[3 * t + k % 3]];

    /// <summary>The triangles, the neighbours and the constraints as the arrays of a <c>Triangulation</c>.</summary>
    public (Coordinate[] Vertices, int[] Triangles, int[] Neighbours, bool[] Constrained) ToArrays()
    {
        var triangles = new int[3 * _count];
        var neighbours = new int[3 * _count];
        var constrained = new bool[3 * _count];
        Array.Copy(_v, triangles, 3 * _count);
        Array.Copy(_n, neighbours, 3 * _count);
        Array.Copy(_user, constrained, 3 * _count);
        return (_points.ToArray(), triangles, neighbours, constrained);
    }

    // ---------- finding ----------

    /// <summary>The triangle and side of the edge between two vertices (either direction), or (−1, −1).</summary>
    public (int T, int K) FindEdge(int a, int b)
    {
        // all triangles around a, going round it one way and, at the border, the other way
        int start = _vertexTriangle[a];
        if (start < 0)
        {
            return (-1, -1);
        }

        int t = start;
        int guard = _count + 3;
        while (guard-- > 0)
        {
            int i = IndexOf(t, a);
            if (_v[3 * t + (i + 1) % 3] == b)
            {
                return (t, i);
            }

            if (_v[3 * t + (i + 2) % 3] == b)
            {
                return (t, (i + 2) % 3);
            }

            int next = _n[3 * t + i];   // across the edge from a to the next vertex: the triangle that follows counter-clockwise… of the other side
            if (next < 0)
            {
                break;
            }

            t = next;
            if (t == start)
            {
                return (-1, -1);
            }
        }

        // the other way round, from the start
        t = start;
        guard = _count + 3;
        while (guard-- > 0)
        {
            int i = IndexOf(t, a);
            int previous = _n[3 * t + (i + 2) % 3];   // across the edge from the vertex before a to a
            if (previous < 0)
            {
                return (-1, -1);
            }

            t = previous;
            int j = IndexOf(t, a);
            if (_v[3 * t + (j + 1) % 3] == b)
            {
                return (t, j);
            }

            if (_v[3 * t + (j + 2) % 3] == b)
            {
                return (t, (j + 2) % 3);
            }
        }

        return (-1, -1);
    }

    private int IndexOf(int t, int vertex) => _v[3 * t] == vertex ? 0 : _v[3 * t + 1] == vertex ? 1 : 2;

    /// <summary>
    /// Walks from a triangle along the straight line from <paramref name="from"/> to <paramref name="to"/> to the triangle that contains
    /// <paramref name="to"/>. If a protected edge (the border or a constraint) is in the way, that edge is returned instead.
    /// </summary>
    /// <returns><c>true</c> with the triangle in <paramref name="triangle"/>; <c>false</c> with the triangle and the side of the edge in the way.</returns>
    public bool Walk(int start, Coordinate from, Coordinate to, out int triangle, out int side)
    {
        int t = start;
        for (int step = 0; step <= 2 * _count + 8; step++)
        {
            int exit = -1;
            bool inside = true;
            for (int k = 0; k < 3; k++)
            {
                var a = _points[_v[3 * t + k]];
                var b = _points[_v[3 * t + (k + 1) % 3]];
                if (Orientation.Orient2D(a, b, to) < 0)
                {
                    inside = false;
                    // the way leaves the triangle through this edge if the edge separates the two ends of the way
                    if (exit < 0 || Orientation.Orient2D(from, to, a) * Orientation.Orient2D(from, to, b) <= 0)
                    {
                        exit = k;
                    }
                }
            }

            if (inside)
            {
                triangle = t;
                side = -1;
                return true;
            }

            if (_protected[3 * t + exit] || _n[3 * t + exit] < 0)
            {
                triangle = t;
                side = exit;
                return false;
            }

            t = _n[3 * t + exit];
        }

        triangle = start;
        side = -1;
        return false;
    }

    // ---------- inserting ----------

    /// <summary>
    /// Inserts a point that lies inside a triangle or on one of its unprotected edges, and restores the Delaunay property. Returns the id of the new
    /// vertex, or −1 if the point is a vertex of the triangle already; every triangle that was made or changed is added to <paramref name="touched"/>.
    /// </summary>
    public int Insert(Coordinate p, int t, List<int> touched)
    {
        int onEdge = -1;
        for (int k = 0; k < 3; k++)
        {
            var a = _points[_v[3 * t + k]];
            if (a.X == p.X && a.Y == p.Y)
            {
                return -1;
            }

            if (Orientation.Orient2D(a, _points[_v[3 * t + (k + 1) % 3]], p) == 0)
            {
                onEdge = k;
            }
        }

        return onEdge >= 0 ? InsertOnEdge(p, t, onEdge, touched) : InsertInside(p, t, touched);
    }

    /// <summary>Splits the edge of a side at a point on it (the middle, say) that is made the new vertex; both halves keep the flags of the edge.</summary>
    public int Split(Coordinate p, int t, int side, List<int> touched) => InsertOnEdge(p, t, side, touched);

    private int NewVertex(Coordinate p)
    {
        _points.Add(p);
        int id = _points.Count - 1;
        if (id >= _vertexTriangle.Length)
        {
            Array.Resize(ref _vertexTriangle, Math.Max(_vertexTriangle.Length * 2, id + 1));
        }

        return id;
    }

    private int NewTriangle()
    {
        int t = _count++;
        if (3 * _count > _v.Length)
        {
            int capacity = _v.Length * 2;
            Array.Resize(ref _v, capacity);
            Array.Resize(ref _n, capacity);
            Array.Resize(ref _protected, capacity);
            Array.Resize(ref _user, capacity);
        }

        if (_count > _stamp.Length)
        {
            Array.Resize(ref _stamp, _stamp.Length * 2);
        }

        return t;
    }

    private void Set(int t, int a, int b, int c)
    {
        _v[3 * t] = a;
        _v[3 * t + 1] = b;
        _v[3 * t + 2] = c;
        _vertexTriangle[a] = t;
        _vertexTriangle[b] = t;
        _vertexTriangle[c] = t;
        _stamp[t]++;
    }

    private void Flags(int t, int k, bool isProtected, bool user)
    {
        _protected[3 * t + k] = isProtected;
        _user[3 * t + k] = user;
    }

    /// <summary>Makes <paramref name="nb"/> the neighbour across a side of a triangle, and the triangle its neighbour across the same edge.</summary>
    private void Link(int t, int k, int nb)
    {
        _n[3 * t + k] = nb;
        if (nb >= 0)
        {
            int from = _v[3 * t + k], to = _v[3 * t + (k + 1) % 3];
            for (int j = 0; j < 3; j++)
            {
                if (_v[3 * nb + j] == to && _v[3 * nb + (j + 1) % 3] == from)
                {
                    _n[3 * nb + j] = t;
                    return;
                }
            }
        }
    }

    private int InsertInside(Coordinate p, int t, List<int> touched)
    {
        int a = _v[3 * t], b = _v[3 * t + 1], c = _v[3 * t + 2];
        int nAB = _n[3 * t], nBC = _n[3 * t + 1], nCA = _n[3 * t + 2];
        bool pAB = _protected[3 * t], pBC = _protected[3 * t + 1], pCA = _protected[3 * t + 2];
        bool uAB = _user[3 * t], uBC = _user[3 * t + 1], uCA = _user[3 * t + 2];
        int m = NewVertex(p);
        int t0 = t, t1 = NewTriangle(), t2 = NewTriangle();
        Set(t0, a, b, m);
        Set(t1, b, c, m);
        Set(t2, c, a, m);
        Flags(t0, 0, pAB, uAB);
        Flags(t0, 1, false, false);
        Flags(t0, 2, false, false);
        Flags(t1, 0, pBC, uBC);
        Flags(t1, 1, false, false);
        Flags(t1, 2, false, false);
        Flags(t2, 0, pCA, uCA);
        Flags(t2, 1, false, false);
        Flags(t2, 2, false, false);
        Link(t0, 0, nAB);
        Link(t0, 1, t1);
        Link(t0, 2, t2);
        Link(t1, 0, nBC);
        Link(t1, 1, t2);
        Link(t1, 2, t0);
        Link(t2, 0, nCA);
        Link(t2, 1, t0);
        Link(t2, 2, t1);
        var stack = new Stack<(int T, int K)>();
        stack.Push((t0, 0));
        stack.Push((t1, 0));
        stack.Push((t2, 0));
        Legalise(stack, touched);
        touched.Add(t0);
        touched.Add(t1);
        touched.Add(t2);
        return m;
    }

    private int InsertOnEdge(Coordinate p, int t, int k, List<int> touched)
    {
        int a = _v[3 * t + k], b = _v[3 * t + (k + 1) % 3], c = _v[3 * t + (k + 2) % 3];
        int nBC = _n[3 * t + (k + 1) % 3], nCA = _n[3 * t + (k + 2) % 3];
        bool pBC = _protected[3 * t + (k + 1) % 3], pCA = _protected[3 * t + (k + 2) % 3];
        bool uBC = _user[3 * t + (k + 1) % 3], uCA = _user[3 * t + (k + 2) % 3];
        bool pEdge = _protected[3 * t + k], uEdge = _user[3 * t + k];
        int u = _n[3 * t + k];
        int m = NewVertex(p);
        var stack = new Stack<(int T, int K)>();
        if (u < 0)
        {
            int ta = t, tb = NewTriangle();
            Set(ta, a, m, c);
            Set(tb, m, b, c);
            Flags(ta, 0, pEdge, uEdge);
            Flags(ta, 1, false, false);
            Flags(ta, 2, pCA, uCA);
            Flags(tb, 0, pEdge, uEdge);
            Flags(tb, 1, pBC, uBC);
            Flags(tb, 2, false, false);
            Link(ta, 0, -1);
            Link(ta, 1, tb);
            Link(ta, 2, nCA);
            Link(tb, 0, -1);
            Link(tb, 1, nBC);
            Link(tb, 2, ta);
            stack.Push((ta, 2));
            stack.Push((tb, 1));
            touched.Add(ta);
            touched.Add(tb);
        }
        else
        {
            int j = _v[3 * u] == b ? 0 : _v[3 * u + 1] == b ? 1 : 2;   // u has the edge as b → a
            int d = _v[3 * u + (j + 2) % 3];
            int nAD = _n[3 * u + (j + 1) % 3], nDB = _n[3 * u + (j + 2) % 3];
            bool pAD = _protected[3 * u + (j + 1) % 3], pDB = _protected[3 * u + (j + 2) % 3];
            bool uAD = _user[3 * u + (j + 1) % 3], uDB = _user[3 * u + (j + 2) % 3];
            int t1 = t, t2 = NewTriangle(), u1 = u, u2 = NewTriangle();
            Set(t1, a, m, c);
            Set(t2, m, b, c);
            Set(u1, b, m, d);
            Set(u2, m, a, d);
            Flags(t1, 0, pEdge, uEdge);
            Flags(t1, 1, false, false);
            Flags(t1, 2, pCA, uCA);
            Flags(t2, 0, pEdge, uEdge);
            Flags(t2, 1, pBC, uBC);
            Flags(t2, 2, false, false);
            Flags(u1, 0, pEdge, uEdge);
            Flags(u1, 1, false, false);
            Flags(u1, 2, pDB, uDB);
            Flags(u2, 0, pEdge, uEdge);
            Flags(u2, 1, pAD, uAD);
            Flags(u2, 2, false, false);
            Link(t1, 0, u2);
            Link(t1, 1, t2);
            Link(t1, 2, nCA);
            Link(t2, 0, u1);
            Link(t2, 1, nBC);
            Link(t2, 2, t1);
            Link(u1, 0, t2);
            Link(u1, 1, u2);
            Link(u1, 2, nDB);
            Link(u2, 0, t1);
            Link(u2, 1, nAD);
            Link(u2, 2, u1);
            stack.Push((t1, 2));
            stack.Push((t2, 1));
            stack.Push((u1, 2));
            stack.Push((u2, 1));
            touched.Add(t1);
            touched.Add(t2);
            touched.Add(u1);
            touched.Add(u2);
        }

        Legalise(stack, touched);
        return m;
    }

    // ---------- the Delaunay property ----------

    /// <summary>Flips edges that are not protected and not locally Delaunay until none is left, starting from the given ones.</summary>
    private void Legalise(Stack<(int T, int K)> stack, List<int> touched)
    {
        while (stack.Count > 0)
        {
            var (t, k) = stack.Pop();
            if (_protected[3 * t + k])
            {
                continue;
            }

            int u = _n[3 * t + k];
            if (u < 0)
            {
                continue;
            }

            int p = _v[3 * t + k], q = _v[3 * t + (k + 1) % 3], r = _v[3 * t + (k + 2) % 3];
            int j = _v[3 * u] == q ? 0 : _v[3 * u + 1] == q ? 1 : 2;   // u has the edge as q → p
            int s = _v[3 * u + (j + 2) % 3];
            var pp = _points[p];
            var pq = _points[q];
            var pr = _points[r];
            var ps = _points[s];
            if (InCircle.Sign(pp, pq, pr, ps) <= 0 || Orientation.Orient2D(pr, ps, pp) * Orientation.Orient2D(pr, ps, pq) >= 0)
            {
                continue;
            }

            // flip: t = (p, q, r), u = (q, p, s)  →  t = (r, p, s), u = (s, q, r)
            int nQR = _n[3 * t + (k + 1) % 3], nRP = _n[3 * t + (k + 2) % 3];
            bool pQR = _protected[3 * t + (k + 1) % 3], pRP = _protected[3 * t + (k + 2) % 3];
            bool uQR = _user[3 * t + (k + 1) % 3], uRP = _user[3 * t + (k + 2) % 3];
            int nPS = _n[3 * u + (j + 1) % 3], nSQ = _n[3 * u + (j + 2) % 3];
            bool pPS = _protected[3 * u + (j + 1) % 3], pSQ = _protected[3 * u + (j + 2) % 3];
            bool uPS = _user[3 * u + (j + 1) % 3], uSQ = _user[3 * u + (j + 2) % 3];
            Set(t, r, p, s);
            Set(u, s, q, r);
            Flags(t, 0, pRP, uRP);
            Flags(t, 1, pPS, uPS);
            Flags(t, 2, false, false);
            Flags(u, 0, pSQ, uSQ);
            Flags(u, 1, pQR, uQR);
            Flags(u, 2, false, false);
            Link(t, 0, nRP);
            Link(t, 1, nPS);
            Link(t, 2, u);
            Link(u, 0, nSQ);
            Link(u, 1, nQR);
            Link(u, 2, t);
            touched.Add(t);
            touched.Add(u);
            stack.Push((t, 0));
            stack.Push((t, 1));
            stack.Push((u, 0));
            stack.Push((u, 1));
        }
    }
}
