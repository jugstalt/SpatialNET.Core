# Networks

A network is a graph of **nodes** and **edges** made from the lines and points of a utility network (cables, connectors, boxes, transformers, customers) or of a street network. SpatialNET searches it; the **host stores it**. The package `SpatialNET.Network` holds the model, the interface `IGraph` and the searches; `SpatialNET.Imp` adds a graph in memory and a cache for hosts that keep their network in a database ([ADR 0049](../../adr/0049-netzwerk-graph-beim-host-hinter-igraph.md)).

This page covers the model, types, weights, the switching state, `Reach` and `ShortestPath`, building a network from lines and points with `SpatialNET.Network.Building`, tracing the supply of customers and checking rules ([ADR 0051](../../adr/0051-tracing-pro-quelle-versorgung.md), [ADR 0052](../../adr/0052-netzwerkregeln-nachbarn-zaehlen.md)).

## The host holds the graph

A network of a city has millions of nodes, and the host already has a database. So SpatialNET does not keep the graph; it asks the host for nodes and edges by id, always for many ids at once, through a small interface:

```csharp
public interface IGraph<TNodeId, TEdgeId>
{
    IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids);              // unknown ids are left out
    IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds); // every edge at these nodes, once
}
```

- **Ids are the host's**: `long`, `Guid`, a struct of its own. They must be unique across all feature classes.
- **Batches**: a search asks for a whole frontier at once, so the host answers with one query. A search reads only the part of the network it visits.
- **The same order for the same data**: then the results are deterministic.
- **No enumeration of all nodes**: every search takes its start nodes from the host.

`GraphNode` carries the id, a type, the location and the normal switching state (`Blocked`). `GraphEdge` carries the id, `From` and `To`, a type, the length, the direction (`Both`, `Forward`, `Backward`), `Disabled` and the values of its weights. The link to the source object of the GIS (table, object id, range on the line) is not in the edge; the host keeps it beside it.

Two implementations come with `SpatialNET.Imp`:

- **`InMemoryGraph<TNodeId, TEdgeId, TSource>`** for tests, small networks and the temporary network of one area; filled with `Add`, it can also keep the source of each edge.
- **`ShardedGraphBase<TNodeId, TEdgeId>`** for a host that stores its network in **shards**, usually one per local network. It keeps a number of whole shards in memory; the host writes two methods, `FindShards` (which shard holds these nodes, in one query) and `LoadShard` (the nodes of a shard and every edge that touches them, also the edges to other shards).

## Building a network

`NetworkBuilder` in the package `SpatialNET.Network.Building` makes the nodes and edges from the host's points and lines ([ADR 0050](../../adr/0050-netzwerk-bauen-verknoten-teilen-shards.md)):

- **Joining.** Line ends and points closer than the `Tolerance` become one node. A group with a point of the host becomes that **real node** with its id and type; a group of line ends only becomes a **helper node** (type 0) at the mean of the ends. No vertex moves: the network says which ends belong together, the geometry stays the host's. Two points of the host within the tolerance are an error in the data.
- **Splitting.** A real node on the inside of a line splits it (a switch in a cable), and so does a line end on the inside of another line (a T junction). Lines that cross without a common point stay apart, unless `NodeAtCrossings` is on (for street networks).
- **Edges.** Every part becomes an edge with its range on the source line (`FromMeasure`, `ToMeasure`, for `Substring`), the geometric length or a share of a surveyed length, and the weights of the line; values of kind `Absolute` are split in proportion to length.
- **Ids** come from the host's `INetworkIdFactory`: deriving them from location, line and part keeps them the same from one nightly build to the next. **Shards** come from `ShardRegions`, polygons such as the local networks; the first region that covers a node wins.
- **Writing.** The builder checks everything that can fail first and throws a `NetworkBuildException` with all conflicts before anything is written. Then it writes all nodes and then all edges to an `INetworkWriter`: the host's database, or an `InMemoryGraph`.

```csharp
// The host's ids: a helper node from its location (stable between builds), an edge from its line and part.
sealed class GisIds : INetworkIdFactory<string, string, string>
{
    public string NewHelperNode(Coordinate at) => FormattableString.Invariant($"H({at.X},{at.Y})");

    public string NewEdge(string source, int part, int partCount) => $"{source}#{part}";
}
```

```csharp
IPolyline2D Line(string wkt) => Wkt.Read<IPolyline2D>(wkt, ImpGeometryFactory.Instance);

var builder = new NetworkBuilder<string, string, string>(new NetworkBuildOptions { Tolerance = 0.01 }, new GisIds());
builder.AddNode("Trafo.1", "T", new Coordinate(0, 0), typeId: 25);     // the points of the GIS are the real nodes
builder.AddNode("Switch.1", "S", new Coordinate(30, 0), typeId: 17);
builder.AddLine("Cable.210", Line("LINESTRING(0 0, 100 0)"), typeId: 11);         // one cable ...
builder.AddLine("Connector.1", Line("LINESTRING(50 30, 50 0.004)"), typeId: 13);  // ... and a connector 4 mm beside it

var graph = new InMemoryGraph<string, string, string>();   // or the host's writer into its database
BuildResult result = builder.Build(graph);
Console.WriteLine($"{result.NodeCount} nodes, {result.EdgeCount} edges");        // 5 nodes, 4 edges

// The cable was split at the switch and at the T junction: three edges with their range on the cable.
foreach (var edge in graph.Edges.Where(e => e.Id.StartsWith("Cable.210")))
{
    graph.TryGetSource(edge.Id, out _, out double from, out double to);
    Console.WriteLine($"{edge.Id}: {edge.From} -> {edge.To}, {from} to {to} m");
}
// Cable.210#0: T -> S, 0 to 30 m
// Cable.210#1: S -> H(50,0.004), 30 to 50 m
// Cable.210#2: H(50,0.004) -> H(100,0), 50 to 100 m
```

`Build` takes an optional `IProgress<BuildProgress>` (the phase, items done and total), reported at most every 250 ms, and a `CancellationToken`. The `BuildResult` lists notes that did not stop the build: nodes outside every region, overlapping regions, chains of ends wider than the tolerance, points without a line, tiny parts left out. The join uses a hash grid, the splitting a spatial index over the lines; on a synthetic network of radial local networks the builder needs about 25 seconds for 3 million lines (`--quick-build` in the benchmarks).

To draw a way, `path.ToPolyline(edge => new EdgeShape(line, from, to))` cuts the range of every edge from its source line, turns pieces round where the way runs against them and joins them.

## Searches

`Reach` returns every node that can be reached from the start nodes, in order of cost and lazily, so a caller can stop early or stream into a table. `ShortestPath` returns the cheapest way as a sequence of edges, or `null`.

```csharp
// A small network in memory: a transformer, a box with a switch, two customers.
var graph = new InMemoryGraph<int, int, string>();
graph.Add(new GraphNode<int>(1, 25, new Coordinate(0, 0)));    // transformer
graph.Add(new GraphNode<int>(2, 15, new Coordinate(40, 0)));   // box
graph.Add(new GraphNode<int>(3, 16, new Coordinate(40, 30)));  // customer
graph.Add(new GraphNode<int>(4, 16, new Coordinate(80, 0)));   // customer
graph.Add(new GraphEdge<int, int>(101, 1, 2, 11, 40));         // cable, 40 m
graph.Add(new GraphEdge<int, int>(102, 2, 3, 13, 30));         // connector, 30 m
graph.Add(new GraphEdge<int, int>(103, 2, 4, 13, 40));         // connector, 40 m

// Everything within 75 m of cable from the transformer, in order of cost.
foreach (var hit in graph.Reach([1], new() { MaxCost = 75 }))
    Console.WriteLine($"{hit.Node} after {hit.Cost} m");       // 1 after 0 m, 2 after 40 m, 3 after 70 m

var path = graph.ShortestPath(1, 4)!;
Console.WriteLine(string.Join(" > ", path.Steps.Select(s => s.Edge)) + $" = {path.Cost} m"); // 101 > 103 = 80 m

// The box is switched off today: it is still reached, but not passed.
var today = new NetworkState<int, int>().Block(2);
Console.WriteLine(graph.ShortestPath(1, 4, new() { State = today }) is null);              // True
```

The options (`TraversalOptions`) are the same for every search:

| Option | Effect |
|---|---|
| `Weight` | how the cost of an edge is computed; length by default |
| `State` | deviations from the normal switching state; `null` for the normal state |
| `EdgeTypes` | only edges of these types are walked, for example only low voltage |
| `NodeTypes` | only nodes of these types are entered; helper nodes and start nodes always |
| `MaxCost` | the search ends at this cost |

Equal costs are decided by the order of the start nodes and of the edges from the host. Loops are never walked; of parallel edges the cheaper one wins.

## Tracing

The question a utility network is asked most is **who is supplied by whom**: which customers hang on a transformer, which no transformer reaches, which two reach at once. `Trace` answers it. The host names the **sources** (for example the transformers of an area); the trace runs **from each source on its own**, in the order of the sources, and stops at blocked nodes and at the other sources of the call. A node that two sources reach is therefore reported once per source, so a multiple supply shows instead of hiding. Within one run the hits come in order of cost (the cable length from the transformer, by default), and the memory of a run is freed when its area is done.

```csharp
// Two transformers feed one street: 1 - 2 - 3 - 4 - 5 - 6, customers 2 to 5, a separation point at 3.
var graph = new InMemoryGraph<int, int, string>();
for (int id = 1; id <= 6; id++)
    graph.Add(new GraphNode<int>(id, id is 1 or 6 ? 25 : 16, new Coordinate(id * 30, 0)));
for (int id = 1; id < 6; id++)
    graph.Add(new GraphEdge<int, int>(100 + id, id, id + 1, 11, 30));

int[] transformers = [1, 6];
int[] customers = [2, 3, 4, 5];

// Who supplies whom? With the separation point closed, every customer hangs on both transformers: a mistake to find.
foreach (var supply in graph.Supply(transformers, customers))
    Console.WriteLine($"{supply.Sink}: {string.Join(", ", supply.Sources)}");       // 2: 1, 6 and so on

// Open the separation point: the node is reached by both runs, but nobody passes it.
var open = new NetworkState<int, int>().Block(3);
foreach (var supply in graph.Supply(transformers, customers, new() { State = open }))
    Console.WriteLine($"{supply.Sink}: {string.Join(", ", supply.Sources)}");       // 2: 1   3:   4: 6   5: 6
Console.WriteLine(string.Join(", ", graph.FindUnsupplied(transformers, customers, new() { State = open }))); // 3

// The area of transformer 1 with the cable length from it, and as edges to draw.
foreach (var hit in graph.Trace([1], new() { State = open }))
    Console.WriteLine($"{hit.Node} {hit.Kind} after {hit.Cost} m");                 // 1 Source after 0 m, 2 Reached after 30 m, 3 Blocked after 60 m
Console.WriteLine(string.Join(", ", graph.TraceEdges([1], new() { State = open }).Select(t => t.Edge.Id))); // 101, 102
```

| Call | Returns |
|---|---|
| `Trace(sources, options)` | `TraceHit` per node and source: `Node`, `TypeId`, `Source`, `Cost`, `Via` and `Kind` (`Source`, `Reached`, `Blocked`, `OtherSource`) |
| `TraceEdges(sources, options)` | `TracedEdge` per edge and source: every edge a run passes or enters, including the closing edges of a mesh, so a supply area can be drawn as lines |
| `Supply(sources, sinks, options, progress)` | `SinkSupply` for every sink in the order of the sinks, after all runs: the list of its sources, empty if nobody supplies it, longer than one for a multiple supply |
| `FindUnsupplied(sources, sinks, options, progress)` | the sinks with an empty list |

- A **blocked source** supplies nothing: it gives only its own hit with `Kind = Blocked`. A blocked sink, or a sink that is another source, is not supplied by that run.
- `Supply` and `FindUnsupplied` need no costs and run as breadth-first searches: `MaxCost` is ignored, and a weight other than length is an `ArgumentException`. They are silent until the last run, so they take an `IProgress<SupplyProgress>` (runs done of the total, at most every 250 ms and at the end). `Trace` and `TraceEdges` stream, so their results are the progress.
- A source is whatever the host passes in this call, not a role of the node: the same transformer is a source for the low-voltage trace and a sink for a medium-voltage one.
- Pass the sources **sorted by shard**: consecutive runs then use the same shards, and a trace over every transformer of a network loads each shard about once.

## Types and patterns

A type is a vector of **facets**, for example voltage level, kind of object and construction. Nodes and edges store only the number of their type; the small type table of the host says what it means. Patterns describe sets of types and are resolved once into a `TypeSet`, so a search compares integers only.

```csharp
var types = new TypeRegistry(
    [
        new Facet("Level", new Dictionary<int, string> { [1] = "Low", [2] = "Medium" }),
        new Facet("Object", new Dictionary<int, string> { [1] = "Line", [2] = "BoxConnector", [4] = "Box", [5] = "Transformer" }),
        new Facet("Build", new Dictionary<int, string> { [1] = "Cable", [2] = "Overhead" }),
    ],
    [
        new NetworkType(11, ElementKind.Edge, [1, 1, 1]),   // Low.Line.Cable
        new NetworkType(12, ElementKind.Edge, [1, 1, 2]),   // Low.Line.Overhead
        new NetworkType(13, ElementKind.Edge, [1, 2]),      // Low.BoxConnector
        new NetworkType(15, ElementKind.Node, [1, 4]),      // Low.Box
        new NetworkType(21, ElementKind.Edge, [2, 1, 1]),   // Medium.Line.Cable
        new NetworkType(23, ElementKind.Edge, [2, 2]),      // Medium.BoxConnector
        new NetworkType(25, ElementKind.Node, [2, 5]),      // Medium.Transformer
    ]);

TypeSet connectors = types.Edges("[*, BoxConnector, *]");  // {13, 23}: every connector, on every level
TypeSet low = types.Edges("Low.*");                        // {11, 12, 13}
TypeSet cables = types.Edges("[*, Line, Cable]");          // {11, 21}
TypeSet stations = types.Nodes("[*, Box, *] | [*, Transformer, *]"); // {15, 25}

// Keep a search on the low voltage side of a transformer.
var options = new TraversalOptions<int, int> { EdgeTypes = low };
```

| Pattern | Matches |
|---|---|
| `[Low, *, Cable]` | one entry per facet: a value name, a number or `*` |
| `Low.Line` | the given facets; the facets after them must be "not set" |
| `Low.*` | `Low` and everything below it; a trailing `*` means any value for all remaining facets |
| `*` / `untyped` | every type of the kind, but not the helper nodes / only the helper nodes (type 0) |
| `a \| b` | either |

The facets are a vector, not a tree: the value of a facet means the same under every other facet, which is why `[*, BoxConnector, *]` finds the connectors on every level. A pattern that matches nothing is an error, because it is usually a typo.

## Weights

A `WeightDefinition` names a weight and its kind; the values are on the edges (`EdgeWeight(WeightId, Value)`) and come with them, so a search never asks for a weight on its own.

| Kind | Cost of an edge | Example |
|---|---|---|
| `Length` | length | the default, `WeightDefinition.Length` |
| `Absolute` | value | a fixed time for a ferry |
| `PerLength` | value × length | ohm per kilometre |
| `LengthOverValue` | length ÷ value | travel time from a speed |

The cost of a way is the sum of its edges. An edge without a value takes the `Fallback` of the definition; without one, the search stops with an `InvalidGraphException` when it reaches the edge, instead of counting 0.

## Switching state

The normal state is part of the records: `Blocked` nodes, `Disabled` edges and the `Direction`. For one search, `NetworkState` holds the deviations (`Block`, `Unblock`, `Disable`, `Enable`, `SetDirection`); the network does not change. A **blocked node is reached but not passed**: it appears in the result, nothing behind it does. A disabled edge does not exist for the search. A host with its own logic, for example "every separation point blocked", implements `INetworkState`, whose methods get the whole record.

The names are neutral on purpose: for an electrician a "closed" switch conducts, for a gate it does not.

## Rules

A network is only as good as its data, and a utility network has many rules that the GIS cannot enforce while drawing: every box has a connector, at the free end of a connector there is exactly one cable, a line does not end in the open. A **rule counts the direct neighbours** of an element (the edges at a node, or the other edges at an end of an edge) that match a type pattern, and compares the count with an allowed range. There are no paths, variables or expressions. A rule is **a row of plain values**, so the host can keep its rules in a table that the GIS team edits; there is no rule language to parse.

```csharp
var types = new TypeRegistry(
    [
        new Facet("Level", new Dictionary<int, string> { [1] = "Low", [2] = "Medium" }),
        new Facet("Object", new Dictionary<int, string> { [1] = "Line", [2] = "BoxConnector", [4] = "Box" }),
    ],
    [
        new NetworkType(11, ElementKind.Edge, [1, 1]),   // Low.Line
        new NetworkType(13, ElementKind.Edge, [1, 2]),   // Low.BoxConnector
        new NetworkType(15, ElementKind.Node, [1, 4]),   // Low.Box
    ]);

// Two boxes (1, 2) and helper nodes (3, 4, 5): box 1 has two connectors, box 2 none, and the line at node 4 ends in the open.
var graph = new InMemoryGraph<int, int, string>();
foreach (var (id, type) in new[] { (1, 15), (2, 15), (3, 0), (4, 0), (5, 0) })
    graph.Add(new GraphNode<int>(id, type, new Coordinate(id * 10, 0)));
graph.Add(new GraphEdge<int, int>(101, 1, 3, 13, 10));   // connector to node 3, where a line continues
graph.Add(new GraphEdge<int, int>(102, 3, 4, 11, 10));   // line from node 3 to node 4
graph.Add(new GraphEdge<int, int>(103, 1, 5, 13, 10));   // connector to node 5, where nothing continues

// Rules as rows, as the GIS team keeps them in a table: patterns as text, several separated by "|".
var rows = new NetworkRuleDefinition[]
{
    new("BOX-CONNECTOR", RuleSeverity.Error, RuleSubject.Node, "[*, Box]", RuleEndFilter.All, null, "[*, BoxConnector]", 1, null),
    new("CONNECTOR-FREE-END", RuleSeverity.Error, RuleSubject.Edge, "[*, BoxConnector]", RuleEndFilter.NodeIsNot, "[*, Box]", "[*, Line]", 1, 1),
};
var rules = rows.Select(row => NetworkRule.FromDefinition(row, types)).ToList();

// ... or in code, here as a warning: a line does not end in the open at a helper node.
rules.Add(NetworkRule.ForEdges("LINE-OPEN-END", types.Edges("[*, Line]"))
    .AtEndsWhereNode(TypeSet.Untyped)
    .CountOtherEdges(types.Edges("*")).AtLeast(1)
    .AsWarning());

Console.WriteLine(rules[1]);   // CONNECTOR-FREE-END: edges [*, BoxConnector], ends not [*, Box], other edges [*, Line] = 1

// The nodes of one area and all edges at them; over the whole network use ValidateNodes and ValidateEdges.
foreach (var violation in graph.Validate(graph.Nodes.Select(node => node.Id), rules))
    Console.WriteLine($"{violation.RuleId} ({violation.Severity}) at node {violation.Node}" +
                      (violation.Kind == ElementKind.Edge ? $", edge {violation.Edge}" : "") + $": {violation.Count} found");
// CONNECTOR-FREE-END (Error) at node 5, edge 103: 0 found
// BOX-CONNECTOR (Error) at node 2: 0 found
// LINE-OPEN-END (Warning) at node 4, edge 102: 0 found
```

- **The shape of a rule:** the subject (nodes or edges of a `TypeSet`; `TypeSet.Untyped` addresses helper nodes), for an edge the ends to look at (`All`, `NodeIs` or `NodeIsNot` a `TypeSet`), what is counted (for a node its edges of the given types, for an edge the **other** edges at the end), and the range `Min` to `Max` (no `Max` means no upper limit). In code: `ForNodes` or `ForEdges`, `AtEndsWhereNode` or `AtEndsWhereNodeIsNot`, `CountEdges` or `CountOtherEdges`, then `Between`, `AtLeast`, `Exactly` or `None`; `AsWarning()` lowers the severity. A finished `NetworkRule` is immutable and can be used for any number of checks and threads.
- **An edge rule is checked at each chosen end on its own:** a connector with nothing at its free end gives one violation at that end, not a sum over both ends. A violation says the rule, the node (the end where was counted), the edge, the count that was found, the allowed range and the location of the node.
- **Counting:** the edge itself never counts, parallel edges count one by one, a loop counts once, several patterns (`"a | b"`) add up. The switching state does not matter: rules check what is built, not how it is switched, so a disabled edge counts like any other. Supply under a switching state is a trace.
- **Rows:** `NetworkRuleDefinition(Id, Severity, Subject, SubjectTypes, EndFilter, EndTypes, CountTypes, Min, Max)` holds the same as plain values; `NetworkRule.FromDefinition(row, types)` resolves it and `rule.ToDefinition()` gives it back (for rules whose types came from a `TypeRegistry`; a `TypeSet.Of(...)` has no text). A row that cannot be a rule throws when it is resolved, with the rule id in the message. `rule.ToString()` gives a readable form for reports.
- **Three ways to check:** `ValidateNodes` (node rules, for each given node id) and `ValidateEdges` (edge rules, for each given edge record) keep no set of checked elements and are meant for streams over the whole network; `Validate` (node rules for the given nodes plus edge rules for every edge at them, each once) remembers what it checked and is meant for a bounded area, for example one local network on save: build a temporary network of all objects that touch the area, pass only the nodes inside, and the edges that leave the area are counted at the border but not reported as open ends. Each method applies only the rules of its kind, so one list serves all three; rule ids must be unique.
- **Streaming:** the input is read in blocks of about a thousand and `IGraph` is asked in one batch per block. Pass nodes and edges **sorted by shard**, so that the neighbours of a block come from the shards in the cache. Violations come in the order of the input.
- **Not rules:** whether a customer is supplied is a trace; attributes the network does not carry (cross-section against fuse) are the host's to check; two real nodes at one place are reported by the builder.

## Large networks

- A search keeps one entry per visited node. Inside one local network that is a few hundred entries.
- **Search one area at a time.** One search from all transformers of a city runs as one wave through every local network at once: through a shard cache it loads each shard again and again, in memory it keeps an entry for every node. Search from one transformer (or one area) at a time instead, in the order of the shards, so that each shard is loaded about once.
- Measured on a synthetic network of radial local networks with 300 nodes each (`--quick-network` in the benchmarks): a search in one local network takes under 1 ms in memory; through `ShardedGraphBase` about 1.5 ms with an empty cache and 0.7 ms with a warm one (about 5 ms with an empty cache when every query to the store takes 0.5 ms). Searching every local network of a network with 3 million nodes one by one takes about 10 s and loads every shard once; a single search from all 3,333 transformers of a network with 1 million nodes through a cache of 64 shards loaded shards over 500,000 times.
- Rules over a whole network (`ValidateEdges` with two rules, through a cache of 64 shards, `--quick-network`): all 3 million edges of the synthetic network, sorted by shard, take about 5 s and load every shard once; 1 million edges take about 2 s. The same edges in random order load shards for nearly every block: a sample of 100,000 edges took 21 s at 1 million edges and 30 s at 3 million, which is about 15 minutes for all of them. Sort by shard.
- Errors of the data (an edge to a node the host does not return, a missing weight, a negative or infinite cost) are an `InvalidGraphException` with the ids involved; errors of the host pass through unchanged. Every search takes a `CancellationToken`.
