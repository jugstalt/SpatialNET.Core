# PreparedGeometry: thread-safe, with distance

`PreparedGeometry` (ADR 0013) is now **thread-safe after construction**, and it answers **distance questions**: `Distance`, `IsWithinDistance` and `NearestPoints`. The thread safety comes from replacing the internal grid index over the segments by the immutable `SpatialIndex<int>` (ADR 0014).

*German version: [0024-prepared-geometry-thread-sicher-und-distanz.de.md](0024-prepared-geometry-thread-sicher-und-distanz.de.md)*

## Consequences

- **Thread safety:** a `PreparedGeometry` holds only data that is never changed after the constructor (the snapshot of the coordinates and the immutable `SpatialIndex<int>` over the envelopes of its segments); every query uses local state. Any number of threads may query one instance, so a prepared geometry can be built once and shared, for example in a service. The earlier advice "one instance per thread" no longer applies. The old internal grid index `SegmentIndex` is removed.
- **Measured (BenchmarkDotNet-style one-shot runs, `--quick-prepared`, star-shaped polygon, 2,000 point lookups and 500 short probe lines):**

  | | 1,000 vertices, grid | 1,000 vertices, tree | 100,000 vertices, grid | 100,000 vertices, tree |
  |---|---|---|---|---|
  | Prepare | 0.39 ms | 0.9–1.4 ms | 102 ms | 68–82 ms |
  | 2,000 `Locate` | 2.25 ms | 3.3 ms | 105 ms | 100–105 ms |
  | 500 `Intersects` | 1.05 ms | 1.1–1.4 ms | 25 ms | 22–27 ms |

  For small polygons the tree is slower (preparing about two to three times, `Locate` about 45 %, `Intersects` up to 30 %), in absolute terms a millisecond or less per polygon. For polygons of many vertices, where preparing pays, it is on a par or faster. Both are far ahead of the unprepared predicates, which take 35 to 60 seconds for the same 500 probes at 100,000 vertices. The worse small case is accepted for the thread safety.
- **`Distance(IGeometry2D other)`** returns the minimum distance, `0` if the geometries meet (decided by the exact `Intersects` path), `double.NaN` if either is empty.
- **`IsWithinDistance(IGeometry2D other, double distance)`** is `Distance <= distance` with an early exit: the envelope gap rules out far geometries first, and the search stops at the first pair of segments within the distance. A negative or non-finite `distance` throws `ArgumentOutOfRangeException` like `Measures.IsWithinDistance` (ADR 0012, ADR 0019); empty gives `false`.
- **`NearestPoints(IGeometry2D other)`** returns `(Coordinate OnPrepared, Coordinate OnOther)?`, `null` if either is empty. For geometries that meet it returns the same point on both, as `NearestPoints` does (ADR 0019). For disjoint geometries it looks up, for every vertex of `other`, the nearest segment of the prepared one through `SpatialIndex<int>.Nearest`, adds the vertices of the prepared geometry that lie within the best distance, and computes the final pair from the segments. Where several pairs are equally near, the pair may differ from the unprepared method; the distance is the same.
- **Not prepared:** `Intersection`, `Clip` and the other overlays. They run the overlay core, which a segment index does not speed up in a simple way; `Clip` to an envelope is already cheap. `Locate` (ADR 0022) is kept.
- **Fix on the way:** a point outside the envelope of a prepared polygon is `Outside` without a query (before, an invalid query envelope could arise); the snapshot keeps the Z of a point as given instead of inventing `0`, so `NearestPoints` agrees with the unprepared result.
- **Tests (ADR 0008):** a property test compares `Distance`, `NearestPoints` (the points lie on the geometries and realise the distance) and `IsWithinDistance` with the unprepared methods for random polygons, lines and multipoints; cases for holes, touching and intersecting geometries, empty and invalid arguments; a test and a stress test run 16 threads against one instance and compare with the sequential answers. All existing predicate tests run unchanged against the tree.
- **Benchmarks:** `PreparedBenchmarks` (prepare, `Locate`, `Intersects` prepared and unprepared, 1,000 and 100,000 vertices) replace `SegmentIndexBenchmarks`.

## Considered Options

- Keep the grid index and document that `PreparedGeometry` is not thread-safe (rejected: the common use of a prepared geometry is a shared, long-lived object; one instance per thread wastes memory and is easy to get wrong)
- Keep the grid index and make it thread-safe by locking (rejected: serialises exactly the queries that should run in parallel)
- Replace the segment index by `SpatialIndex<int>` (chosen: the tree is immutable and already tested; it is slower for small polygons, which is accepted)
- Prepared `Intersection` and `Clip` (rejected: no simple speed-up, see above)
- A separate `PreparedDistance` type (rejected: one object per prepared geometry is easier to use)
