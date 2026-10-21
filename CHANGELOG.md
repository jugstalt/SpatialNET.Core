# Changelog

All notable changes to SpatialNET are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). All four packages (`SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp`, `SpatialNET.IO`) share one version.

## [Unreleased]

### Added

- Geometry interfaces, value types (`Coordinate`, `Envelope2D`) and the geometry factory contract (`SpatialNET.Core`).
- Algorithms as extension methods (`SpatialNET.Extensions`): overlays (`Intersection`, `Union`, `Difference`, `SymDifference`) with winding numbers, selectable fill rules and snap rounding to a grid; `Buffer` and `OffsetCurve`; `Cut`; `Simplify` (optionally topology-preserving); `ConvexHull`; spatial predicates with full DE-9IM and `Relate`; `Prepare()`; `IsValid`/`Validate`; measures (`Area`, `Length`, `Envelope`, `Centroid`, `Distance`).
- Reference implementation of the geometry interfaces and the factory (`SpatialNET.Imp`).
- WKT and GeoJSON reading and writing through the geometry factory (`SpatialNET.IO`).
- Playground: an interactive web application (ASP.NET Core + Leaflet) to try the algorithms on a map.
- Bilingual documentation (English leading, German): README, getting started, host integration, concepts, glossary, architecture decision records; API reference generated with DocFX.
- Continuous integration, documentation publishing and a tag-triggered release pipeline (GitHub Actions).
