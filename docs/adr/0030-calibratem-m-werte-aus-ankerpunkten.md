# CalibrateM: M values for a line from anchor points

Web GIS often holds a road section as a polyline **without M** and, next to it, **points on the line that carry M** (for example kilometre posts). To find the M of a clicked position, the host looks up the previous and the next anchor and interpolates linearly in the station between them. **`CalibrateM`** does this once and keeps the result: it projects the anchors onto the line, inserts a vertex with M at each of them, and interpolates M along the line between them. The result is a polyline with M, and `ProjectToM` and `InterpolateByM` (ADR 0017) answer from it exactly as the manual formula would. Calibrating also turns a table of points into a line that can be stored and queried like any other line with M.

*German version: [0030-calibratem-m-werte-aus-ankerpunkten.de.md](0030-calibratem-m-werte-aus-ankerpunkten.de.md)*

## Consequences

- **Placement:** `CalibrateM` in `LinearReferencing` (ADR 0017) and the enum `MExtrapolation`, in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API** (extension methods on `IPolyline2D`; parameter order as in ADR 0012):

  ```csharp
  IPolyline2D CalibrateM(IEnumerable<Coordinate> anchors, double maxSnapDistance,
      MExtrapolation extrapolation = MExtrapolation.None, IGeometryFactory? factory = null)
  ```

  An overload takes `IEnumerable<IPoint2D>` and reads M through `IPointM` (a point without it has no M). The anchors are **values** (`Coordinate` with X, Y and M), not geometries, as the helpers of ADR 0019. The result is a geometry and comes from the factory.
- **The tolerance is required and not defaulted.** `maxSnapDistance` is in map units; no value is right for every coordinate system, so the caller names it. An anchor farther from the line than that is **an error** (`ArgumentException` that names the anchor and its distance), never skipped silently: a point that does not lie on the road is a data problem that the caller should see. 0 is allowed and means "exactly on the line".
- **Algorithm.** Each anchor is projected onto the line like `Project` (ADR 0017): nearest point, smallest length among equally near ones. That gives its station (length along the line, paths laid end to end). Anchors are sorted by station, so their order in the input does not matter. A vertex is inserted at each station that does not already lie on a vertex (the new vertex is on the line, not at the anchor's own X and Y, and gets Z interpolated), and a station within 1e-9 of the line length from a vertex is moved onto that vertex, so no near-duplicate vertices appear. Every vertex gets M by **linear interpolation in the station** between the anchors on both sides: the formula `M1 + (M2 − M1) / (s2 − s1) · (s − s1)`. M need not be monotonic. M that the line already carries is **ignored and replaced**; Z is kept.
- **Anchors at the same station.** Stations within 1e-9 of the line length are the same position. Equal M merges them; **different M is an error**, since the line would need two measures at one point.
- **Before the first and after the last anchor** `MExtrapolation` decides. `None` (the default): no M, so that stretch cannot answer, like any line without M (ADR 0017: `ProjectToM` gives `NaN`); no measure is invented. `Clamp`: the M of the nearest anchor. `Linear`: the slope of the first or last interval continues; this needs anchors at two different stations and is an error otherwise.
- **Other errors (ADR 0012):** `ArgumentNullException` for `null`; `ArgumentOutOfRangeException` for a negative, `NaN` or infinite `maxSnapDistance`, non-finite X or Y of an anchor, or an undefined enum value; `ArgumentException` for **no anchor**, an anchor without finite M, and the cases above; `InvalidOperationException` without a factory **or with a factory that does not support M** (the result would have none, which defeats the call). A line without points gives an empty polyline.
- **Known limit: loops.** A line that crosses or doubles back has several nearest points for an anchor near the crossing; the smallest length wins, as in `Project`. An anchor on the later pass therefore lands on the earlier one. A projection restricted to a window of lengths solves it for `Project` (ADR 0031); `CalibrateM` does not use it yet.
- **Cost:** O(anchors · vertices): each anchor is projected by a scan, as `Project` does, and the vertex snap is a linear scan too. Fine for road sections; a length index for many anchors on a long line comes with the index that ADR 0017 already defers.
- **Tests (ADR 0008):** NTS has no counterpart, so the cases are hand-written (`[Covers("CalibrateM")]`) and compare against the host's formula (M by station between the previous and the next anchor), plus inserted vertices, snapping, tolerance, order, non-monotonic M, merging and conflicts, all three extrapolations, Z, several paths, `IPoint2D` input and the error cases.
- **Playground and documentation:** exempt from the descriptor rule (ADR 0011): it needs a list of anchor points with M as a second input, which the surface does not offer (and GeoJSON has no M, ADR 0015). Added to the concept page "linear referencing".
- **Out of scope for now:** a result with distance and side of the nearest point, all hits for an M value, `LocateBetween` (all stretches whose M lies in a range), an offset to the side in `Interpolate`, and event tables.

## Considered Options

- Computing the M of a click directly from the anchors (`InterpolateM(line, anchors, click)`), rejected: the calibrated line can be stored, cached and used for every later query, and the direct form would repeat the projection of all anchors per click
- A default for `maxSnapDistance`, rejected: map units differ too much; a wrong default would reject good data or accept bad
- Skipping anchors beyond the tolerance, rejected: a point that is not on the road is a data problem, and silently dropping it changes the measures
- Interpolating M by the straight-line distance between anchors instead of the station, rejected: M runs along the line, and the old formula uses the station
- Extrapolating by default, rejected: the library does not invent measures (ADR 0017); `Clamp` and `Linear` are an explicit choice
- Keeping the line's own M where the anchors give none, rejected: it would mix two sources of measure
- Setting M on existing vertices only and not inserting vertices, rejected: M would then be wrong between vertices wherever an anchor lies inside a segment
- Resolving loops by the order of the anchors, rejected for now: the order of the input is not a statement about the line; the window projection is the better tool
