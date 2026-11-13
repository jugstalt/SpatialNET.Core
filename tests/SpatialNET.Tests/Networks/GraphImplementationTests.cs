using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// InMemoryGraph und ShardedGraphBase aus SpatialNET.Imp (ADR 0049).
public class GraphImplementationTests
{
    [Fact]
    public void InMemoryGraph_returns_nodes_in_the_asked_order_and_each_edge_once()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 1), E(102, 2, 3, 1), E(103, 3, 3, 1)]);

        Assert.Equal([3, 1], graph.GetNodes([3, 99, 1, 3]).Select(n => n.Id));
        Assert.Equal([101, 102, 103], graph.GetEdges([2, 3, 1]).Select(e => e.Id));
        Assert.Equal([1, 2, 3], graph.Nodes.Select(n => n.Id));
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(3, graph.EdgeCount);
    }

    [Fact]
    public void InMemoryGraph_rejects_duplicate_ids_and_keeps_sources()
    {
        var graph = Graph([N(1), N(2)], []);
        graph.Add(E(101, 1, 2, 1), "Leitung.210", 0, 48);
        graph.Add(E(102, 1, 2, 1));

        Assert.Throws<ArgumentException>(() => graph.Add(N(1)));
        Assert.Throws<ArgumentException>(() => graph.Add(E(101, 2, 1, 1)));
        Assert.True(graph.TryGetSource(101, out var source, out double from, out double to));
        Assert.Equal(("Leitung.210", 0.0, 48.0), (source, from, to));
        Assert.False(graph.TryGetSource(102, out _, out _, out _));
        Assert.False(graph.TryGetSource(999, out _, out _, out _));
    }

    [Fact]
    public void ShardedGraphBase_loads_each_shard_once_while_it_fits_in_the_cache()
    {
        var chain = Chain(30);
        var sharded = new FakeShardedGraph(chain, id => (id - 1) / 10, maxCachedShards: 32);

        Assert.Equal(30, sharded.Reach([1]).Count());
        Assert.Equal([0L, 1, 2], sharded.Loads);
        Assert.Equal(3, sharded.CachedShardCount);

        Assert.Equal(30, sharded.Reach([30]).Count());
        Assert.Equal(3, sharded.Loads.Count);
    }

    [Fact]
    public void ShardedGraphBase_drops_the_least_recently_used_shard()
    {
        var chain = Chain(30);
        var sharded = new FakeShardedGraph(chain, id => (id - 1) / 10, maxCachedShards: 1);

        Assert.Equal(29, sharded.ShortestPath(1, 30)!.Cost);
        Assert.Equal(1, sharded.CachedShardCount);
        Assert.Equal(29, sharded.ShortestPath(30, 1)!.Cost);
    }

    [Fact]
    public void ShardedGraphBase_returns_edges_to_other_shards_and_leaves_out_unknown_ids()
    {
        var graph = Graph([N(1), N(2)], [E(101, 1, 2, 1)]);
        var sharded = new FakeShardedGraph(graph, id => id, maxCachedShards: 4);

        Assert.Equal([101], sharded.GetEdges([1]).Select(e => e.Id));
        Assert.Equal([2], sharded.GetNodes([2, 7]).Select(n => n.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeShardedGraph(graph, id => id, maxCachedShards: 0));
    }
}
