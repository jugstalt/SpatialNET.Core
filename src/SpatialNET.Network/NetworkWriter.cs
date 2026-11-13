namespace SpatialNET.Network;

/// <summary>A node as the network builder writes it (ADR 0050).</summary>
/// <param name="Node">The node.</param>
/// <param name="Shard">The shard the node belongs to.</param>
/// <param name="IsHelper">Whether it is a helper node, created where line ends meet without a point of the host.</param>
/// <param name="Source">The host's reference to the point it came from; the default value for a helper node.</param>
public readonly record struct BuiltNode<TNodeId, TSource>(GraphNode<TNodeId> Node, long Shard, bool IsHelper, TSource? Source)
    where TNodeId : notnull, IEquatable<TNodeId>;

/// <summary>An edge as the network builder writes it (ADR 0050).</summary>
/// <param name="Edge">The edge.</param>
/// <param name="Source">The host's reference to the line it came from.</param>
/// <param name="FromMeasure">Where the edge starts on the source line, as geometric length from its start (for <c>Substring</c>).</param>
/// <param name="ToMeasure">Where the edge ends on the source line.</param>
/// <param name="FromShard">The shard of the node at <c>From</c>.</param>
/// <param name="ToShard">The shard of the node at <c>To</c>; an edge between two shards belongs to both.</param>
public readonly record struct BuiltEdge<TNodeId, TEdgeId, TSource>(
    GraphEdge<TNodeId, TEdgeId> Edge,
    TSource Source,
    double FromMeasure,
    double ToMeasure,
    long FromShard,
    long ToShard)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;

/// <summary>
/// Where the network builder writes the network, implemented by the host (a database) or by <c>InMemoryGraph</c> (ADR 0050).
/// All nodes are written before the first edge.
/// </summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
/// <typeparam name="TSource">The host's reference to a source object.</typeparam>
public interface INetworkWriter<TNodeId, TEdgeId, TSource>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>Writes a node.</summary>
    void WriteNode(in BuiltNode<TNodeId, TSource> node);

    /// <summary>Writes an edge.</summary>
    void WriteEdge(in BuiltEdge<TNodeId, TEdgeId, TSource> edge);
}
