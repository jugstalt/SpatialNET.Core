namespace SpatialNET.Internal.Topology;

/// <summary>Eine Eingabekante für den Graph: Richtung a→b, Beiträge je Operand.</summary>
/// <param name="A">Anfangspunkt.</param>
/// <param name="B">Endpunkt.</param>
/// <param name="Operand">0 = a, 1 = b.</param>
/// <param name="Winding">Beitrag zur Windungszahl in Richtung a→b (Ringkanten: +1; Linien: 0).</param>
/// <param name="Line">1, wenn die Kante zu einer Linie des Operanden gehört.</param>
/// <param name="Source">Freie Herkunftsnummer (z. B. für Z beim Overlay), −1 = keine.</param>
internal readonly record struct GraphInput<T>(T A, T B, int Operand, int Winding, int Line, int Source) where T : struct, IExactPoint<T>;

/// <summary>
/// Planarer Graph über genodeten Kanten mit exakten Punkten (ADR 0005): zusammengeführte Kanten, Halbkanten
/// mit Winkelordnung, Maschen-Nachfolger und Windungszahl je Operand für die Masche links jeder Halbkante.
/// Halbkante 2e läuft Nodes[A]→Nodes[B] der Kante e (A &lt; B lexikographisch), 2e+1 zurück.
/// </summary>
internal sealed class PlanarGraph<T> where T : struct, IExactPoint<T>
{
    public const int Operands = 2;

    public sealed class Edge
    {
        public int A, B;
        public readonly int[] Winding = new int[Operands]; // in Richtung A→B
        public readonly int[] Line = new int[Operands];
        public readonly List<int> Sources = [];
    }

    public readonly List<T> Nodes = [];
    public readonly List<Edge> Edges = [];
    private readonly Dictionary<T, int> _nodeIndex = [];
    private readonly Dictionary<(int, int), int> _edgeIndex = [];

    private List<int>[] _outgoing = [];
    private int[] _next = [];
    private int[,] _leftWinding = new int[0, 0];

    public PlanarGraph(IEnumerable<GraphInput<T>> input)
    {
        foreach (var e in input)
        {
            if (e.A.Equals(e.B))
            {
                continue;
            }

            int a = Node(e.A), b = Node(e.B), sign = 1;
            if (ExactGeometry.Compare(e.A, e.B) > 0)
            {
                (a, b, sign) = (b, a, -1);
            }

            if (!_edgeIndex.TryGetValue((a, b), out int index))
            {
                _edgeIndex[(a, b)] = index = Edges.Count;
                Edges.Add(new Edge { A = a, B = b });
            }

            var edge = Edges[index];
            edge.Winding[e.Operand] += sign * e.Winding;
            edge.Line[e.Operand] += e.Line;
            if (e.Source >= 0)
            {
                edge.Sources.Add(e.Source);
            }
        }

        Link();
    }

    public int HalfEdgeCount => Edges.Count * 2;

    public int Node(T p)
    {
        if (!_nodeIndex.TryGetValue(p, out int id))
        {
            _nodeIndex[p] = id = Nodes.Count;
            Nodes.Add(p);
        }

        return id;
    }

    public bool TryGetNode(T p, out int id) => _nodeIndex.TryGetValue(p, out id);

    public int Origin(int h) => (h & 1) == 0 ? Edges[h >> 1].A : Edges[h >> 1].B;

    public int Head(int h) => Origin(h ^ 1);

    public static int Twin(int h) => h ^ 1;

    /// <summary>Nachfolger in der Masche links von h: die Halbkante am Kopf, die im Uhrzeigersinn direkt nach Twin(h) kommt.</summary>
    public int Next(int h) => _next[h];

    public IReadOnlyList<int> Outgoing(int node) => _outgoing[node];

    /// <summary>Beitrag der Halbkante zur Windungszahl (Querung von rechts nach links).</summary>
    public int Winding(int h, int operand) => (h & 1) == 0 ? Edges[h >> 1].Winding[operand] : -Edges[h >> 1].Winding[operand];

    /// <summary>Windungszahl des Operanden in der Masche links von h.</summary>
    public int LeftWinding(int h, int operand) => _leftWinding[h, operand];

    /// <summary>Windungszahl des Operanden in der Masche rechts von h.</summary>
    public int RightWinding(int h, int operand) => _leftWinding[h ^ 1, operand];

    private void Link()
    {
        _outgoing = new List<int>[Nodes.Count];
        for (int i = 0; i < Nodes.Count; i++) _outgoing[i] = [];
        for (int h = 0; h < HalfEdgeCount; h++) _outgoing[Origin(h)].Add(h);

        for (int n = 0; n < Nodes.Count; n++)
        {
            var o = Nodes[n];
            _outgoing[n].Sort((x, y) => ExactGeometry.CompareAngle(o, Nodes[Head(x)], Nodes[Head(y)]));
        }

        _next = new int[HalfEdgeCount];
        var position = new int[HalfEdgeCount];
        for (int n = 0; n < Nodes.Count; n++)
            for (int i = 0; i < _outgoing[n].Count; i++)
                position[_outgoing[n][i]] = i;

        for (int h = 0; h < HalfEdgeCount; h++)
        {
            var list = _outgoing[Head(h)];
            int i = position[h ^ 1];
            _next[h] = list[(i - 1 + list.Count) % list.Count];
        }

        ComputeWinding();
    }

    /// <summary>
    /// Windungszahlen: je Zusammenhangskomponente ein exakter Strahl nach links vom tiefsten linken Knoten
    /// gegen die übrigen Komponenten, dann Weitergabe über Maschen (Next) und Kanten (Twin).
    /// </summary>
    private void ComputeWinding()
    {
        _leftWinding = new int[HalfEdgeCount, Operands];
        var assigned = new bool[HalfEdgeCount];
        var component = Components(out int componentCount);
        var start = new int[componentCount];
        Array.Fill(start, -1);

        // tiefster linker Knoten je Komponente
        var lowest = new int[componentCount];
        Array.Fill(lowest, -1);
        for (int n = 0; n < Nodes.Count; n++)
        {
            if (_outgoing[n].Count == 0) continue;
            int c = component[_outgoing[n][0] >> 1];
            if (lowest[c] < 0 || ExactGeometry.Compare(Nodes[n], Nodes[lowest[c]]) < 0) lowest[c] = n;
        }

        var queue = new Queue<int>();
        for (int c = 0; c < componentCount; c++)
        {
            int v = lowest[c];
            var list = _outgoing[v];
            // gegen den Uhrzeigersinn letzte Richtung in (−90°, 90°]: letzte der oberen Halbebene, sonst letzte insgesamt
            int h = list[^1];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (ExactGeometry.Half(Nodes[v], Nodes[Head(list[i])]) == 0)
                {
                    h = list[i];
                    break;
                }
            }

            for (int k = 0; k < Operands; k++)
            {
                // eine einzige Komponente liegt im Unendlichen außen: Windungszahl 0
                _leftWinding[h, k] = componentCount == 1 ? 0 : RayWinding(Nodes[v], k, component, c);
            }

            assigned[h] = true;
            queue.Enqueue(h);
            while (queue.Count > 0)
            {
                int x = queue.Dequeue();
                Propagate(x, _next[x], 0);
                Propagate(x, x ^ 1, 1);
            }

            void Propagate(int from, int to, int viaTwin)
            {
                if (assigned[to]) return;
                for (int k = 0; k < Operands; k++)
                {
                    _leftWinding[to, k] = viaTwin == 0 ? _leftWinding[from, k] : _leftWinding[from, k] - Winding(from, k);
                }

                assigned[to] = true;
                queue.Enqueue(to);
            }
        }
    }

    /// <summary>
    /// Windungszahl des Operanden an einem Punkt knapp links oberhalb von <paramref name="p"/>, gezählt über alle
    /// Kanten außerhalb der Komponente <paramref name="excluded"/> (−1 = alle), die einen Strahl nach links kreuzen.
    /// Kandidaten sind nur die Kanten, deren Y-Bereich die Höhe von <paramref name="p"/> enthält (Segmentbaum).
    /// </summary>
    public int RayWinding(T p, int operand, int[]? component = null, int excluded = -1)
    {
        int sum = 0;
        _yIndex ??= new YIndex(this);
        foreach (int e in _yIndex.Stab(p))
        {
            var edge = Edges[e];
            int w = edge.Winding[operand];
            if (w == 0 || (component is not null && component[e] == excluded)) continue;

            T a = Nodes[edge.A], b = Nodes[edge.B];
            int dy = T.CompareY(a, b);
            if (dy == 0) continue;
            var (lo, hi, downward) = dy < 0 ? (a, b, false) : (b, a, true);
            if (T.CompareY(lo, p) > 0 || T.CompareY(hi, p) <= 0) continue;
            if (T.Orient(lo, hi, p) >= 0) continue; // p liegt links der aufwärts gerichteten Kante: Kante rechts von p

            sum += downward ? w : -w;
        }

        return sum;
    }

    private YIndex? _yIndex;

    /// <summary>
    /// Segmentbaum über die sortierten, verschiedenen Y-Werte der Knoten: jede Kante mit Windungsbeitrag liegt in
    /// O(log n) Baumknoten ihres halboffenen Y-Bereichs [unten, oben); eine Abfrage liefert genau die Kanten,
    /// deren Bereich die Höhe enthält. Generisch über <see cref="IExactPoint{T}.CompareY"/>, ohne Rundung.
    /// </summary>
    private sealed class YIndex
    {
        private readonly PlanarGraph<T> _graph;
        private readonly T[] _ys;
        private readonly List<int>?[] _tree;
        private readonly int _size;

        public YIndex(PlanarGraph<T> graph)
        {
            _graph = graph;
            var ys = graph.Nodes.ToArray();
            Array.Sort(ys, (a, b) => T.CompareY(a, b));
            var distinct = new List<T>(ys.Length);
            foreach (var y in ys)
            {
                if (distinct.Count == 0 || T.CompareY(distinct[^1], y) != 0) distinct.Add(y);
            }

            _ys = distinct.ToArray();
            _size = Math.Max(1, _ys.Length);
            _tree = new List<int>?[2 * _size];

            for (int e = 0; e < graph.Edges.Count; e++)
            {
                var edge = graph.Edges[e];
                if (edge.Winding.All(w => w == 0)) continue;
                int ra = LowerBound(graph.Nodes[edge.A]), rb = LowerBound(graph.Nodes[edge.B]);
                if (ra == rb) continue; // waagrecht: kreuzt keinen waagrechten Strahl
                Insert(Math.Min(ra, rb), Math.Max(ra, rb) - 1, e);
            }
        }

        /// <summary>Erste Position mit Y ≥ p.Y.</summary>
        private int LowerBound(T p)
        {
            int lo = 0, hi = _ys.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (T.CompareY(_ys[mid], p) < 0) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        /// <summary>Kante in die Blätter <paramref name="from"/>..<paramref name="to"/> (inklusive) eintragen.</summary>
        private void Insert(int from, int to, int edge)
        {
            for (int l = from + _size, r = to + _size + 1; l < r; l >>= 1, r >>= 1)
            {
                if ((l & 1) == 1) (_tree[l++] ??= []).Add(edge);
                if ((r & 1) == 1) (_tree[--r] ??= []).Add(edge);
            }
        }

        /// <summary>Kanten, deren Y-Bereich [unten, oben) die Höhe von <paramref name="p"/> enthält (Obermenge, genau geprüft wird beim Aufrufer).</summary>
        public IEnumerable<int> Stab(T p)
        {
            int i = LowerBound(p);
            // liegt p.Y zwischen zwei Knotenhöhen, gilt der Bereich der unteren
            int leaf = i < _ys.Length && T.CompareY(_ys[i], p) == 0 ? i : i - 1;
            if (leaf < 0 || leaf >= _ys.Length) yield break;
            for (int node = leaf + _size; node >= 1; node >>= 1)
            {
                if (_tree[node] is { } list)
                {
                    foreach (int e in list) yield return e;
                }
            }
        }
    }

    private int[] Components(out int count)
    {
        var parent = Enumerable.Range(0, Nodes.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        foreach (var e in Edges) parent[Find(e.A)] = Find(e.B);

        var ids = new Dictionary<int, int>();
        var component = new int[Edges.Count];
        for (int e = 0; e < Edges.Count; e++)
        {
            int root = Find(Edges[e].A);
            if (!ids.TryGetValue(root, out int id)) ids[root] = id = ids.Count;
            component[e] = id;
        }

        count = ids.Count;
        return component;
    }
}
