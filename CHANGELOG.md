# Changelog

All notable changes to SpatialNET are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). All four packages (`SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp`, `SpatialNET.IO`) share one version.

## [Unreleased]

### Added

- `Triangulation.ContourBands` (ADR 0042): the areas of a surface between heights, the filled version of the contour lines. One polygon per band (below the first height, between two, above the last), the bands tile the surface and share their boundaries along the contour lines exactly. Playground entry `contourBands`.

## [0.6.0-preview.1] - 2026-10-27

### Added

- Natural neighbour interpolation, quality meshes and questions about whole surfaces (ADR 0041), on a `Triangulation`: `InterpolateNaturalNeighbour` (the smooth height after Sibson: continuous, exact for a plane, within the range of the heights; the linear height where there are break lines), `Refine` and `MinAngle` (Delaunay refinement after Ruppert: points are added until no angle is below a limit and no triangle above a size, the border and the break lines stay, the new points lie on the surface), `Volume(other)` (the volume between two surfaces, exact over the common area: cut and fill), `SlopeZones` (one polygon per slope class) and `LineOfSight` (whether the ground blocks the view from one point to another). `Drape` and `LineOfSight` look up long lines in pieces along the line, so that a diagonal across a large surface is several times faster. A `--quick-terrain` measurement in the benchmarks. Playground entries `naturalNeighbour`, `refine`, `slopeZones`, `lineOfSight` and `surfaceVolume`, a section in the concept page and glossary entries.
- Terrain analysis on a `Triangulation` (ADR 0039): `Gradient`, `Slope` (degrees), `Aspect` (the direction of descent, clockwise from north), `SurfaceArea` (per triangle and total) and `Volume(z)` (exact: the volume above and below a horizontal plane), and `Drape(line)`, which lays a polyline over the surface with the height as Z and the distance along the line as M, with a vertex on every edge it crosses; parts outside the surface are left out. Playground entries `Drape`, `SteepTriangles` and `TerrainVolume`, a cookbook recipe for the height profile of a road.
- Point events (ADR 0040): `PointEvent<T>` and `PointEvents.Between`, `Within` (the line events that contain each point, one result per pair) and `Outside` (the check for gaps); `LocateEvents` draws a whole table of line events or point events on the route, one entry per event with its geometry, with the index of the route built once. Playground: a new parameter type `text` for tables of events (`from..to value` or `m value` per line) and the entries `LocateEvents` and `IntersectEvents`.
- Documentation: a section "Delaunay or constrained?" on the concept page, with a tested example in which a ditch is lost without its break line.
- `VoronoiDiagram`, `VoronoiEdges`, `DelaunayEdges` and `VoronoiCell` (ADR 0036): the Voronoi diagram of the vertices of a geometry, cut to a clip area (a polygon, by default the widened envelope), one cell per distinct site with the site (and its Z) beside it, in the order of the sites. The cells have no gaps and do not overlap, their corners are rounded to a grid, and they are valid; two cells share the corners of a common edge exactly. Faster than the NTS `VoronoiDiagramBuilder` by a factor of 2 to 4 (measurements in the ADR). `VoronoiEdges` gives the lines between the cells, `DelaunayEdges` the edges of the Delaunay triangulation, each once.
- `Triangulation` (ADR 0037): the triangulation as an immutable, thread-safe surface. Vertices, triangles with their neighbours, `Locate` (the triangle that contains a point, exact, with an index), `InterpolateZ` (the height at a point, exact on a plane), `Contour` (the line of one height, closed where it returns) and `ToPolygons`, `Edges`, `ConstrainedEdges`. `Triangulation.FromPoints` is the Delaunay triangulation.
- `Triangulation.FromPointsAndLines` and `Triangulation.FromPolygon`, and `Shapes.Triangulate(IPolygon2D)` (ADR 0038): the constrained Delaunay triangulation, for **break lines** that must be edges (ridges, ditches, boundaries), and the triangles inside a polygon (even-odd, holes left out; `n − 2` triangles, two more per hole). Constraints are inserted by flips with exact predicates; constraints that cross are noded like in an overlay, and only then rounded to the grid. Compared with the NTS `PolygonTriangulator` by triangle count and area; for a smooth boundary of many thousand vertices it is faster than NTS (measurements in the ADR).
- `Triangulation.FromPolygon` is much faster for polygons with very many vertices (ADR 0038): a valid polygon is triangulated by ear clipping (holes joined by bridges) and exact flips with the rings fixed, which ends in the same constrained Delaunay triangulation without building the Delaunay triangulation of all vertices. A smooth ring of 100,000 vertices takes 0.4 s instead of 9.8 s (NTS `PolygonTriangulator`: 1.2 s). Invalid polygons and every input the fast path cannot take use the previous path.
- Playground: entries for `VoronoiDiagram`, `VoronoiEdges`, `DelaunayEdges`, `Triangulate`, `ConstrainedDelaunay`, `InterpolateZ` and `Contour` with examples (terrain with contour lines at an interval, a U shape with a break line, service areas within a boundary). The second input of an algorithm can now be **optional** (the clip area of the Voronoi diagram). Parts of a result that are empty are no longer sent to the map.
- Documentation: a concept page "Triangulation, Voronoi and terrain", two cookbook recipes (service areas, height at a point and contour lines), glossary entries, and a `--quick-voronoi` measurement in the benchmarks.
- `Interpolate(distance, offset) and `InterpolateByM(m, offset)` (ADR 0035): the point on the line moved sideways, positive to the left of the direction of the line, negative to the right. `ProjectDetailed` with the value type `LineProjection`: position, nearest point, distance and the **signed offset** of a point, also within a window of lengths, so that `Interpolate(Position, Offset)` gives a point beside the line back. At a vertex the direction is that of the segment that ends there, where NTS takes the one that starts there (documented, tested).
- `LocateFeature` (ADR 0034): the reverse of `LocateBetween`, from a polyline or polygon to the ranges of M along a route where it lies. The feature is widened by a required tolerance and cut with the route; every piece gives a range from its lowest to its highest M, touching ranges are joined. A parcel next to a road becomes "from 4 200 to 4 900", ready to be stored as an event.
- `LineEvent<T>` and `LineEvents` (ADR 0033): interval arithmetic on the event tables of a route, the events being ranges of M values with attributes of the host's choice. `Intersect` (where do two tables hold at the same time), `Union` (the route cut at every boundary, with a function for each of the cases both, only the first, only the second), `Difference` (what one table has that the other lacks), `Dissolve` (join neighbours with equal values), `Clip` and `At`. It computes with the numbers only: no geometry, no factory, no database access; the host hands over the rows and groups by route itself. New concept page "event tables", a cookbook recipe from the tables to the lengths of the pieces on the road, and glossary entries (also for linear referencing, M and calibrating in the German glossary).
- `LocateBetween` and `LocateAlong` (ADR 0032): every stretch of a line whose M lies in an interval (one path per run of consecutive segments, in the direction of the line), and every point where M has a value (as a multipoint). Unlike the first-hit `...ByM` methods, they find all of it when M is not monotonic. Playground entries for both.
- `Project(point, from, to)` and `ProjectToM(point, from, to)` (ADR 0031): the nearest point within a window of lengths, so that a line that crosses itself or doubles back (hairpin, loop) can be told apart by pass. The result is still a length (or M) on the whole line; "after a position" is the window `[position, Length()]`.
- `CalibrateM` and `MExtrapolation` (ADR 0030): gives a polyline M values from anchor points that carry M. Each anchor is projected onto the line, a vertex is inserted at its position, and M is interpolated linearly in the length along the line between the anchors, so `ProjectToM` and `InterpolateByM` answer from the calibrated line. `maxSnapDistance` is required and an anchor beyond it is an error; before the first and after the last anchor M stays empty (`None`), is clamped or continues linearly. The concept page "linear referencing" has a section on it.

### Fixed

- Voronoi cells at the hull for almost collinear sites (ADR 0036): the circumcentre of an almost flat triangle is computed exactly (it was infinite or pointed the wrong way), and where a cell edge crosses the clip rectangle the point comes from the bisector of the two sites, not from the far ends of the edge. Found by stress tests (`tests/SpatialNET.Tests/Stress`); the same stress tests also found a negative `Volume` from rounding, an `Aspect` of exactly 360 and a wrong height in `Drape` for a line that runs almost along an edge of the triangulation, all fixed.

## [0.5.0-preview.1] - 2026-10-22

### Changed

- The playground is in English (labels, messages and the names of the examples). The German documentation pages now have the same file and folder names as the English ones (`docs/de/concepts/`, `getting-started.md`, `host-integration.md`, `glossary.md`, `cookbook.md`), so the language switch lands on the same page.
- **Breaking (before 1.0):** the concrete classes of `SpatialNET.Imp` (`Point`, `Point3D`, `PointM`, `Point3DM`, `PointSequence`, `MultiPoint`, `Path`, `Ring`, `Polyline`, `Polygon`) are now `internal`; use `ImpGeometryFactory` to create geometries. They clashed with the types of hosts that import the namespace (ADR 0028).
- **Breaking (before 1.0):** `Hull.ConvexHull` moved to `Shapes.ConvexHull`, and the class `Hull` is gone. The extension call `geometry.ConvexHull()` is unchanged; only a static call `Hull.ConvexHull(geometry)` must be adapted (ADR 0028).
- `PreparedGeometry` is now thread-safe after construction (ADR 0024): its segment index is the immutable `SpatialIndex<int>`. Preparing and querying small polygons is somewhat slower (about a millisecond or less per polygon), large polygons are on a par or faster.

### Fixed

- The point-in-ring test (used by `Locate`, `Distance`, `NearestPoints`, `IsWithinDistance` and the nesting of rings) decided a point within about 1e-80 of an edge by a floating-point crossing; the side is now decided exactly (orient2d), like in the prepared geometry (found by a stress test).
- Snap rounding: a crossing that lies within rounding error of a pixel border could be routed through its hot pixel by only one of the two segments, so that output edges crossed (found by a stress test, very rare). Both segments now pass through the pixel of their crossing.
- A vertex no longer loses its Z in overlays, `Cut`, `Simplify` and `OffsetCurve` when the other end of its only segment has none; the end points of a segment keep their own Z, as they already did for M.
- `ConvexHull` now respects `SupportsZ` of the factory.
- The documentation of `Union` and `UnionAll` for polylines no longer claims "maximal paths": a path is cut where different input lines meet or end (behaviour unchanged).
- ADR 0017 and the concept page state that a length exactly on the boundary of two paths belongs to the end of the earlier path (behaviour unchanged).

### Added

- Documentation: one page per predicate (`docs/en/concepts/predicates/`, `docs/de/concepts/predicates/`) with its DE-9IM pattern, a table of 27 situations computed with the library itself, the details that surprise (points and lines, closed lines, `Contains` versus `Covers`), and the alternative with a tolerance. The overview page has the matrix of all predicates against all situations.
- Playground: every predicate shows the distance of its inputs and, below it, the call that asks with a tolerance (`IsWithinDistance`, `Buffer`, …), and a note says when a `false` is only a rounding difference (a point drawn "on" a line is rarely exactly on it). The predicates stay exact; the library has no global tolerance.
- `DelaunayTriangles`, `ConcaveHull` and `ConcaveHullByRatio` (ADR 0029): the Delaunay triangulation of the vertices of a geometry (a flat list of triangles, with exact predicates), and a concave hull that takes triangles away from its border. Playground entries for all three.
- Playground: WKT with Z and M. Every input has a WKT field, every result shows its WKT with a copy button, `POST /api/convert` converts between WKT and GeoJSON, the projection keeps Z and M, and `InterpolateByM`, `ProjectToM` and `SubstringByM` are available with the examples that carry M.
- The public API is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt` holds the API of `v0.4.0-preview.1`, `PublicAPI.Unshipped.txt` the changes since), and `tools/ship-public-api.ps1` moves the entries at release time. ADR 0028 records the criteria for version 1.0.
- Documentation: a Cookbook (`docs/en/cookbook.md`, `docs/de/cookbook.md`) with ten tested recipes, and new sections in Host integration on threads and mutable geometries, units and the grid, errors and edge cases, choosing the right tool, reading and writing, and a checklist.
- `RemoveHoles`, `RemoveSmallParts`, `SnapToGrid` and `ReducePrecision` (ADR 0027): remove holes and parts below a size, round to a grid (may leave a polygon invalid), and reduce precision with a valid result. Playground entries for all four.
- `MinimumRotatedRectangle`, `MinimumBoundingCircle` (with the value type `Circle`) and `Polylabel` (ADR 0026): the tightest rectangle and the smallest circle around a geometry, and the best place for a label in a polygon. Playground entries for all three.
- `Translate`, `Scale`, `Rotate` and `Transform` (ADR 0025): coordinate transformations for every geometry kind, keeping Z and M and the orientation of polygon rings. `IsSimple`, `IsClosed`, `IsRing` and `EqualsExact`: checks about the form of a geometry. `HausdorffDistance`: the discrete Hausdorff distance. Playground entries for all of them except `Transform`.
- `PreparedGeometry.Distance`, `IsWithinDistance` and `NearestPoints` (ADR 0024), with the same results as the unprepared methods.
- `LineMerge` (ADR 0023): joins the paths of a polyline where exactly two path ends meet (exact, optionally directed). `Polygonize` (ADR 0023): the areas a polyline encloses, one polygon per bounded face, noded on the grid. Playground entries for both.
- `Locate` and the public `Location` enum (ADR 0022): where a point lies relative to a geometry (inside, boundary, outside), exact like the predicates; also `PreparedGeometry.Locate`.
- `Clip` (ADR 0022): a geometry cut to a closed rectangle, with fast paths (completely inside: a copy without rounding, no contact: empty). Playground entry.
- Helpers on `Envelope2D` (`Intersects`, `Contains`, `Union`, `Intersection`, `Expand`, `Center`) and `Envelope2D.ToPolygon`.
- `SpatialIndex<T>.Join` and `SelfJoin` (ADR 0021): all pairs of two indexes, or of different entries within one, whose envelopes meet (optionally widened by a distance), found by walking the trees together. `SelfJoin` returns every unordered pair once.
- `MakeValid` (ADR 0020): repairs a polygon by even-odd normalisation through the overlay core (not the OGC `ST_MakeValid`), and cleans polylines and points of non-finite and duplicate points. Playground entry.

## [0.4.0-preview.1] - 2026-10-21

### Added

- Measure and point helpers (ADR 0019): `NearestPoints`, `IsWithinDistance` and `PointOnSurface` in `Measures`, and `Boundary`, `Densify` and `Reverse` in the new `Constructions`. Playground entries for all six.
- `UnionAll`: the union of many polygons, polylines or multipoints (dissolve), as a cascaded union over the STR packing of the spatial index with one grid for all steps (ADR 0018); far faster than folding `Union` for many inputs.
- Linear referencing on polylines (ADR 0017): `Interpolate`, `Project` and `Substring` by length, and `InterpolateByM`, `ProjectToM` and `SubstringByM` by M value (first hit along the line). Playground entries for the length variants.
- WKB reading and writing in `SpatialNET.IO` (ADR 0016): `Wkb.Read`/`Read<T>`/`ReadAll` accept ISO WKB and EWKB in either byte order, `ToWkb` writes ISO WKB (little endian) with Z and M. An SRID is read and discarded; curves are not supported.

## [0.3.0-preview.1] - 2026-10-21

### Added

- M values (ADR 0015): `Coordinate.M`/`HasM`, the geometry interface `IPointM` and `IGeometryFactory.SupportsM` (default `false`). Overlays, `Cut`, `Simplify` and `OffsetCurve` carry M and interpolate it at new vertices like Z; `Buffer` and `ConvexHull` produce none. M never influences a computation.
- WKT reads and writes `M` and `ZM` (`WktOptions.MMode`); GeoJSON stays without M.
- Reference implementation: `PointM`, `Point3DM` and `ImpGeometryFactory(supportsZ, supportsM)`.

### Changed

- Breaking (pre-release): `Coordinate` has a fourth parameter `M`; its constructor and `Deconstruct` change.

## [0.2.0-preview.1] - 2026-10-21

### Added

- `SpatialIndex<T>`: a static, thread-safe spatial index over envelopes with a payload (STR-packed), with range queries (`Query`) and k-nearest-neighbours (`Nearest`), plus `ToSpatialIndex()` and `Query(IGeometry2D)` for geometries (ADR 0014).
- API compatibility check against the last published version at pack time (package validation).
- Contribution guide, issue and pull request templates.

## [0.1.0-preview.1] - 2026-10-21

First pre-release.

### Added

- Geometry interfaces, value types (`Coordinate`, `Envelope2D`) and the geometry factory contract (`SpatialNET.Core`).
- Algorithms as extension methods (`SpatialNET.Extensions`): overlays (`Intersection`, `Union`, `Difference`, `SymDifference`) with winding numbers, selectable fill rules and snap rounding to a grid; `Buffer` and `OffsetCurve`; `Cut`; `Simplify` (optionally topology-preserving); `ConvexHull`; spatial predicates with full DE-9IM and `Relate`; `Prepare()`; `IsValid`/`Validate`; measures (`Area`, `Length`, `Envelope`, `Centroid`, `Distance`).
- Reference implementation of the geometry interfaces and the factory (`SpatialNET.Imp`).
- WKT and GeoJSON reading and writing through the geometry factory (`SpatialNET.IO`).
- Playground: an interactive web application (ASP.NET Core + Leaflet) to try the algorithms on a map.
- Bilingual documentation (English leading, German): README, getting started, host integration, concepts, glossary, architecture decision records; API reference generated with DocFX.
- Continuous integration, documentation publishing and a tag-triggered release pipeline (GitHub Actions).

[Unreleased]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.6.0-preview.1...HEAD
[0.6.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.5.0-preview.1...v0.6.0-preview.1
[0.5.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.4.0-preview.1...v0.5.0-preview.1
[0.4.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.2.0-preview.1...v0.3.0-preview.1
[0.2.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/releases/tag/v0.1.0-preview.1
