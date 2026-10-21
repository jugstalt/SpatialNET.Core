# Measure and point helpers: nearest points, distance test, point on surface, boundary, densify, reverse

Six small helpers close gaps that hosts meet daily and that the existing algorithms do not cover: **`NearestPoints`** (the two points that realise `Distance`), **`IsWithinDistance`** (a fast yes/no distance test), **`PointOnSurface`** (a point that lies on the geometry, for labels), **`Boundary`**, **`Densify`** and **`Reverse`**. They follow the rules already fixed: point-like and scalar results are **values** without a factory (ADR 0012), geometric results come from the factory and are restricted to what it supports (ADR 0004, ADR 0015), empty and invalid input never throws, and invalid numeric parameters throw `ArgumentOutOfRangeException`. They add no new concept, so they share one ADR.

*German version: [0019-mess-und-punkt-helfer-werte-statt-geometrien.de.md](0019-mess-und-punkt-helfer-werte-statt-geometrien.de.md)*

## Consequences

- **Placement:** `NearestPoints`, `IsWithinDistance` and `PointOnSurface` join `Measures` (distance and point values, like `Distance` and `Centroid`). `Boundary`, `Densify` and `Reverse` are in a new static class `Constructions` (geometric results through the factory). `Measures` becomes `partial`. Both are in `SpatialNET.Extensions`, namespace `SpatialNET`; for the package validation this is an addition only.
- **`NearestPoints(IGeometry2D a, IGeometry2D b)`** returns `(Coordinate OnA, Coordinate OnB)?`:
  - `null` if either geometry is empty (as `Distance` is `NaN`, ADR 0012); the distance between the two points is `Distance(a, b)`.
  - If the geometries intersect, or one lies in the area of the other, the point is the same on both sides (a point of intersection, or a point of the inner geometry that lies in the area).
  - Among several equally near pairs, the **first** in input order wins, so the result is deterministic.
  - Z and M of the two points are **interpolated on their segment** under the rules of ADR 0015 and ADR 0017 (only if both ends carry them, at a vertex its own values). The coordinates of a crossing of two segments are computed in floating point, so the two sides may differ by rounding.
  - There is no `ShortestLine`; a caller builds the line with `factory.CreatePolyline` in one line.
- **`IsWithinDistance(IGeometry2D a, IGeometry2D b, double distance)`** returns `bool`: the distance is at most `distance`; `distance = 0` means the geometries touch or overlap. Empty geometries give `false`. A negative, `NaN` or infinite `distance` throws `ArgumentOutOfRangeException`. It stops at the first pair of segments within the distance and starts with an envelope test, so it is faster than comparing `Distance` for large inputs.
- **`PointOnSurface(IGeometry2D)`** returns `Coordinate?` without Z and M (like `Centroid`), `null` for an empty geometry. The point lies **on the geometry**:
  - **Polygon:** an interior point, taken from the widest interior stretch of a horizontal line through the envelope, between the two vertex heights nearest to its centre (the method of JTS `InteriorPointArea`); holes and parts are handled by even-odd over all rings. A polygon without area falls back to a vertex of its rings.
  - **Polyline:** the interior vertex (not a path end) nearest to the centroid; if there is none, the point at half the length along the line.
  - **Point and multipoint:** the point nearest to the centroid, the first among equals.
  - The result is deterministic. It is not the same point as the one of JTS or NTS in every case, so the oracle checks the property (on the geometry, in the interior where an area has one), not equality.
- **`Boundary`:**
  - `Boundary(IPolygon2D)` returns `IPolyline2D`: the rings, normalised (shells counter-clockwise, holes clockwise, degenerate rings dropped, ADR 0003), one path per ring.
  - `Boundary(IPolyline2D)` returns `IMultiPoint2D`: the end points by the **mod-2 rule** of ADR 0013 (an end point that occurs an odd number of times; closed paths have no boundary).
  - Point and multipoint have an empty boundary, returned as an empty `IMultiPoint2D`. A general overload on `IGeometry2D` returns `IGeometry2D`.
  - Z and M are kept from the rings and end points, restricted to the factory.
- **`Densify(maxSegmentLength)`** for `IPolyline2D`, `IPolygon2D` and a general `IGeometry2D` overload (points are copied unchanged):
  - A segment longer than `maxSegmentLength` is split into `ceil(length / maxSegmentLength)` pieces of equal length; shorter segments are untouched. The shape does not change, and original vertices are kept. This is the rule of the NTS `Densifier`.
  - Z and M of the new vertices are interpolated (both ends needed, ADR 0015). Polygon rings are densified after the normalisation of ADR 0003, so shells stay counter-clockwise and the polygon stays valid.
  - `maxSegmentLength` must be finite and greater than 0, otherwise `ArgumentOutOfRangeException`. A value so small that the result would exceed 50 million vertices also throws `ArgumentOutOfRangeException` instead of exhausting memory.
- **`Reverse(IPolyline2D)`:** reverses every path and the order of the paths, so that the result is the same line traversed backwards. There is no `Reverse` for polygons: their orientation is normalised (ADR 0003), so reversing would have no effect.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `NotSupportedException` for unknown kinds, `InvalidOperationException` without a factory (for the geometric results). Empty geometries answer `null`, `false` or an empty geometry as above.
- **Tests (ADR 0008):**
  - Fixtures with the NTS oracle for `nearestPoints` (`DistanceOp.NearestPoints`: the distance of the returned pair equals the oracle distance, and each point lies on its geometry; the points themselves may differ among ties), `isWithinDistance` (`IsWithinDistance`), `pointOnSurface` (the property check above), `boundary` (`Boundary`) and `densify` (`Densifier`). They take the inputs of the existing fixtures of `distance` and `centroid`, so the mandatory tag catalog is covered, with justified exceptions in `_catalog.json`.
  - `Reverse` is covered by `[Covers]` and hand-written tests against NTS `Reverse`.
  - Property tests (CsCheck): `Distance` equals the distance of the `NearestPoints`; `IsWithinDistance(d)` agrees with `Distance <= d`; `Reverse` twice gives the line back; `Densify` keeps the length and every original vertex and bounds the segment length; the boundary of a polygon has the length of its perimeter; the point on a polygon lies in it.
- **Playground and documentation:** descriptors for all six methods (ADR 0011), a section in the concept page of measures and constructions, glossary entries.

## Considered Options

- `NearestPoints` returning a line `ShortestLine` (rejected: a value needs no factory and the caller builds the line in one line; the tuple can also express "no nearest points" as `null`)
- `IsWithinDistance` as `Distance(a, b) <= d` (rejected: it computes every segment pair; the dedicated test stops early and starts with the envelopes)
- Naming the interior point `InteriorPoint` as JTS does (rejected: `PointOnSurface` is the OGC name; "interior point" is a glossary alias)
- `PointOnSurface` returning a point with Z and M (rejected: the point is computed, not an original vertex in general; `Centroid` returns a plain `Coordinate` too)
- Always returning the JTS point (rejected: an exact copy of JTS's choice would tie the project to its internals; the property is what callers need)
- Boundary of a point as an empty polygon or collection (rejected: there is no geometry collection (ADR 0002); an empty multipoint is the closest lower-dimension type)
- Boundary of a polygon as a multipoint of vertices (rejected: that is not the boundary)
- `Densify` by a fixed step instead of equal pieces (rejected: leaves a short last piece; equal pieces match NTS and keep the segments uniform)
- A `Reverse` for polygons (rejected: no effect after normalisation)
- Six ADRs, one per method (rejected: they introduce no concept of their own; the rules are those of ADR 0004, 0012, 0013, 0015)
