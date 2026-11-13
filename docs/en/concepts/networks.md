# Networks

A network is a graph of **nodes** and **edges** made from the lines and points of a utility network (cables, connectors, boxes, transformers, customers) or of a street network. SpatialNET searches it; the **host stores it**. The package `SpatialNET.Network` holds the model, the interface `IGraph` and the searches; `SpatialNET.Imp` adds a graph in memory and a cache for hosts that keep their network in a database ([ADR 0049](../../adr/0049-netzwerk-graph-beim-host-hinter-igraph.md)).

This page covers what is there today: the model, types, weights, the switching state, `Reach` and `ShortestPath`. Building a network from lines and points, tracing the supply of customers and rule checks follow ([ADR 0050](../../adr/0050-netzwerk-bauen-verknoten-teilen-shards.md), [ADR 0051](../../adr/0051-tracing-pro-quelle-versorgung.md), [ADR 0052](../../adr/0052-netzwerkregeln-nachbarn-zaehlen.md)).

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

## Large networks

- A search keeps one entry per visited node. Inside one local network that is a few hundred entries; a search from all transformers of a city visits millions of nodes and needs some hundred megabytes. Run such work per local network.
- With `ShardedGraphBase`, a search inside one local network loads one or two shards; later searches in the same area come from the cache.
- Errors of the data (an edge to a node the host does not return, a missing weight, a negative or infinite cost) are an `InvalidGraphException` with the ids involved; errors of the host pass through unchanged. Every search takes a `CancellationToken`.
