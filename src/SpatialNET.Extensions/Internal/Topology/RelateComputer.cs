using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Topology;

/// <summary>
/// DE-9IM (ADR 0013): exact planar graph of both geometries without a grid; each face (dimension 2), edge (1)
/// and node (0) gets its position interior/boundary/exterior with respect to a and b; the matrix entry is the highest
/// dimension per combination of positions. Areas by even-odd (ADR 0003), line boundaries by mod-2.
/// </summary>
internal static class RelateComputer
{
    private const int I = 0, B = 1, E = 2;

    private sealed class Operand
    {
        public int Dimension = -1; // -1 leer, 0 Punkte, 1 Linien, 2 Fläche
        public readonly List<Coordinate[]> Rings = [];
        public readonly List<Coordinate[]> Paths = [];
        public readonly List<Coordinate> Points = [];
    }

    private sealed class Seg(int operand, bool area, Coordinate p, Coordinate q, HPoint hp, HPoint hq)
    {
        public readonly int Operand = operand;
        public readonly bool Area = area;
        public readonly Coordinate P = p, Q = q;
        public readonly HPoint HP = hp, HQ = hq;
        public readonly List<HPoint> Splits = [];
        public double MinX => Math.Min(P.X, Q.X);
        public double MaxX => Math.Max(P.X, Q.X);
        public double MinY => Math.Min(P.Y, Q.Y);
        public double MaxY => Math.Max(P.Y, Q.Y);
    }

    public static int[] Compute(IGeometry2D a, IGeometry2D b)
    {
        var ops = new[] { Read(a), Read(b) };
        var scale = new ExactScale(ops.SelectMany(o =>
            o.Rings.SelectMany(r => r).Concat(o.Paths.SelectMany(p => p)).Concat(o.Points))
            .SelectMany(c => new[] { c.X, c.Y }));

        var segments = new List<Seg>();
        for (int k = 0; k < 2; k++)
        {
            foreach (var ring in ops[k].Rings) AddSegments(segments, ring, k, area: true, scale);
            foreach (var path in ops[k].Paths) AddSegments(segments, path, k, area: false, scale);
        }

        var points = new List<(int Operand, Coordinate C, HPoint H)>();
        for (int k = 0; k < 2; k++)
            foreach (var p in ops[k].Points)
                points.Add((k, p, scale.ToPoint(p)));

        Node(segments, points);

        var input = new List<GraphInput<HPoint>>();
        foreach (var s in segments)
        {
            var sorted = Sorted(s);
            for (int i = 1; i < sorted.Count; i++)
            {
                input.Add(new GraphInput<HPoint>(sorted[i - 1], sorted[i], s.Operand, s.Area ? 1 : 0, s.Area ? 0 : 1, -1));
            }
        }

        var graph = new PlanarGraph<HPoint>(input);

        // Mod-2-Rand der Linien und Punktmengen
        var lineBoundary = new HashSet<HPoint>[2];
        var pointSet = new HashSet<HPoint>[2];
        for (int k = 0; k < 2; k++)
        {
            var count = new Dictionary<HPoint, int>();
            foreach (var path in ops[k].Paths.Where(p => p.Length >= 2))
            {
                foreach (var end in new[] { path[0], path[^1] })
                {
                    var h = scale.ToPoint(end);
                    count[h] = count.GetValueOrDefault(h) + 1;
                }
            }

            lineBoundary[k] = count.Where(c => c.Value % 2 == 1).Select(c => c.Key).ToHashSet();
            pointSet[k] = points.Where(p => p.Operand == k).Select(p => p.H).ToHashSet();
        }

        var m = new int[9];
        Array.Fill(m, -1);
        m[E * 3 + E] = 2;
        void Set(int la, int lb, int dim) => m[la * 3 + lb] = Math.Max(m[la * 3 + lb], dim);

        // Maschen
        for (int h = 0; h < graph.HalfEdgeCount; h++)
        {
            Set(FaceLocation(ops[0], graph.LeftWinding(h, 0)), FaceLocation(ops[1], graph.LeftWinding(h, 1)), 2);
        }

        // Kanten
        for (int e = 0; e < graph.Edges.Count; e++)
        {
            Set(EdgeLocation(ops[0], graph, e, 0), EdgeLocation(ops[1], graph, e, 1), 1);
        }

        // Knoten
        for (int n = 0; n < graph.Nodes.Count; n++)
        {
            Set(NodeLocation(ops[0], graph, n, 0, lineBoundary[0], pointSet[0]),
                NodeLocation(ops[1], graph, n, 1, lineBoundary[1], pointSet[1]), 0);
        }

        // isolierte Punkte (kein Knoten des Graphen)
        foreach (var (k, _, h) in points)
        {
            if (graph.TryGetNode(h, out _)) continue;
            int own = I;
            int other = IsolatedLocation(ops[1 - k], graph, h, 1 - k, pointSet[1 - k]);
            Set(k == 0 ? own : other, k == 0 ? other : own, 0);
        }

        return m;
    }

    public static int DimensionOf(IGeometry2D g) => Read(g).Dimension;

    private static int FaceLocation(Operand op, int winding) =>
        op.Dimension == 2 && (winding & 1) == 1 ? I : E;

    private static int EdgeLocation(Operand op, PlanarGraph<HPoint> graph, int e, int k)
    {
        switch (op.Dimension)
        {
            case 2:
                int left = graph.LeftWinding(2 * e, k) & 1, right = graph.RightWinding(2 * e, k) & 1;
                return left != right ? B : left == 1 ? I : E;
            case 1:
                return graph.Edges[e].Line[k] > 0 ? I : E;
            default:
                return E;
        }
    }

    private static int NodeLocation(Operand op, PlanarGraph<HPoint> graph, int n, int k, HashSet<HPoint> boundary, HashSet<HPoint> pointSet)
    {
        var outgoing = graph.Outgoing(n);
        switch (op.Dimension)
        {
            case 2:
                foreach (int h in outgoing)
                {
                    if (((graph.LeftWinding(h, k) ^ graph.RightWinding(h, k)) & 1) == 1) return B;
                }

                return outgoing.Count > 0 && (graph.LeftWinding(outgoing[0], k) & 1) == 1 ? I : E;
            case 1:
                if (boundary.Contains(graph.Nodes[n])) return B;
                return outgoing.Any(h => graph.Edges[h >> 1].Line[k] > 0) ? I : E;
            case 0:
                return pointSet.Contains(graph.Nodes[n]) ? I : E;
            default:
                return E;
        }
    }

    private static int IsolatedLocation(Operand op, PlanarGraph<HPoint> graph, HPoint p, int k, HashSet<HPoint> pointSet) => op.Dimension switch
    {
        2 => (graph.RayWinding(p, k) & 1) == 1 ? I : E,
        0 => pointSet.Contains(p) ? I : E,
        _ => E,
    };

    private static Operand Read(IGeometry2D g)
    {
        var op = new Operand();
        switch (g)
        {
            case IPoint2D p:
                AddPoint(op, p.ToCoordinate());
                break;
            case IMultiPoint2D mp:
                foreach (var c in Rings.Coordinates(mp)) AddPoint(op, c);
                break;
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                {
                    var path = Rings.Coordinates(line[i]).Where(Finite).ToArray();
                    if (path.Length == 0) continue;
                    op.Paths.Add(path);
                }

                break;
            case IPolygon2D polygon:
                for (int i = 0; i < polygon.RingCount; i++)
                {
                    if (polygon[i].PointCount == 0) continue;
                    op.Rings.Add(Rings.Closed(polygon[i]).Where(Finite).ToArray());
                }

                break;
        }

        op.Dimension = op.Rings.Count > 0 ? 2 : op.Paths.Count > 0 ? 1 : op.Points.Count > 0 ? 0 : -1;
        return op;
    }

    private static bool Finite(Coordinate c) => double.IsFinite(c.X) && double.IsFinite(c.Y);

    private static void AddPoint(Operand op, Coordinate c)
    {
        if (Finite(c)) op.Points.Add(c);
    }

    private static void AddSegments(List<Seg> segments, Coordinate[] path, int operand, bool area, ExactScale scale)
    {
        for (int i = 1; i < path.Length; i++)
        {
            if (Rings.SameXY(path[i - 1], path[i])) continue;
            segments.Add(new Seg(operand, area, path[i - 1], path[i], scale.ToPoint(path[i - 1]), scale.ToPoint(path[i])));
        }
    }

    /// <summary>Exact noding: proper crossings as rational points, touches and overlaps via the end points.</summary>
    private static void Node(List<Seg> segments, List<(int Operand, Coordinate C, HPoint H)> points)
    {
        var order = segments.OrderBy(s => s.MinX).ToArray();
        var active = new List<Seg>();
        foreach (var s in order)
        {
            active.RemoveAll(a => a.MaxX < s.MinX);
            foreach (var t in active)
            {
                if (t.MaxY < s.MinY || s.MaxY < t.MinY) continue;
                int o1 = Orientation.Orient2D(t.P, t.Q, s.P), o2 = Orientation.Orient2D(t.P, t.Q, s.Q);
                int o3 = Orientation.Orient2D(s.P, s.Q, t.P), o4 = Orientation.Orient2D(s.P, s.Q, t.Q);
                if (o1 * o2 < 0 && o3 * o4 < 0)
                {
                    var x = HPoint.Intersection(t.HP, t.HQ, s.HP, s.HQ);
                    t.Splits.Add(x);
                    s.Splits.Add(x);
                    continue;
                }

                if (o1 == 0 && Segments.IsOnSegment(s.P, t.P, t.Q)) t.Splits.Add(s.HP);
                if (o2 == 0 && Segments.IsOnSegment(s.Q, t.P, t.Q)) t.Splits.Add(s.HQ);
                if (o3 == 0 && Segments.IsOnSegment(t.P, s.P, s.Q)) s.Splits.Add(t.HP);
                if (o4 == 0 && Segments.IsOnSegment(t.Q, s.P, s.Q)) s.Splits.Add(t.HQ);
            }

            active.Add(s);
        }

        foreach (var (_, c, h) in points)
        {
            foreach (var s in segments)
            {
                if (c.X < s.MinX || c.X > s.MaxX || c.Y < s.MinY || c.Y > s.MaxY) continue;
                if (Segments.IsOnSegment(c, s.P, s.Q)) s.Splits.Add(h);
            }
        }
    }

    /// <summary>End points and split points along the segment direction, without duplicates.</summary>
    private static List<HPoint> Sorted(Seg s)
    {
        var all = new List<HPoint>(s.Splits.Count + 2) { s.HP, s.HQ };
        all.AddRange(s.Splits);
        bool useX = Math.Abs(s.Q.X - s.P.X) >= Math.Abs(s.Q.Y - s.P.Y);
        int direction = useX ? Math.Sign(s.Q.X - s.P.X) : Math.Sign(s.Q.Y - s.P.Y);
        all.Sort((u, v) => direction * (useX ? HPoint.CompareX(u, v) : HPoint.CompareY(u, v)));

        var result = new List<HPoint>(all.Count);
        foreach (var p in all)
        {
            if (result.Count == 0 || !result[^1].Equals(p)) result.Add(p);
        }

        return result;
    }
}
