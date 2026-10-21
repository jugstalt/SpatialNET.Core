# Changelog

All notable changes to SpatialNET are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). All four packages (`SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp`, `SpatialNET.IO`) share one version.

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.3.0-preview.1...HEAD
[0.3.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.2.0-preview.1...v0.3.0-preview.1
[0.2.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/jugstalt/SpatialNET.Core/releases/tag/v0.1.0-preview.1
