# Locate, envelope helpers and rectangle clip

Three small pieces fill gaps that every host meets: **`Locate`** (where does a point lie relative to a geometry: inside, on the boundary or outside), **helpers on `Envelope2D`** (`Intersects`, `Contains`, `Union`, `Intersection`, `Expand`, `Center`, plus `ToPolygon`) and **`Clip`** (a geometry cut to a rectangle, the tile and viewport case). `Locate` makes public what the predicates already compute internally. `Clip` is honestly a convenience with fast paths: its core is the existing intersection with the rectangle. They follow the rules already fixed (exact decisions without a grid for what is not constructed, the overlay grid for what is, ADR 0005; empty and invalid input never throws, invalid numeric parameters throw, ADR 0012).

*German version: [0022-locate-envelope-helfer-und-clip.de.md](0022-locate-envelope-helfer-und-clip.de.md)*

## Consequences

- **`Location`** is a public enum in `SpatialNET.Extensions`, namespace `SpatialNET`, with `Outside`, `Boundary` and `Inside` (the internal enum of the same name moves there). The name is the one of JTS and NTS; code that imports both `SpatialNET` and `NetTopologySuite.Geometries` must qualify it, which the test project does with an alias.
- **`Locate(this IGeometry2D geometry, Coordinate point)`** (and an `IPoint2D` overload) in the new static class `Locations` returns where the point lies:
  - **Polygon:** by the even-odd rule over all rings (ADR 0003): `Inside` in the area, `Outside` also in a hole, `Boundary` on any ring, a hole's included. Rings without area are ignored and rings that cancel each other out as in the construction of parts.
  - **Polyline:** `Boundary` on an end point by the mod-2 rule (ADR 0013), `Inside` elsewhere on the line, `Outside` off the line.
  - **Point and multipoint:** `Inside` if the point is equal to one of them, otherwise `Outside`; points have no boundary.
  - **Empty geometry:** `Outside`. Invalid polygons are read by even-odd, deterministically.
  - The decisions are exact (orient2d), like the predicates, so `Locate` agrees with `Relate`: for a polygon and a point, `Inside` is `Contains`, `Outside` is `Disjoint`, and `Boundary` is the rest of `Intersects`.
  - A point with a `NaN` or infinite coordinate throws `ArgumentOutOfRangeException`; `null` throws `ArgumentNullException`.
- **`PreparedGeometry.Locate(Coordinate)`** gives the same answer for repeated points against one geometry: a polygon goes through the segment index, everything else through the unprepared computation. `PreparedGeometry` stays not thread-safe (ADR 0013). There is no batch overload with spans in this step.
- **Envelope helpers** are instance members of `Envelope2D` in `SpatialNET.Core` (purely additive, binary compatible; a value type with arithmetic stays within "Core = contracts and value types", ADR 0002):
  - `Intersects(Envelope2D)` and `Contains(Envelope2D)` treat the border as part of the envelope (closed intervals); `Contains(Coordinate)` likewise. An empty envelope meets nothing, contains nothing and lies in nothing.
  - `Union(Envelope2D)` is the smallest envelope containing both (the other one if one is empty), `Intersection(Envelope2D)` the common part (`Empty` if they do not meet), `Center` the middle (NaN if empty).
  - `Expand(double distance)` widens on all sides; a negative distance shrinks and gives `Empty` if nothing is left. Core does not throw: an empty envelope or a `NaN` distance gives `Empty`.
  - `ToPolygon(factory)` in the static class `Envelopes` (`SpatialNET.Extensions`) creates the rectangle as a polygon, counter-clockwise and closed. An empty envelope, or one without area (zero width or height), gives an empty polygon. An invalid envelope (not finite, or Min greater than Max) throws `ArgumentOutOfRangeException`.
- **`Clip(this IPolygon2D, Envelope2D, factory, gridSize)`** and the overloads for `IPolyline2D`, `IMultiPoint2D` and `IGeometry2D` in `Overlays` return the part inside the **closed** rectangle:
  - Polygon and polyline run through the overlay with the rectangle as polygon (an intersection, so a polyline keeps the parts inside and on the border; strict semantics, ADR 0005); points are filtered exactly and never rounded. `gridSize` follows the cascade of ADR 0005.
  - **Fast paths:** a geometry whose envelope lies completely in the rectangle is returned as a copy **without rounding** (a polygon as a normalised copy: orientation fixed, rings without area dropped; invalid input is not repaired, use `MakeValid`), and a geometry whose envelope does not meet the rectangle gives an empty geometry. Only where the border of the rectangle cuts, the overlay runs and only those results lie on the grid. This difference is documented.
  - A rectangle without area (zero width or height) gives an empty polygon or polyline; a multipoint is still filtered, since a point can lie on a degenerate rectangle. An empty envelope gives an empty result; an invalid envelope throws `ArgumentOutOfRangeException`.
  - Z and M are carried like in `Intersection` (ADR 0005, ADR 0015). The corners of the rectangle that become vertices of the result belong to the rectangle, which has no Z and M, so they carry none.
- **No prepared clip** (one polygon against many features) in this step; the existing `Intersection` with a `PreparedGeometry` is not part of the API either.
- **Tests (ADR 0008):**
  - `locate` fixtures carry the geometry; the test probes every vertex, every segment midpoint, the centre of the envelope and points just outside, and compares each answer with the NTS `PointLocator` (for polygons on the geometry in the semantics of ADR 0003). Property tests: `Locate` agrees with `Contains`, `Disjoint` and `Intersects` of the predicates, the prepared answer equals the unprepared one, a hole is outside and its ring is boundary.
  - `clip` fixtures carry the geometry and the rectangle as parameters (`minX`, `minY`, `maxX`, `maxY`) and are compared with NTS `OverlayNG` intersecting the same rectangle on the same grid; extra fixtures for a rectangle that contains the geometry and one that misses it. Property tests: the result lies in the rectangle, a geometry inside comes back with identical coordinates, and `Clip` equals `Intersection` with `ToPolygon` up to the grid.
  - The envelope helpers are checked against brute-force definitions on random rectangles, including empty ones and touching borders.
- **Playground and documentation:** `Clip` gets a descriptor with four number parameters (ADR 0011). `Locate` and `ToPolygon` are exempt: the first answers with a category the interface does not display, the second is a conversion and no algorithm. A section in the concept pages for the geometry model and the overlays, glossary entries.

## Considered Options

- A different name for `Location`, such as `PointLocation` (rejected: the name of JTS and NTS is what people look for; the clash is limited to code that imports both namespaces)
- `Locate` returning a boolean pair or `Contains`/`Touches` only (rejected: `Relate` already gives the matrix; a three-valued answer is what repeated point lookups want)
- A batch `Locate(ReadOnlySpan<Coordinate>, Span<Location>)` (rejected for now: nothing asks for it yet, and it can follow without a break)
- Envelope helpers as extension methods in `SpatialNET.Extensions` (rejected: they are plain arithmetic on a value type from `SpatialNET.Core`; instance members are found without a `using`)
- `Envelope2D.ToPolygon` in Core (rejected: it needs the factory, and `SpatialNET.Core` is contracts and values only)
- `Clip` by Sutherland–Hodgman on the rings (rejected: it leaves degenerate bridges along the border for concave polygons that an overlay would have to repair anyway; the intersection with the rectangle is correct for every polygon)
- `Clip` rounding every result to the grid (rejected: tiling should leave geometries that lie completely inside unchanged; the rule "rounded only where cut" is cheap to state)
- A prepared clip, or clipping by an arbitrary polygon as a separate method (rejected: that is `Intersection`)
