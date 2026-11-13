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

/// <summary>What a search knows about a node it reached.</summary>
internal sealed class Label<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    public double Cost;
    public TNodeId Start = default!;
    public bool IsStart;
    public TEdgeId? Via;
    public TNodeId? ViaFrom;
    public double ViaCost;
    public bool Reversed;
    public bool Done;
    public long Sequence;
}

/// <summary>A node whose cost is final, in the order the search settles them.</summary>
internal readonly record struct Settled<TNodeId, TEdgeId>(TNodeId Id, GraphNode<TNodeId> Node, Label<TNodeId, TEdgeId> Label, bool Blocked)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>
/// Dijkstra from one or more start nodes with a binary heap (ADR 0049). Equal costs are decided by the order in which nodes
/// were discovered, which follows the order of the start nodes and of the edges from the host, so the result is
/// deterministic. Blocked nodes are settled, but not expanded.
/// </summary>
internal sealed class Search<TNodeId, TEdgeId>(IGraph<TNodeId, TEdgeId> graph, SearchOptions<TNodeId, TEdgeId> options, CancellationToken cancellationToken)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    public GraphView<TNodeId, TEdgeId> View { get; } = new(graph);

    public Dictionary<TNodeId, Label<TNodeId, TEdgeId>> Labels { get; } = [];

    /// <summary>Settles the nodes in order of cost. The start nodes must be loaded with <see cref="GraphView{TNodeId, TEdgeId}.LoadGivenNodes"/>.</summary>
    public IEnumerable<Settled<TNodeId, TEdgeId>> Run(IReadOnlyList<TNodeId> starts)
    {
        var heap = new PriorityQueue<TNodeId, (double Cost, long Sequence)>();
        long sequence = 0;
        foreach (var start in starts)
        {
            var label = new Label<TNodeId, TEdgeId> { Cost = 0, Start = start, IsStart = true, Sequence = sequence };
            Labels[start] = label;
            heap.Enqueue(start, (0, sequence++));
            View.MarkPending(start);
        }

        while (heap.TryDequeue(out var u, out var priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = Labels[u];
            if (label.Done || label.Sequence != priority.Sequence)
            {
                continue; // an older entry of a node whose cost has since dropped
            }

            label.Done = true;
            var node = View.Node(u);
            bool blocked = options.State.IsBlocked(node);
            yield return new Settled<TNodeId, TEdgeId>(u, node, label, blocked);
            if (blocked)
            {
                continue;
            }

            foreach (var edge in View.EdgesOf(u))
            {
                if (edge.From.Equals(edge.To) ||
                    (options.EdgeTypes is { } edgeTypes && !edgeTypes.Contains(edge.TypeId)) ||
                    options.State.IsDisabled(edge))
                {
                    continue;
                }

                bool forward = edge.From.Equals(u);
                var direction = options.State.GetDirection(edge);
                if (forward ? direction == EdgeDirection.Backward : direction == EdgeDirection.Forward)
                {
                    continue;
                }

                var v = forward ? edge.To : edge.From;
                Labels.TryGetValue(v, out var known);
                if (known is { Done: true })
                {
                    continue;
                }

                if (options.NodeTypes is { } nodeTypes && known is not { IsStart: true })
                {
                    int type = View.Node(v).TypeId;
                    if (type != 0 && !nodeTypes.Contains(type))
                    {
                        continue;
                    }
                }

                double edgeCost = EdgeCosts.Of(edge, options.Weight);
                double cost = label.Cost + edgeCost;
                if (cost > options.MaxCost || (known is not null && cost >= known.Cost))
                {
                    continue;
                }

                if (known is null)
                {
                    known = new Label<TNodeId, TEdgeId>();
                    Labels[v] = known;
                    View.MarkPending(v);
                }

                known.Cost = cost;
                known.Start = label.Start;
                known.Via = edge.Id;
                known.ViaFrom = u;
                known.ViaCost = edgeCost;
                known.Reversed = !forward;
                known.Sequence = sequence;
                heap.Enqueue(v, (cost, sequence++));
            }
        }
    }
}
