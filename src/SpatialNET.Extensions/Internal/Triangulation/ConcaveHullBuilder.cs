namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The concave hull of a Delaunay triangulation (ADR 0029): triangles are taken away from the border, longest border edge first,
/// as long as that edge is longer than the limit and the region stays whole and keeps every point.
/// </summary>
/// <remarks>
/// A border triangle with exactly one border edge can go if its third vertex (the apex) is not on a border yet: then the region stays
/// connected and the apex remains in it. A triangle with two border edges would orphan its apex, so it stays. With holes allowed, interior
/// triangles of which all vertices are interior and which have an edge longer than the limit open a hole, which grows by the same
/// rule; a hole never touches the outer border or another hole.
/// </remarks>
internal sealed class ConcaveHullBuilder
{
    private readonly DelaunayMesh _mesh;
    private readonly double _maxLength;
    private readonly bool[] _removed;
    private readonly int[] _borderCount;   // border edges at a vertex
    private readonly PriorityQueue<int, (double, int)> _queue = new();

    private ConcaveHullBuilder(DelaunayMesh mesh, double maxLength)
    {
        _mesh = mesh;
        _maxLength = maxLength;
        _removed = new bool[mesh.TriangleCount];
        _borderCount = new int[mesh.Points.Length];
        for (int e = 0; e < mesh.TriangleCount * 3; e++)
        {
            if (mesh.HalfEdges[e] == -1)
            {
                _borderCount[mesh.Triangles[e]]++;
                _borderCount[mesh.Triangles[DelaunayMesh.Next(e)]]++;
            }
        }
    }

    /// <summary>The longest edge of the triangulation, the shortest, for the length ratio.</summary>
    public static (double Shortest, double Longest) EdgeLengths(DelaunayMesh mesh)
    {
        double shortest = double.PositiveInfinity, longest = 0;
        for (int e = 0; e < mesh.TriangleCount * 3; e++)
        {
            double length = Length(mesh, e);
            shortest = Math.Min(shortest, length);
            longest = Math.Max(longest, length);
        }

        return (shortest, longest);
    }

    /// <summary>The rings of the hull: the shell counter-clockwise first, then the holes clockwise (vertex ids, not closed).</summary>
    public static List<int[]> Build(DelaunayMesh mesh, double maxLength, bool holesAllowed)
    {
        var builder = new ConcaveHullBuilder(mesh, maxLength);
        builder.Erode(holesAllowed);
        return builder.Boundaries();
    }

    private static double Length(DelaunayMesh mesh, int edge)
    {
        var a = mesh.Points[mesh.Triangles[edge]];
        var b = mesh.Points[mesh.Triangles[DelaunayMesh.Next(edge)]];
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    private bool IsPresent(int halfEdge) => halfEdge != -1 && !_removed[halfEdge / 3];

    /// <summary>The border edge of a triangle with exactly one, or −1.</summary>
    private int SingleBorderEdge(int triangle)
    {
        int border = -1;
        for (int k = 0; k < 3; k++)
        {
            int e = triangle * 3 + k;
            if (!IsPresent(_mesh.HalfEdges[e]))
            {
                if (border != -1)
                {
                    return -1;
                }

                border = e;
            }
        }

        return border;
    }

    private void Push(int triangle)
    {
        int border = SingleBorderEdge(triangle);
        if (border != -1)
        {
            _queue.Enqueue(triangle, (-Length(_mesh, border), triangle));
        }
    }

    private void Remove(int triangle)
    {
        _removed[triangle] = true;
        for (int k = 0; k < 3; k++)
        {
            int e = triangle * 3 + k;
            int delta = IsPresent(_mesh.HalfEdges[e]) ? 1 : -1; // an inner edge becomes a border edge, a border edge disappears
            _borderCount[_mesh.Triangles[e]] += delta;
            _borderCount[_mesh.Triangles[DelaunayMesh.Next(e)]] += delta;
        }

        for (int k = 0; k < 3; k++)
        {
            int h = _mesh.HalfEdges[triangle * 3 + k];
            if (IsPresent(h))
            {
                Push(h / 3);
            }
        }
    }

    private void Erode(bool holesAllowed)
    {
        for (int t = 0; t < _mesh.TriangleCount; t++)
        {
            Push(t);
        }

        Run();
        if (!holesAllowed)
        {
            return;
        }

        var seeds = new List<(double Length, int Triangle)>();
        for (int t = 0; t < _mesh.TriangleCount; t++)
        {
            if (IsInteriorSeed(t) && LongestEdge(t) > _maxLength)
            {
                seeds.Add((LongestEdge(t), t));
            }
        }

        foreach (var (_, t) in seeds.OrderByDescending(s => s.Length).ThenBy(s => s.Triangle))
        {
            if (IsInteriorSeed(t))
            {
                Remove(t);
                Run();
            }
        }
    }

    private bool IsInteriorSeed(int t)
    {
        if (_removed[t])
        {
            return false;
        }

        for (int k = 0; k < 3; k++)
        {
            int e = t * 3 + k;
            if (!IsPresent(_mesh.HalfEdges[e]) || _borderCount[_mesh.Triangles[e]] > 0)
            {
                return false;
            }
        }

        return true;
    }

    private double LongestEdge(int t) => Math.Max(Length(_mesh, t * 3), Math.Max(Length(_mesh, t * 3 + 1), Length(_mesh, t * 3 + 2)));

    private void Run()
    {
        while (_queue.TryDequeue(out int t, out var priority))
        {
            int border = _removed[t] ? -1 : SingleBorderEdge(t);
            if (border == -1)
            {
                continue; // a stale entry: the triangle is gone or its border changed
            }

            double length = Length(_mesh, border);
            if (-priority.Item1 != length)
            {
                continue;
            }

            if (length <= _maxLength)
            {
                _queue.Clear(); // the longest remaining border edge is short enough
                return;
            }

            int apex = _mesh.Triangles[DelaunayMesh.Prev(border)];
            if (_borderCount[apex] == 0)
            {
                Remove(t);
            }
        }
    }

    private List<int[]> Boundaries()
    {
        var outgoing = new Dictionary<int, int>();
        var starts = new List<int>();
        for (int t = 0; t < _mesh.TriangleCount; t++)
        {
            if (_removed[t])
            {
                continue;
            }

            for (int k = 0; k < 3; k++)
            {
                int e = t * 3 + k;
                if (!IsPresent(_mesh.HalfEdges[e]))
                {
                    int from = _mesh.Triangles[e];
                    if (outgoing.TryAdd(from, e))
                    {
                        starts.Add(e);
                    }
                }
            }
        }

        var rings = new List<int[]>();
        var done = new HashSet<int>();
        foreach (int start in starts)
        {
            if (done.Contains(start))
            {
                continue;
            }

            var ring = new List<int>();
            int e = start;
            while (done.Add(e))
            {
                ring.Add(_mesh.Triangles[e]);
                int to = _mesh.Triangles[DelaunayMesh.Next(e)];
                if (!outgoing.TryGetValue(to, out e))
                {
                    break;
                }
            }

            rings.Add(ring.ToArray());
        }

        // the shell (counter-clockwise, the only ring with positive area) first
        return rings.OrderByDescending(r => Rings.SignedArea(r.Append(r[0]).Select(i => _mesh.Points[i]).ToArray())).ToList();
    }
}
