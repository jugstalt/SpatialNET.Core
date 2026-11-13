# Network rules: counting the neighbours of an element, rules as rows, checks over streams

A network is only as good as its data, and a utility network has many rules that the GIS cannot enforce while drawing: every box has at least one connector, at the free end of a connector there is exactly one cable or overhead line, low and medium voltage meet only at a transformer, a line does not end in the open. SpatialNET checks such rules on `IGraph` (ADR 0049). A **rule counts the direct neighbours** of an element (the edges at a node, or the other edges at an end of an edge) that match a type pattern, and compares the count with an allowed range. Every rule the host has named so far fits this one shape, so there are no paths, variables or expressions. A rule is **a row of plain values** (patterns as text, numbers, a severity), so the host can keep its rules in a table that the GIS team edits; there is no rule language to parse. The checks **stream**: the host passes the nodes or the edges to check, sorted by shard, and gets the violations back one by one, so a check over 3 million edges needs no more memory than a check over one local network.

*German version: [0052-netzwerkregeln-nachbarn-zaehlen.de.md](0052-netzwerkregeln-nachbarn-zaehlen.de.md)*

## Consequences

- **Placement:** `SpatialNET.Network` (ADR 0049): `NetworkRule`, `NetworkRuleDefinition`, `RuleViolation` and the static class `NetworkValidation` with extension methods on `IGraph`. The geometry check `Validate` on `IGeometry2D` is a different method on a different type; the two never meet in overload resolution.
- **The shape of a rule:**
  - **Subject:** nodes or edges whose type is in a `TypeSet`; `TypeSet.Untyped` addresses helper nodes.
  - **For an edge, the ends to look at:** all ends, ends whose node is in a `TypeSet`, or ends whose node is not. An end that is excluded is not checked; an edge with no end left is not checked at all.
  - **What is counted:** for a node, its edges whose type is in a `TypeSet`; for an edge, the **other** edges at the chosen end (the subject itself never counts).
  - **The range:** `Min` and `Max` (no `Max` means no upper limit). A count outside is a violation.
  - **Severity:** `Error` or `Warning`, for the host to sort and to show.
  - An edge rule is checked **at each chosen end on its own**: a connector with nothing at its free end gives one violation at that end, not a sum over both ends.
- **Building rules in code:**

  ```csharp
  NetworkRule.ForNodes("KASTEN-ABGANG", types.Nodes("[*, Kasten, *]"))
      .CountEdges(types.Edges("[*, Kastenverbinder, *]")).AtLeast(1);

  NetworkRule.ForEdges("KV-FREIES-ENDE", types.Edges("[*, Kastenverbinder, *]"))
      .AtEndsWhereNodeIsNot(types.Nodes("[*, Kasten, *]"))
      .CountOtherEdges(types.Edges("[*, Leitung, *]")).Exactly(1);
  ```

  `ForNodes` and `ForEdges` start a rule, `AtEndsWhereNode` and `AtEndsWhereNodeIsNot` choose the ends, `CountEdges` and `CountOtherEdges` set what is counted, and `Between`, `AtLeast`, `Exactly` and `None` finish it; `AsWarning()` lowers the severity. A finished `NetworkRule` is immutable and can be used for any number of checks and threads.
- **Rules as rows:** `NetworkRuleDefinition(Id, Severity, Subject, SubjectTypes, EndFilter, EndTypes, CountTypes, Min, Max)` holds the same as plain values: `Subject` is `Node` or `Edge`, `EndFilter` is `All`, `NodeIs` or `NodeIsNot`, and the three `…Types` are **pattern text** as for the type registry (ADR 0049). `NetworkRule.FromDefinition(definition, types)` resolves it, and `rule.ToDefinition()` gives it back.
  - Several patterns in one text are separated by `|` and mean OR (`"[*, Kasten, *] | [*, Trafo, *]"`); the same holds for `types.Nodes(…)` and `types.Edges(…)`.
  - `rule.ToString()` gives a short readable form for reports, such as `KV-FREIES-ENDE: edges [*, Kastenverbinder, *], ends not [*, Kasten, *], other edges [*, Leitung, *] = 1`. It is for people, not for parsing.
  - A definition that cannot be a rule throws when it is resolved, with the rule id in the message: a pattern without a match or with a type of the wrong kind (`ArgumentException`), an end filter on a node rule (`ArgumentException`), `Min` below 0 or above `Max` (`ArgumentOutOfRangeException`).
- **Checking:**
  - `ValidateNodes(nodes, rules, cancellationToken)`: the node rules for each given node id. Meant for streams over the whole network.
  - `ValidateEdges(edges, rules, cancellationToken)`: the edge rules for each given **edge record**. The host reads the edges from its database anyway, so `IGraph` needs no lookup of edges by id.
  - `Validate(nodes, rules, cancellationToken)`: the node rules for the given nodes and the edge rules for every edge attached to them, each edge once. It remembers the checked edges and is meant for a bounded area such as one local network.
  - Each method applies only the rules of its kind and ignores the others, so one list of rules serves all three. Rule ids must be unique in the list (`ArgumentException`).
  - `ValidateNodes` and `ValidateEdges` check what they are given; an element passed twice is checked twice. They keep **no set of checked elements**, so their memory does not grow with the network.
- **Reading and the cache:** the methods read their input in blocks (about a thousand elements), ask `IGraph` for the nodes at the ends and the edges at those nodes in one batch each, check the block and yield its violations. Passed **sorted by shard**, the neighbours of a block come from the shards already in the cache. In random order nearly every block would load other shards; this is the difference between minutes and hours for the whole network, and the benchmark shows it.
- **`RuleViolation<TNodeId, TEdgeId>`** with `RuleId`, `Severity`, `Kind` (node or edge), `Node` (the node, or for an edge rule the end where was counted), `Edge` (only for an edge rule), `TypeId` of the checked element, `Count`, `Min`, `Max` and `Location` (of the node, which every node has, ADR 0049). Violations come in the order of the input, for one element in the order of the rules, for an edge first at `From`, then at `To`.
- **Counting:**
  - Parallel edges count one by one: two cables between the same box and junction are two.
  - A loop at the node counts once; it is one edge.
  - The **switching state does not matter**: rules check what is built, not how it is switched; a disabled edge counts like any other.
  - Several patterns in the count add up, as they mean OR.
- **Errors:** the exceptions of ADR 0049; an edge record whose end `GetNodes` does not return is an `InvalidGraphException`.
- **Acknowledged violations** are the host's: the pair of rule id and element is stable between builds, because helper node ids are derived from the location and edge ids from source and part (ADR 0050).
- **Checking one local network on save:** the host builds a temporary network from all objects that touch the area into an `InMemoryGraph` (ADR 0050) and calls `Validate` with only the nodes inside the area. The objects outside are context: their edges are counted at the border, but they are not checked themselves, so a line that leaves the area is not reported as an open end.
- **Not part of the rules:**
  - whether a customer is supplied: that is a trace (`Supply`, ADR 0051), not a local count;
  - attributes the network does not carry (cross-section against fuse): the host checks them;
  - two real nodes at one place: the builder reports them (ADR 0050).
- **Tests (ADR 0008):** a reference in the test project that counts by brute force over an `InMemoryGraph`, compared with CsCheck on random graphs and random rules: the same violations; `ValidateNodes` over all nodes together with `ValidateEdges` over all edges equal to `Validate` over all nodes; the same set of violations for a shuffled input; `FromDefinition` and `ToDefinition` round-tripping. The four rules of the concept are hand-written cases (box with and without connectors, the free end of a connector with a cable, an overhead line and nothing, low and medium voltage at a transformer and at a helper node, an open line end), as are parallel edges, loops, rules with `|` and the errors of definitions.
- **Benchmark:** `NetworkBenchmarks` (ADR 0049) gets `ValidateEdges` over the edges of the synthetic network, sorted by shard and shuffled, and records time, memory and shard loads.
- **Playground:** the network view (ADR 0050) gets a small table of rule rows and marks the violations on the map.

## Considered Options

- A rule language of its own with a parser, kept as text (rejected: a grammar to design, document and keep stable for a shape that fits into a few columns; the patterns are the only text that needs parsing, and the registry already parses them)
- Rules over paths, as a pattern along a chain of elements (rejected: every rule the host named is a count of direct neighbours, and a chain is much harder to check fast and to explain in a report)
- Rules with variables, for example "the same level on both sides" (rejected: the cases that came up are forbidden pairs, which a count with `None()` expresses)
- Only one `Validate` that takes nodes and checks their edges too (rejected: over the whole network it would have to remember millions of checked edges, and checking only edges was not possible)
- Edge ids instead of edge records for `ValidateEdges` (rejected: `IGraph` would need a lookup of edges by id, and every edge would be read twice)
- Summing an edge rule over both ends (rejected: a connector with a cable at one end and nothing at the other would pass a rule "one line at the free end"; the end filter only makes sense per end)
- Rules that look at the switching state (rejected: a check of the data must not change with a switch; supply under a switching state is a trace)
- Repairing what the rules find (rejected: the right repair depends on the data and the domain, as for `Validate` on geometries)
