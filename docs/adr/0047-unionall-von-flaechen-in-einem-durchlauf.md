# UnionAll of polygons in one pass

`UnionAll` (ADR 0018) united polygons in a cascade: groups of four, level by level, each step a two-operand overlay. ADR 0018 left a single pass open "if a benchmark asks for it". A host benchmark did (gView GIS: 1,000 overlapping blobs of 32 vertices, 871 ms in the cascade against 99 ms for Clipper in one run): every level nodes almost every vertex again. **Polygons are now united in one overlay of all inputs.** The two-operand engine is not generalised: the union needs no winding number per input, only whether a point lies in any of them, which the sum of the windings of oriented rings tells, as for the buffer (ADR 0006). Polylines and multipoints keep the cascade.

*German version: [0047-unionall-von-flaechen-in-einem-durchlauf.de.md](0047-unionall-von-flaechen-in-einem-durchlauf.de.md)*

## Consequences

- **No API change.** `UnionAll(IEnumerable<IPolygon2D>, …)` and the general overload with polygonal inputs take the new path; the contract of ADR 0018 (inputs read under the fill rule, empty ones skipped, one grid resolved over all inputs, a single input normalised) stays.
- **Algorithm:**
  - Every input becomes **rings oriented by their nesting** (`PolygonStructure`, ADR 0003): shells counter-clockwise, holes clockwise, an island in a hole a shell again. For rings that do not meet, the winding number of the input is then 1 in its area and 0 outside, exactly its even-odd reading.
  - Whether the rings of an input meet is checked exactly (a sweep over its segments; only neighbours in a ring may share their common vertex). An input whose rings cross, touch, overlap or have no area is **normalised by the overlay first**, under EvenOdd, and then oriented; so are all inputs for a fill rule other than EvenOdd (as before, ADR 0018). Such an input costs one small overlay more; valid data, the common case, costs none.
  - All rings go into **one overlay with `FillRule.Positive`**: the sum of the windings is positive exactly where a point lies in at least one input. The result is valid and normalised like every overlay result.
- **Results:** the same area and the same parts as the cascade, up to the grid. The vertices can differ by about one grid cell where edges cross, because the crossings are now rounded once from the original edges instead of from intermediate results, which is the tolerance ADR 0018 already allowed for a different grouping. The order of the parts and the start vertex of a ring follow the input order. The same sequence still gives the identical result; Z and M come from the first input in the input order that has them at a node.
- **Speed** (BenchmarkDotNet short runs, blobs of 32 vertices in UTM metres, partly overlapping; `HostOverlayBenchmarks`):

  | Blobs | cascade 0.8.0 (ms) | cascade with the engine fixes (ms) | one pass (ms) |
  |---|---|---|---|
  | 100 | 45 | 21 | 7 |
  | 1,000 | 871 | 346 | 118 |

  The cascade numbers already include the faster overlay core of the same release (hash for coincident rings, floating-point filter for crossings, array sweep).
  With the squares of `UnionAllBenchmarks` (`--quick-unionall`), 1,000 disjoint squares take 12 ms instead of the 103 ms of ADR 0018, 1,000 overlapping ones 27 ms instead of 36 ms. The memory falls with the time: 58 MB instead of 762 MB for the 1,000 blobs.
- **Limits:** one overlay holds all vertices at once, so the memory is that of the inputs together instead of a group; for very many inputs this is still linear. The cascade stays for polylines and multipoints, where the noding must keep the ends of every input line (ADR 0018), which a sum of windings does not carry.
- **Tests (ADR 0008):** the tests of ADR 0018 run unchanged (fixtures against the NTS fold on the same grid, property tests against the fold of `Union` in area and validity, any order the same area, the same sequence bit-identical, fill rules, Z and M, parallel calls) and the stress test `UnionAll_equals_the_fold_of_Union`.

## Considered Options

- Keep the cascade and speed up the engine only (rejected: even with the faster core the cascade nodes each vertex once per level and stays about three times slower than one pass)
- Skip parts in each cascade step whose envelope is far from the other operand (the "overlap union" of NTS; rejected: it keeps the levels, needs a closure over the envelopes to stay valid, and changes the result in degenerate cases as well)
- A general n-ary engine with a winding number per input (rejected: the most delicate part of the project generalised, for a union that needs only the sum)
- Normalising every input by the overlay before the pass (rejected: one overlay per input; the exact check for rings that do not meet makes it unnecessary for valid data)
