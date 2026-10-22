# Changelog

All notable changes to SpatialNET are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). All four packages (`SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp`, `SpatialNET.IO`) share one version.

## [Unreleased]

### Changed

- `PreparedGeometry` is now thread-safe after construction (ADR 0024): its segment index is the immutable `SpatialIndex<int>`. Preparing and querying small polygons is somewhat slower (about a millisecond or less per polygon), large polygons are on a par or faster.

### Fixed

- The point-in-ring test (used by `Locate`, `Distance`, `NearestPoints`, `IsWithinDistance` and the nesting of rings) decided a point within about 1e-80 of an edge by a floating-point crossing; the side is now decided exactly (orient2d), like in the prepared geometry (found by a stress test).
- Snap rounding: a crossing that lies within rounding error of a pixel border could be routed through its hot pixel by only one of the two segments, so that output edges crossed (found by a stress test, very rare). Both segments now pass through the pixel of their crossing.
- A vertex no longer loses its Z in overlays, `Cut`, `Simplify` and `OffsetCurve` when the other end of its only segment has none; the end points of a segment keep their own Z, as they already did for M.
- `ConvexHull` now respects `SupportsZ` of the factory.
- The documentation of `Union` and `UnionAll` for polylines no longer claims "maximal paths": a path is cut where different input lines meet or end (behaviour unchanged).
- ADR 0017 and the concept page state that a length exactly on the boundary of two paths belongs to the end of the earlier path (behaviour unchanged).

### Added

- The public API is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt` holds the API of `v0.4.0-preview.1`, `PublicAPI.Unshipped.txt` the changes since), and `tools/ship-public-api.ps1` moves the entries at release time. ADR 0028 records the criteria for version 1.0.
- Documentation: a Cookbook (`docs/en/cookbook.md`, `docs/de/rezepte.md`) with ten tested recipes, and new sections in Host integration on threads and mutable geometries, units and the grid, errors and edge cases, choosing the right tool, reading and writing, and a checklist.
- `RemoveHoles`, `RemoveSmallParts`, `SnapToGrid` and `ReducePrecision` (ADR 0027): remove holes and parts below a size, round to a grid (may leave a polygon invalid), and reduce precision with a valid result. Playground entries for all four.
- `MinimumRotatedRectangle`, `MinimumBoundingCircle` (with the value type `Circle`) and `Polylabel` (ADR 0026): the tightest rectangle and the smallest circle around a geometry, and the best place for a label in a polygon. Playground entries for all three. `ConcaveHull` is deferred.
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

[Unreleased]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.4.0-preview.1...HEAD
[0.4.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.2.0-preview.1...v0.3.0-preview.1
[0.2.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/releases/tag/v0.1.0-preview.1
