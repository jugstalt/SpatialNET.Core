using SpatialNET.Imp;
using SpatialNET.Network;

namespace SpatialNET.Tests.Networks;

/// <summary>Kleine Netze von Hand, Zufallsnetze und die Referenz für die Suchen (ADR 0049): Bellman-Ford über alle Kanten.</summary>
internal static class NetworkTestKit
{
    public static GraphNode<int> N(int id, int type = 0, bool blocked = false) => new(id, type, new Coordinate(id, 0), blocked);

    public static GraphEdge<int, int> E(int id, int from, int to, double length, EdgeDirection direction = EdgeDirection.Both,
        bool disabled = false, int type = 11, params EdgeWeight[] weights) =>
        new(id, from, to, type, length, direction, disabled, weights);

    public static InMemoryGraph<int, int, string> Graph(IEnumerable<GraphNode<int>> nodes, IEnumerable<GraphEdge<int, int>> edges)
    {
        var graph = new InMemoryGraph<int, int, string>();
        foreach (var node in nodes)
        {
            graph.Add(node);
        }

        foreach (var edge in edges)
        {
            graph.Add(edge);
        }

        return graph;
    }

    /// <summary>A chain 1 – 2 – … – n with edges 101, 102, … of the given length.</summary>
    public static InMemoryGraph<int, int, string> Chain(int n, double length = 1) =>
        Graph(Enumerable.Range(1, n).Select(i => N(i)), Enumerable.Range(1, n - 1).Select(i => E(100 + i, i, i + 1, length)));

    /// <summary>A random network with integer lengths, so that sums of costs are exact; node types 0, 1, 2, edge types 11, 12.</summary>
    public static (InMemoryGraph<int, int, string> Graph, int[] Starts, TraversalOptions<int, int> Options) Random(int seed)
    {
        var random = new Random(seed);
        int n = random.Next(1, 26);
        var nodes = Enumerable.Range(1, n).Select(i => N(i, random.Next(3), random.Next(7) == 0)).ToList();
        int m = random.Next(0, 3 * n + 1);
        var edges = Enumerable.Range(1, m).Select(i => E(
            1000 + i,
            random.Next(1, n + 1),
            random.Next(1, n + 1),
            random.Next(0, 10),
            random.Next(4) switch { 0 => EdgeDirection.Forward, 1 => EdgeDirection.Backward, _ => EdgeDirection.Both },
            random.Next(10) == 0,
            11 + random.Next(2))).ToList();

        var starts = Enumerable.Range(0, random.Next(1, 4)).Select(_ => random.Next(1, n + 1)).ToArray();
        NetworkState<int, int>? state = null;
        if (random.Next(2) == 0)
        {
            state = new NetworkState<int, int>();
            for (int i = 0; i < 3; i++)
            {
                int node = random.Next(1, n + 1);
                if (random.Next(2) == 0) state.Block(node); else state.Unblock(node);
                if (m > 0)
                {
                    int edge = 1000 + random.Next(1, m + 1);
                    if (random.Next(2) == 0) state.Disable(edge); else state.Enable(edge);
                    state.SetDirection(1000 + random.Next(1, m + 1), (EdgeDirection)random.Next(3));
                }
            }
        }

        var options = new TraversalOptions<int, int>
        {
            State = state,
            EdgeTypes = random.Next(3) == 0 ? TypeSet.Of(11) : null,
            NodeTypes = random.Next(3) == 0 ? TypeSet.Of(1) : null,
            MaxCost = random.Next(3) == 0 ? random.Next(0, 15) : double.PositiveInfinity,
        };

        return (Graph(nodes, edges), starts, options);
    }

    /// <summary>The cost of the cheapest way from any start to every reachable node, by Bellman-Ford over all edges.</summary>
    public static Dictionary<int, double> Reference(InMemoryGraph<int, int, string> graph, IEnumerable<int> starts, TraversalOptions<int, int> options)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var startSet = starts.ToHashSet();
        bool Blocked(int id) => options.State?.IsBlocked(nodes[id]) ?? nodes[id].Blocked;
        bool Enters(int id) => startSet.Contains(id) || options.NodeTypes is null || nodes[id].TypeId == 0 || options.NodeTypes.Contains(nodes[id].TypeId);

        var cost = startSet.ToDictionary(s => s, _ => 0.0);
        for (int round = 0; round <= nodes.Count; round++)
        {
            bool changed = false;
            foreach (var edge in graph.Edges)
            {
                if (edge.From == edge.To || (options.EdgeTypes is { } types && !types.Contains(edge.TypeId)) ||
                    (options.State?.IsDisabled(edge) ?? edge.Disabled))
                {
                    continue;
                }

                var direction = options.State?.GetDirection(edge) ?? edge.Direction;
                foreach (var (u, v) in new[] { (edge.From, edge.To), (edge.To, edge.From) })
                {
                    bool forward = u == edge.From;
                    if ((forward && direction == EdgeDirection.Backward) || (!forward && direction == EdgeDirection.Forward) ||
                        !cost.TryGetValue(u, out double cu) || Blocked(u) || !Enters(v))
                    {
                        continue;
                    }

                    double c = cu + edge.Length;
                    if (c <= options.MaxCost && (!cost.TryGetValue(v, out double cv) || c < cv))
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
}

/// <summary>An <see cref="IGraph{TNodeId, TEdgeId}"/> that counts the calls and passes them on.</summary>
internal sealed class CountingGraph(IGraph<int, int> inner) : IGraph<int, int>
{
    public int NodeCalls { get; private set; }

    public int EdgeCalls { get; private set; }

    public int LargestEdgeBatch { get; private set; }

    public IEnumerable<GraphNode<int>> GetNodes(IReadOnlyCollection<int> ids)
    {
        NodeCalls++;
        return inner.GetNodes(ids);
    }

    public IEnumerable<GraphEdge<int, int>> GetEdges(IReadOnlyCollection<int> nodeIds)
    {
        EdgeCalls++;
        LargestEdgeBatch = Math.Max(LargestEdgeBatch, nodeIds.Count);
        return inner.GetEdges(nodeIds);
    }
}

/// <summary>A sharded graph over an in-memory network, for testing <see cref="ShardedGraphBase{TNodeId, TEdgeId}"/>.</summary>
internal sealed class FakeShardedGraph(InMemoryGraph<int, int, string> graph, Func<int, long> shardOf, int maxCachedShards)
    : ShardedGraphBase<int, int>(maxCachedShards)
{
    private readonly HashSet<int> _known = graph.Nodes.Select(n => n.Id).ToHashSet();

    public int FindCalls { get; private set; }

    public List<long> Loads { get; } = [];

    protected override IReadOnlyDictionary<int, long> FindShards(IReadOnlyCollection<int> nodeIds)
    {
        FindCalls++;
        return nodeIds.Where(_known.Contains).ToDictionary(id => id, shardOf);
    }

    protected override ShardData<int, int> LoadShard(long shardId)
    {
        Loads.Add(shardId);
        return new ShardData<int, int>(
            graph.Nodes.Where(n => shardOf(n.Id) == shardId).ToList(),
            graph.Edges.Where(e => shardOf(e.From) == shardId || shardOf(e.To) == shardId).ToList());
    }
}
