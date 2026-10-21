# Spatial join: pairs of two indexes by a joint walk, envelopes only

`SpatialIndex<T>` gets two methods that ADR 0014 left out as a separate topic: **`Join`** (all pairs of an item of one index and an item of another whose envelopes meet) and **`SelfJoin`** (all pairs of different entries within one index). Both walk the trees **together**, so that a pair of subtrees whose envelopes do not meet is skipped as a whole. As in ADR 0014 this is a **pre-filter on envelopes only**: the exact test on the geometries stays with the caller (`.Where(p => p.Left.Intersects(p.Right))`), and there is no predicate shortcut. The structure stays immutable and thread-safe, and `SelfJoin` is the main gain: it returns every overlapping pair once, which a loop of queries cannot.

*German version: [0021-spatial-join-doppelbaum-nur-vorfilter.de.md](0021-spatial-join-doppelbaum-nur-vorfilter.de.md)*

## Consequences

- **Placement:** instance methods of `SpatialIndex<T>` in `SpatialNET.Extensions` (the class becomes `partial`). No new package. For the package validation this is an addition only.
- **API:**
  - `Join<TOther>(SpatialIndex<TOther> other, double distance = 0)` returns `IEnumerable<(T Left, TOther Right)>`; `Join<TOther>(other, ICollection<(T, TOther)> results, double distance = 0)` adds the pairs to a collection that the caller owns and does not clear.
  - `SelfJoin(double distance = 0)` returns `IEnumerable<(T First, T Second)>`: every **unordered** pair of **different entries** once. Never an entry with itself, never both `(a, b)` and `(b, a)`; which of the two is `First` is not specified. Entries with an equal payload are different entries and pair up.
  - `Join` with the index itself is allowed and returns every ordered pair, including each entry with itself; that is the plain definition of a join, and `SelfJoin` is the call for the unique pairs.
- **Distance:** `distance` (finite, not negative) widens the envelopes of one side by that amount before they are compared, so the pair is returned if the gap between the envelopes is at most `distance` in both axes. This is still only an envelope test and no distance predicate, like "within distance d" in ADR 0014. A negative, `NaN` or infinite value throws `ArgumentOutOfRangeException` (ADR 0012).
- **Result:** every qualifying pair exactly once, in **no particular order**, like `Query`. A caller that needs an order sorts. Empty indexes give no pairs. `null` throws `ArgumentNullException`. The enumeration is lazy and can be repeated.
- **Thread safety:** both indexes are immutable, and a join uses only local state. Any number of threads may join the same instances, also at the same time.
- **Algorithm:** a stack of pairs of nodes, starting with the two roots. A pair is dropped if the envelopes do not meet (widened by `distance`); two leaves are compared entry by entry; otherwise the children are paired. For `SelfJoin` only pairs `(x, y)` with `x ≤ y` are visited, so each unordered pair of nodes is walked once and, inside one leaf, entry `i` is paired only with `j > i`.
- **Measured:** `SpatialJoinBenchmarks` (one-shot `--quick-join`; the second set is the first one shifted slightly) shows that the gain is real but modest and depends on the data. For 100,000 evenly spread items `Join` takes 46 ms against 100 ms for a loop of `Query` calls (2.2 times faster), and `SelfJoin` 22 ms; for 1,000 items, or for 100,000 items in dense clusters, where the number of pairs dominates (1.4 million), `Join` is on a par with the loop (168 ms against 187 ms). So `Join` is a convenience with a speed-up for large, evenly spread sets; `SelfJoin` is the method that offers something new.
- **Tests (ADR 0008):** oracles are brute force over all pairs and a loop of NTS `STRtree` queries (with the envelope widened by the distance). Property tests (CsCheck) cover random sets of points, lines and rectangles on a coarse grid (many touching and equal envelopes) with different node capacities for the two sides, distances including 0, the collection overload, `SelfJoin` (each pair once, never with itself), a join with the index itself, equal payloads, different payload types, empty and `null` input, invalid distances and parallel joins. Fixtures do not apply because the index has no geometry result.
- **Playground and documentation:** the playground is not affected (instance methods of a data structure, like `Query`); a section in the concept page of the spatial index shows `Join` followed by an exact `.Where`.

## Considered Options

- A predicate shortcut such as `Join(other, predicate)` or `IntersectingPairs()` (rejected: a second API for predicates, as in ADR 0014; `.Where` is enough and shows what the exact test is)
- Extension methods on `IEnumerable` of geometries that build the indexes (rejected: the caller usually has the indexes already, and building them is `ToSpatialIndex()`)
- Returning pairs in a defined order (rejected: sorting costs for every caller, and `Query` is unordered too; ordered output is a one-liner)
- `SelfJoin` returning ordered pairs (rejected: every pair would occur twice, which is exactly what makes the loop of queries unsuitable)
- Excluding an entry's equal-payload twins from `SelfJoin` (rejected: two entries are two entries; a caller that wants to ignore twins filters on the payload)
- A join that applies an exact distance (rejected: that is a geometry predicate, ADR 0014; `distance` only widens envelopes)
- A parallel join (rejected for now: the measured gain is modest, and determinism and simplicity come first)
