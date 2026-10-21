# Linear referencing: by length and by M, first hit along the line, values instead of geometries

SpatialNET gets **linear referencing** on polylines: a position on a line is addressed either by its **length along the line** or by its **M value** (ADR 0015). The operations are `Interpolate` (the point at a position), `Project` (the position of the point nearest to a given point) and `Substring` (the piece between two positions), each in a length variant and a `...ByM` variant. They are extension methods in `SpatialNET.Extensions`. As elsewhere (ADR 0012), point-like and scalar results are **values** (`Coordinate?`, `double`) that need no factory; only the pieces of `Substring` are geometries and come from the factory. M addresses a position only where the line carries M and never changes the geometry; for non-monotonic M, which real data has, the rule is **first hit along the line**, and nothing throws.

*German version: [0017-lineare-referenzierung-laenge-und-m-erster-treffer.de.md](0017-lineare-referenzierung-laenge-und-m-erster-treffer.de.md)*

## Consequences

- **Placement:** static class `LinearReferencing` in `SpatialNET.Extensions`, namespace `SpatialNET`. No new package, `SpatialNET.Core` stays contracts only. For the package validation this is an addition only.
- **API** (extension methods on `IPolyline2D`; parameter order as in ADR 0012: required parameters, then `factory`):

  | Method | Returns | Meaning |
  |---|---|---|
  | `Interpolate(double distance)` | `Coordinate?` | the point at a length; `null` for a line without points |
  | `Project(Coordinate point)` | `double` | the length of the point on the line nearest to `point`; `NaN` for a line without points |
  | `Substring(double from, double to, factory)` | `IPolyline2D` | the piece between two lengths |
  | `InterpolateByM(double m)` | `Coordinate?` | the point where the line first reaches `m`; `null` if there is none |
  | `ProjectToM(Coordinate point)` | `double` | the M value at the nearest point; `NaN` if the line has no M there |
  | `SubstringByM(double fromM, double toM, factory)` | `IPolyline2D` | the piece between the first hits of two M values |

  `Project` and `ProjectToM` also exist for `IPoint2D`, which only converts. There are no `IGeometry2D` overloads: the operations are defined on lines, and the boundary of a polygon is not a line here.
- **Position along the line:** the planar length, the same as `Length()`. Z does not take part. The paths of a polyline are **laid end to end in their order** (cumulative length), with no connection assumed between the end of one path and the start of the next. Segments of length 0 contribute nothing.
- **`Interpolate`:**
  - `distance` is **clamped** to `[0, length]`: negative gives the start, too large gives the end. There is no "negative counts from the end" as in JTS (a caller computes `length - d`).
  - A line whose points are all equal has length 0 and returns that point.
  - The result carries **Z and M interpolated** on the segment, under the rules of ADR 0015: only if both ends have it, and at an original vertex exactly that vertex's own values.
- **`Project`:** nearest point in the plane, planar distance. Among equally near points (a line that crosses or touches itself) the **smallest length** is returned, so that the answer is deterministic. The projection onto a segment is clamped to the segment.
- **`Substring`:**
  - Both lengths are clamped. `from < to` runs forward, `from > to` returns the piece **reversed** (the direction follows from → to), as in JTS. If the lengths are equal after clamping, the result is an **empty polyline**: a piece of length 0 is nothing, in line with the collapse rules of ADR 0006 and ADR 0007.
  - The piece is a polyline of **one path per touched path**; the gap between two paths is not bridged.
  - New end vertices carry interpolated Z and M, original vertices inside keep theirs. The result is restricted to `SupportsZ`/`SupportsM` of the factory (ADR 0015).
- **The `...ByM` variants:**
  - Only segments whose **both ends carry M** take part. A stretch without M is neither interpolated across nor an error: it simply cannot answer.
  - **First hit:** the line is scanned in its order (paths in order, segments in order). The first segment whose M range contains the value (closed interval) answers; the position inside it is linear in M. A segment with constant M equal to the value answers with its start. Non-monotonic M and values that occur several times (a loop) are therefore allowed, and the first occurrence wins.
  - `InterpolateByM` returns `null` if no segment answers. `SubstringByM` finds the length of both first hits and then behaves like `Substring`; if one value has no hit, the result is an empty polyline. `ProjectToM` returns the interpolated M at the nearest point (the same nearest point as `Project`), `NaN` if that segment has no M at both ends.
  - A list of all hits is not part of this step (see Considered Options).
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for a `NaN` or infinite `distance`, `from`, `to`, `m`, `fromM`, `toM` or a non-finite point, `InvalidOperationException` without a factory (only `Substring` and `SubstringByM`). Empty or degenerate lines **never** throw; they answer `null`, `NaN` or an empty polyline as above.
- **No state, no snapshot:** each call computes from the geometry in O(n). A reusable index of cumulative lengths for many queries on one long line can be added later without a break if a benchmark asks for it.
- **Tests (ADR 0008):**
  - Oracle for the length variants is the NTS `LengthIndexedLine` (`ExtractPoint`, `IndexOf`, `ExtractLine`), with the documented differences (negative indices, clamping, the tie rule of `Project`) as `divergence`. Fixtures under `samples/interpolate/`, `samples/project/` and `samples/substring/` for the cases NTS can judge; the tag catalog is satisfied or its exceptions are justified in `_catalog.json` (hole, nesting and similar tags do not apply to lines).
  - NTS has no M-based counterpart, so `...ByM` is covered by hand-written cases (`[Covers]`) and property tests: the first-hit rule against a brute-force scan, loops, gaps without M, constant M, M of zero.
  - Property tests (CsCheck) for the length variants: `Project(Interpolate(d))` is `d` on simple lines; the length of `Substring(a, b)` is `|b − a|` after clamping; `Substring(0, length)` has the same X/Y as the line; for monotone M, `InterpolateByM(M of Interpolate(d))` returns the same point; M never changes any X/Y.
- **Playground and documentation:** every public method needs a descriptor (ADR 0011); the six methods follow the pattern of `Area` and `Centroid` (scalar and point results) and `Cut` (geometry results). A new concept page "linear referencing" in English and German, and a glossary entry.
- **Out of scope for now:** polygon boundaries, the list of all hits for a value, 3D lengths, M-based measures such as a length along M, and a precomputed length index.

## Considered Options

- Length variants only (rejected: the main reason for M in the model is to address a line by it)
- Negative lengths count from the end as in JTS (rejected: clamping is simpler and avoids silent surprises; the end is `length - d`)
- Throw if a length lies outside the line (rejected: ADR 0012 keeps algorithms from throwing on data; clamping is predictable)
- Require monotonic M for the M variants and throw otherwise (rejected: real networks have loops and resets; the first-hit rule answers without an exception)
- Bridge gaps without M by interpolating across them (rejected: it would invent measures)
- A list of all hits as the only M API (rejected for now: most callers want one point; a list variant can be added without a break)
- `Interpolate` creating a point through the factory (rejected: a point-like result is a value, as `Centroid` in ADR 0012)
- `Project` returning a structure with length, point and distance (rejected: `Interpolate(Project(p))` and `Distance` give the rest)
- `IGeometry2D` overloads that accept polygon boundaries (rejected: the position along a polygon's rings has no natural start)
- A `LengthIndex` object from the first step (rejected: no benchmark asks for it; it can follow without a break)
