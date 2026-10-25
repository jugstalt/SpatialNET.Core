# Project with a window of lengths

A line that crosses itself or doubles back (a hairpin bend, a loop, a road that returns along itself) has several nearest points for one click, and `Project` (ADR 0017) answers with the **smallest length**, so a click on the later pass lands on the earlier one. A host usually knows where the point should be: near the previous result, or within the section it is working on. **`Project` and `ProjectToM` with a window `[from, to]`** search only that stretch of the line and tell the passes apart.

*German version: [0031-project-mit-laengenfenster.de.md](0031-project-mit-laengenfenster.de.md)*

## Consequences

- **Placement:** two more overloads each of `Project` and `ProjectToM` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API** (extension methods on `IPolyline2D`):

  | Method | Returns |
  |---|---|
  | `Project(Coordinate point, double from, double to)` | the length, on the whole line, of the point nearest to `point` within the window |
  | `ProjectToM(Coordinate point, double from, double to)` | the M value at that point |

  Both also exist for `IPoint2D`, which only converts, as without a window. The result is a length (or M) on the **whole line**, so it can be fed to `Interpolate`, `Substring` and the rest unchanged.
- **The window** is the part of the line between the lengths `from` and `to`, with the paths laid end to end as everywhere (ADR 0017). Both are clamped to `[0, length]`, and if `from` is greater than `to` they are **swapped**: unlike `Substring`, a search has no direction, so there is nothing to reverse. The window is **closed**, the nearest point may be at its start or its end, and a point that is nearer to the line outside the window still gets the nearest point *inside* it. An empty window (`from == to`) answers with that length.
- **Same rules as without a window:** among equally near points the smallest length wins; the projection onto a segment is clamped to the part of the segment inside the window; `ProjectToM` gives the M of that point, interpolated if both ends of its segment carry M, `NaN` otherwise; an empty line gives `NaN`. A window over the whole line gives exactly the result of `Project(point)`.
- **"After a position"** (JTS `IndexOfAfter`) is the window `[position, length]`: pass `line.Length()` as `to`. A separate `ProjectAfter` is not added, since JTS falls back to the whole line when nothing lies after the position and this API would rather answer within the window than guess.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for a non-finite point or a `NaN` or infinite `from` or `to`. Infinity is not accepted as "to the end" so that the behaviour is the same as for all other lengths in `LinearReferencing`.
- **Cost:** O(n) like `Project`; segments outside the window are skipped without a distance computation.
- **`CalibrateM` (ADR 0030)** does not use the window yet: its anchors carry no hint where they lie. Anchors on a later pass of a loop still land on the earlier one; a way to give them a window (or to use their order) is a possible next step.
- **Tests (ADR 0008):** NTS has only `IndexOfAfter`, so the cases are hand-written (`[Covers]`), plus a brute-force scan of a dense sampling of a hairpin road against random points and windows. Also a window over the whole line equals no window, the result stays inside the window, several paths and single-point paths, empty lines and invalid input.
- **Playground and documentation:** the playground keeps one input per algorithm without a way to give lengths for a window, and M-based methods are exempt anyway (ADR 0017), so no descriptor; the concept page "linear referencing" has a section.
- **Out of scope for now:** a result with distance and side of the nearest point, a window in terms of M values, a window for `CalibrateM`.

## Considered Options

- `ProjectAfter(point, position)` only, rejected: it covers "after" but not "within this section", and the window includes it
- Accepting `double.PositiveInfinity` as "to the end", rejected: all other lengths here must be finite, and `Length()` is one call
- Throwing if `from` is greater than `to`, rejected: for a search the order of the bounds carries no meaning, so swapping loses nothing
- Falling back to the whole line when the window has no nearer point (as JTS `IndexOfAfter`), rejected: the answer would then lie outside the window the caller asked for
- A `ProjectionOptions` object with window, side and tolerance, rejected for now: two lengths are all this step needs, and the other parts are not decided
