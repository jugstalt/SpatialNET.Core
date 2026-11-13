# Building a network: joining line ends within a tolerance, splitting lines, ids and shards from the host

A network (ADR 0049) comes from the host's lines and points: cables, overhead lines and connectors from several feature classes, and boxes, transformers, switches and customers as points. SpatialNET gets a **builder** that turns them into nodes and edges. It **joins** every line end and point that lie within a **tolerance** into one node, **splits** a line where a point or the end of another line lies on it (a T junction), assigns every node to a **shard** from polygons the host passes (usually its local networks), and **streams** the nodes and edges to a writer of the host. The geometry is not changed: unlike `SnapTo` (ADR 0046) no vertex moves; a node only says which ends belong together. Ids for everything the builder creates come from the host, so that they stay the same from one nightly build to the next. Every search inside the builder goes through a grid or a spatial index, so the work grows with *n* log *n*, not with *n*², for about 3 million lines.

*German version: [0050-netzwerk-bauen-verknoten-teilen-shards.de.md](0050-netzwerk-bauen-verknoten-teilen-shards.de.md)*

## Consequences

- **Placement:** the package `SpatialNET.Network.Building` (ADR 0049), namespace `SpatialNET.Network.Building`; it depends on Core, Extensions and Network, because it uses the spatial index, prepared geometries and linear referencing.
- **API:**

  ```csharp
  public sealed class NetworkBuilder<TNodeId, TEdgeId, TSource>
  {
      public NetworkBuilder(NetworkBuildOptions options, INetworkIdFactory<TNodeId, TEdgeId, TSource> ids);
      public void AddNode(TSource source, TNodeId id, IPoint2D point, int typeId, bool blocked = false);
      public void AddNode(TSource source, TNodeId id, Coordinate location, int typeId, bool blocked = false);
      public void AddLine(TSource source, IPolyline2D line, int typeId, LineAttributes? attributes = null);
      public BuildResult Build(INetworkWriter<TNodeId, TEdgeId, TSource> writer, CancellationToken cancellationToken = default);
  }
  public sealed record LineAttributes(EdgeDirection Direction = Both, bool Disabled = false,
                                      double? Length = null, IReadOnlyList<EdgeWeight>? Weights = null);
  ```

  - `TSource` is the host's reference to the source object (for example a struct of table and object id); the builder only passes it on.
  - A builder is used once: `Add…` calls can come in any order, `Build` processes the points before the lines, and any call after `Build` throws `InvalidOperationException`. It is not thread-safe.
  - `AddLine` copies the coordinates into compact arrays, so the host can stream its features from the database and release them.
- **Options:** `NetworkBuildOptions` with `Tolerance` (required, finite, greater than 0, in the units of the coordinates, as for `SnapTo`), `SplitAtNodes` and `SplitAtLineEnds` (both on), `NodeAtCrossings` (off), `ShardRegions`, `DefaultShard`, `FallbackShardCellSize` and `Weights` (the weight definitions, needed when lines carry weights). The tolerance is not the grid of the overlays (ADR 0005): the builder rounds nothing.
- **Joining (Verknoten):**
  - Real nodes and line ends go into a hash grid with cells of the tolerance size; each looks only at the 3 × 3 cells around it. Everything within the tolerance of something else is united (union-find). The **connected groups become the nodes**, so the result does not depend on the order of the input.
  - A group with **one real node** becomes that node, with its id, type and location. A group of line ends only becomes a **helper node** (type 0) at the mean of its ends, computed in a fixed order.
  - A group with **two or more real nodes** is a conflict: two boxes at the same place are an error in the data.
  - A chain of ends that reaches further than the tolerance (A near B, B near C, A far from C) is still one node; the builder reports it, because it usually means a too large tolerance or dense data.
- **Splitting:**
  - `SplitAtNodes`: a real node within the tolerance of the inside of a line splits the line at the nearest point and becomes the node between the two parts (a switch in the middle of a cable).
  - `SplitAtLineEnds`: a node of line ends within the tolerance of the inside of another line splits that line there (a connector that ends on a cable).
  - Lines that cross without a common point stay apart (an overhead line over a cable). `NodeAtCrossings` makes every crossing a helper node that splits both lines; it is meant for street networks.
  - The candidates come from a static STR index over the envelopes of the lines, grown by the tolerance (`SpatialIndex<T>`, ADR 0014); only the segments of the candidates are tested exactly. An index over lines instead of segments keeps it at 3 million entries instead of about 15 million.
  - A multi-part line gives edges per part; the part index counts over all parts in order. A closed line whose ends meet gives a loop.
- **Edges:**
  - Each part of a line becomes an edge from the node at its start to the node at its end, in the direction of the line; `Direction`, `Disabled` and `TypeId` come from the line.
  - `FromMeasure` and `ToMeasure` give the range of the part on the source line as geometric length from its start, so that `Substring` (ADR 0017) cuts out exactly that part.
  - `Length` is the geometric length of the part, or, if `LineAttributes.Length` is set (a surveyed length), that length split in proportion to the geometric lengths of the parts.
  - Weights are copied to every part, except weights of kind `Absolute`, which are split in proportion to length like the surveyed length; a line with a weight whose id is not in `options.Weights` is an error.
  - A part shorter than the tolerance, whose ends join the same node, is dropped and reported.
- **Ids:** the host implements `INetworkIdFactory<TNodeId, TEdgeId, TSource>`:
  - `TNodeId NewHelperNode(Coordinate location)` for each helper node. The location is the mean of the ends; deriving the id from it (a hash of the rounded coordinate) keeps it stable between builds, which acknowledged errors and comparisons between nights depend on.
  - `TEdgeId NewEdge(TSource source, int part, int partCount)` for every edge, also for a line that is not split (part 0 of 1).
  - Real nodes keep the id from `AddNode`. A duplicate id from `AddNode` throws `ArgumentException` at once; a duplicate id from the factory is a conflict.
- **Shards:**
  - `ShardRegion(IPolygon2D Area, long ShardId, TypeSet? AppliesTo)`. A node gets the shard of the **first region in the list** whose area covers its location (the boundary counts) and that applies to it. Regions are found with an STR index over their envelopes and tested with prepared geometries (ADR 0013).
  - `AppliesTo` may hold node and edge types. A real node matches by its type; a helper node, which has no type, matches if one of its edges has a type in the set. This lets local networks take the low voltage and coarser polygons the medium voltage.
  - Without a matching region a node gets `DefaultShard`, or, if that is not set, the key of a square grid cell of `FallbackShardCellSize`; both are reported.
  - Overlapping regions are reported once per pair of regions, with the first location.
  - An edge carries the shards of both ends (`FromShard`, `ToShard`); an edge between two shards belongs to both, and the host stores it with both (ADR 0049).
  - The shard id is a `long`. A generic type would make every signature longer for little gain; a host with string keys numbers its regions.
- **Writing:** the host implements `INetworkWriter<TNodeId, TEdgeId, TSource>` with `WriteNode(in BuiltNode)` (node, shard, helper or not, source) and `WriteEdge(in BuiltEdge)` (edge, source, measures, both shards). `InMemoryGraph` (ADR 0049) implements it already. All nodes are written before the first edge, so a database with foreign keys can take them: real nodes in the order of `AddNode`, then helper nodes in a fixed order of their location; edges in the order of `AddLine`, parts ascending.
- **Errors before the first write:** all checks that can fail run before anything is written. Conflicts (real nodes in one group, duplicate ids from the factory, a helper id equal to a real node's id) are collected and thrown together as `NetworkBuildException : InvalidOperationException` with a list of `NodeConflict` (location, kind, the sources involved). The writer therefore never sees a half network because of bad data; an exception of the host or a cancellation can still stop it in the middle, so a host writes into new tables and swaps them only after success.
- **Report:** `BuildResult(NodeCount, EdgeCount, Diagnostics)`; a `BuildDiagnostic` has a kind (`OutsideShardRegions`, `OverlappingShardRegions`, `ClusterWiderThanTolerance`, `IsolatedNode`, `ShortPartDropped`), a location and a message. Diagnostics do not stop the build.
- **Argument checks** (ADR 0012): `null` throws `ArgumentNullException`; a type 0 for a node, a negative surveyed length or an invalid tolerance throw `ArgumentOutOfRangeException`; an empty geometry or coordinates with `NaN` throw `ArgumentException`.
- **Determinism:** the same input in the same order gives the same nodes, edges, ids and output order. Another order gives the same nodes and edges, possibly written in another order.
- **Size:** for about 3 million lines with 5 vertices on average, the copied coordinates take about 250 MB and the grid, the index and the groups about as much again; the computing time is in the range of seconds to a few minutes. Reading from the GIS and writing to the database usually take longer.
- **Path geometry:** `NetworkPathGeometry.ToPolyline(this NetworkPath, Func<TEdgeId, EdgeShape> shapeOf, IGeometryFactory? factory = null)` with `EdgeShape(IPolyline2D Line, double FromMeasure, double ToMeasure)`. The host returns the source line and the range of each edge; the method cuts each part with `Substring`, reverses parts walked against their direction and joins them into one path.
- **Tests (ADR 0008):** NTS has no network builder, so there is no oracle. A brute-force builder in the test project (every end against every end and every line, *n*²) is the reference for small random inputs (CsCheck): the same groups and splits, the same result for a shuffled input up to ids, every line end in exactly one node, the parts of a line covering its length without gaps, surveyed lengths and absolute weights summing to the input, shards equal to a brute-force point-in-polygon. Hand-written cases cover the T junction, the switch in the middle of a cable, two boxes too close, chains, crossings with and without `NodeAtCrossings`, multi-part and closed lines, and the errors.
- **Benchmark:** `NetworkBuildBenchmarks` builds a synthetic network of about 3 million lines in radial local networks and records time and memory; together with `NetworkBenchmarks` (ADR 0049) it decides whether parallel steps or building in tiles are needed.
- **Playground:** a network view of its own, not a descriptor (ADR 0011), because its input is a set of lines and points: draw or load lines and points, build, show real nodes, helper nodes and edges, and the shortest path between two clicked nodes.
- **Not part of this step:**
  - **Incremental updates.** The network is built anew every night. To check one local network on save, the host builds a temporary network from all objects that touch its polygon into an `InMemoryGraph` and checks only the nodes inside (ADR 0052).
  - **Building in tiles** and **parallel steps**, until the benchmark asks for them.
  - Moving vertices: data that should meet but does not can be prepared with `SnapTo` before building.

## Considered Options

- Rounding all ends to the grid of the overlays and treating equal grid points as one node (rejected: two ends 1 mm apart on both sides of a cell border would stay apart, a wrong break in the network at a random place; groups within a tolerance do not have that edge)
- Joining an end to the nearest node already placed, in input order (rejected: the result would depend on the order of the input)
- Moving the vertices onto each other, as `SnapTo` does (rejected: the network would change the geometry the host owns; a node only records which ends belong together)
- Nodes at every crossing by default (rejected: in a utility network an overhead line over a cable or two cables in one trench do not connect; for street networks it is an option)
- Taking the first of two real nodes at the same place and dropping the other (rejected: two boxes at one place are a data error that would otherwise vanish silently)
- Edge ids derived by the library, for example source id and part (rejected: `TEdgeId` is the host's type; the factory gets source and part and can do exactly that)
- Shards from a key per input object (rejected: helper nodes have no object, and at the border of two local networks the lines would disagree; a polygon decides by the node's own location)
- A generic shard id (rejected for now: one more type parameter on every signature)
- Writing nodes and edges mixed as they are found (rejected: a database with foreign keys needs the nodes first, and the conflicts must be known before the first write)
- A segment index instead of a line index (rejected: about five times the entries for the same candidates)
