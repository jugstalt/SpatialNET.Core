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
                nodes.Add(new GraphNode<long>(id, type, new Coordinate(at.X + 5 + 45 * random.NextDouble(), at.Y - 20 + 40 * random.NextDouble())));
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

    /// <summary>The sinks of a supply benchmark (ADR 0051): up to <paramref name="count"/> customers, spread evenly over the network.</summary>
    public long[] Customers(int count)
    {
        var customers = Nodes.Where(n => n.TypeId == CustomerType).Select(n => n.Id).ToArray();
        int step = Math.Max(1, customers.Length / Math.Max(1, count));
        return customers.Where((_, i) => i % step == 0).Take(count).ToArray();
    }

    /// <summary>The transformers (sources of a supply benchmark): in the order of the shards, or in a fixed random order.</summary>
    public long[] SourcesFor(int count, bool shuffled)
    {
        var sources = Transformers.Take(count).ToArray();
        if (shuffled)
        {
            new Random(7).Shuffle(sources);
        }

        return sources;
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
    private ShardStore _storeData = null!;
    private long[] _supplySources = null!, _supplyShuffled = null!, _supplySinks = null!;
    private int _next;

    [Params(100_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _network = SyntheticNetwork.Create(N);
        _storeData = new ShardStore(_network);
        _supplySources = _network.SourcesFor(10_000, shuffled: false);
        _supplyShuffled = _network.SourcesFor(10_000, shuffled: true);
        _supplySinks = _network.Customers(500_000);
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
    public int SupplyInMemory() => _memory.Supply(_supplySources, _supplySinks).Count(s => s.Sources.Count == 1);

    [Benchmark]
    public int SupplyThroughShardsSortedByShard() => SupplyThroughShards(_supplySources);

    [Benchmark]
    public int SupplyThroughShardsShuffled() => SupplyThroughShards(_supplyShuffled);

    private int SupplyThroughShards(long[] sources) =>
        new SimulatedStoreGraph(_storeData, maxCachedShards: 64, latencyMilliseconds: 0).Supply(sources, _supplySinks).Count(s => s.Sources.Count == 1);

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

/// <summary>The input of a build: the points and lines of a <see cref="SyntheticNetwork"/>, as a host would read them (ADR 0050).</summary>
public sealed class SyntheticBuildInput
{
    private SyntheticBuildInput(List<(long Id, Coordinate At, int Type)> points, List<(long Source, IPolyline2D Line)> lines)
    {
        Points = points;
        Lines = lines;
    }

    /// <summary>The real nodes: transformers, boxes and customers. The other nodes of the network are only line ends.</summary>
    public List<(long Id, Coordinate At, int Type)> Points { get; }

    /// <summary>One line per edge, with a vertex in the middle.</summary>
    public List<(long Source, IPolyline2D Line)> Lines { get; }

    public static SyntheticBuildInput Create(SyntheticNetwork network)
    {
        var points = network.Nodes.Where(n => n.TypeId != 0).Select(n => (n.Id, n.Location, n.TypeId)).ToList();
        var lines = new List<(long, IPolyline2D)>(network.Edges.Length);
        foreach (var edge in network.Edges)
        {
            var a = network.Nodes[edge.From].Location;
            var b = network.Nodes[edge.To].Location;
            var middle = new Coordinate((a.X + b.X) / 2 + 0.5, (a.Y + b.Y) / 2);
            lines.Add((edge.Id, SpatialNET.Imp.ImpGeometryFactory.Instance.CreatePolyline([new[] { a, middle, b }], null)));
        }

        return new SyntheticBuildInput(points, lines);
    }

    public SpatialNET.Network.Building.BuildResult Build(INetworkWriter<long, long, long> writer, IProgress<SpatialNET.Network.Building.BuildProgress>? progress = null)
    {
        var builder = new SpatialNET.Network.Building.NetworkBuilder<long, long, long>(
            new SpatialNET.Network.Building.NetworkBuildOptions { Tolerance = 0.01, DefaultShard = 0 }, new SequentialIds());
        foreach (var (id, at, type) in Points)
        {
            builder.AddNode(id, id, at, type);
        }

        foreach (var (source, line) in Lines)
        {
            builder.AddLine(source, line, SyntheticNetwork.CableType);
        }

        return builder.Build(writer, progress);
    }

    private sealed class SequentialIds : SpatialNET.Network.Building.INetworkIdFactory<long, long, long>
    {
        private long _next = 1L << 40;

        public long NewHelperNode(Coordinate location) => _next++;

        public long NewEdge(long source, int part, int partCount) => source * 16 + part;
    }
}

/// <summary>A writer that only counts, so that a benchmark measures the builder alone.</summary>
public sealed class CountingWriter : INetworkWriter<long, long, long>
{
    public long Nodes { get; private set; }

    public long Edges { get; private set; }

    public void WriteNode(in BuiltNode<long, long> node) => Nodes++;

    public void WriteEdge(in BuiltEdge<long, long, long> edge) => Edges++;
}

/// <summary>The builder of ADR 0050 on the points and lines of a synthetic network.</summary>
[MemoryDiagnoser]
public class NetworkBuildBenchmarks
{
    private SyntheticBuildInput _input = null!;

    [Params(100_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _input = SyntheticBuildInput.Create(SyntheticNetwork.Create(N));

    [Benchmark]
    public long Build()
    {
        var writer = new CountingWriter();
        _input.Build(writer);
        return writer.Edges;
    }
}
