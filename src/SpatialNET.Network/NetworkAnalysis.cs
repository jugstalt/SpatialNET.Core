using SpatialNET.Network.Internal;

namespace SpatialNET.Network;

/// <summary>A node that <see cref="NetworkAnalysis.Reach{TNodeId, TEdgeId}"/> reached.</summary>
/// <param name="Node">The node.</param>
/// <param name="TypeId">Its type.</param>
/// <param name="Start">The start node of the cheapest way to it.</param>
/// <param name="Cost">The cost of that way.</param>
/// <param name="Via">The edge over which it was reached; the default value for a start node (see <paramref name="IsStart"/>).</param>
/// <param name="IsStart">Whether the node is one of the start nodes.</param>
/// <param name="Blocked">Whether the node is blocked: reached, but not passed.</param>
public readonly record struct ReachedNode<TNodeId, TEdgeId>(TNodeId Node, int TypeId, TNodeId Start, double Cost, TEdgeId? Via, bool IsStart, bool Blocked)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>One edge of a <see cref="NetworkPath{TNodeId, TEdgeId}"/>.</summary>
/// <param name="Edge">The edge.</param>
/// <param name="From">The node where the step starts.</param>
/// <param name="To">The node where the step ends.</param>
/// <param name="Reversed">Whether the edge was walked against its digitizing direction (from its <c>To</c> to its <c>From</c>).</param>
/// <param name="Cost">The cost of the edge.</param>
public readonly record struct PathStep<TNodeId, TEdgeId>(TEdgeId Edge, TNodeId From, TNodeId To, bool Reversed, double Cost)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>The cheapest way between two nodes, as a sequence of edges (ADR 0049).</summary>
/// <param name="From">The first node.</param>
/// <param name="To">The last node.</param>
/// <param name="Cost">The sum of the costs of the steps.</param>
/// <param name="Steps">The edges in order; empty if <paramref name="From"/> and <paramref name="To"/> are the same node.</param>
public sealed record NetworkPath<TNodeId, TEdgeId>(TNodeId From, TNodeId To, double Cost, IReadOnlyList<PathStep<TNodeId, TEdgeId>> Steps)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>
/// Searches on a network (ADR 0049), as extension methods on <see cref="IGraph{TNodeId, TEdgeId}"/>. They read only the
/// part of the network they visit, in batches, and keep one entry per visited node.
/// </summary>
/// <remarks>
/// <para>Blocked nodes are reached but not passed, disabled edges are left out, directions are respected (see
/// <see cref="TraversalOptions{TNodeId, TEdgeId}"/>). Loops are never walked. Equal costs are decided by the order of the
/// start nodes and of the edges from the host, so the same input gives the same output.</para>
/// <para>Thread-safe as far as the graph is: a search has no shared state.</para>
/// </remarks>
public static class NetworkAnalysis
{
    /// <summary>
    /// Every node that can be reached from the start nodes, in order of cost, lazily: stop early or stream into a table.
    /// Each node is reported once, with the start node of its cheapest way.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="starts">The start nodes; duplicates are used once.</param>
    /// <param name="options">Weight, switching state, type filters and maximum cost; <c>null</c> for the defaults.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="starts"/> is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid (fallback or <c>MaxCost</c> negative or NaN).</exception>
    /// <exception cref="ArgumentException">While enumerating: a start node is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<ReachedNode<TNodeId, TEdgeId>> Reach<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> starts,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(starts);
        var resolved = SearchOptions<TNodeId, TEdgeId>.Resolve(options);
        return ReachCore(graph, starts, resolved, cancellationToken);
    }

    /// <summary>The cheapest way from one node to another (Dijkstra), or <c>null</c> if there is none.</summary>
    /// <param name="graph">The network.</param>
    /// <param name="from">The start node. A blocked start node is not passed, so it reaches only itself.</param>
    /// <param name="to">The target node; it may be blocked, since it is only reached.</param>
    /// <param name="options">Weight, switching state, type filters and maximum cost; <c>null</c> for the defaults.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="from"/> or <paramref name="to"/> is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">The data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static NetworkPath<TNodeId, TEdgeId>? ShortestPath<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        TNodeId from,
        TNodeId to,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var resolved = SearchOptions<TNodeId, TEdgeId>.Resolve(options);

        var search = new Search<TNodeId, TEdgeId>(graph, resolved, cancellationToken);
        search.View.LoadGivenNodes(from.Equals(to) ? [from] : [from, to], from.Equals(to) ? nameof(from) : "from, to");
        if (from.Equals(to))
        {
            return new NetworkPath<TNodeId, TEdgeId>(from, to, 0, []);
        }

        foreach (var settled in search.Run([from]))
        {
            if (settled.Id.Equals(to))
            {
                return PathTo(search.Labels, from, to);
            }
        }

        return null;
    }

    private static IEnumerable<ReachedNode<TNodeId, TEdgeId>> ReachCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> starts,
        SearchOptions<TNodeId, TEdgeId> options,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var startList = Distinct(starts, nameof(starts));
        var search = new Search<TNodeId, TEdgeId>(graph, options, cancellationToken);
        search.View.LoadGivenNodes(startList, nameof(starts));
        foreach (var settled in search.Run(startList))
        {
            var label = settled.Label;
            yield return new ReachedNode<TNodeId, TEdgeId>(
                settled.Id, settled.Node.TypeId, label.Start, label.Cost, label.Via, label.IsStart, settled.Blocked);
        }
    }

    private static NetworkPath<TNodeId, TEdgeId> PathTo<TNodeId, TEdgeId>(
        Dictionary<TNodeId, Label<TNodeId, TEdgeId>> labels, TNodeId from, TNodeId to)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var steps = new List<PathStep<TNodeId, TEdgeId>>();
        var node = to;
        while (!node.Equals(from))
        {
            var label = labels[node];
            steps.Add(new PathStep<TNodeId, TEdgeId>(label.Via!, label.ViaFrom!, node, label.Reversed, label.ViaCost));
            node = label.ViaFrom!;
        }

        steps.Reverse();
        return new NetworkPath<TNodeId, TEdgeId>(from, to, labels[to].Cost, steps);
    }

    private static List<TNodeId> Distinct<TNodeId>(IEnumerable<TNodeId> ids, string paramName)
        where TNodeId : notnull, IEquatable<TNodeId>
    {
        var seen = new HashSet<TNodeId>();
        var list = new List<TNodeId>();
        foreach (var id in ids)
        {
            if (id is null)
            {
                throw new ArgumentNullException(paramName, "A node id is null.");
            }

            if (seen.Add(id))
            {
                list.Add(id);
            }
        }

        return list;
    }
}
