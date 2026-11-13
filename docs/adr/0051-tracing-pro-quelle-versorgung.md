# Tracing: one run per source, supply areas and the supply of sinks

The question a utility network is mostly asked is not the shortest path but **who is supplied by whom**: which customers hang on transformer 4711, which customers no transformer reaches, and which ones two transformers reach at once, which in a low-voltage network is usually a mistake in the switching state or the data. SpatialNET answers it with a **trace** on `IGraph` (ADR 0049). The host names the **sources** (for example all transformers of an area) and, where it asks about supply, the **sinks** (for example the customers). The trace runs **from each source on its own**, stops at blocked nodes and at other sources, and streams what it reaches. A node that two sources reach is therefore reported **once per source**, so multiple supply becomes visible instead of hidden. The memory a run needs is bounded by **one supply area**, not by the whole network, and a trace over all 10,000 transformers of a network keeps the shard cache warm when the sources come sorted by shard.

*German version: [0051-tracing-pro-quelle-versorgung.de.md](0051-tracing-pro-quelle-versorgung.de.md)*

## Consequences

- **Placement:** static class `NetworkAnalysis` in `SpatialNET.Network`, next to `Reach` and `ShortestPath` (ADR 0049); options are the `TraversalOptions` from there (weight, state, edge and node types, `MaxCost`).
- **Sources and sinks are what the host passes**, not roles in the model: a node is a source because it is in the list of sources of this call. The same transformer is a source for the low-voltage trace and a sink for a medium-voltage one.
- **`Trace(sources, options, cancellationToken)`** returns `IEnumerable<TraceHit<TNodeId, TEdgeId>>`:
  - `TraceHit(Node, TypeId, Source, Cost, Via, Kind)` with `Kind` one of `Source` (the source itself, cost 0), `Reached`, `Blocked` (reached but not passed) and `OtherSource` (another source of the same call, reached but not passed). `Via` is the edge over which the node was reached first; none for the source.
  - **One run per source**, in the order of `sources`; within a run in order of cost (Dijkstra with the chosen weight, default length). The cost is that of the cheapest way from this source, for example the cable length from the transformer.
  - A run does not pass **blocked nodes** (normal state or deviation, ADR 0049) and **other sources**. Without the second stop a trace in a meshed network would run through every station into the whole network.
  - A **blocked source** gives only its own hit with `Kind = Blocked`: a transformer out of service supplies nothing.
  - A node that several sources reach appears **once per source**. Duplicates in `sources` are run once.
  - Directions are respected (ADR 0049); for a network with a direction of flow the trace runs downstream only.
- **`TraceEdges(sources, options, cancellationToken)`** returns `IEnumerable<TracedEdge<TNodeId, TEdgeId>>(Edge, Source)`: every edge a run passes or enters, once per source, also the edge that leads to a blocked node or another source. With it the host draws a **supply area** as lines; the `Via` edges of `Trace` form only a tree and miss the closing edges of a mesh.
- **`Supply(sources, sinks, options, cancellationToken)`** returns `IEnumerable<SinkSupply<TNodeId>>(Sink, Sources)` for **every sink**, in the order of `sinks`, after all runs: an empty list means not supplied, more than one entry means multiple supply. It keeps one small entry per sink (for 500,000 customers some tens of megabytes) and one supply area at a time. `FindUnsupplied(sources, sinks, options, cancellationToken)` returns only the sinks with an empty list.
  - The results of `Supply` come after the last run, because a sink can be reached by the last source; `Trace` and `TraceEdges` stream from the first hit.
  - `Supply` and `FindUnsupplied` need no costs and run as a breadth-first search; `MaxCost` is then ignored, and setting a weight other than length is an `ArgumentException`.
- **Memory:** a run keeps one entry per visited node of its area and frees it when the area is done. In radial low-voltage networks an area has some hundreds to a few thousand nodes. The whole-network case of ADR 0049 (one visited set over millions of nodes) does not arise; only `Supply` keeps its entries per sink.
- **Order and the cache:** the runs ask `IGraph` for the frontier of the current area in batches. If the host passes the sources **sorted by shard**, consecutive runs use the same shards, and a trace over all transformers loads each shard about once. This is a recommendation, not a rule; the result does not depend on the order of the sources except for the order of the output.
- **Determinism:** the same sources, sinks and data in the same order give the same hits in the same order; equal costs are decided by the order of the edges from the host, as in ADR 0049.
- **Exceptions** as in ADR 0049: an unknown source or sink id throws `ArgumentException`; the data errors of `InvalidGraphException`; exceptions of the host pass through; `OperationCanceledException` on cancellation.
- **Tests (ADR 0008):** a reference in the test project that runs a plain breadth-first search and Bellman-Ford per source, compared with CsCheck on random graphs: the same nodes per source, the same costs, stops at blocked nodes and other sources, a blocked source supplies nothing, multiple supply found, `Supply` equal to the reference for every sink, `TraceEdges` equal to every edge with an end in the area, and the same output for the same input. Hand-written cases for two transformers with an open and a closed separation point, a ring fed from both sides, directed edges and duplicate sources.
- **Benchmark:** `NetworkBenchmarks` (ADR 0049) gets a case for `Supply` over the synthetic network with 10,000 sources and 500,000 sinks, with the sources sorted by shard and shuffled, and records time, memory and shard loads.
- **Playground:** in the network view (ADR 0050) nodes can be marked as sources; the view colours each supply area and marks sinks that are not or multiply supplied.

## Considered Options

- One wave from all sources at once that labels each node with the nearest source (rejected: it hides multiple supply, which is the error the host looks for, and needs one visited set over the whole network)
- One hit per node with a list of all its sources (rejected: the list is only known when every run is done, which again needs memory over the whole network; `Supply` gives exactly this form for the sinks, where the host needs it)
- Stopping only at blocked nodes, not at other sources (rejected: in a meshed network every run would cross every station and cover the whole network)
- A breadth-first search for `Trace` too (rejected: the cost from the source, such as the cable length, is wanted, and Dijkstra on one supply area costs little; the breadth-first search stays for `Supply` and `FindUnsupplied`)
- Roles source and sink in the model, or a parameter with barrier types (rejected: the list of sources of a call says what a source is; barriers by type are an `INetworkState` that blocks by type, ADR 0049)
- Only the tree edges (`Via`) for the supply area (rejected: in a mesh they leave out the closing edges, and the drawn area would have gaps)
- Running the sources in parallel (rejected for now: the order of the output and the shard cache are simpler in sequence; the benchmark decides)
