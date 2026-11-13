using SpatialNET.Network;

namespace SpatialNET.Imp;

/// <summary>The content of one shard: its nodes and every edge that touches one of them (ADR 0049).</summary>
/// <param name="Nodes">The nodes of the shard.</param>
/// <param name="Edges">Every edge with at least one end in the shard, also the edges to other shards.</param>
public sealed record ShardData<TNodeId, TEdgeId>(IReadOnlyList<GraphNode<TNodeId>> Nodes, IReadOnlyList<GraphEdge<TNodeId, TEdgeId>> Edges)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>
/// An <see cref="IGraph{TNodeId, TEdgeId}"/> for a host that stores its network in shards, for example one per local
/// network in a database (ADR 0049). It keeps a bounded number of whole shards in memory and drops the one used least
/// recently. The host implements two methods: <see cref="FindShards"/> (which shard holds these nodes, in one query) and
/// <see cref="LoadShard"/> (all nodes of a shard and all edges that touch them).
/// </summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
/// <remarks>
/// <para>Thread-safe: several searches may use one instance. The two methods of the host are called one at a time.</para>
/// <para>A shard needed by one call stays in memory for that call, even if the call needs more shards than the cache holds.</para>
/// </remarks>
public abstract class ShardedGraphBase<TNodeId, TEdgeId> : IGraph<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, Shard> _shards = [];
    private readonly LinkedList<long> _recent = new(); // least recently used first
    private readonly Dictionary<TNodeId, long> _shardOfNode = [];

    /// <summary>Creates the cache.</summary>
    /// <param name="maxCachedShards">How many shards are kept in memory; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCachedShards"/> is below 1.</exception>
    protected ShardedGraphBase(int maxCachedShards = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCachedShards, 1);
        MaxCachedShards = maxCachedShards;
    }

    /// <summary>How many shards are kept in memory.</summary>
    public int MaxCachedShards { get; }

    /// <summary>How many shards are in memory now.</summary>
    public int CachedShardCount
    {
        get
        {
            lock (_lock)
            {
                return _shards.Count;
            }
        }
    }

    /// <summary>Maps node ids to the shards that hold them, in one query. Ids the host does not know are left out.</summary>
    /// <param name="nodeIds">The ids, distinct; none of them lies in a shard that is in memory.</param>
    protected abstract IReadOnlyDictionary<TNodeId, long> FindShards(IReadOnlyCollection<TNodeId> nodeIds);

    /// <summary>Loads all nodes of a shard and every edge that touches one of them.</summary>
    /// <param name="shardId">A shard that <see cref="FindShards"/> returned.</param>
    protected abstract ShardData<TNodeId, TEdgeId> LoadShard(long shardId);

    /// <inheritdoc/>
    public IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_lock)
        {
            var shardOf = Ensure(ids);
            var result = new List<GraphNode<TNodeId>>(ids.Count);
            var seen = new HashSet<TNodeId>();
            foreach (var id in ids)
            {
                if (seen.Add(id) && shardOf.TryGetValue(id, out long shard) && _shards[shard].Nodes.TryGetValue(id, out var node))
                {
                    result.Add(node);
                }
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        lock (_lock)
        {
            var shardOf = Ensure(nodeIds);
            var result = new List<GraphEdge<TNodeId, TEdgeId>>();
            var seenNodes = new HashSet<TNodeId>();
            var seenEdges = new HashSet<TEdgeId>();
            foreach (var id in nodeIds)
            {
                if (seenNodes.Add(id) && shardOf.TryGetValue(id, out long shard) && _shards[shard].Incident.TryGetValue(id, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        if (seenEdges.Add(edge.Id))
                        {
                            result.Add(edge);
                        }
                    }
                }
            }

            return result;
        }
    }

    private Dictionary<TNodeId, long> Ensure(IReadOnlyCollection<TNodeId> ids)
    {
        var shardOf = new Dictionary<TNodeId, long>();
        var unknown = new List<TNodeId>();
        var unknownSet = new HashSet<TNodeId>();
        var needed = new HashSet<long>();
        foreach (var id in ids)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(ids));
            if (shardOf.ContainsKey(id))
            {
                continue;
            }

            if (_shardOfNode.TryGetValue(id, out long shard))
            {
                shardOf[id] = shard;
                needed.Add(shard);
            }
            else if (unknownSet.Add(id))
            {
                unknown.Add(id);
            }
        }

        if (unknown.Count > 0)
        {
            var found = FindShards(unknown) ?? throw new InvalidOperationException($"{nameof(FindShards)} returned null.");
            foreach (var id in unknown)
            {
                if (found.TryGetValue(id, out long shard))
                {
                    shardOf[id] = shard;
                    needed.Add(shard);
                }
            }
        }

        foreach (long shard in needed.Order())
        {
            if (_shards.TryGetValue(shard, out var cached))
            {
                _recent.Remove(cached.Recent);
                _recent.AddLast(cached.Recent);
            }
            else
            {
                Load(shard);
            }
        }

        Trim(needed);
        return shardOf;
    }

    private void Load(long shardId)
    {
        var data = LoadShard(shardId) ?? throw new InvalidOperationException($"{nameof(LoadShard)}({shardId}) returned null.");
        var nodes = new Dictionary<TNodeId, GraphNode<TNodeId>>(data.Nodes.Count);
        foreach (var node in data.Nodes)
        {
            nodes.TryAdd(node.Id, node);
        }

        var incident = new Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>>();
        foreach (var edge in data.Edges)
        {
            if (nodes.ContainsKey(edge.From))
            {
                Incident(edge.From).Add(edge);
            }

            if (!edge.To.Equals(edge.From) && nodes.ContainsKey(edge.To))
            {
                Incident(edge.To).Add(edge);
            }
        }

        var shard = new Shard(nodes, incident, new LinkedListNode<long>(shardId));
        _shards[shardId] = shard;
        _recent.AddLast(shard.Recent);
        foreach (var id in nodes.Keys)
        {
            _shardOfNode[id] = shardId;
        }

        List<GraphEdge<TNodeId, TEdgeId>> Incident(TNodeId node)
        {
            if (!incident.TryGetValue(node, out var list))
            {
                list = [];
                incident[node] = list;
            }

            return list;
        }
    }

    private void Trim(HashSet<long> keep)
    {
        var candidate = _recent.First;
        while (_shards.Count > MaxCachedShards && candidate is not null)
        {
            var next = candidate.Next;
            if (!keep.Contains(candidate.Value))
            {
                var shard = _shards[candidate.Value];
                foreach (var id in shard.Nodes.Keys)
                {
                    if (_shardOfNode.TryGetValue(id, out long owner) && owner == candidate.Value)
                    {
                        _shardOfNode.Remove(id);
                    }
                }

                _shards.Remove(candidate.Value);
                _recent.Remove(candidate);
            }

            candidate = next;
        }
    }

    private sealed record Shard(
        Dictionary<TNodeId, GraphNode<TNodeId>> Nodes,
        Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>> Incident,
        LinkedListNode<long> Recent);
}
