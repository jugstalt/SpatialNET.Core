namespace SpatialNET.Network;

/// <summary>
/// The switching state of a network for one search (ADR 0049). The normal state is part of the records; an implementation
/// returns deviations from it. Each method gets the whole record, so a host can decide by type.
/// </summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
public interface INetworkState<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>Whether the node is blocked: reached, but not passed. The normal state is <see cref="GraphNode{TNodeId}.Blocked"/>.</summary>
    bool IsBlocked(in GraphNode<TNodeId> node);

    /// <summary>Whether the edge is disabled: it does not exist for the search. The normal state is <see cref="GraphEdge{TNodeId, TEdgeId}.Disabled"/>.</summary>
    bool IsDisabled(in GraphEdge<TNodeId, TEdgeId> edge);

    /// <summary>The direction in which the edge may be walked. The normal state is <see cref="GraphEdge{TNodeId, TEdgeId}.Direction"/>.</summary>
    EdgeDirection GetDirection(in GraphEdge<TNodeId, TEdgeId> edge);
}

/// <summary>
/// Deviations from the normal switching state for one or more searches, kept in small sets (ADR 0049): "switch 4711 is
/// open today". The network itself does not change.
/// </summary>
/// <remarks>Not thread-safe while it is changed; reading it from several searches at once is safe.</remarks>
public sealed class NetworkState<TNodeId, TEdgeId> : INetworkState<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    private readonly Dictionary<TNodeId, bool> _blocked = [];
    private readonly Dictionary<TEdgeId, bool> _disabled = [];
    private readonly Dictionary<TEdgeId, EdgeDirection> _directions = [];

    /// <summary>Blocks the node, whatever its normal state.</summary>
    public NetworkState<TNodeId, TEdgeId> Block(TNodeId node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _blocked[node] = true;
        return this;
    }

    /// <summary>Makes the node passable, whatever its normal state.</summary>
    public NetworkState<TNodeId, TEdgeId> Unblock(TNodeId node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _blocked[node] = false;
        return this;
    }

    /// <summary>Disables the edge, whatever its normal state.</summary>
    public NetworkState<TNodeId, TEdgeId> Disable(TEdgeId edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _disabled[edge] = true;
        return this;
    }

    /// <summary>Enables the edge, whatever its normal state.</summary>
    public NetworkState<TNodeId, TEdgeId> Enable(TEdgeId edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _disabled[edge] = false;
        return this;
    }

    /// <summary>Sets the direction of the edge instead of its normal direction.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    public NetworkState<TNodeId, TEdgeId> SetDirection(TEdgeId edge, EdgeDirection direction)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (direction is not (EdgeDirection.Both or EdgeDirection.Forward or EdgeDirection.Backward))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction.");
        }

        _directions[edge] = direction;
        return this;
    }

    /// <inheritdoc/>
    public bool IsBlocked(in GraphNode<TNodeId> node) =>
        _blocked.TryGetValue(node.Id, out bool blocked) ? blocked : node.Blocked;

    /// <inheritdoc/>
    public bool IsDisabled(in GraphEdge<TNodeId, TEdgeId> edge) =>
        _disabled.TryGetValue(edge.Id, out bool disabled) ? disabled : edge.Disabled;

    /// <inheritdoc/>
    public EdgeDirection GetDirection(in GraphEdge<TNodeId, TEdgeId> edge) =>
        _directions.TryGetValue(edge.Id, out var direction) ? direction : edge.Direction;
}

/// <summary>The options shared by the searches (ADR 0049).</summary>
/// <typeparam name="TNodeId">The host's node id.</typeparam>
/// <typeparam name="TEdgeId">The host's edge id.</typeparam>
public sealed record TraversalOptions<TNodeId, TEdgeId>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>How the cost of an edge is computed; <see cref="WeightDefinition.Length"/> by default.</summary>
    public WeightDefinition Weight { get; init; } = WeightDefinition.Length;

    /// <summary>Deviations from the normal switching state; <c>null</c> means the normal state.</summary>
    public INetworkState<TNodeId, TEdgeId>? State { get; init; }

    /// <summary>
    /// Only edges of these types are walked; <c>null</c> means every edge. The nodes behind other edges are not loaded,
    /// so a filter on the voltage level also keeps the shards of the other level out of the search.
    /// </summary>
    public TypeSet? EdgeTypes { get; init; }

    /// <summary>
    /// Only nodes of these types are entered; <c>null</c> means every node. Untyped helper nodes and the start nodes are
    /// always entered.
    /// </summary>
    public TypeSet? NodeTypes { get; init; }

    /// <summary>The search ends at this cost; nodes beyond it are not reached. Not negative, <c>+∞</c> by default.</summary>
    public double MaxCost { get; init; } = double.PositiveInfinity;
}
