using SpatialNET.Network;

namespace SpatialNET.Imp;

/// <summary>
/// A network held in memory (ADR 0049): for tests, small networks and the temporary network of one area that is checked
/// on save. Filled by hand with <see cref="Add(GraphNode{TNodeId})"/> and <see cref="Add(GraphEdge{TNodeId, TEdgeId})"/>.
/// It keeps the source of each edge, if one is given, and may enumerate its nodes and edges.
/// </summary>
/// <typeparam name="TNodeId">The node id.</typeparam>
/// <typeparam name="TEdgeId">The edge id.</typeparam>
/// <typeparam name="TSource">The host's reference to the source object of an edge, for example table and object id.</typeparam>
/// <remarks>
/// <para>Edges may be added before the nodes at their ends; a search reports a missing node as an <see cref="InvalidGraphException"/>.</para>
/// <para>Not thread-safe while it is filled; once filled, any number of searches may read it at the same time.</para>
/// </remarks>
public sealed class InMemoryGraph<TNodeId, TEdgeId, TSource> : IGraph<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    private readonly Dictionary<TNodeId, GraphNode<TNodeId>> _nodes = [];
    private readonly List<TNodeId> _nodeOrder = [];
    private readonly Dictionary<TEdgeId, int> _edgeIndex = [];
    private readonly List<GraphEdge<TNodeId, TEdgeId>> _edges = [];
    private readonly List<(bool Has, TSource Source, double From, double To)> _sources = [];
    private readonly Dictionary<TNodeId, List<int>> _incident = [];

    /// <summary>The number of nodes.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>The number of edges.</summary>
    public int EdgeCount => _edges.Count;

    /// <summary>The nodes in the order they were added.</summary>
    public IEnumerable<GraphNode<TNodeId>> Nodes => _nodeOrder.Select(id => _nodes[id]);

    /// <summary>The edges in the order they were added.</summary>
    public IEnumerable<GraphEdge<TNodeId, TEdgeId>> Edges => _edges;

    /// <summary>Adds a node.</summary>
    /// <exception cref="ArgumentNullException">The id is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A node with this id was added before.</exception>
    public void Add(GraphNode<TNodeId> node)
    {
        ArgumentNullException.ThrowIfNull(node.Id, nameof(node));
        if (!_nodes.TryAdd(node.Id, node))
        {
            throw new ArgumentException($"The node {node.Id} was added before.", nameof(node));
        }

        _nodeOrder.Add(node.Id);
    }

    /// <summary>Adds an edge without a source.</summary>
    /// <exception cref="ArgumentNullException">An id is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An edge with this id was added before.</exception>
    public void Add(GraphEdge<TNodeId, TEdgeId> edge) => AddEdge(edge, (false, default!, 0, 0));

    /// <summary>Adds an edge with its source object and its range on the source line.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="source">The host's reference to the source object.</param>
    /// <param name="fromMeasure">Where the edge starts on the source line.</param>
    /// <param name="toMeasure">Where the edge ends on the source line.</param>
    /// <exception cref="ArgumentNullException">An id is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An edge with this id was added before.</exception>
    public void Add(GraphEdge<TNodeId, TEdgeId> edge, TSource source, double fromMeasure, double toMeasure) =>
        AddEdge(edge, (true, source, fromMeasure, toMeasure));

    /// <summary>The source object and the range of an edge, if it was added with one.</summary>
    public bool TryGetSource(TEdgeId edge, out TSource source, out double fromMeasure, out double toMeasure)
    {
        if (_edgeIndex.TryGetValue(edge, out int index) && _sources[index].Has)
        {
            (_, source, fromMeasure, toMeasure) = _sources[index];
            return true;
        }

        source = default!;
        fromMeasure = toMeasure = 0;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new List<GraphNode<TNodeId>>(ids.Count);
        var seen = new HashSet<TNodeId>();
        foreach (var id in ids)
        {
            if (seen.Add(id) && _nodes.TryGetValue(id, out var node))
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var result = new List<GraphEdge<TNodeId, TEdgeId>>();
        var seen = new HashSet<int>();
        foreach (var id in nodeIds)
        {
            if (_incident.TryGetValue(id, out var indices))
            {
                foreach (int index in indices)
                {
                    if (seen.Add(index))
                    {
                        result.Add(_edges[index]);
                    }
                }
            }
        }

        return result;
    }

    private void AddEdge(GraphEdge<TNodeId, TEdgeId> edge, (bool, TSource, double, double) source)
    {
        ArgumentNullException.ThrowIfNull(edge.Id, nameof(edge));
        ArgumentNullException.ThrowIfNull(edge.From, nameof(edge));
        ArgumentNullException.ThrowIfNull(edge.To, nameof(edge));
        int index = _edges.Count;
        if (!_edgeIndex.TryAdd(edge.Id, index))
        {
            throw new ArgumentException($"The edge {edge.Id} was added before.", nameof(edge));
        }

        _edges.Add(edge);
        _sources.Add(source);
        Incident(edge.From).Add(index);
        if (!edge.To.Equals(edge.From))
        {
            Incident(edge.To).Add(index);
        }
    }

    private List<int> Incident(TNodeId node)
    {
        if (!_incident.TryGetValue(node, out var list))
        {
            list = [];
            _incident[node] = list;
        }

        return list;
    }
}
