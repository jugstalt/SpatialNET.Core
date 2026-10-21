# Playground projection: server-side with Proj4Net, default auto-UTM, distances in CRS units

The playground projects **on the server** with Proj4Net.Core. The client sends and receives only GeoJSON in WGS84 for Leaflet. Computation happens in a selectable computation CRS; the default is **auto-UTM**: WGS84/UTM, where the zone is determined per computation from the envelope center of all inputs. Distance parameters (buffer, offset, tolerances) are given **in the units of the computation CRS** and are not converted. The playground thus shows exactly what SpatialNET does, namely computing planar in map units, and the default CRS still yields correct meters right away. EPSG:3857 as a fixed default was rejected because a "meter" there corresponds to roughly 1.5 real meters in Central Europe, for example.

*German version: [0010-playground-projektion-serverseitig-auto-utm.de.md](0010-playground-projektion-serverseitig-auto-utm.de.md)*

## Consequences

- **Computation CRS:** choice of auto-UTM, EPSG:3857, common national CRSs (e.g. 25832/25833, 31256) or a free EPSG code. The chosen or automatically determined zone is displayed so that a case is reproducible. The UI shows the unit of the CRS. For EPSG:3857 a note on the scale distortion at the location is added.
- **Fixtures with `crs`:** their `crs` is the computation CRS, and the selection is then locked. Drawn or uploaded geometries are projected into this CRS. Fixtures without `crs` are shown and edited without projection in local coordinates with `L.CRS.Simple` (ADR 0008).
- **Projecting back:** only the vertices are transformed, without densification. Exactly the result vertices are displayed.
- **Code boundary:** Proj4Net.Core is a dependency **only** of `SpatialNET.Playground`. Projection runs through a small playground function (`Project(IGeometry2D, transform, factory)`) using the factory of the reference implementation. The library packages get no projection API.

## Considered Options

- Fixed default EPSG:3857 (rejected: not length-preserving)
- Projection in the client with proj4js (rejected: second projection library)
- Distances always in meters with conversion (rejected: obscures planar computation, only approximate for 3857)
- Fix the zone per session (rejected: result would depend on the map extent)
