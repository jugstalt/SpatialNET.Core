using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;
using SpatialNET.Network;

namespace SpatialNET.Benchmarks;

/// <summary>
/// A synthetic utility network (ADR 0049): radial local networks of about <c>nodesPerLocalNetwork</c> nodes, each fed by
/// one transformer and stored as one shard, with an open separation edge to the next local network.
/// </summary>
public sealed class SyntheticNetwork
{
    public const int TransformerType = 25, CustomerType = 16, BoxType = 15, CableType = 11;

    private SyntheticNetwork(GraphNode<long>[] nodes, GraphEdge<long, long>[] edges, long[] shardOfNode, long[] transformers)
    {
        Nodes = nodes;
        Edges = edges;
        ShardOfNode = shardOfNode;
        Transformers = transformers;
    }

    public GraphNode<long>[] Nodes { get; }

    public GraphEdge<long, long>[] Edges { get; }

    /// <summary>The shard (local network) of each node, by node id.</summary>
    public long[] ShardOfNode { get; }

    /// <summary>One transformer per local network, in the order of the shards.</summary>
    public long[] Transformers { get; }

    public static SyntheticNetwork Create(int nodeCount, int nodesPerLocalNetwork = 300, int seed = 1)
    {
        var random = new Random(seed);
        int localNetworks = Math.Max(1, nodeCount / nodesPerLocalNetwork);
        int side = (int)Math.Ceiling(Math.Sqrt(localNetworks));
        var nodes = new List<GraphNode<long>>(nodeCount);
        var edges = new List<GraphEdge<long, long>>(nodeCount + localNetworks);
        var shard = new List<long>(nodeCount);
        var transformers = new long[localNetworks];
        long edgeId = 0;
        for (int k = 0; k < localNetworks; k++)
        {
            double x0 = k % side * 2_000, y0 = k / side * 2_000;
            long first = nodes.Count;
            transformers[k] = first;
            nodes.Add(new GraphNode<long>(first, TransformerType, new Coordinate(x0, y0)));
            shard.Add(k);
            for (int i = 1; i < nodesPerLocalNetwork; i++)
            {
                // the parent is one of the last few nodes, so the tree has long feeders with branches
                long parent = first + random.Next(Math.Max(0, i - 8), i);
                long id = nodes.Count;
                int type = random.Next(10) switch { < 5 => 0, < 9 => CustomerType, _ => BoxType };
                var at = nodes[(int)parent].Location;
                nodes.Add(new GraphNode<long>(id, type, new Coordinate(at.X + random.Next(5, 50), at.Y + random.Next(-20, 20))));
                shard.Add(k);
                edges.Add(new GraphEdge<long, long>(edgeId++, parent, id, CableType, random.Next(5, 50)));
            }

            if (k > 0)
            {
                // an open separation point between this and the previous local network: a boundary edge, disabled
                edges.Add(new GraphEdge<long, long>(edgeId++, first - 1, first + nodesPerLocalNetwork / 2, CableType, 10, Disabled: true));
            }
        }

        return new SyntheticNetwork([.. nodes], [.. edges], [.. shard], transformers);
    }

    public InMemoryGraph<long, long, int> ToMemory()
    {
        var graph = new InMemoryGraph<long, long, int>();
        foreach (var node in Nodes)
        {
            graph.Add(node);
        }

        foreach (var edge in Edges)
        {
            graph.Add(edge);
        }

        return graph;
    }
}

/// <summary>The shards of a <see cref="SyntheticNetwork"/>, grouped once, as a host's database would hold them.</summary>
public sealed class ShardStore
{
    public ShardStore(SyntheticNetwork network)
    {
        Network = network;
        int count = network.Transformers.Length;
        var nodes = Enumerable.Range(0, count).Select(_ => new List<GraphNode<long>>()).ToArray();
        var edges = Enumerable.Range(0, count).Select(_ => new List<GraphEdge<long, long>>()).ToArray();
        foreach (var node in network.Nodes)
        {
            nodes[network.ShardOfNode[node.Id]].Add(node);
        }

        foreach (var edge in network.Edges)
        {
            long from = network.ShardOfNode[edge.From], to = network.ShardOfNode[edge.To];
            edges[from].Add(edge);
            if (to != from)
            {
                edges[to].Add(edge);
            }
        }

        Shards = Enumerable.Range(0, count).Select(i => new ShardData<long, long>(nodes[i], edges[i])).ToArray();
    }

    public SyntheticNetwork Network { get; }

    public ShardData<long, long>[] Shards { get; }
}

/// <summary>A host's store of shards behind <see cref="ShardedGraphBase{TNodeId, TEdgeId}"/>, with a simulated latency per query.</summary>
public sealed class SimulatedStoreGraph(ShardStore store, int maxCachedShards, double latencyMilliseconds) : ShardedGraphBase<long, long>(maxCachedShards)
{
    public int Queries { get; private set; }

    public int ShardLoads { get; private set; }

    protected override IReadOnlyDictionary<long, long> FindShards(IReadOnlyCollection<long> nodeIds)
    {
        Wait();
        var shardOf = store.Network.ShardOfNode;
        return nodeIds.Where(id => id >= 0 && id < shardOf.Length).ToDictionary(id => id, id => shardOf[id]);
    }

    protected override ShardData<long, long> LoadShard(long shardId)
    {
        Wait();
        ShardLoads++;
        return store.Shards[shardId];
    }

    private void Wait()
    {
        Queries++;
        if (latencyMilliseconds > 0)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < latencyMilliseconds)
            {
                Thread.SpinWait(50);
            }
        }
    }
}

/// <summary>
/// The searches of ADR 0049 on a synthetic network: inside one local network (in memory and through the shard cache)
/// and over the whole network. Decides the default cache and batch sizes.
/// </summary>
[MemoryDiagnoser]
public class NetworkBenchmarks
{
    private SyntheticNetwork _network = null!;
    private InMemoryGraph<long, long, int> _memory = null!;
    private SimulatedStoreGraph _store = null!;
    private int _next;

    [Params(100_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _network = SyntheticNetwork.Create(N);
        _memory = _network.ToMemory();
        _store = new SimulatedStoreGraph(new ShardStore(_network), maxCachedShards: 64, latencyMilliseconds: 0);
    }

    private long NextTransformer() => _network.Transformers[_next++ % _network.Transformers.Length];

    // 32 local networks fit into the cache of 64 shards with their neighbours
    private long NextCachedTransformer() => _network.Transformers[_next++ % 32];

    [Benchmark]
    public int ReachLocalNetworkInMemory() => _memory.Reach([NextTransformer()]).Count();

    [Benchmark]
    public int ReachLocalNetworkThroughWarmShards() => _store.Reach([NextCachedTransformer()]).Count();

    [Benchmark]
    public double ShortestPathInLocalNetwork()
    {
        long transformer = NextTransformer();
        return _memory.ShortestPath(transformer, transformer + 250)?.Cost ?? -1;
    }

    [Benchmark]
    public int ReachWholeNetworkInMemory() => _memory.Reach(_network.Transformers).Count();

    [Benchmark]
    public int ReachEveryLocalNetworkThroughShards()
    {
        int reached = 0;
        foreach (long transformer in _network.Transformers)
        {
            reached += _store.Reach([transformer]).Count();
        }

        return reached;
    }
}
