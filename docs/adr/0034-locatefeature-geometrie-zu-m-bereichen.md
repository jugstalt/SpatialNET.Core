# LocateFeature: from a geometry to ranges of M along a route

`LocateBetween` (ADR 0032) turns a range of M into a stretch of the road. A route system also needs the way back: *which part of the route does this parcel, this pipe, this flooded area belong to?* A parcel next to a road becomes "from 4 200 to 4 900", and that range can be stored as an event and combined with the others (ADR 0033). **`LocateFeature`** computes it: the feature is widened by a tolerance and cut with the route, and the M values of what is left are the ranges.

*German version: [0034-locatefeature-geometrie-zu-m-bereichen.de.md](0034-locatefeature-geometrie-zu-m-bereichen.de.md)*

## Consequences

- **Placement:** `LocateFeature` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API** (an extension method on `IPolyline2D`, the route):

  ```csharp
  IReadOnlyList<(double From, double To)> LocateFeature(this IPolyline2D route, IGeometry2D feature,
      double tolerance, IGeometryFactory? factory = null)
  ```

  The feature is a polyline or a polygon. The result is a **list of plain `(From, To)` pairs**, sorted, and not `LineEvent<T>` values: the attribute of an event belongs to the host, and `ranges.Select(r => new LineEvent<T>(r.From, r.To, value))` is one line. A `LineEvent<T>` with a value argument would mix two tasks.
- **The tolerance is required and has no default**, as in `CalibrateM` (ADR 0030): map units differ too much for one default. For a **polygon** it may be 0 (only the area itself, its boundary included); for a **line** it must be greater than 0, since a line has no area and "exactly on the route" is not a thing a buffer can say. A negative, `NaN` or infinite tolerance is an `ArgumentOutOfRangeException`.
- **Algorithm.** The feature is buffered by the tolerance (`Buffer`, ADR 0006; for a polygon with 0 this normalizes it by even-odd, so invalid input is read as usual), and the route is cut with the resulting polygon by the overlay (`Intersection`, ADR 0005), which carries M and interpolates it at the new vertices (ADR 0015). Every piece of the route inside the zone gives one range: the **lowest and the highest M of its vertices**. Since M is interpolated linearly on a segment, that is exactly the M the piece covers, also where M falls or the piece bends back; a piece does not need monotonic M. Ranges that touch or overlap are then joined.
- **Rules, as for the other M methods (ADR 0017):**
  - Only segments with M at both ends take part. A piece is cut at a vertex without M, and the stretch without M is left out; a route without M gives an empty list.
  - A piece **without length** is not a range: a feature that touches the route at one point gives nothing, as `LocateBetween` returns no stretch of length 0 (ADR 0032).
  - A route that passes the feature twice gives two ranges if they are apart in M, and one if they touch or overlap in M. The ranges are ranges of M, not of passes: where the M of two passes overlaps, it cannot be told apart.
  - The route may run in either direction of M; a range is always `From` &lt; `To`.
- **Precision.** The M values at the ends of a range come from the overlay and are exact to about its grid (ADR 0005), not to the last bit. `gridSize` is not exposed; the automatic grid is fine for map data.
- **The factory must support M.** The route is intersected through the factory, so a factory without M would lose the M that the result consists of: `InvalidOperationException`, as `CalibrateM`. An empty route, an empty feature or a polygon that is eroded away give an empty list.
- **Points are not features here.** A point has an M value, not a range, and `LineEvents` ignores ranges without length. A point or a multipoint is an `ArgumentException` that points to `ProjectToM`; with a distance check the caller adds `IsWithinDistance`. Several geometry kinds on one `IGeometry2D` implementation use the first match (polygon, then polyline).
- **Cost:** one buffer and one overlay of the route with it, O(n log n) in the size of both. For many features against one long route a prefilter by envelope and a batch variant would pay; they are not part of this step.
- **Tests (ADR 0008):** NTS has no counterpart, so the cases are hand-written (`[Covers]`): an area crossing the road, sharing its edge, next to it with and without a tolerance, a line next to it, a route that passes twice, joining at a vertex, falling M, stretches without M, a touch at a point, empty inputs, the error cases, and a use of the result as events (`Intersect` with a speed table). M is compared to the exact values, to a small delta where the buffer is round.
- **Playground and documentation:** no descriptor (ADR 0011): the result is a list of number pairs that the surface does not show, and it needs a route with M and a second geometry; listed as exempt with this reason. The concept pages "linear referencing" and "event tables" have a section, and the cookbook recipe on event tables ends with it.
- **Out of scope for now:** side and distance of the feature to the route, a batch for many features with an envelope prefilter, points, a window of lengths so that a feature next to a loop picks one pass (`Project` has one, ADR 0031), and a grid size parameter.

## Considered Options

- Returning `LineEvent<T>` with a value parameter, rejected: the attribute belongs to the host; two lines of LINQ keep the two tasks apart
- A default tolerance, rejected: as for `CalibrateM`, no value is right for every coordinate system
- A tolerance of 0 for lines by intersecting two polylines, rejected: shared pieces of two lines depend on exact collinearity, which real data rarely has, and it would give a result that looks exact and is not
- Computing the ranges by projecting every vertex of the feature (`ProjectToM`) and taking the lowest and highest, rejected: a feature that bends around a corner or spans a bend of the route would give a range that is too long or too short; the cut with the route is the correct answer
- Start and end M of each piece instead of lowest and highest, rejected: a piece with a hairpin or a falling stretch would lose part of the range
- Not joining touching ranges, rejected: the overlay may split a route piece at a vertex, and the caller would see two ranges that are one
- Including points with a range of length 0, rejected: `LineEvents` ignores ranges without length; `ProjectToM` is the right call for a point
- Throwing for a route without M, rejected as in ADR 0017: a stretch that cannot answer is a gap, not an error
