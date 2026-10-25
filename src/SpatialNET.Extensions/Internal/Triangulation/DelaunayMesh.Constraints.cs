using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The constrained Delaunay triangulation (ADR 0038): edges that must be part of the triangulation are inserted into the
/// Delaunay triangulation by flips (Sloan 1993), then every edge that is not a constraint is made locally Delaunay again.
/// </summary>
/// <remarks>
/// The constraints are pairs of vertices of the mesh. A constraint that passes exactly through another vertex is split there. Two
/// constraints must not cross properly; the caller nodes them first (the constraints may touch, overlap or share end points). All
/// decisions use the exact predicates <see cref="Orientation"/> and <see cref="InCircle"/>.
/// </remarks>
internal sealed partial class DelaunayMesh
{
    private int[]? _vertexEdge;                 // for every vertex an outgoing half-edge, kept up to date by Flip once it exists
    private HashSet<(int, int)>? _constrained;  // by vertex pair (smaller id first): robust against the re-indexing of half-edges by flips

    /// <summary>Whether the edge between two vertices is a constraint.</summary>
    public bool IsConstrained(int u, int v) => _constrained is not null && _constrained.Contains(u < v ? (u, v) : (v, u));

    private void Mark(int u, int v)
    {
        _constrained ??= [];
        _constrained.Add(u < v ? (u, v) : (v, u));
    }

    /// <summary>
    /// Inserts constraints (pairs of vertex ids) and restores the constrained Delaunay property.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two constraints cross.</exception>
    public void InsertConstraints(IEnumerable<(int A, int B)> constraints)
    {
        EnsureVertexEdges();
        foreach (var (a, b) in constraints)
        {
            InsertConstraint(a, b);
        }

        MakeConstrainedDelaunay();
    }

    private void EnsureVertexEdges()
    {
        if (_vertexEdge is not null)
        {
            return;
        }

        _vertexEdge = new int[_points.Length];
        Array.Fill(_vertexEdge, -1);
        for (int e = 0; e < _triangleCount * 3; e++)
        {
            _vertexEdge[_triangles[e]] = e;
        }
    }

    /// <summary>All half-edges that start at the vertex, in no particular order.</summary>
    private void Around(int v, List<int> result)
    {
        result.Clear();
        int start = _vertexEdge![v];
        int e = start;
        bool closed = false;
        while (true)
        {
            result.Add(e);
            int h = _halfEdges[e];
            if (h == -1)
            {
                break;
            }

            e = Next(h);
            if (e == start)
            {
                closed = true;
                break;
            }
        }

        if (closed)
        {
            return;
        }

        // a vertex on the hull: the other way round from the start
        e = start;
        while (true)
        {
            int h = _halfEdges[Prev(e)];
            if (h == -1)
            {
                break;
            }

            e = h;
            result.Add(e);
        }
    }

    private readonly List<int> _around = [];

    /// <summary>A half-edge of the edge between two vertices (either direction), or −1.</summary>
    private int FindEdge(int p, int q)
    {
        Around(p, _around);
        foreach (int e in _around)
        {
            if (_triangles[Next(e)] == q)
            {
                return e;
            }
        }

        Around(q, _around);
        foreach (int e in _around)
        {
            if (_triangles[Next(e)] == p)
            {
                return e;
            }
        }

        return -1;
    }

    private int O(int a, int b, int c) => Orientation.Orient2D(_points[a], _points[b], _points[c]);

    /// <summary>x (collinear with a and b) lies strictly between a and b.</summary>
    private bool Between(int a, int x, int b)
    {
        var pa = _points[a];
        var px = _points[x];
        var pb = _points[b];
        return pa.X != pb.X
            ? (pa.X < px.X && px.X < pb.X) || (pb.X < px.X && px.X < pa.X)
            : (pa.Y < px.Y && px.Y < pb.Y) || (pb.Y < px.Y && px.Y < pa.Y);
    }

    private void InsertConstraint(int a, int b)
    {
        var around = new List<int>();
        while (a != b)
        {
            if (FindEdge(a, b) >= 0)
            {
                Mark(a, b);
                return;
            }

            // the triangle of a whose wedge contains the direction to b
            Around(a, around);
            int onSegment = -1, crossStart = -1;
            foreach (int e in around)
            {
                int x = _triangles[Next(e)], y = _triangles[Prev(e)];
                int ox = O(a, x, b);
                if (ox == 0 && Between(a, x, b))
                {
                    onSegment = x;
                    break;
                }

                if (ox > 0)
                {
                    int oy = O(a, y, b);
                    if (oy < 0)
                    {
                        crossStart = Next(e);   // x → y, the edge opposite a
                        break;
                    }

                    if (oy == 0 && Between(a, y, b))
                    {
                        onSegment = y;
                        break;
                    }
                }
            }

            if (onSegment >= 0)
            {
                Mark(a, onSegment);
                a = onSegment;
                continue;
            }

            if (crossStart < 0)
            {
                throw new InvalidOperationException("The constraint leaves the triangulation.");
            }

            // walk along the segment and collect the edges it crosses; stop at b or at a vertex on the segment
            var crossed = new List<(int, int)>();
            int ce = crossStart;
            int end = -1;
            while (true)
            {
                int x = _triangles[ce], y = _triangles[Next(ce)];
                crossed.Add((x, y));
                int h = _halfEdges[ce];
                if (h == -1)
                {
                    throw new InvalidOperationException("The constraint leaves the triangulation.");
                }

                int w = _triangles[Prev(h)];
                if (w == b)
                {
                    end = b;
                    break;
                }

                int o = O(a, b, w);
                if (o == 0)
                {
                    end = w;
                    break;
                }

                ce = o > 0 ? Next(h) : Prev(h);
            }

            RemoveCrossings(a, end, crossed);
            Mark(a, end);
            a = end;
        }
    }

    /// <summary>Flips the crossed edges until the segment a–b is an edge (Sloan 1993).</summary>
    private void RemoveCrossings(int a, int b, List<(int P, int Q)> crossed)
    {
        var queue = new Queue<(int P, int Q)>(crossed);
        long guard = 100 + 4L * crossed.Count * crossed.Count;
        while (queue.Count > 0)
        {
            if (--guard < 0)
            {
                throw new InvalidOperationException("The constraint could not be inserted.");
            }

            var (p, q) = queue.Dequeue();
            if (IsConstrained(p, q))
            {
                throw new InvalidOperationException("Two constraints cross; node them first.");
            }

            int ea = FindEdge(p, q);
            int eb = _halfEdges[ea];
            int u = _triangles[ea], v = _triangles[Next(ea)], pa = _triangles[Prev(ea)], pb = _triangles[Prev(eb)];

            // the quadrilateral u, pb, v, pa can be flipped if its diagonal pa–pb separates u and v
            if (O(pa, pb, u) * O(pa, pb, v) >= 0)
            {
                queue.Enqueue((p, q));
                continue;
            }

            Flip(ea, eb);
            if (O(a, b, pa) * O(a, b, pb) < 0 && O(pa, pb, a) * O(pa, pb, b) < 0)
            {
                queue.Enqueue((pa, pb));
            }
        }
    }

    /// <summary>Flips every edge that is not a constraint and not locally Delaunay until none is left.</summary>
    private void MakeConstrainedDelaunay()
    {
        var stack = new Stack<int>();
        for (int e = 0; e < _triangleCount * 3; e++)
        {
            int h = _halfEdges[e];
            if (h != -1 && e < h)
            {
                stack.Push(e);
            }
        }

        while (stack.Count > 0)
        {
            int a = stack.Pop();
            int b = _halfEdges[a];
            if (b == -1)
            {
                continue;
            }

            int a1 = Next(a), a2 = Prev(a), b1 = Next(b), b2 = Prev(b);
            int u = _triangles[a], v = _triangles[a1], pa = _triangles[a2], pb = _triangles[b2];
            if (IsConstrained(u, v) || InCircle.Sign(_points[u], _points[v], _points[pa], _points[pb]) <= 0 || O(pa, pb, u) * O(pa, pb, v) >= 0)
            {
                continue;
            }

            Flip(a, b);
            stack.Push(a);
            stack.Push(b);
            stack.Push(b1);
            stack.Push(a2);
        }
    }
}
