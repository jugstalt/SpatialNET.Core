# Networks: a graph held by the host behind IGraph, typed elements, the first searches

SpatialNET gets **networks**: lines and points of a utility network (cables, connectors, boxes, transformers, customers), or of a street network, turned into a graph of **nodes** and **edges** that can be searched and checked. The library does **not hold the graph**. The host stores it, typically in a database split into **shards** (usually one per local network), and hands out nodes and edges by id through a small interface, **`IGraph<TNodeId, TEdgeId>`**, always for many ids at once. The algorithms are extension methods on that interface, as the geometry algorithms are extension methods on the geometry interfaces (ADR 0001, ADR 0002), and read only the part of the network they actually visit. Everything domain-specific stays with the host: elements carry an integer **type** that a small registry of **facets** describes, edges carry **weights** whose kind the library knows how to turn into a cost, and the **switching state** is stored with the elements and can deviate per call. A target network of about 10,000 transformers and 500,000 customers (2 to 3 million nodes) shaped these choices. This ADR fixes the model and the first two searches; building, tracing and rule checks follow in their own ADRs.

*German version: [0049-netzwerk-graph-beim-host-hinter-igraph.de.md](0049-netzwerk-graph-beim-host-hinter-igraph.de.md)*

## Consequences

- **Packages:**
  - A new package **`SpatialNET.Network`**, namespace `SpatialNET.Network`, that depends on `SpatialNET.Core` only. It holds the model, `IGraph`, types, weights, the switching state and all searches and checks. It needs no geometry algorithms.
  - A second package **`SpatialNET.Network.Building`** (Core, Extensions, Network) for the builder and the path geometry; decided in ADR 0050.
  - `SpatialNET.Imp` gets **`InMemoryGraph`** and **`ShardedGraphBase`** and therefore references `SpatialNET.Network`.
  - The new projects take part in the public API tracking from the start (ADR 0028); package validation has no baseline until the first release that contains them.
- **Model** (immutable `readonly record struct`s):
  - `GraphNode<TNodeId>(Id, TypeId, Location, Blocked)`. `Location` is a `Coordinate` and is set for every node, also for helper nodes created by the builder, so that results can always be shown on a map.
  - `GraphEdge<TNodeId, TEdgeId>(Id, From, To, TypeId, Length, Direction, Disabled, Weights)`. `Direction` is `Both`, `Forward` or `Backward` relative to `From → To`; `Weights` is a `ReadOnlyMemory<EdgeWeight>` of `(WeightId, Value)` pairs.
  - The link to the source object (table, object id, range on the line) is **not** part of the model. The algorithms do not need it; the host keeps it beside the edge.
  - Parallel edges and loops are allowed.
- **Ids:** `TNodeId` and `TEdgeId` are type parameters with `notnull, IEquatable<T>` (a `long`, a `Guid`, a struct of the host). The library never orders them. They must be unique across all feature classes of the host.
- **`IGraph<TNodeId, TEdgeId>`**, implemented by the host:
  - `IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids)`: unknown ids are left out.
  - `IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds)`: every edge that touches at least one of the nodes, each edge once, including edges whose other end lies in another shard.
  - Synchronous and batched: the library asks for many ids at once (a frontier of a search, a block of a stream), so the host can answer with one query. Batch sizes are an implementation detail.
  - For the same data the host returns the same order; the results of the library are deterministic under that condition.
  - The interface has no enumeration of all nodes. Every search and check takes its **start set** from the host (all transformers of a local network, changed objects), which keeps "the whole network" an explicit choice of the caller.
- **`ShardedGraphBase<TNodeId, TEdgeId>`** in `SpatialNET.Imp`: an `IGraph` with a bounded cache of whole shards (least recently used). The host implements two methods, `FindShards(nodeIds)` (one query that maps ids to shards) and `LoadShard(shardId)` (all nodes of the shard and all edges that touch them). How a host computes shards is up to it; ADR 0050 lets the builder assign them from polygons.
- **`InMemoryGraph<TNodeId, TEdgeId, TSource>`** in `SpatialNET.Imp`: an `IGraph` for tests, small networks and the temporary network of one local network, filled by hand or as the target of the builder. It keeps the source of each edge and may enumerate its nodes.
- **Types:**
  - A type is a vector of **facets** (for example level, object kind, construction), each with its own values; `TypeRegistry(facets, types)` is built from the host's type table, which is small and kept in memory. Records carry only the integer `TypeId`; `0` is reserved for untyped helper nodes. Each type is either a node type or an edge type.
  - **Patterns** such as `"[*, Kastenverbinder, *]"` or `"Niederspannung.*"` are resolved once into a **`TypeSet`** (`types.Nodes(…)`, `types.Edges(…)`); the searches then compare integers only. `*` matches every value of its facet, including "not set", so `A.*` contains `A` itself. Several patterns mean OR. `*` does not match untyped helper nodes; `TypeSet.Untyped` does. A pattern that matches no type is an error, because it is usually a typo.
  - Facets, not a tree: the value of a facet means the same under every other facet, so a rule "every connector, on every voltage level" is the pattern `[*, Kastenverbinder, *]`. In a tree the connector under low voltage would have a different number than under medium voltage.
- **Weights:**
  - `WeightDefinition(Id, Name, Kind, Fallback)` with four kinds: `Length` (cost = length; built in as `WeightDefinition.Length`), `Absolute` (cost = value), `PerLength` (value × length, e.g. ohm per km) and `LengthOverValue` (length ÷ value, e.g. travel time from a speed). The host converts units.
  - The cost of a path is the sum of its edges. Combinations of weights, bottleneck costs, time-dependent weights and turn restrictions are not supported.
  - The values travel inside the edge and come with the shard, so a search never queries a weight on its own. A missing value takes the `Fallback`; without one the search stops with an exception when it reaches such an edge, instead of silently using 0.
- **Switching state:**
  - The **normal state** is part of the records (`Blocked`, `Disabled`, `Direction`) and comes with the shard; the default is "everything passable".
  - **Deviations** for one call go through `INetworkState<TNodeId, TEdgeId>` (`IsBlocked`, `IsDisabled`, `GetDirection`, each with the record, so a host can decide by type). `NetworkState<TNodeId, TEdgeId>` implements it with small sets: `Block`, `Unblock`, `Disable`, `Enable`, `SetDirection`.
  - A **blocked node is reached but not passed**; it appears in the result, nothing behind it does. A disabled edge does not exist for the call.
  - The names `Blocked` and `Disabled` are neutral on purpose: for an electrician a "closed" switch conducts, for a gate it does not.
- **Searches in this step** (static class `NetworkAnalysis`, extension methods on `IGraph`):
  - `TraversalOptions<TNodeId, TEdgeId>` with `Weight` (default length), `State` (null = normal state), `EdgeTypes` and `NodeTypes` (null = all; helper nodes are always passable) and `MaxCost`.
  - `Reach(starts, options, cancellationToken)` returns `IEnumerable<ReachedNode>` (node, type, start, cost, the edge it was reached over, blocked) in order of cost, **lazily**: the caller can stop early or stream into a table.
  - `ShortestPath(from, to, options, cancellationToken)` returns `NetworkPath?` (from, to, cost and steps of edge, from, to, reversed, cost); `null` if there is no path. Dijkstra with a binary heap; equal costs are decided by the order in which the host returned the edges.
  - A search keeps one entry per visited node. For about 3 million nodes and `long` ids that is roughly 100 to 300 MB; searches over the whole network should therefore run per local network.
- **Exceptions** (the rules of ADR 0012, plus one type):
  - `InvalidGraphException : InvalidOperationException` when the data cannot be a network: an edge points to a node that `GetNodes` does not return, a weight is missing without a fallback, or a weight is negative, `NaN` or infinite. It names the ids involved.
  - `ArgumentNullException`, `ArgumentOutOfRangeException` and `ArgumentException` for invalid arguments (an unknown start node, a pattern without a match, a negative `MaxCost`).
  - Exceptions of the host pass through unchanged; `OperationCanceledException` when the token fires.
- **Thread safety:** the searches have no shared state. Whether an `IGraph` may be used by several searches at once is up to the host; `ShardedGraphBase` is safe for that, `InMemoryGraph` once it has been filled.
- **Tests (ADR 0008):** there is no NTS oracle for networks. The reference is a plain Bellman-Ford and breadth-first search in the test project, checked with CsCheck on random graphs (optimality of every path, symmetry for undirected graphs, the effect of blocked nodes, disabled edges and directions, determinism of the same input), plus hand-written cases for the pattern rules, missing weights, parallel edges and loops. `ShardedGraphBase` is tested with a fake store that counts queries.
- **Benchmark:** `NetworkBenchmarks` with a synthetic network of about 3 million nodes in radial local networks, read through `ShardedGraphBase` with a simulated query latency. It records the time and memory of a search inside one local network and over the whole network, and decides the default cache size and batch size.
- **Playground:** not yet; a network needs the builder (ADR 0050) to come from drawn geometries.
- **Follow-up decisions:**
  - ADR 0050 Building: joining line ends that lie within a tolerance into one node, splitting lines at points and line ends, ids from the host, shards from polygons, streaming the result to the host.
  - ADR 0051 Tracing: sources and sinks, supply areas, unsupplied customers, nodes that several sources reach.
  - ADR 0052 Rules: local rules that count the neighbours of an element by type, checked over streams of nodes or edges.

## Considered Options

- The library holds the graph in its own structure, like `SpatialIndex<T>` (rejected: a network of millions of elements cannot always be in memory, and the host already has a database; the library would duplicate storage it cannot own)
- Dense `int` ids assigned by the library (rejected: hosts use their own ids, often `Guid`s; translating every result back would cost a lookup per element)
- An asynchronous interface from the start (rejected: it would change every signature and test; a second interface `IAsyncGraph` can follow if a host needs it)
- One call per node (rejected: one database round trip per node makes a search over thousands of nodes slow; batching costs the host nothing)
- Weights and the switching state only as functions per call (rejected: a function that reads the host's database per edge defeats the shards; the normal state and the values belong to the record, deviations per call stay small)
- Fixed node roles in the library (source, sink, switch) (rejected: they force one domain on every host; types, patterns and the switching state express the same without the library knowing the words)
- Types as a tree, or the facet values as columns of every node and edge (rejected: a tree cannot express "every connector on every level"; columns per facet would repeat in millions of records what a small table already says)
- A graph library such as QuikGraph (rejected: ADR 0001, and it would need the whole graph in memory)
- Building, tracing and rules in this ADR (rejected: each has its own trade-offs; this ADR fixes what they share)
