# Coincident real nodes: the host may choose a winner

ADR 0050 made two real nodes within the tolerance of each other a data error: the build stops before the first write and lists every such group. Real utility data breaks this rule on purpose. A customer is often drawn on the house connection box it is supplied from (one network had 11,362 such pairs), and a line end marker sits on whatever node ends the line. These are one place in the network, drawn twice. The host knows which of them should stand for the place; the builder does not.

*German version: [0054-zusammenfallende-echte-knoten.de.md](0054-zusammenfallende-echte-knoten.de.md)*

## Consequences

- **The rule of ADR 0050 stays the default.** Without a resolver, a group of real nodes within the tolerance is a `RealNodesTooClose` conflict, as before.
- **`NetworkBuilder.ResolveCoincidentNodes`** (a `CoincidentNodesResolver<TNodeId, TSource>`) is called **once per group**, before the first write, with all real nodes of the group (`CoincidentNode`: id, type, location, source) in the order they were added. It returns the index of the node that stays, or -1 for a conflict. Any other value throws `InvalidOperationException`.
- **Once per group, not per pair:** three nodes at one place get one decision, so the result cannot depend on the order in which pairs are found. A resolver that decides by the nodes (type, source), not by their position in the list, keeps the build independent of the input order.
- **The losers are merged, not dropped.** They are not written as nodes, do not split lines and are not reported as isolated; the winner is written with `BuiltNode.Merged`, the list of merged nodes, so the host can keep them as aliases (a customer must still find its node). One `NodesMerged` diagnostic per group.
- **The policy belongs to the host** (for example: the layer higher in the map's table of contents wins). The builder has no priority lists, no allowed pairs and no marker types.

## Considered Options

- A priority list of type patterns in the build options (rejected: the builder would hold domain policy, and the host's natural order, such as the table of contents, is not a type pattern)
- A list of type pairs that may coincide (rejected for now: a resolver can do it, and every merge is reported)
- Marker types that never form a node of their own and attach to the node at their place (rejected: a second kind of node in the builder for what a resolver expresses with one callback)
- Keeping the first node in input order (rejected in ADR 0050 already: the result would depend on the data order)
- A callback per pair (rejected: three nodes at one place would give order-dependent results)
