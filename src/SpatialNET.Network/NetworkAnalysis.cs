using System.Diagnostics;
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

/// <summary>What a <see cref="TraceHit{TNodeId, TEdgeId}"/> says about its node (ADR 0051).</summary>
public enum TraceHitKind
{
    /// <summary>The source of the run itself, at cost 0.</summary>
    Source,

    /// <summary>A node the run reached and passed.</summary>
    Reached,

    /// <summary>A node the run reached but did not pass because it is blocked (a blocked source gives only its own hit with this kind).</summary>
    Blocked,

    /// <summary>Another source of the same call: reached, but not passed. It wins over <see cref="Blocked"/> if both apply.</summary>
    OtherSource,
}

/// <summary>A node a run of <see cref="NetworkAnalysis.Trace{TNodeId, TEdgeId}"/> reached, once per source that reaches it.</summary>
/// <param name="Node">The node.</param>
/// <param name="TypeId">Its type.</param>
/// <param name="Source">The source of the run.</param>
/// <param name="Cost">The cost of the cheapest way from <paramref name="Source"/>, under the chosen weight.</param>
/// <param name="Via">The edge over which the node was reached first; the default value for the source itself.</param>
/// <param name="Kind">What the hit is.</param>
public readonly record struct TraceHit<TNodeId, TEdgeId>(TNodeId Node, int TypeId, TNodeId Source, double Cost, TEdgeId? Via, TraceHitKind Kind)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>An edge a run of <see cref="NetworkAnalysis.TraceEdges{TNodeId, TEdgeId}"/> passed or entered.</summary>
/// <param name="Edge">The edge.</param>
/// <param name="Source">The source of the run.</param>
public readonly record struct TracedEdge<TNodeId, TEdgeId>(GraphEdge<TNodeId, TEdgeId> Edge, TNodeId Source)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>The sources that supply a sink (ADR 0051).</summary>
/// <param name="Sink">The sink.</param>
/// <param name="Sources">The sources whose run reaches and passes to the sink, in the order of the sources of the call: empty means not supplied, more than one means multiple supply.</param>
public readonly record struct SinkSupply<TNodeId>(TNodeId Sink, IReadOnlyList<TNodeId> Sources)
    where TNodeId : notnull, IEquatable<TNodeId>;

/// <summary>How far <see cref="NetworkAnalysis.Supply{TNodeId, TEdgeId}"/> is: <paramref name="SourcesDone"/> of <paramref name="SourcesTotal"/> runs.</summary>
public readonly record struct SupplyProgress(long SourcesDone, long SourcesTotal);

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
    /// <remarks>
    /// One search from many start nodes far apart (for example every transformer of a city) runs as one wave through all
    /// of their areas at once. Through a shard cache such as <c>ShardedGraphBase</c> it touches every shard again and
    /// again, and the cache cannot keep up; in memory it keeps one entry per node of the whole network. For work over a
    /// whole network, search from one start node (or one area) at a time, in the order of the shards.
    /// </remarks>
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
        if (from.Equals(to))
        {
            search.LoadGivenNodes([from], nameof(from));
            return new NetworkPath<TNodeId, TEdgeId>(from, to, 0, []);
        }

        var given = search.LoadGivenNodes([from, to], "from, to");
        foreach (int settled in search.Run([given[0]]))
        {
            if (settled == given[1])
            {
                return PathTo(search, given[0], given[1]);
            }
        }

        return null;
    }

    /// <summary>
    /// Runs one search per source (ADR 0051) and streams every node it reaches, in the order of the sources and, within a run,
    /// in order of cost. A node that several sources reach is reported once per source.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="sources">The sources; duplicates are run once. They are also the stops: a run does not pass another source.</param>
    /// <param name="options">Weight, switching state, type filters and maximum cost; <c>null</c> for the defaults.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <remarks>
    /// A run does not pass blocked nodes and other sources; it reports them as <see cref="TraceHitKind.Blocked"/> and
    /// <see cref="TraceHitKind.OtherSource"/>. A blocked source gives only its own hit. A run keeps its memory only until its
    /// area is done. For the shard cache, pass the sources sorted by shard.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="sources"/> is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    /// <exception cref="ArgumentException">While enumerating: a source is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<TraceHit<TNodeId, TEdgeId>> Trace<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sources);
        var resolved = SearchOptions<TNodeId, TEdgeId>.Resolve(options);
        return TraceCore(graph, sources, resolved, cancellationToken);
    }

    /// <summary>
    /// Every edge a run passes or enters (ADR 0051), once per source, in the order of the sources: the edges of a supply area,
    /// including the closing edges of a mesh and the edge that leads to a blocked node or another source.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="sources">The sources; duplicates are run once. They are also the stops.</param>
    /// <param name="options">Weight, switching state, type filters and maximum cost; <c>null</c> for the defaults.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="sources"/> is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    /// <exception cref="ArgumentException">While enumerating: a source is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<TracedEdge<TNodeId, TEdgeId>> TraceEdges<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sources);
        var resolved = SearchOptions<TNodeId, TEdgeId>.Resolve(options);
        return TraceEdgesCore(graph, sources, resolved, cancellationToken);
    }

    /// <summary>
    /// For every sink the sources that supply it (ADR 0051), in the order of the sinks, after all runs. An empty list is a sink
    /// nobody supplies, more than one entry is multiple supply.
    /// </summary>
    /// <param name="graph">The network.</param>
    /// <param name="sources">The sources; duplicates are run once.</param>
    /// <param name="sinks">The sinks; duplicates are reported once.</param>
    /// <param name="options">Switching state and type filters; <c>null</c> for the defaults. The weight must be length and <c>MaxCost</c> is ignored, since the runs are breadth-first searches without costs.</param>
    /// <param name="progress">Runs done of all runs; reported at most every 250 ms and at the end of the last run, on the calling thread.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <remarks>
    /// A sink is supplied by a source if the run of that source reaches and passes to it (hit kind <see cref="TraceHitKind.Source"/>
    /// or <see cref="TraceHitKind.Reached"/>); a blocked sink, or a sink that is another source, is not supplied by that run.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    /// <exception cref="ArgumentException">The weight is not length; while enumerating: a source or a sink is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<SinkSupply<TNodeId>> Supply<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        IEnumerable<TNodeId> sinks,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        IProgress<SupplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sinks);
        var resolved = SearchOptions<TNodeId, TEdgeId>.Resolve(options);
        if (resolved.Weight.Kind != WeightKind.Length)
        {
            throw new ArgumentException($"Supply runs without costs and needs the weight length, not '{resolved.Weight.Name}'.", nameof(options));
        }

        return SupplyCore(graph, sources, sinks, resolved, progress, cancellationToken);
    }

    /// <summary>The sinks no source supplies (ADR 0051), in the order of the sinks; see <see cref="Supply{TNodeId, TEdgeId}"/>.</summary>
    /// <param name="graph">The network.</param>
    /// <param name="sources">The sources; duplicates are run once.</param>
    /// <param name="sinks">The sinks; duplicates are reported once.</param>
    /// <param name="options">As for <see cref="Supply{TNodeId, TEdgeId}"/>.</param>
    /// <param name="progress">As for <see cref="Supply{TNodeId, TEdgeId}"/>.</param>
    /// <param name="cancellationToken">Stops the search between two nodes.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>, or the weight of the options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    /// <exception cref="ArgumentException">The weight is not length; while enumerating: a source or a sink is not in the graph.</exception>
    /// <exception cref="InvalidGraphException">While enumerating: the data cannot be a network.</exception>
    /// <exception cref="OperationCanceledException">While enumerating: the token was cancelled.</exception>
    public static IEnumerable<TNodeId> FindUnsupplied<TNodeId, TEdgeId>(
        this IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        IEnumerable<TNodeId> sinks,
        TraversalOptions<TNodeId, TEdgeId>? options = null,
        IProgress<SupplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        return graph.Supply(sources, sinks, options, progress, cancellationToken)
            .Where(supply => supply.Sources.Count == 0)
            .Select(supply => supply.Sink);
    }

    private static IEnumerable<TraceHit<TNodeId, TEdgeId>> TraceCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        SearchOptions<TNodeId, TEdgeId> options,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var sourceList = Distinct(sources, nameof(sources));
        var stops = sourceList.ToHashSet();
        foreach (var source in sourceList)
        {
            var search = new Search<TNodeId, TEdgeId>(graph, options, cancellationToken) { Stops = stops };
            var start = search.LoadGivenNodes([source], nameof(sources));
            foreach (int settled in search.Run(start))
            {
                var via = search.ViaOf(settled);
                yield return new TraceHit<TNodeId, TEdgeId>(
                    search.IdOf(settled),
                    search.NodeOf(settled).TypeId,
                    source,
                    search.CostOf(settled),
                    via is { } step ? step.Edge : default,
                    KindOf(search, settled));
            }
        }
    }

    private static IEnumerable<TracedEdge<TNodeId, TEdgeId>> TraceEdgesCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        SearchOptions<TNodeId, TEdgeId> options,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var sourceList = Distinct(sources, nameof(sources));
        var stops = sourceList.ToHashSet();
        foreach (var source in sourceList)
        {
            var search = new Search<TNodeId, TEdgeId>(graph, options, cancellationToken) { Stops = stops, CollectEdges = true };
            var start = search.LoadGivenNodes([source], nameof(sources));
            var seen = new HashSet<TEdgeId>();
            foreach (int _ in search.Run(start))
            {
                // the edges of the node settled before this one are recorded now
                foreach (var edge in TakeNew(search, seen))
                {
                    yield return new TracedEdge<TNodeId, TEdgeId>(edge, source);
                }
            }

            foreach (var edge in TakeNew(search, seen))
            {
                yield return new TracedEdge<TNodeId, TEdgeId>(edge, source);
            }
        }
    }

    private static List<GraphEdge<TNodeId, TEdgeId>> TakeNew<TNodeId, TEdgeId>(Search<TNodeId, TEdgeId> search, HashSet<TEdgeId> seen)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var fresh = new List<GraphEdge<TNodeId, TEdgeId>>();
        foreach (var edge in search.TraversedEdges)
        {
            if (seen.Add(edge.Id))
            {
                fresh.Add(edge);
            }
        }

        search.TraversedEdges.Clear();
        return fresh;
    }

    private static IEnumerable<SinkSupply<TNodeId>> SupplyCore<TNodeId, TEdgeId>(
        IGraph<TNodeId, TEdgeId> graph,
        IEnumerable<TNodeId> sources,
        IEnumerable<TNodeId> sinks,
        SearchOptions<TNodeId, TEdgeId> options,
        IProgress<SupplyProgress>? progress,
        CancellationToken cancellationToken)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var sourceList = Distinct(sources, nameof(sources));
        var sinkList = Distinct(sinks, nameof(sinks));

        var sinkPosition = new Dictionary<TNodeId, int>(sinkList.Count);
        for (int i = 0; i < sinkList.Count; i++)
        {
            sinkPosition[sinkList[i]] = i;
        }

        var suppliedBy = new List<TNodeId>?[sinkList.Count];
        var stops = sourceList.ToHashSet();
        var clock = Stopwatch.StartNew();
        long lastReport = long.MinValue;
        for (int done = 0; done < sourceList.Count; done++)
        {
            var source = sourceList[done];
            var search = new Search<TNodeId, TEdgeId>(graph, options, cancellationToken) { Stops = stops };
            var start = search.LoadGivenNodes([source], nameof(sources));
            foreach (int settled in search.RunBreadthFirst(start[0]))
            {
                if (KindOf(search, settled) is TraceHitKind.Source or TraceHitKind.Reached &&
                    sinkPosition.TryGetValue(search.IdOf(settled), out int position))
                {
                    (suppliedBy[position] ??= []).Add(source);
                }
            }

            if (progress is not null)
            {
                long now = clock.ElapsedMilliseconds;
                if (done + 1 == sourceList.Count || now - lastReport >= 250)
                {
                    lastReport = now;
                    progress.Report(new SupplyProgress(done + 1, sourceList.Count));
                }
            }
        }

        // a supplied sink is in the graph; only the others can be unknown ids
        var unsupplied = new List<TNodeId>();
        for (int i = 0; i < sinkList.Count; i++)
        {
            if (suppliedBy[i] is null)
            {
                unsupplied.Add(sinkList[i]);
            }
        }

        RequireNodes(graph, unsupplied, nameof(sinks));
        for (int i = 0; i < sinkList.Count; i++)
        {
            yield return new SinkSupply<TNodeId>(sinkList[i], suppliedBy[i] ?? (IReadOnlyList<TNodeId>)[]);
        }
    }

    private static TraceHitKind KindOf<TNodeId, TEdgeId>(Search<TNodeId, TEdgeId> search, int node)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        if (search.IsStart(node))
        {
            return search.Blocked(node) ? TraceHitKind.Blocked : TraceHitKind.Source;
        }

        return search.IsStop(node) ? TraceHitKind.OtherSource : search.Blocked(node) ? TraceHitKind.Blocked : TraceHitKind.Reached;
    }

    /// <summary>Checks, in batches, that the graph has all of the nodes.</summary>
    private static void RequireNodes<TNodeId, TEdgeId>(IGraph<TNodeId, TEdgeId> graph, IReadOnlyList<TNodeId> ids, string paramName)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        for (int offset = 0; offset < ids.Count; offset += Search<TNodeId, TEdgeId>.BatchSize)
        {
            var chunk = new TNodeId[Math.Min(Search<TNodeId, TEdgeId>.BatchSize, ids.Count - offset)];
            for (int i = 0; i < chunk.Length; i++)
            {
                chunk[i] = ids[offset + i];
            }

            var found = new HashSet<TNodeId>();
            foreach (var node in graph.GetNodes(chunk))
            {
                found.Add(node.Id);
            }

            foreach (var id in chunk)
            {
                if (!found.Contains(id))
                {
                    throw new ArgumentException($"The node {id} is not in the graph.", paramName);
                }
            }
        }
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
        var startIndices = search.LoadGivenNodes(startList, nameof(starts));
        foreach (int settled in search.Run(startIndices))
        {
            var via = search.ViaOf(settled);
            yield return new ReachedNode<TNodeId, TEdgeId>(
                search.IdOf(settled),
                search.NodeOf(settled).TypeId,
                search.IdOf(search.StartOf(settled)),
                search.CostOf(settled),
                via is { } step ? step.Edge : default,
                search.IsStart(settled),
                search.Blocked(settled));
        }
    }

    private static NetworkPath<TNodeId, TEdgeId> PathTo<TNodeId, TEdgeId>(Search<TNodeId, TEdgeId> search, int from, int to)
        where TNodeId : notnull, IEquatable<TNodeId>
        where TEdgeId : notnull, IEquatable<TEdgeId>
    {
        var steps = new List<PathStep<TNodeId, TEdgeId>>();
        for (int node = to; node != from;)
        {
            var (edge, previous, cost, reversed) = search.ViaOf(node)!.Value;
            steps.Add(new PathStep<TNodeId, TEdgeId>(edge, search.IdOf(previous), search.IdOf(node), reversed, cost));
            node = previous;
        }

        steps.Reverse();
        return new NetworkPath<TNodeId, TEdgeId>(search.IdOf(from), search.IdOf(to), search.CostOf(to), steps);
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
