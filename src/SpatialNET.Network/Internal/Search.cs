using System.Runtime.InteropServices;

namespace SpatialNET.Network.Internal;

/// <summary>The options of a search after the checks, with the normal state in place of <c>null</c>.</summary>
internal sealed record SearchOptions<TNodeId, TEdgeId>(
    WeightDefinition Weight,
    INetworkState<TNodeId, TEdgeId> State,
    TypeSet? EdgeTypes,
    TypeSet? NodeTypes,
    double MaxCost)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>Checks the options and fills in the defaults.</summary>
    /// <exception cref="ArgumentNullException">The weight is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The weight kind, its fallback or <c>MaxCost</c> is invalid.</exception>
    public static SearchOptions<TNodeId, TEdgeId> Resolve(TraversalOptions<TNodeId, TEdgeId>? options)
    {
        options ??= new TraversalOptions<TNodeId, TEdgeId>();
        var weight = options.Weight ?? throw new ArgumentNullException(nameof(options), "The weight of the options is null.");
        if (weight.Kind is not (WeightKind.Length or WeightKind.Absolute or WeightKind.PerLength or WeightKind.LengthOverValue))
        {
            throw new ArgumentOutOfRangeException(nameof(options), weight.Kind, $"The weight '{weight.Name}' has an unknown kind.");
        }

        if (weight.Fallback is { } fallback &&
            (!double.IsFinite(fallback) || fallback < 0 || (weight.Kind == WeightKind.LengthOverValue && fallback == 0)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), fallback,
                $"The fallback of the weight '{weight.Name}' must be finite and not negative (greater than 0 for {nameof(WeightKind.LengthOverValue)}).");
        }

        if (double.IsNaN(options.MaxCost) || options.MaxCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxCost, "MaxCost must not be negative or NaN.");
        }

        return new SearchOptions<TNodeId, TEdgeId>(
            weight, options.State ?? NormalState<TNodeId, TEdgeId>.Instance, options.EdgeTypes, options.NodeTypes, options.MaxCost);
    }
}

/// <summary>The normal switching state: exactly what the records say.</summary>
internal sealed class NormalState<TNodeId, TEdgeId> : INetworkState<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    public static NormalState<TNodeId, TEdgeId> Instance { get; } = new();

    public bool IsBlocked(in GraphNode<TNodeId> node) => node.Blocked;

    public bool IsDisabled(in GraphEdge<TNodeId, TEdgeId> edge) => edge.Disabled;

    public EdgeDirection GetDirection(in GraphEdge<TNodeId, TEdgeId> edge) => edge.Direction;
}

/// <summary>The cost of an edge under a weight (ADR 0049).</summary>
internal static class EdgeCosts
{
    /// <exception cref="InvalidGraphException">The value is missing without a fallback, or the cost is negative, NaN or infinite.</exception>
    public static double Of<TNodeId, TEdgeId>(in GraphEdge<TNodeId, TEdgeId> edge, WeightDefinition weight)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        double cost = weight.Kind switch
        {
            WeightKind.Length => edge.Length,
            WeightKind.Absolute => ValueOf(edge, weight),
            WeightKind.PerLength => ValueOf(edge, weight) * edge.Length,
            _ => LengthOverValue(edge, weight),
        };

        if (!double.IsFinite(cost) || cost < 0)
        {
            throw new InvalidGraphException(
                $"The edge {edge.Id} costs {cost} under the weight '{weight.Name}' (length {edge.Length}); a cost must be finite and not negative.");
        }

        return cost;
    }

    private static double LengthOverValue<TNodeId, TEdgeId>(in GraphEdge<TNodeId, TEdgeId> edge, WeightDefinition weight)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        double value = ValueOf(edge, weight);
        if (!(value > 0))
        {
            throw new InvalidGraphException(
                $"The edge {edge.Id} has the value {value} for the weight '{weight.Name}'; a value of kind {nameof(WeightKind.LengthOverValue)} must be greater than 0.");
        }

        return edge.Length / value;
    }

    private static double ValueOf<TNodeId, TEdgeId>(in GraphEdge<TNodeId, TEdgeId> edge, WeightDefinition weight)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        foreach (var w in edge.Weights.Span)
        {
            if (w.WeightId == weight.Id)
            {
                return w.Value;
            }
        }

        return weight.Fallback ?? throw new InvalidGraphException(
            $"The edge {edge.Id} has no value for the weight '{weight.Name}' ({weight.Id}), and the weight has no fallback.");
    }
}

/// <summary>
/// Dijkstra from one or more start nodes on the part of a network it has seen (ADR 0049).
/// </summary>
/// <remarks>
/// <para>Loading: when the edges of a node are needed, the edges of up to <see cref="BatchSize"/> discovered nodes are asked
/// for at once, then every new node at their ends in one more call.</para>
/// <para>Memory: one struct per known node in an array, and the edges of the loaded nodes in one shared list with the index
/// of the node at the other end, so that the search loop looks nothing up by id.</para>
/// <para>Order: equal costs are decided by the order in which nodes were discovered, which follows the order of the start
/// nodes and of the edges from the host. Blocked nodes are settled, but not expanded.</para>
/// </remarks>
internal sealed class Search<TNodeId, TEdgeId>(IGraph<TNodeId, TEdgeId> graph, SearchOptions<TNodeId, TEdgeId> options, CancellationToken cancellationToken)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>How many nodes one call of <c>GetEdges</c> asks for at most; the benchmark decides the value (ADR 0049).</summary>
    internal const int BatchSize = 1024;

    private const byte Loaded = 1, Pending = 2, Reached = 4, Done = 8, Start = 16, Reversed = 32, IsBlocked = 64;

    private readonly Dictionary<TNodeId, int> _index = [];
    private readonly List<Adjacent> _adjacent = [];
    private readonly Queue<int> _pending = new();
    private NodeState[] _nodes = new NodeState[64];
    private int _count;

    // reused by Load
    private readonly List<int> _batch = [];
    private readonly Dictionary<TNodeId, int> _batchPosition = [];
    private readonly List<GraphEdge<TNodeId, TEdgeId>> _received = [];
    private readonly List<TNodeId> _missing = [];
    private readonly Dictionary<TNodeId, TEdgeId> _firstEdge = [];

    public TNodeId IdOf(int node) => _nodes[node].Node.Id;

    public ref readonly GraphNode<TNodeId> NodeOf(int node) => ref _nodes[node].Node;

    public double CostOf(int node) => _nodes[node].Cost;

    public int StartOf(int node) => _nodes[node].Start;

    public bool IsStart(int node) => (_nodes[node].Flags & Start) != 0;

    public bool Blocked(int node) => (_nodes[node].Flags & IsBlocked) != 0;

    /// <summary>The step by which a reached node was reached: the edge, the node before it, the edge's cost and its direction.</summary>
    public (TEdgeId Edge, int From, double Cost, bool Reversed)? ViaOf(int node)
    {
        ref readonly var state = ref _nodes[node];
        return (state.Flags & Start) != 0
            ? null
            : (_adjacent[state.ViaEdge].Edge.Id, state.ViaFrom, state.ViaCost, (state.Flags & Reversed) != 0);
    }

    /// <summary>Loads the records of the nodes the caller named (start, target) and returns their indices.</summary>
    /// <exception cref="ArgumentException">A node is not in the graph.</exception>
    public int[] LoadGivenNodes(IReadOnlyList<TNodeId> ids, string paramName)
    {
        var missing = ids.Where(id => !_index.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var asked = missing.ToHashSet();
            foreach (var node in graph.GetNodes(missing))
            {
                if (asked.Contains(node.Id) && !_index.ContainsKey(node.Id))
                {
                    Add(node);
                }
            }

            foreach (var id in missing)
            {
                if (!_index.ContainsKey(id))
                {
                    throw new ArgumentException($"The node {id} is not in the graph.", paramName);
                }
            }
        }

        return ids.Select(id => _index[id]).ToArray();
    }

    /// <summary>Settles the nodes in order of cost and yields their indices. The start nodes come from <see cref="LoadGivenNodes"/>.</summary>
    public IEnumerable<int> Run(IReadOnlyList<int> starts)
    {
        var heap = new PriorityQueue<int, (double Cost, long Sequence)>();
        long sequence = 0;
        foreach (int start in starts)
        {
            ref var state = ref _nodes[start];
            state.Cost = 0;
            state.Start = start;
            state.Flags |= Reached | Start;
            state.Sequence = sequence;
            heap.Enqueue(start, (0, sequence++));
            MarkPending(start);
        }

        while (heap.TryDequeue(out int u, out var priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((_nodes[u].Flags & Done) != 0 || _nodes[u].Sequence != priority.Sequence)
            {
                continue; // an older entry of a node whose cost has since dropped
            }

            _nodes[u].Flags |= Done;
            bool blocked = options.State.IsBlocked(_nodes[u].Node);
            if (blocked)
            {
                _nodes[u].Flags |= IsBlocked;
            }

            yield return u;
            if (blocked)
            {
                continue;
            }

            if ((_nodes[u].Flags & Loaded) == 0)
            {
                Load(u);
            }

            // no loads below, so the arrays do not move while the edges of u are relaxed
            double uCost = _nodes[u].Cost;
            int uStart = _nodes[u].Start, first = _nodes[u].EdgeStart, end = first + _nodes[u].EdgeCount;
            for (int i = first; i < end; i++)
            {
                var adjacent = _adjacent[i];
                if ((options.EdgeTypes is { } edgeTypes && !edgeTypes.Contains(adjacent.Edge.TypeId)) || options.State.IsDisabled(adjacent.Edge))
                {
                    continue;
                }

                var direction = options.State.GetDirection(adjacent.Edge);
                if (adjacent.Forward ? direction == EdgeDirection.Backward : direction == EdgeDirection.Forward)
                {
                    continue;
                }

                ref var v = ref _nodes[adjacent.Other];
                if ((v.Flags & Done) != 0)
                {
                    continue;
                }

                if (options.NodeTypes is { } nodeTypes && (v.Flags & Start) == 0 && v.Node.TypeId != 0 && !nodeTypes.Contains(v.Node.TypeId))
                {
                    continue;
                }

                double edgeCost = EdgeCosts.Of(adjacent.Edge, options.Weight);
                double cost = uCost + edgeCost;
                bool known = (v.Flags & Reached) != 0;
                if (cost > options.MaxCost || (known && cost >= v.Cost))
                {
                    continue;
                }

                if (!known)
                {
                    v.Flags |= Reached;
                    MarkPending(adjacent.Other);
                }

                v.Cost = cost;
                v.Start = uStart;
                v.ViaEdge = i;
                v.ViaFrom = u;
                v.ViaCost = edgeCost;
                v.Flags = adjacent.Forward ? (byte)(v.Flags & ~Reversed) : (byte)(v.Flags | Reversed);
                v.Sequence = sequence;
                heap.Enqueue(adjacent.Other, (cost, sequence++));
            }
        }
    }

    private int Add(in GraphNode<TNodeId> node)
    {
        if (_count == _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
        }

        _nodes[_count] = new NodeState { Node = node };
        _index[node.Id] = _count;
        return _count++;
    }

    private void MarkPending(int node)
    {
        ref var state = ref _nodes[node];
        if ((state.Flags & (Loaded | Pending)) == 0)
        {
            state.Flags |= Pending;
            _pending.Enqueue(node);
        }
    }

    private void Load(int first)
    {
        _batch.Clear();
        _batchPosition.Clear();
        _batch.Add(first);
        _batchPosition[_nodes[first].Node.Id] = 0;
        while (_batch.Count < BatchSize && _pending.TryDequeue(out int node))
        {
            if ((_nodes[node].Flags & Loaded) == 0 && _batchPosition.TryAdd(_nodes[node].Node.Id, _batch.Count))
            {
                _batch.Add(node);
            }
        }

        var ids = new TNodeId[_batch.Count];
        for (int b = 0; b < _batch.Count; b++)
        {
            ids[b] = _nodes[_batch[b]].Node.Id;
        }

        // first pass: count the edges of each asked node and collect the ends that are not known yet
        _received.Clear();
        _missing.Clear();
        _firstEdge.Clear();
        var counts = new int[_batch.Count];
        foreach (var edge in graph.GetEdges(ids))
        {
            if (edge.From.Equals(edge.To))
            {
                continue; // a loop is never walked
            }

            bool fromAsked = _batchPosition.TryGetValue(edge.From, out int fromPosition);
            bool toAsked = _batchPosition.TryGetValue(edge.To, out int toPosition);
            if (!fromAsked && !toAsked)
            {
                continue; // the host returned an edge nobody asked for
            }

            if (fromAsked)
            {
                counts[fromPosition]++;
            }

            if (toAsked)
            {
                counts[toPosition]++;
            }

            _received.Add(edge);
            Need(edge.From, edge.Id);
            Need(edge.To, edge.Id);
        }

        if (_missing.Count > 0)
        {
            foreach (var node in graph.GetNodes(_missing))
            {
                if (_firstEdge.ContainsKey(node.Id) && !_index.ContainsKey(node.Id))
                {
                    Add(node);
                }
            }

            foreach (var id in _missing)
            {
                if (!_index.ContainsKey(id))
                {
                    throw new InvalidGraphException($"The edge {_firstEdge[id]} points to the node {id}, which the graph does not return.");
                }
            }
        }

        // second pass: one run of the shared edge list per asked node, in the order of the host
        int offset = _adjacent.Count, total = 0;
        var next = new int[_batch.Count];
        for (int b = 0; b < _batch.Count; b++)
        {
            ref var state = ref _nodes[_batch[b]];
            state.EdgeStart = offset + total;
            state.EdgeCount = counts[b];
            state.Flags = (byte)((state.Flags | Loaded) & ~Pending);
            next[b] = state.EdgeStart;
            total += counts[b];
        }

        CollectionsMarshal.SetCount(_adjacent, offset + total);
        var slots = CollectionsMarshal.AsSpan(_adjacent);
        foreach (var edge in _received)
        {
            int from = _index[edge.From], to = _index[edge.To];
            if (_batchPosition.TryGetValue(edge.From, out int fromPosition))
            {
                slots[next[fromPosition]++] = new Adjacent(edge, to, Forward: true);
            }

            if (_batchPosition.TryGetValue(edge.To, out int toPosition))
            {
                slots[next[toPosition]++] = new Adjacent(edge, from, Forward: false);
            }
        }

        void Need(TNodeId node, TEdgeId edge)
        {
            if (!_index.ContainsKey(node) && _firstEdge.TryAdd(node, edge))
            {
                _missing.Add(node);
            }
        }
    }

    /// <summary>What a search knows about one node.</summary>
    private struct NodeState
    {
        public GraphNode<TNodeId> Node;
        public double Cost;
        public double ViaCost;
        public long Sequence;
        public int EdgeStart;
        public int EdgeCount;
        public int Start;
        public int ViaEdge;
        public int ViaFrom;
        public byte Flags;
    }

    /// <summary>An edge of a loaded node, with the index of the node at the other end and whether it leaves by <c>From</c>.</summary>
    private readonly record struct Adjacent(GraphEdge<TNodeId, TEdgeId> Edge, int Other, bool Forward);
}
