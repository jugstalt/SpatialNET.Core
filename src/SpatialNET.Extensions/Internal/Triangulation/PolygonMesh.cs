using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>
/// The fast path of <c>Triangulation.FromPolygon</c> (ADR 0038): the triangles of a valid polygon by ear clipping (holes are joined to
/// the outer ring by bridges), then flips with the ring edges fixed. The flips end in the same constrained Delaunay triangulation as
/// inserting the rings into the Delaunay triangulation of all vertices, but the Delaunay triangulation of vertices on a curve is
/// the expensive step and this path never builds it.
/// </summary>
/// <remarks>
/// It takes only what it can do exactly: a first ring with the other rings as holes inside it that do not lie in each other, all
/// vertices different, no ring touching or crossing another or itself. Anything else, and every case where ear clipping or the check of
/// its result fails (collinear vertices can leave no ear), gives <c>null</c> and the caller takes the general path.
/// </remarks>
internal static class PolygonMesh
{
    public static DelaunayMesh? TryBuild(IPolygon2D polygon)
    {
        var points = new List<Coordinate>();
        var rings = new List<int[]>();
        var ids = new Dictionary<(double, double), int>();
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var ring = Distinct(Rings.Closed(polygon[r]));
            if (ring is null)
            {
                return null;
            }

            var vertexIds = new int[ring.Count];
            for (int i = 0; i < ring.Count; i++)
            {
                if (!ids.TryAdd((ring[i].X, ring[i].Y), points.Count))
                {
                    return null;    // a vertex is used twice: rings that touch or a ring that revisits a point
                }

                vertexIds[i] = points.Count;
                points.Add(ring[i] with { M = double.NaN });
            }

            rings.Add(vertexIds);
        }

        if (rings.Count == 0)
        {
            return null;
        }

        var pts = points.ToArray();

        // the first ring counter-clockwise, the holes clockwise: the inside is on the left of every edge
        for (int r = 0; r < rings.Count; r++)
        {
            int o = RingOrientation(pts, rings[r]);
            if (o == 0)
            {
                return null;
            }

            if ((o > 0) != (r == 0))
            {
                Array.Reverse(rings[r]);
            }
        }

        var segA = new List<int>();
        var segB = new List<int>();
        var border = new HashSet<long>();
        foreach (var ring in rings)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                int a = ring[i], b = ring[(i + 1) % ring.Length];
                segA.Add(a);
                segB.Add(b);
                border.Add(Key(a, b, pts.Length));
            }
        }

        var index = new SpatialIndex<int>(Enumerable.Range(0, segA.Count).Select(i => (Box(pts[segA[i]], pts[segB[i]]), i)));
        if (HasContact(pts, segA, segB, index) || !HolesAreInside(pts, rings))
        {
            return null;
        }

        var triangles = EarClip(pts, rings, segA, segB, index);
        return triangles is null ? null : Mesh(pts, triangles, border);
    }

    /// <summary>The ring without repeated points and without the closing point; <c>null</c> if it is not finite or has fewer than three points.</summary>
    private static List<Coordinate>? Distinct(Coordinate[] closed)
    {
        var ring = new List<Coordinate>(closed.Length);
        foreach (var c in closed)
        {
            if (!double.IsFinite(c.X) || !double.IsFinite(c.Y))
            {
                return null;
            }

            if (ring.Count == 0 || ring[^1].X != c.X || ring[^1].Y != c.Y)
            {
                ring.Add(c);
            }
        }

        if (ring.Count > 1 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y)
        {
            ring.RemoveAt(ring.Count - 1);
        }

        return ring.Count < 3 ? null : ring;
    }

    private static long Key(int u, int v, int n) => (long)u * n + v;

    private static Envelope2D Box(Coordinate a, Coordinate b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>+1 counter-clockwise, −1 clockwise, decided at the lowest, leftmost vertex (a convex corner of a simple ring).</summary>
    private static int RingOrientation(Coordinate[] pts, int[] ring)
    {
        int low = 0;
        for (int i = 1; i < ring.Length; i++)
        {
            var c = pts[ring[i]];
            var l = pts[ring[low]];
            if (c.Y < l.Y || (c.Y == l.Y && c.X < l.X))
            {
                low = i;
            }
        }

        return Orientation.Orient2D(pts[ring[(low + ring.Length - 1) % ring.Length]], pts[ring[low]], pts[ring[(low + 1) % ring.Length]]);
    }

    /// <summary>Whether the point q (collinear with a and b) lies within the box of a and b.</summary>
    private static bool Within(Coordinate a, Coordinate b, Coordinate q)
        => q.X >= Math.Min(a.X, b.X) && q.X <= Math.Max(a.X, b.X) && q.Y >= Math.Min(a.Y, b.Y) && q.Y <= Math.Max(a.Y, b.Y);

    /// <summary>
    /// Whether a segment and another touch or cross anywhere but in a shared end point of consecutive segments. The vertices are all
    /// different, so two segments meet if they cross properly or if an end point of one lies on the other.
    /// </summary>
    private static bool HasContact(Coordinate[] pts, List<int> segA, List<int> segB, SpatialIndex<int> index)
    {
        var candidates = new List<int>();
        for (int i = 0; i < segA.Count; i++)
        {
            var a = pts[segA[i]];
            var b = pts[segB[i]];
            candidates.Clear();
            index.Query(Box(a, b), candidates);
            foreach (int j in candidates)
            {
                if (j <= i)
                {
                    continue;   // every pair once
                }

                int ia = segA[i], ib = segB[i], ic = segA[j], id = segB[j];
                var c = pts[ic];
                var d = pts[id];
                int oc = Orientation.Orient2D(a, b, c), od = Orientation.Orient2D(a, b, d);
                if (oc * od > 0)
                {
                    continue;   // both ends strictly on one side of the line of the first: nothing in common
                }

                int oa = Orientation.Orient2D(c, d, a), ob = Orientation.Orient2D(c, d, b);
                if ((oc * od < 0 && oa * ob < 0)
                    || (oc == 0 && ic != ia && ic != ib && Within(a, b, c))
                    || (od == 0 && id != ia && id != ib && Within(a, b, d))
                    || (oa == 0 && ia != ic && ia != id && Within(c, d, a))
                    || (ob == 0 && ib != ic && ib != id && Within(c, d, b)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The holes lie inside the first ring and none lies inside another (the rings do not touch, so one vertex decides).</summary>
    private static bool HolesAreInside(Coordinate[] pts, List<int[]> rings)
    {
        if (rings.Count == 1)
        {
            return true;
        }

        var index = new SpatialIndex<int>(Enumerable.Range(1, rings.Count - 1).Select(r =>
        {
            var env = Box(pts[rings[r][0]], pts[rings[r][0]]);
            foreach (int v in rings[r])
            {
                env = new Envelope2D(Math.Min(env.MinX, pts[v].X), Math.Min(env.MinY, pts[v].Y), Math.Max(env.MaxX, pts[v].X), Math.Max(env.MaxY, pts[v].Y));
            }

            return (env, r);
        }));

        var found = new List<int>();
        for (int r = 1; r < rings.Count; r++)
        {
            var p = pts[rings[r][0]];
            if (!Contains(pts, rings[0], p))
            {
                return false;
            }

            found.Clear();
            index.Query(new Envelope2D(p.X, p.Y, p.X, p.Y), found);
            foreach (int other in found)
            {
                if (other != r && Contains(pts, rings[other], p))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a point that is not on the ring lies inside it (crossing number of a ray to the right).</summary>
    private static bool Contains(Coordinate[] pts, int[] ring, Coordinate p)
    {
        bool inside = false;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = pts[ring[i]];
            var b = pts[ring[(i + 1) % ring.Length]];
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                int o = Orientation.Orient2D(a, b, p);
                if (b.Y > a.Y ? o > 0 : o < 0)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    // ---------- the polygon as one ring: the holes joined by bridges ----------

    private sealed class Ring
    {
        public int[] Vertex = [];   // per node: the vertex id
        public int[] Prev = [];
        public int[] Next = [];
        public bool[] Removed = [];
        public int Count;           // nodes in use

        public int NewNode(int vertex)
        {
            Vertex[Count] = vertex;
            return Count++;
        }
    }

    private static int[]? EarClip(Coordinate[] pts, List<int[]> rings, List<int> segA, List<int> segB, SpatialIndex<int> index)
    {
        int holes = rings.Count - 1;
        int total = pts.Length;
        var ring = new Ring
        {
            Vertex = new int[total + 2 * holes],
            Prev = new int[total + 2 * holes],
            Next = new int[total + 2 * holes],
            Removed = new bool[total + 2 * holes],
        };

        int[] Add(int[] vertices)
        {
            var nodes = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                nodes[i] = ring.NewNode(vertices[i]);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                ring.Next[nodes[i]] = nodes[(i + 1) % nodes.Length];
                ring.Prev[nodes[(i + 1) % nodes.Length]] = nodes[i];
            }

            return nodes;
        }

        var shell = Add(rings[0]);
        var merged = new List<int>(shell);
        var bridges = new List<(int M, int P)>();

        // the holes from the right to the left; each gets a bridge to a vertex that is already part of the ring
        var order = Enumerable.Range(1, holes).Select(r => (Ring: r, Right: RightMost(pts, rings[r]))).OrderByDescending(h => pts[rings[h.Ring][h.Right]].X).ToArray();
        var candidates = new List<int>();
        foreach (var (r, right) in order)
        {
            var nodes = Add(rings[r]);
            int m = nodes[right];
            var mPoint = pts[ring.Vertex[m]];
            var queue = new PriorityQueue<int, double>(merged.Select(node =>
            {
                var q = pts[ring.Vertex[node]];
                return (node, (q.X - mPoint.X) * (q.X - mPoint.X) + (q.Y - mPoint.Y) * (q.Y - mPoint.Y));
            }));

            int p = -1;
            while (queue.TryDequeue(out int node, out _))
            {
                if (EntersInside(pts, ring, node, mPoint) && BridgeIsFree(pts, ring.Vertex[m], ring.Vertex[node], segA, segB, index, bridges, candidates))
                {
                    p = node;
                    break;
                }
            }

            if (p < 0)
            {
                return null;
            }

            // ... P → M → (the hole) → M' → P' → (the old successor of P)
            int pNext = ring.Next[p];
            int mPrev = ring.Prev[m];
            int mCopy = ring.NewNode(ring.Vertex[m]);
            int pCopy = ring.NewNode(ring.Vertex[p]);
            ring.Next[p] = m;
            ring.Prev[m] = p;
            ring.Next[mPrev] = mCopy;
            ring.Prev[mCopy] = mPrev;
            ring.Next[mCopy] = pCopy;
            ring.Prev[pCopy] = mCopy;
            ring.Next[pCopy] = pNext;
            ring.Prev[pNext] = pCopy;
            bridges.Add((ring.Vertex[m], ring.Vertex[p]));
            merged.AddRange(nodes);
            merged.Add(mCopy);
            merged.Add(pCopy);
        }

        return Clip(pts, ring, shell[0]);
    }

    /// <summary>Whether the direction from a node of the ring to a point leads into the inside, which is on the left of the edges (strictly).</summary>
    private static bool EntersInside(Coordinate[] pts, Ring ring, int node, Coordinate target)
    {
        var a = pts[ring.Vertex[ring.Prev[node]]];
        var b = pts[ring.Vertex[node]];
        var c = pts[ring.Vertex[ring.Next[node]]];
        bool before = Orientation.Orient2D(a, b, target) > 0, after = Orientation.Orient2D(b, c, target) > 0;
        return Orientation.Orient2D(a, b, c) > 0 ? before && after : before || after;
    }

    private static int RightMost(Coordinate[] pts, int[] ring)
    {
        int best = 0;
        for (int i = 1; i < ring.Length; i++)
        {
            if (pts[ring[i]].X > pts[ring[best]].X)
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>Whether the segment between two vertices meets no ring edge and no earlier bridge except in its own end points, and runs along none.</summary>
    private static bool BridgeIsFree(Coordinate[] pts, int u, int v, List<int> segA, List<int> segB, SpatialIndex<int> index,
        List<(int M, int P)> bridges, List<int> candidates)
    {
        var a = pts[u];
        var b = pts[v];
        candidates.Clear();
        index.Query(Box(a, b), candidates);
        foreach (int s in candidates)
        {
            if (!Free(pts, u, v, segA[s], segB[s]))
            {
                return false;
            }
        }

        foreach (var (m, p) in bridges)
        {
            if (!Free(pts, u, v, m, p))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Free(Coordinate[] pts, int u, int v, int c, int d)
    {
        var a = pts[u];
        var b = pts[v];
        if (c == u || c == v || d == u || d == v)
        {
            // they share an end point: not free if the other end is collinear (they may run along each other)
            int other = c == u || c == v ? d : c;
            return other == u || other == v || Orientation.Orient2D(a, b, pts[other]) != 0;
        }

        var pc = pts[c];
        var pd = pts[d];
        int oc = Orientation.Orient2D(a, b, pc), od = Orientation.Orient2D(a, b, pd);
        int oa = Orientation.Orient2D(pc, pd, a), ob = Orientation.Orient2D(pc, pd, b);
        if (oc * od < 0 && oa * ob < 0)
        {
            return false;
        }

        return !((oc == 0 && Within(a, b, pc)) || (od == 0 && Within(a, b, pd)) || (oa == 0 && Within(pc, pd, a)) || (ob == 0 && Within(pc, pd, b)));
    }

    // ---------- ear clipping ----------

    /// <summary>Clips ears off the ring. <c>null</c> if no ear can be found or the last triangle is not counter-clockwise.</summary>
    private static int[]? Clip(Coordinate[] pts, Ring ring, int start)
    {
        int remaining = ring.Count;
        var triangles = new int[(remaining - 2) * 3];
        int written = 0;

        // the vertices that are not convex: only they can lie in an ear (they only become convex while ears are clipped)
        var reflex = new List<int>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int n = 0; n < ring.Count; n++)
        {
            var c = pts[ring.Vertex[n]];
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
            if (Orientation.Orient2D(pts[ring.Vertex[ring.Prev[n]]], c, pts[ring.Vertex[ring.Next[n]]]) <= 0)
            {
                reflex.Add(n);
            }
        }

        int cells = Math.Max(1, (int)Math.Sqrt(reflex.Count / 2.0));
        double width = Math.Max((maxX - minX) / cells, double.Epsilon), height = Math.Max((maxY - minY) / cells, double.Epsilon);
        int CellX(double x) => Math.Clamp((int)((x - minX) / width), 0, cells - 1);
        int CellY(double y) => Math.Clamp((int)((y - minY) / height), 0, cells - 1);

        var first = new int[cells * cells + 1];
        foreach (int n in reflex)
        {
            var c = pts[ring.Vertex[n]];
            first[CellY(c.Y) * cells + CellX(c.X) + 1]++;
        }

        for (int i = 1; i < first.Length; i++)
        {
            first[i] += first[i - 1];
        }

        var entries = new int[reflex.Count];
        var fill = (int[])first.Clone();
        foreach (int n in reflex)
        {
            var c = pts[ring.Vertex[n]];
            entries[fill[CellY(c.Y) * cells + CellX(c.X)]++] = n;
        }

        bool IsEar(int b)
        {
            int a = ring.Prev[b], c = ring.Next[b];
            var pa = pts[ring.Vertex[a]];
            var pb = pts[ring.Vertex[b]];
            var pc = pts[ring.Vertex[c]];
            if (Orientation.Orient2D(pa, pb, pc) <= 0)
            {
                return false;
            }

            double x0 = Math.Min(pa.X, Math.Min(pb.X, pc.X)), x1 = Math.Max(pa.X, Math.Max(pb.X, pc.X));
            double y0 = Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)), y1 = Math.Max(pa.Y, Math.Max(pb.Y, pc.Y));
            int va = ring.Vertex[a], vb = ring.Vertex[b], vc = ring.Vertex[c];
            for (int cy = CellY(y0); cy <= CellY(y1); cy++)
            {
                for (int cx = CellX(x0); cx <= CellX(x1); cx++)
                {
                    int cell = cy * cells + cx;
                    for (int k = first[cell]; k < first[cell + 1]; k++)
                    {
                        int n = entries[k];
                        int vn = ring.Vertex[n];
                        if (ring.Removed[n] || vn == va || vn == vb || vn == vc)
                        {
                            continue;
                        }

                        var q = pts[vn];
                        if (q.X < x0 || q.X > x1 || q.Y < y0 || q.Y > y1)
                        {
                            continue;
                        }

                        // inside the triangle or on its border: not an ear
                        if (Orientation.Orient2D(pa, pb, q) >= 0 && Orientation.Orient2D(pb, pc, q) >= 0 && Orientation.Orient2D(pc, pa, q) >= 0)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        // the ears with the smallest circumcircle first: they are close to the Delaunay triangles, so few flips are left to do
        double Radius(int b)
        {
            var pa = pts[ring.Vertex[ring.Prev[b]]];
            var pb = pts[ring.Vertex[b]];
            var pc = pts[ring.Vertex[ring.Next[b]]];
            double abx = pb.X - pa.X, aby = pb.Y - pa.Y, bcx = pc.X - pb.X, bcy = pc.Y - pb.Y, cax = pa.X - pc.X, cay = pa.Y - pc.Y;
            double cross = abx * bcy - aby * bcx;
            return (abx * abx + aby * aby) * (bcx * bcx + bcy * bcy) * (cax * cax + cay * cay) / (cross * cross);
        }

        var stamp = new int[ring.Count];
        var heap = new PriorityQueue<(int Node, int Stamp), double>();
        void Push(int n)
        {
            stamp[n]++;
            var pa = pts[ring.Vertex[ring.Prev[n]]];
            var pb = pts[ring.Vertex[n]];
            var pc = pts[ring.Vertex[ring.Next[n]]];
            if (Orientation.Orient2D(pa, pb, pc) > 0)
            {
                heap.Enqueue((n, stamp[n]), Radius(n));
            }
        }

        int alive = start;
        bool clipped = true;
        while (remaining > 3)
        {
            if (heap.Count == 0)
            {
                if (!clipped)
                {
                    return null;    // not one ear in a whole pass
                }

                clipped = false;
                for (int n = 0; n < ring.Count; n++)
                {
                    if (!ring.Removed[n])
                    {
                        Push(n);
                    }
                }

                continue;
            }

            var (current, version) = heap.Dequeue();
            if (ring.Removed[current] || stamp[current] != version || !IsEar(current))
            {
                continue;
            }

            int a = ring.Prev[current], c = ring.Next[current];
            triangles[written++] = ring.Vertex[a];
            triangles[written++] = ring.Vertex[current];
            triangles[written++] = ring.Vertex[c];
            ring.Removed[current] = true;
            ring.Next[a] = c;
            ring.Prev[c] = a;
            remaining--;
            clipped = true;
            alive = a;
            Push(a);
            Push(c);
        }

        int current2 = alive;
        int p = ring.Prev[current2], n2 = ring.Next[current2];
        if (Orientation.Orient2D(pts[ring.Vertex[p]], pts[ring.Vertex[current2]], pts[ring.Vertex[n2]]) <= 0)
        {
            return null;
        }

        triangles[written++] = ring.Vertex[p];
        triangles[written++] = ring.Vertex[current2];
        triangles[written++] = ring.Vertex[n2];
        return triangles;
    }

    // ---------- the mesh and its flips ----------

    private static DelaunayMesh? Mesh(Coordinate[] pts, int[] triangles, HashSet<long> border)
    {
        int n = pts.Length;
        var halfEdges = new int[triangles.Length];
        var directed = new Dictionary<long, int>(triangles.Length);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                if (!directed.TryAdd(Key(triangles[t + k], triangles[t + (k + 1) % 3], n), t + k))
                {
                    return null;
                }
            }
        }

        int outer = 0;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int u = triangles[t + k], v = triangles[t + (k + 1) % 3];
                if (directed.TryGetValue(Key(v, u, n), out int opposite))
                {
                    halfEdges[t + k] = opposite;
                }
                else if (border.Contains(Key(u, v, n)))
                {
                    halfEdges[t + k] = -1;
                    outer++;
                }
                else
                {
                    return null;    // an edge with a neighbour on neither side that is not on a ring
                }
            }
        }

        if (outer != border.Count)
        {
            return null;
        }

        var mesh = DelaunayMesh.FromTriangles(pts, triangles, halfEdges);
        mesh.MakeBorderConstrainedDelaunay();
        return mesh;
    }
}
