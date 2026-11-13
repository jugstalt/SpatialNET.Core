using SpatialNET.Imp;
using SpatialNET.Network;

namespace SpatialNET.Tests.Networks;

/// <summary>
/// The reference for tracing (ADR 0051): per source a plain breadth-first search for who is reached, and Bellman-Ford over all
/// edges for the costs. No code of the library is used.
/// </summary>
internal static class TraceReference
{
    /// <summary>What a run from <c>source</c> does with the node: reached, and then of which kind.</summary>
    public static Dictionary<int, TraceHitKind> Kinds(InMemoryGraph<int, int, string> graph, int source, ISet<int> sources, TraversalOptions<int, int> options)
    {
        var kinds = new Dictionary<int, TraceHitKind>();
        var queue = new Queue<int>();
        kinds[source] = KindOf(graph, source, source, sources, options);
        queue.Enqueue(source);
        while (queue.TryDequeue(out int u))
        {
            if (!Expands(graph, u, source, sources, options))
            {
                continue;
            }

            foreach (var (edge, v) in Steps(graph, u, options))
            {
                if (!kinds.ContainsKey(v) && Enters(graph, v, source, options))
                {
                    kinds[v] = KindOf(graph, v, source, sources, options);
                    queue.Enqueue(v);
                }
            }
        }

        return kinds;
    }

    /// <summary>The cheapest cost from <c>source</c> to every node the run reaches (Bellman-Ford over all edges).</summary>
    public static Dictionary<int, double> Costs(InMemoryGraph<int, int, string> graph, int source, ISet<int> sources, TraversalOptions<int, int> options)
    {
        var cost = new Dictionary<int, double> { [source] = 0 };
        for (int round = 0; round <= graph.Nodes.Count(); round++)
        {
            bool changed = false;
            foreach (var u in cost.Keys.ToList())
            {
                if (!Expands(graph, u, source, sources, options))
                {
                    continue;
                }

                foreach (var (edge, v) in Steps(graph, u, options))
                {
                    double c = cost[u] + edge.Length;
                    if (Enters(graph, v, source, options) && c <= options.MaxCost && (!cost.TryGetValue(v, out double old) || c < old))
                    {
                        cost[v] = c;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return cost;
    }

    /// <summary>Every edge that may be walked from an expanded node of the run to a node it enters, within <c>MaxCost</c>.</summary>
    public static HashSet<int> Edges(InMemoryGraph<int, int, string> graph, int source, ISet<int> sources, TraversalOptions<int, int> options)
    {
        var edges = new HashSet<int>();
        foreach (var (u, cost) in Costs(graph, source, sources, options))
        {
            if (!Expands(graph, u, source, sources, options))
            {
                continue;
            }

            foreach (var (edge, v) in Steps(graph, u, options))
            {
                if (Enters(graph, v, source, options) && cost + edge.Length <= options.MaxCost)
                {
                    edges.Add(edge.Id);
                }
            }
        }

        return edges;
    }

    /// <summary>The sources that supply a sink: the runs that reach and pass to it, in the order of the (distinct) sources.</summary>
    public static Dictionary<int, List<int>> Supply(InMemoryGraph<int, int, string> graph, IEnumerable<int> sources, IEnumerable<int> sinks, TraversalOptions<int, int> options)
    {
        var sourceList = sources.Distinct().ToList();
        var sourceSet = sourceList.ToHashSet();
        var supply = sinks.Distinct().ToDictionary(s => s, _ => new List<int>());
        foreach (int source in sourceList)
        {
            foreach (var (node, kind) in Kinds(graph, source, sourceSet, options))
            {
                if (kind is TraceHitKind.Source or TraceHitKind.Reached && supply.TryGetValue(node, out var list))
                {
                    list.Add(source);
                }
            }
        }

        return supply;
    }

    public static TraceHitKind KindOf(InMemoryGraph<int, int, string> graph, int node, int source, ISet<int> sources, TraversalOptions<int, int> options)
    {
        bool blocked = IsBlocked(graph, node, options);
        if (node == source)
        {
            return blocked ? TraceHitKind.Blocked : TraceHitKind.Source;
        }

        return sources.Contains(node) ? TraceHitKind.OtherSource : blocked ? TraceHitKind.Blocked : TraceHitKind.Reached;
    }

    /// <summary>A node is passed unless it is blocked or another source.</summary>
    private static bool Expands(InMemoryGraph<int, int, string> graph, int node, int source, ISet<int> sources, TraversalOptions<int, int> options) =>
        !IsBlocked(graph, node, options) && (node == source || !sources.Contains(node));

    private static bool IsBlocked(InMemoryGraph<int, int, string> graph, int node, TraversalOptions<int, int> options)
    {
        var record = graph.Nodes.Single(n => n.Id == node);
        return options.State?.IsBlocked(record) ?? record.Blocked;
    }

    private static bool Enters(InMemoryGraph<int, int, string> graph, int node, int source, TraversalOptions<int, int> options)
    {
        var record = graph.Nodes.Single(n => n.Id == node);
        return node == source || options.NodeTypes is null || record.TypeId == 0 || options.NodeTypes.Contains(record.TypeId);
    }

    /// <summary>The edges that may be walked from the node, with the node at the other end.</summary>
    private static IEnumerable<(GraphEdge<int, int> Edge, int To)> Steps(InMemoryGraph<int, int, string> graph, int node, TraversalOptions<int, int> options)
    {
        foreach (var edge in graph.Edges)
        {
            if (edge.From == edge.To || (options.EdgeTypes is { } types && !types.Contains(edge.TypeId)) ||
                (options.State?.IsDisabled(edge) ?? edge.Disabled))
            {
                continue;
            }

            var direction = options.State?.GetDirection(edge) ?? edge.Direction;
            if (edge.From == node && direction != EdgeDirection.Backward)
            {
                yield return (edge, edge.To);
            }

            if (edge.To == node && direction != EdgeDirection.Forward)
            {
                yield return (edge, edge.From);
            }
        }
    }
}
