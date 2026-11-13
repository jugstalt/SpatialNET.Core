namespace SpatialNET.Network.Internal;

/// <summary>
/// The part of a network that one search has seen, loaded from <see cref="IGraph{TNodeId, TEdgeId}"/> in batches
/// (ADR 0049): when the edges of a node are needed, the edges of up to <see cref="BatchSize"/> discovered nodes are asked
/// for at once, and then every new node at their ends in one more call.
/// </summary>
internal sealed class GraphView<TNodeId, TEdgeId>(IGraph<TNodeId, TEdgeId> graph)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>How many nodes one call of <c>GetEdges</c> asks for at most; the benchmark decides the value (ADR 0049).</summary>
    internal const int BatchSize = 1024;

    private readonly Dictionary<TNodeId, GraphNode<TNodeId>> _nodes = [];
    private readonly Dictionary<TNodeId, List<GraphEdge<TNodeId, TEdgeId>>> _adjacency = [];
    private readonly Queue<TNodeId> _pending = new();
    private readonly HashSet<TNodeId> _pendingSet = [];

    /// <summary>The record of a node that is loaded.</summary>
    public GraphNode<TNodeId> Node(TNodeId id) => _nodes[id];

    /// <summary>Loads the records of the given nodes, which the caller named (start, target).</summary>
    /// <exception cref="ArgumentException">A node is not in the graph.</exception>
    public void LoadGivenNodes(IReadOnlyList<TNodeId> ids, string paramName)
    {
        var missing = ids.Where(id => !_nodes.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var asked = missing.ToHashSet();
        foreach (var node in graph.GetNodes(missing))
        {
            if (asked.Contains(node.Id))
            {
                _nodes.TryAdd(node.Id, node);
            }
        }

        foreach (var id in missing)
        {
            if (!_nodes.ContainsKey(id))
            {
                throw new ArgumentException($"The node {id} is not in the graph.", paramName);
            }
        }
    }

    /// <summary>Remembers a discovered node, so that its edges are loaded together with others.</summary>
    public void MarkPending(TNodeId id)
    {
        if (!_adjacency.ContainsKey(id) && _pendingSet.Add(id))
        {
            _pending.Enqueue(id);
        }
    }

    /// <summary>The edges of a node, in the order the host returned them; loads them with a batch if needed.</summary>
    public List<GraphEdge<TNodeId, TEdgeId>> EdgesOf(TNodeId id)
    {
        if (!_adjacency.TryGetValue(id, out var edges))
        {
            Load(id);
            edges = _adjacency[id];
        }

        return edges;
    }

    private void Load(TNodeId first)
    {
        var batch = new List<TNodeId> { first };
        var asked = new HashSet<TNodeId> { first };
        _pendingSet.Remove(first);
        while (batch.Count < BatchSize && _pending.TryDequeue(out var id))
        {
            if (_pendingSet.Remove(id) && !_adjacency.ContainsKey(id) && asked.Add(id))
            {
                batch.Add(id);
            }
        }

        foreach (var id in batch)
        {
            _adjacency[id] = [];
        }

        var missing = new List<TNodeId>();
        var firstEdge = new Dictionary<TNodeId, TEdgeId>();
        foreach (var edge in graph.GetEdges(batch))
        {
            bool fromAsked = asked.Contains(edge.From), toAsked = asked.Contains(edge.To);
            if (!fromAsked && !toAsked)
            {
                continue; // the host returned an edge nobody asked for
            }

            if (fromAsked)
            {
                _adjacency[edge.From].Add(edge);
            }

            if (toAsked && !edge.To.Equals(edge.From))
            {
                _adjacency[edge.To].Add(edge);
            }

            Need(edge.From, edge.Id);
            Need(edge.To, edge.Id);
        }

        if (missing.Count == 0)
        {
            return;
        }

        foreach (var node in graph.GetNodes(missing))
        {
            if (firstEdge.ContainsKey(node.Id))
            {
                _nodes.TryAdd(node.Id, node);
            }
        }

        foreach (var id in missing)
        {
            if (!_nodes.ContainsKey(id))
            {
                throw new InvalidGraphException($"The edge {firstEdge[id]} points to the node {id}, which the graph does not return.");
            }
        }

        void Need(TNodeId node, TEdgeId edge)
        {
            if (!_nodes.ContainsKey(node) && firstEdge.TryAdd(node, edge))
            {
                missing.Add(node);
            }
        }
    }
}
