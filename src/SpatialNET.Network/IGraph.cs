namespace SpatialNET.Network;

/// <summary>
/// The graph of a network, held and implemented by the host (ADR 0049). The library asks for many ids at once, so that
/// the host can answer each call with one query; the searches are extension methods on this interface
/// (<see cref="NetworkAnalysis"/>).
/// </summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
/// <remarks>
/// <para>For the same data the host returns the same order; the results of the searches are deterministic under that condition.</para>
/// <para>The interface cannot enumerate all nodes on purpose: every search takes its start set from the host.</para>
/// <para>Exceptions of the host (for example a database that cannot be reached) pass through the searches unchanged.</para>
/// </remarks>
public interface IGraph<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>Returns the nodes with the given ids. Unknown ids are left out.</summary>
    /// <param name="ids">The ids, distinct.</param>
    IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids);

    /// <summary>
    /// Returns every edge that touches at least one of the given nodes, each edge once, including edges whose other end
    /// lies in another shard of the host.
    /// </summary>
    /// <param name="nodeIds">The ids of the nodes, distinct.</param>
    IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds);
}

/// <summary>
/// Thrown when the data of a network cannot be a network (ADR 0049): an edge points to a node that the graph does not
/// return, a weight is missing without a fallback, or a cost is negative, <c>NaN</c> or infinite.
/// </summary>
public sealed class InvalidGraphException : InvalidOperationException
{
    /// <summary>Creates the exception with a default message.</summary>
    public InvalidGraphException()
        : base("The data does not form a valid network.")
    {
    }

    /// <summary>Creates the exception with a message that names the ids involved.</summary>
    public InvalidGraphException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public InvalidGraphException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
