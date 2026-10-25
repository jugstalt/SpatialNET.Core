# LocateBetween and LocateAlong: all stretches and all points for M

`SubstringByM` and `InterpolateByM` (ADR 0017) follow the **first hit** of an M value. Real M is not monotonic: a road loops, M resets, a route returns along itself. The questions a web GIS asks are then different: "which stretches of this route lie between kilometre 12 and 15?" and "where is kilometre 12.5, everywhere it occurs?". **`LocateBetween`** answers the first with every stretch whose M lies in an interval, **`LocateAlong`** the second with every point where M has a value. They are the OGC operations `ST_LocateBetween` and `ST_LocateAlong` for lines.

*German version: [0032-locatebetween-und-locatealong.de.md](0032-locatebetween-und-locatealong.de.md)*

## Consequences

- **Placement:** `LocateBetween` and `LocateAlong` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API** (extension methods on `IPolyline2D`; parameter order as in ADR 0012):

  | Method | Returns | Meaning |
  |---|---|---|
  | `LocateBetween(double fromM, double toM, factory)` | `IPolyline2D` | all stretches where M lies in `[fromM, toM]` |
  | `LocateAlong(double m, factory)` | `IMultiPoint2D` | all points where M equals `m`, in the order along the line |

  Both results are geometries and come from the factory. A hit list that was a value in ADR 0017 ("a list of all hits is not part of this step") is a geometry here because the OGC operations return one, and because the results are things a host draws.
- **Same rules as the other M methods (ADR 0017):** only segments whose **both ends carry M** take part; a stretch without M is neither bridged nor an error but a gap. M is interpolated linearly on a segment. Nothing throws on data: no hit gives an empty polyline or multipoint. Z is interpolated at new points, original vertices keep their own values (ADR 0015).
- **`LocateBetween`:**
  - The interval is **closed**; if `fromM` is greater than `toM` the bounds are **swapped**, as a search has no direction. The result keeps the **direction of the line**: a stretch where M falls runs from the higher to the lower M.
  - The stretches of consecutive segments form **one path**; a gap (a segment outside the interval or without M) or the end of an input path starts the next path. Cut ends get exactly the bound as M (no rounding error from the interpolation).
  - A stretch **without length** is not part of the result: if M touches the interval only at one point (the top of a hill, a bound equal to M at a single vertex), nothing is returned for it, as a piece of length 0 is nothing (ADR 0017, `Substring`). `LocateAlong` finds such points. Segments of length 0 are skipped.
  - A segment with constant M is entirely inside or entirely outside.
- **`LocateAlong`:** every segment whose M range contains the value gives one point; a hit at a vertex shared by two segments counts **once**; a segment with constant M equal to the value answers with its **start**, as `InterpolateByM` does (so the first point of `LocateAlong` equals `InterpolateByM`). It does not return the whole constant segment: that is what `LocateBetween(m, m)` does.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for a `NaN` or infinite `fromM`, `toM` or `m`, `InvalidOperationException` without a factory.
- **Cost:** O(n), one scan, no state.
- **Tests (ADR 0008):** NTS has no M-based counterpart, so the cases are hand-written (`[Covers]`) and compared with a brute-force scan (the length inside the interval over many random lines with random M). Also: non-monotonic M with several stretches and their direction, joined segments, boundaries and swapped bounds, gaps without M and several paths, constant M, touching at a point, Z at the cut ends, the shared-vertex rule, agreement with `InterpolateByM`, invalid input, empty lines.
- **Playground and documentation:** both have a descriptor (ADR 0011) on the examples with M of the length variants, as the other `...ByM` methods; the concept page "linear referencing" has a section.
- **Out of scope for now:** M intervals for several lines at once, an offset to the side, event tables (point and line events with overlay of intervals).

## Considered Options

- Only `LocateBetween`, rejected: `LocateAlong` is the same scan for points, and without it a repeated M value can only be found by the first hit
- A list or an array of results instead of geometries, rejected: the OGC operations return geometries, and a host draws or stores the result like any other line or multipoint
- Returning one path per segment, rejected: it would split a stretch that continues over many vertices and burden the caller with joining it
- Returning zero-length stretches as one-point paths, rejected: a polyline path needs a length; the points are what `LocateAlong` is for
- Throwing if `fromM` is greater than `toM`, rejected: for a search the order of the bounds carries no meaning, swapping loses nothing
- Letting `LocateAlong` return a constant segment completely, rejected: a point result stays a point; the segment is `LocateBetween(m, m)`
- Interpolating across a gap without M, rejected as in ADR 0017: it would invent measures
