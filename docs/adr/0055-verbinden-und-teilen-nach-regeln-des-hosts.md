# Connecting and splitting by the host's rules

ADR 0050 joins every line end and point within the tolerance into one node and splits every line at every node within the tolerance of its inside. In a utility network several tiers (high, medium and low voltage) are often kept by different departments, and their elements lie on top of each other by chance: a low voltage joint on a vertex of a medium voltage cable split the cable, and a trace on the medium voltage ran into the low voltage. The graph does not need geometry, so two nodes at one place are no problem; the builder only has to know which line meets which node. That is domain policy, so the host decides, by types.

*German version: [0055-verbinden-und-teilen-nach-regeln-des-hosts.de.md](0055-verbinden-und-teilen-nach-regeln-des-hosts.de.md)*

## Consequences

- **Four optional callbacks on `NetworkBuildOptions`, all on type ids.** Without them the build is the one of ADR 0050.
  - `HelperNodeType(edgeType) → nodeType`: the type of the helper node at the end of a line. Line ends join into one helper node only with ends of the same helper type, so ends of two tiers at one place give two helper nodes. A connection between tiers always needs a real node (a transformer); there is no chain of ends across tiers.
  - `Connect(nodeType, edgeType)`: whether a line end within the tolerance of a real node connects to it. Every end of a group of ends connects to the first real node of its place that its type may connect to; the others form the helper node of the group.
  - `Split(nodeType, edgeType)`: whether a node within the tolerance of the inside of a line splits it; asked with the type of the real node, or with the helper type for a T junction. Helper nodes at crossings (`NodeAtCrossings`) are not asked.
  - `Partition(nodeType) → int`: real nodes of different partitions within the tolerance are not a conflict and stay two nodes. When a line end may connect to real nodes of several groups at one place, they become one group for `ResolveCoincidentNodes` (ADR 0054), since an end ends at one node only.
- **`ConnectionDecision`**: `Connect`, `Ignore` (no connection, no note) or `Report` (no connection and a `ConnectionRefused` or `SplitRefused` diagnostic, which names the node's and the line's source in `BuildDiagnostic.Sources`): a refusal the host expects is silent, one that points to a data error is reported. Added later: `ConnectAndReport` connects or splits like `Connect` and adds a `ConnectionReported` or `SplitReported` diagnostic, for data errors the network should not wait for (a line not broken up at a joint as the rules want: the network is built, the notes are the list of places to fix). The diagnostics of connections, refused or reported, carry the pair of types the rule was asked with (`NodeTypeId`, `EdgeTypeId`), so the host can name the rule that decided.
- **Asked once per pair of types** and cached, so the cost does not grow with the data and the answer cannot depend on the order.
- **Helper types.** `NetworkType.Helper` marks node types of helper nodes. Like type 0 they are matched by `untyped` and not by `*` or by a pattern without `untyped`; `untyped & [Low, *, *]` is the helper type of the tier Low.
- **`INetworkIdFactory.NewHelperNode(Coordinate, int typeId)`**: helper nodes of different types may share a location and need different ids that stay stable between builds. Breaking change.
- `SplitAtNodes`, `SplitAtLineEnds` and `NodeAtCrossings` stay as coarse switches before the callbacks.

## Considered Options

- A list of split rules (type patterns) in the build options (rejected: the builder would hold domain policy; the host resolves its patterns once and answers by ids)
- One callback on the generic builder with the sources of node and line (rejected: the decisions are by type, and a callback per object could not be cached)
- One callback per pair of elements for joining ends (rejected: "may connect" is not transitive, so union-find over pairs would join A and C through B; ends join by helper type and attach to real nodes one by one)
- A boolean answer (rejected: a refusal that is a data error needs a note, an expected one must stay silent)
- Cross-tier connections of line ends without a real node (rejected: there is no physical connection between voltage levels without equipment)
