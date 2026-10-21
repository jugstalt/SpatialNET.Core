# Geometry factory: coordinate-based, optional parameter with a global default

Algorithms create results through `IGeometryFactory`. Every algorithm takes it as an **optional parameter** (`IGeometryFactory? factory = null`). If it is `null`, the algorithm falls back to the static `SpatialNET.GeometryFactory.Default` in Core. If that is not set either, it throws an `InvalidOperationException` with a clear message. There is deliberately no implicit default: Extensions depends only on Core, and a silent fallback to the reference implementation would push foreign objects into a host. The factory receives **coordinates only** (`readonly struct Coordinate(X, Y, Z = NaN)`), no point objects. This way objects from different hosts or implementations cannot be mixed, and unchanged input points do not have to be recreated.

*German version: [0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md](0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md)*

## Consequences

- Contract: `CreatePoint(Coordinate, source)`, `CreateMultiPoint(ReadOnlySpan<Coordinate>, source)`, `CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>>, source)`, `CreatePolygon(ReadOnlySpan<PolygonPart>, source)` and `bool SupportsZ`. `PolygonPart` is a part (shell plus holes, valid, closed, shell counter-clockwise, ADR 0003). The return values are the SpatialNET interfaces; host wrappers cast to their types.
- Factories are thread-safe or stateless and must not reference passed buffers after the call.
- `source` is the primary input geometry (or `null`). Through it the host takes over metadata such as the spatial reference without SpatialNET knowing it.
- Z: with `SupportsZ == false` the algorithm computes purely in 2D. Otherwise it delivers Z if the input points are `IPoint3D`, and the factory may drop it. This is necessary because gView/WebGIS always deliver `IPoint3D`.
- No `CreateGeometryCollection`: results are dimension-homogeneous. Empty results are empty geometries (0 parts/paths/points), never `null`.
- `Default` is a plain static property, set once at startup. Tests pass the factory explicitly. An `AsyncLocal` scope can be added later without a break.

## Considered Options

- Global registration only (rejected: factory cannot be chosen per call)
- Factory via the input geometry (rejected: new interface member, contradicts ADR 0002)
- Object-hierarchical factory `CreatePath(IReadOnlyList<IPoint2D>)` (rejected: mixed objects, one allocation per vertex) and builder (rejected: larger, stateful contract)
- Generic factory `IGeometryFactory<TPoint, …>` (rejected: four type parameters on every algorithm)
