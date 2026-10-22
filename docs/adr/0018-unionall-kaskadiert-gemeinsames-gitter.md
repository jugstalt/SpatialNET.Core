# UnionAll: cascaded union over the spatial index, one grid for all steps

SpatialNET gets **`UnionAll`**, the union of many geometries (the "dissolve" of GIS software), because `Union` only takes two operands and a loop over it is quadratic. It is built **from the existing overlay core and `SpatialNET.Extensions` internals**: the inputs are ordered by the Sort-Tile-Recursive packing of the spatial index (ADR 0014) and then united **in small groups, level by level**, so that neighbours meet early and the intermediate results stay small (the cascaded union of JTS). The overlay core itself stays a two-operand engine. All steps share **one grid**, resolved once over all inputs, so that the result does not depend on how the inputs were grouped. A true single-pass n-ary overlay stays an option for later, if a benchmark asks for it.

*German version: [0018-unionall-kaskadiert-gemeinsames-gitter.de.md](0018-unionall-kaskadiert-gemeinsames-gitter.de.md)*

## Consequences

- **Placement:** `UnionAll` in the static class `Overlays`, `SpatialNET.Extensions`, namespace `SpatialNET`. No new package. For the package validation this is an addition only.
- **API** (extension methods; parameters as for `Union`, ADR 0012: required parameters, then `fillRule`, `factory`, `gridSize`):
  - `UnionAll(this IEnumerable<IPolygon2D>, FillRule fillRule = EvenOdd, …)` returns `IPolygon2D`.
  - `UnionAll(this IEnumerable<IPolyline2D>, …)` returns `IPolyline2D` (noded like `Union`: a path is cut where different input lines meet or end). `UnionAll(this IEnumerable<IMultiPoint2D>, …)` returns `IMultiPoint2D`.
  - `UnionAll(this IEnumerable<IGeometry2D>, FillRule, …)` returns `IGeometry2D` and, like the binary `Union`, accepts only inputs of **equal dimension** (points and multipoints count as one dimension); mixed dimensions throw `NotSupportedException`.
- **Inputs:**
  - Each input is read on its own under `fillRule` (default EvenOdd) and then united, exactly as `Union` reads its operands. For a fill rule other than EvenOdd, every input is normalised once under that rule first, and the groups are united under EvenOdd on these valid polygons; this keeps `FillRule.Negative`, which expects clockwise shells, correct across the steps, because intermediate results are counter-clockwise by ADR 0003.
  - Empty geometries are skipped. An empty sequence returns an empty geometry through the factory (an empty polygon for the general overload, as `ConvexHull` does). A `null` element throws `ArgumentNullException`, a `null` sequence too.
  - A **single** input still runs through the core, so the result is valid and normalised (like `Simplify`).
  - The sequence is read once and copied; later changes to the source have no effect (snapshot, ADR 0013).
- **Algorithm:** the inputs are ordered by STR packing of their envelopes (the packing of `SpatialIndex<T>`, shared as an internal helper), cut into groups of four, and each group is united by recursive halving with the two-operand overlay. The result of a group becomes an input of the next level, with its own envelope; the levels repeat until one geometry remains. The cost is about `n log n` two-operand overlays of growing size instead of `n` overlays against an ever larger result.
- **Grid and determinism:** the grid size is resolved **once** over all inputs (explicit `gridSize`, otherwise `Precision.DefaultGridSize`, otherwise automatic from the largest coordinate of all inputs, ADR 0005) and passed explicitly to every step. Every intermediate result then lies on that grid, so how the inputs are grouped can change the result only by about one grid cell (a crossing of already rounded edges may round differently than the crossing of the original edges), never by more.
  - The same input sequence always gives the identical result.
  - A different order of the inputs gives a valid result of the same area up to that grid tolerance, but vertices, the order of the parts and the start vertex of a ring may differ.
- **Z and M:** carried and interpolated as in `Union` (ADR 0005, ADR 0015); at a crossing, which input's value applies is deterministic but not specified, because the inputs are regrouped by position. Intermediate results are created through the factory; if it does not support Z or M, they are dropped from the first step on, which is also the final state.
- **Thread safety:** the method has no shared state; any number of threads may call it, also on the same inputs. It runs sequentially itself. Parallel levels would be a measured optimisation for later and must keep the determinism above.
- **Not part of this step:**
  - a join helper on `SpatialIndex<T>` (pairs of overlapping envelopes), which ADR 0014 left as a separate topic, and
  - "dissolve by key" as its own method: `items.GroupBy(key).Select(g => g.UnionAll())` does it in one line and is shown in the concept page.
- **Tests (ADR 0008):**
  - Fixtures under `samples/unionAll/` carry the inputs as one `GEOMETRYCOLLECTION` in `a` (the test reads it with `Wkt.ReadAll`); the oracle is NTS `OverlayNG` folded over the inputs with the same grid. The mandatory tag catalog applies as for `union`.
  - Property tests (CsCheck): the result equals the fold of `Union` in area and validity for random rectangles and stars, any order of the inputs gives the same area, the same sequence gives bit-identical output, a single input is normalised, many disjoint and many overlapping inputs, and parallel calls.
  - Hand-written cases: empty sequence, empty and `null` elements, mixed dimensions, lines and points, `FillRule.Negative` and `NonZero` inputs.
- **Playground:** exempt from the descriptor test (ADR 0011), because the playground knows only two operands A and B.
- **Benchmarks:** `UnionAllBenchmarks` compares the cascade with the fold `Aggregate(Union)` and the NTS cascaded union; it also decides if a single-pass n-ary core is ever needed. A one-shot run (`--quick-unionall`) with 1,000 squares takes 103 ms for the cascade against 7,448 ms for the fold when the squares are disjoint, and 36 ms against 289 ms when they overlap their neighbours.

## Considered Options

- A loop over the binary `Union` (the fold) as the implementation (rejected: quadratic in the number of inputs, each step re-processes the whole accumulated result)
- A single-pass n-ary overlay core (rejected for now: it generalises the most delicate part of the project from two operands to n; the cascade needs no change there and can be replaced later without an API change)
- Polygons only (rejected: dissolving road networks and point sets is as common, and the typed overloads cost little)
- One general method only (rejected: the typed overloads give the compiler the result type, ADR 0012)
- Mixed dimensions with the lower dimensions dropped (rejected: the strict semantics of ADR 0005 and the binary `Union` forbid it)
- Each step resolving its own automatic grid (rejected: the result would depend on the grouping)
- A parallel cascade from the start (rejected: determinism and simplicity first; the levels are independent and can be parallelised after a benchmark)
- Including the index join and "dissolve by key" in this ADR (rejected: separate topics; the second is one LINQ line)
