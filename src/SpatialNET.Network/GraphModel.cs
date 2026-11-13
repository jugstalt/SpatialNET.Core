namespace SpatialNET.Network;

/// <summary>Whether something refers to a node or to an edge of a network.</summary>
public enum ElementKind
{
    /// <summary>A node.</summary>
    Node,

    /// <summary>An edge.</summary>
    Edge,
}

/// <summary>
/// Which way an edge may be walked, relative to its digitizing direction
/// <see cref="GraphEdge{TNodeId, TEdgeId}.From"/> → <see cref="GraphEdge{TNodeId, TEdgeId}.To"/> (ADR 0049).
/// </summary>
public enum EdgeDirection
{
    /// <summary>Both ways (the default).</summary>
    Both,

    /// <summary>Only from <c>From</c> to <c>To</c>.</summary>
    Forward,

    /// <summary>Only from <c>To</c> to <c>From</c>.</summary>
    Backward,
}

/// <summary>The value of one weight on an edge, for example ohm per kilometre or a maximum speed (ADR 0049).</summary>
/// <param name="WeightId">The id of the <see cref="WeightDefinition"/> the value belongs to.</param>
/// <param name="Value">The value; how it turns into a cost depends on the <see cref="WeightKind"/> of the definition.</param>
public readonly record struct EdgeWeight(int WeightId, double Value);

/// <summary>A node of a network as the host returns it (ADR 0049).</summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <param name="Id">The id; unique across all feature classes of the host.</param>
/// <param name="TypeId">The type (see <see cref="TypeRegistry"/>); <c>0</c> for an untyped helper node.</param>
/// <param name="Location">Where the node lies; set for every node, also for helper nodes.</param>
/// <param name="Blocked">The normal switching state: a blocked node is reached, but not passed.</param>
public readonly record struct GraphNode<TNodeId>(TNodeId Id, int TypeId, Coordinate Location, bool Blocked = false)
    where TNodeId : notnull, IEquatable<TNodeId>;

/// <summary>An edge of a network as the host returns it (ADR 0049). Parallel edges and loops are allowed.</summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
/// <param name="Id">The id; unique across all feature classes of the host.</param>
/// <param name="From">The node at the start, in digitizing direction.</param>
/// <param name="To">The node at the end.</param>
/// <param name="TypeId">The type (see <see cref="TypeRegistry"/>).</param>
/// <param name="Length">The length in the units of the coordinates, or a surveyed length.</param>
/// <param name="Direction">The normal direction in which the edge may be walked.</param>
/// <param name="Disabled">The normal switching state: a disabled edge does not exist for a search.</param>
/// <param name="Weights">The values of the weights, travelling with the edge so that a search never queries them on their own.</param>
public readonly record struct GraphEdge<TNodeId, TEdgeId>(
    TEdgeId Id,
    TNodeId From,
    TNodeId To,
    int TypeId,
    double Length,
    EdgeDirection Direction = EdgeDirection.Both,
    bool Disabled = false,
    ReadOnlyMemory<EdgeWeight> Weights = default)
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>;
