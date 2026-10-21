# Public spatial index: static, STR-packed, over envelopes with a payload

SpatialNET gets a small **public spatial index**, `SpatialIndex<T>` in `SpatialNET.Extensions`. It is a building block for users without a host index and for in-memory result lists, not a replacement for the indexes of a host or a database. It is **static**: built once from `(Envelope2D, T)` items by bulk loading (Sort-Tile-Recursive packing) and immutable afterwards. Mutation (insert, remove) is left out on purpose: a rebuild is cheap, and an immutable structure is thread-safe for reads without locks, which fits the snapshot semantics of ADR 0013. The internal grid `SegmentIndex` of `PreparedGeometry` stays, because the benchmark does not justify replacing it.

*German version: [0014-oeffentlicher-raeumlicher-index-statisch-str.de.md](0014-oeffentlicher-raeumlicher-index-statisch-str.de.md)*

## Consequences

- **Scope:** envelope range queries and k-nearest-neighbours. "Within distance d" is a range query with the envelope widened by d and has no API of its own. Union and spatial joins are separate topics.
- **Placement:** `SpatialNET.Extensions`, namespace `SpatialNET`. No new package, and `SpatialNET.Core` stays contracts only. For the package validation this is an addition only.
- **API:**
  - `new SpatialIndex<T>(IEnumerable<(Envelope2D Envelope, T Item)> items, int nodeCapacity = 16)`, plus `Count` and `Extent`.
  - `IEnumerable<T> Query(Envelope2D)` is the main form. `Query(Envelope2D, ICollection<T> results)` adds to a collection without allocating an enumerator, for hot paths. There is no callback form.
  - `IReadOnlyList<T> Nearest(Coordinate point, int k, Func<T, Coordinate, double>? distance = null)` returns the k nearest items, ascending by distance.
  - For geometries: `ToSpatialIndex()` on `IEnumerable<TGeometry>` (keyed by `Envelope()`), `ToSpatialIndex(Func<T, Envelope2D>)` for arbitrary items, and the extension `Query(IGeometry2D)` with the envelope as a pre-filter.
  - Optional parameters follow ADR 0012.
- **Pre-filter only:** the index knows envelopes, not geometries. A query returns the items whose envelope touches or overlaps the query envelope (closed intervals). The exact test stays with the caller, for example `index.Query(g).Where(x => x.Intersects(g))`. There is deliberately no predicate shortcut, to avoid a second API for predicates.
- **Nearest:** by default the distance is the one from the point to the item's envelope. The optional `distance` delegate returns the exact distance and must never be smaller than the envelope distance, which holds for any real geometry inside its envelope. The search is best-first with exact refinement. Ties are resolved deterministically by input order, so results can be compared against brute force.
- **Empty and invalid envelopes:**
  - An empty envelope (`Envelope2D.Empty`) in the input is skipped, because an empty geometry never meets anything. `Count` counts indexed items only.
  - Partly NaN, infinite or `Min > Max` envelopes throw `ArgumentOutOfRangeException`, as do an invalid query envelope, a non-finite point, `k < 1` and `nodeCapacity < 2` (rules of ADR 0012). An empty query envelope returns nothing. A `distance` delegate that returns NaN throws `InvalidOperationException`.
  - Points and zero-width envelopes (lines) are valid.
- **Duplicates and order:** duplicates are kept, each item is an entry of its own. The order of `Query` results is unspecified; `Nearest` is ordered. `T` may be `null`. The input is copied (snapshot, ADR 0013).
- **Thread safety:** after construction the instance is immutable and every query uses only local state. Any number of threads may query the same instance. This is the difference to the not thread-safe `PreparedGeometry` and `SegmentIndex`.
- **`SegmentIndex` is kept.** The benchmark `SegmentIndexBenchmarks` (segments of a ring, small probe envelopes as in the predicates) shows no gain for the tree: the grid builds faster (up to twice as fast at 100,000 segments for clustered data), and the queries are on par (within about 25 % either way, the tree slightly ahead only for small clustered inputs). A replacement would add risk to the predicates without a gain. It can be revisited if `PreparedGeometry` is ever made thread-safe.
- **Tests (ADR 0008):** oracles are brute force (exact, including the order of ties) and the NTS `STRtree` (range queries and nearest distances). Property tests (CsCheck) cover random sets with duplicates, points, lines and many equal distances, node capacities from 2 to 20, empty and invalid input, and parallel queries. Fixtures do not apply, because the index has no geometry result; `[Covers]` tests take their place.
- **Benchmarks:** `SpatialIndexBenchmarks` (build, query, nearest against brute force and the NTS `STRtree`, 1k to 1M items, uniform and clustered) and `SegmentIndexBenchmarks`. A one-shot run shows the build at 1M items about two to three times faster than the NTS `STRtree` and the range queries on a par or faster.

## Considered Options

- No public index, point to the host indexes (rejected: a library without any index leaves users with brute force on in-memory lists)
- Mutable index with insert and remove (rejected: not thread-safe, harder to test, rebuild is cheap)
- Index of geometries only (rejected: callers often index ids or records; geometries are covered by the helper)
- A predicate shortcut such as `Query(geometry, predicate)` (rejected: a second API for predicates; `.Where` is enough)
- A grid index as the public structure (rejected: depends on the distribution of the data; STR adapts to it)
- Replacing the internal `SegmentIndex` (rejected: no gain in the benchmark)
- A new package for the index (rejected: `SpatialNET.Extensions` is the home of algorithms; one more package is not worth it)
