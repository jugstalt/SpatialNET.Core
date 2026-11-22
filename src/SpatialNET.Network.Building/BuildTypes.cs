namespace SpatialNET.Network.Building;

/// <summary>The options of a <see cref="NetworkBuilder{TNodeId, TEdgeId, TSource}"/> (ADR 0050).</summary>
public sealed record NetworkBuildOptions
{
    /// <summary>
    /// Line ends and points closer than this are joined into one node, and a point or a line end this close to the inside
    /// of a line splits it. In the units of the coordinates; required, finite and greater than 0. It is not the grid of the
    /// overlays: the builder rounds nothing and moves no vertex.
    /// </summary>
    public required double Tolerance { get; init; }

    /// <summary>A real node within the tolerance of the inside of a line splits the line (a switch in the middle of a cable). On by default.</summary>
    public bool SplitAtNodes { get; init; } = true;

    /// <summary>A node of line ends within the tolerance of the inside of another line splits that line (a T junction). On by default.</summary>
    public bool SplitAtLineEnds { get; init; } = true;

    /// <summary>
    /// Every crossing of two lines without a common point becomes a helper node that splits both. Off by default, since in a
    /// utility network an overhead line over a cable does not connect; meant for street networks.
    /// </summary>
    public bool NodeAtCrossings { get; init; }

    /// <summary>
    /// The type of the helper node at the end of a line of the given edge type (ADR 0055). Line ends join into one helper
    /// node only with ends of the same helper type, so the ends of different tiers at one place stay apart. <c>null</c>
    /// (the default) gives every helper node the type 0. A type other than 0 must be a node type of the host's registry,
    /// usually one marked <see cref="NetworkType.Helper"/>.
    /// </summary>
    public Func<int, int>? HelperNodeType { get; init; }

    /// <summary>
    /// Whether a line end within the tolerance of a real node connects to it (ADR 0055), asked with the node type and the
    /// edge type. Ends that connect to no real node form a helper node. <c>null</c> (the default) connects every end.
    /// </summary>
    public ConnectionRule? Connect { get; init; }

    /// <summary>
    /// Whether a node within the tolerance of the inside of a line splits it (ADR 0055), asked with the node type (the type
    /// of the real node, or the helper type of a T junction) and the edge type. <c>null</c> (the default) splits always,
    /// within <see cref="SplitAtNodes"/> and <see cref="SplitAtLineEnds"/>. Helper nodes at crossings are not asked.
    /// </summary>
    public ConnectionRule? Split { get; init; }

    /// <summary>
    /// The partition of a real node by its type (ADR 0055): real nodes of different partitions within the tolerance of
    /// each other are not a conflict and stay two nodes, unless a line end connects to both; then they are one group for
    /// <see cref="NetworkBuilder{TNodeId, TEdgeId, TSource}.ResolveCoincidentNodes"/>. <c>null</c> (the default) puts every
    /// real node into one partition.
    /// </summary>
    public Func<int, int>? Partition { get; init; }

    /// <summary>The polygons that assign shards; the first region in the list that covers a node and applies to it wins.</summary>
    public IReadOnlyList<ShardRegion> ShardRegions { get; init; } = [];

    /// <summary>The shard of a node outside every region; without it, the key of a square grid cell of <see cref="FallbackShardCellSize"/>.</summary>
    public long? DefaultShard { get; init; }

    /// <summary>The size of the grid cells for nodes outside every region when there is no <see cref="DefaultShard"/>. 2,000 by default.</summary>
    public double FallbackShardCellSize { get; init; } = 2000;

    /// <summary>The weight definitions; needed when lines carry weights, so that values of kind <see cref="WeightKind.Absolute"/> can be split.</summary>
    public IReadOnlyList<WeightDefinition> Weights { get; init; } = [];
}

/// <summary>A polygon of the host that gives the nodes in it a shard, usually a local network (ADR 0050).</summary>
/// <param name="Area">The polygon; its boundary counts as inside.</param>
/// <param name="ShardId">The shard.</param>
/// <param name="AppliesTo">
/// The types the region applies to, node and edge types; <c>null</c> for every node. A real node matches by its type, a helper
/// node by the types of its edges.
/// </param>
public sealed record ShardRegion(IPolygon2D Area, long ShardId, TypeSet? AppliesTo = null);

/// <summary>The attributes of a line that the builder copies to its edges (ADR 0050).</summary>
/// <param name="Direction">The direction relative to the digitizing direction of the line.</param>
/// <param name="Disabled">The normal switching state of the line.</param>
/// <param name="Length">A surveyed length; split among the parts in proportion to their geometric length. <c>null</c> for the geometric length.</param>
/// <param name="Weights">The values of weights; copied to every part, values of kind <see cref="WeightKind.Absolute"/> split in proportion to length.</param>
public sealed record LineAttributes(
    EdgeDirection Direction = EdgeDirection.Both,
    bool Disabled = false,
    double? Length = null,
    IReadOnlyList<EdgeWeight>? Weights = null);

/// <summary>
/// Gives ids to everything the builder creates, implemented by the host (ADR 0050). Ids that follow from their arguments
/// stay the same between builds, which acknowledged errors and comparisons between nights depend on.
/// </summary>
public interface INetworkIdFactory<TNodeId, TEdgeId, TSource>
    where TNodeId : notnull, IEquatable<TNodeId>
    where TEdgeId : notnull, IEquatable<TEdgeId>
{
    /// <summary>
    /// The id of a helper node at the location, the mean of the line ends joined there, with its type (0, or the type from
    /// <see cref="NetworkBuildOptions.HelperNodeType"/>): helper nodes of different types may share a location.
    /// </summary>
    TNodeId NewHelperNode(Coordinate location, int typeId);

    /// <summary>The id of part <paramref name="part"/> (0 .. <paramref name="partCount"/> - 1) of the line from <paramref name="source"/>.</summary>
    TEdgeId NewEdge(TSource source, int part, int partCount);
}

/// <summary>The phases of a build, in order (ADR 0050).</summary>
public enum BuildPhase
{
    /// <summary>Joining line ends and points within the tolerance into nodes.</summary>
    JoiningEnds,

    /// <summary>Splitting lines at nodes and crossings.</summary>
    Splitting,

    /// <summary>Assigning shards from the regions.</summary>
    AssigningShards,

    /// <summary>Checking ids before the first write.</summary>
    Checking,

    /// <summary>Writing the nodes.</summary>
    WritingNodes,

    /// <summary>Writing the edges.</summary>
    WritingEdges,
}

/// <summary>How far a build is: <paramref name="Done"/> of <paramref name="Total"/> items in the <paramref name="Phase"/>.</summary>
public readonly record struct BuildProgress(BuildPhase Phase, long Done, long Total);

/// <summary>What a <see cref="BuildDiagnostic"/> reports.</summary>
public enum BuildDiagnosticKind
{
    /// <summary>Nodes outside every region got the default shard or a grid cell; one report per shard.</summary>
    OutsideShardRegions,

    /// <summary>Two regions both matched a node and the first won; one report per pair of regions.</summary>
    OverlappingShardRegions,

    /// <summary>A chain of line ends joined into one node reaches further than the tolerance.</summary>
    ClusterWiderThanTolerance,

    /// <summary>A real node without any line.</summary>
    IsolatedNode,

    /// <summary>A part of a line shorter than the tolerance whose ends join the same node was left out.</summary>
    ShortPartDropped,

    /// <summary>Real nodes within the tolerance of each other were merged into the one the host chose; one report per group (ADR 0054).</summary>
    NodesMerged,

    /// <summary>
    /// A line end within the tolerance of a real node did not connect to it because <see cref="NetworkBuildOptions.Connect"/>
    /// answered <see cref="ConnectionDecision.Report"/>; the sources are the node's and the line's (ADR 0055).
    /// </summary>
    ConnectionRefused,

    /// <summary>
    /// A node within the tolerance of the inside of a line did not split it because <see cref="NetworkBuildOptions.Split"/>
    /// answered <see cref="ConnectionDecision.Report"/>; the sources are the node's (null for a helper node) and the line's (ADR 0055).
    /// </summary>
    SplitRefused,
}

/// <summary>The answer of a <see cref="ConnectionRule"/> (ADR 0055).</summary>
public enum ConnectionDecision
{
    /// <summary>Connect the line end to the node, or split the line at the node.</summary>
    Connect,

    /// <summary>Do not connect or split; no note.</summary>
    Ignore,

    /// <summary>Do not connect or split, and add a note (<see cref="BuildDiagnosticKind.ConnectionRefused"/> or <see cref="BuildDiagnosticKind.SplitRefused"/>): probably a data error.</summary>
    Report,
}

/// <summary>
/// Decides by types whether a line meets a node (ADR 0055); called at most once per pair of types in a build, so it
/// must give the same answer for the same pair.
/// </summary>
/// <param name="nodeTypeId">The node type: of a real node, or the helper type of line ends.</param>
/// <param name="edgeTypeId">The edge type of the line.</param>
public delegate ConnectionDecision ConnectionRule(int nodeTypeId, int edgeTypeId);

/// <summary>
/// Decides about a group of real nodes within the tolerance of each other (ADR 0054): returns the index of the node that
/// stays, the others are merged into it, or -1 for a <see cref="NodeConflictKind.RealNodesTooClose"/> conflict.
/// </summary>
/// <param name="nodes">The group, two or more nodes in the order they were added.</param>
/// <returns>The index of the winner in <paramref name="nodes"/>, or -1.</returns>
public delegate int CoincidentNodesResolver<TNodeId, TSource>(IReadOnlyList<CoincidentNode<TNodeId, TSource>> nodes)
    where TNodeId : notnull, IEquatable<TNodeId>;

/// <summary>A note about the input that did not stop the build.</summary>
/// <param name="Kind">What is reported.</param>
/// <param name="Location">Where.</param>
/// <param name="Message">A message for people.</param>
public readonly record struct BuildDiagnostic(BuildDiagnosticKind Kind, Coordinate Location, string Message)
{
    /// <summary>The host's references involved, where the kind names them (the node first, then the line); otherwise <c>null</c>.</summary>
    public IReadOnlyList<object?>? Sources { get; init; }
}

/// <summary>The result of a build.</summary>
/// <param name="NodeCount">The number of nodes written.</param>
/// <param name="EdgeCount">The number of edges written.</param>
/// <param name="Diagnostics">The notes about the input.</param>
public sealed record BuildResult(int NodeCount, int EdgeCount, IReadOnlyList<BuildDiagnostic> Diagnostics);

/// <summary>Why the input cannot form a network.</summary>
public enum NodeConflictKind
{
    /// <summary>Two or more real nodes lie within the tolerance of each other (two boxes at one place).</summary>
    RealNodesTooClose,

    /// <summary>The id factory returned the same edge id twice.</summary>
    DuplicateEdgeId,

    /// <summary>The id factory returned a helper node id that another node already has.</summary>
    HelperIdTaken,
}

/// <summary>One conflict in the input.</summary>
/// <param name="Location">Where.</param>
/// <param name="Kind">What.</param>
/// <param name="Sources">The sources involved (the host's references, or ids for id conflicts).</param>
public readonly record struct NodeConflict(Coordinate Location, NodeConflictKind Kind, IReadOnlyList<object?> Sources);

/// <summary>
/// Thrown by <see cref="NetworkBuilder{TNodeId, TEdgeId, TSource}.Build"/> before the first write when the input cannot
/// form a network; lists all conflicts at once (ADR 0050).
/// </summary>
public sealed class NetworkBuildException : InvalidOperationException
{
    /// <summary>Creates the exception with the conflicts.</summary>
    public NetworkBuildException(IReadOnlyList<NodeConflict> conflicts)
        : base(Describe(conflicts)) => Conflicts = conflicts;

    /// <summary>The conflicts.</summary>
    public IReadOnlyList<NodeConflict> Conflicts { get; }

    private static string Describe(IReadOnlyList<NodeConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        var first = conflicts.Count > 0 ? conflicts[0] : default;
        return conflicts.Count == 0
            ? "The input cannot form a network."
            : $"The input cannot form a network: {conflicts.Count} conflict(s), the first {first.Kind} at ({first.Location.X}, {first.Location.Y}) with {string.Join(", ", first.Sources)}.";
    }
}
