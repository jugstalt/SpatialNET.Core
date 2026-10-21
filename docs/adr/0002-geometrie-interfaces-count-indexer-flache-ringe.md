# Geometry interfaces: count + indexer, flat ring list, Z only on the point

The geometry interfaces in SpatialNET.Core are read-only and minimal: `IGeometry2D` (empty base type), `IPoint2D` (X, Y), `IPoint3D : IPoint2D` (Z), `IPointCollection2D` (`PointCount` + indexer) as the base of `IPath2D`, `IRing2D : IPath2D` and `IMultiPoint2D`, plus `IPolyline2D` (`PathCount` + indexer) and `IPolygon2D` (`RingCount` + indexer over a flat ring list). The envelope is not an interface but the value type `Envelope2D`. Point sequences are deliberately **not** `IReadOnlyList<T>`: gView's `IPointCollection`/`IPolyline`/`IPolygon` are already `IEnumerable<IPoint/IPath/IRing>`. A second `IEnumerable<T>` makes LINQ ambiguous on all gView geometries (CS0411), including in gView's existing code. The names `PointCount`/`PathCount`/`RingCount`, on the other hand, are already satisfied implicitly by both hosts. For LINQ/`foreach`, SpatialNET.Extensions provides an `IReadOnlyList` view (`.AsList()`). A host may additionally implement `IReadOnlyList<T>` itself and is then passed through directly.

*German version: [0002-geometrie-interfaces-count-indexer-flache-ringe.de.md](0002-geometrie-interfaces-count-indexer-flache-ringe.de.md)*

## Consequences

- Adapting the hosts is cheap. **gView** attaches the interfaces to its own interfaces (`IPoint : IPoint3D` …) and implements the three indexers with a differing return type there as default interface members. The classes stay unchanged. **WebGIS** attaches the interfaces to the classes and needs three explicit indexers (`PointCollection`, `Polyline`, `Polygon`).
- 3D exists only on the point (no `IPath3D`/`IPolyline3D`/`IPolygon3D`). An algorithm interpolates Z if the points involved are `IPoint3D`. Both hosts always deliver `IPoint3D`, even with Z = 0.
- `IPolyline2D` is at the same time a multi-line and `IPolygon2D` at the same time a multi-polygon. The only separate multi type is `IMultiPoint2D`; there is no GeometryCollection in v1. Path and ring are parts, not geometries.
- Whether a ring is a shell or a hole is not part of the interface (no `IHole2D`). Algorithms determine it themselves; the rules for that are settled in #3. A ring may repeat its closing point or omit it.
- Further members (envelope, IsEmpty, Length, Area …) do not belong in the interfaces. In the hosts they collide with members of the same name but a different shape (e.g. gView `IsEmpty()` vs. WebGIS `IsEmpty`). They come as extensions. Algorithms are extension *methods*: a C# 14 extension *property* would be silently shadowed on host types by a host property of the same name (e.g. `Area`). Extension properties are therefore intended only for convenience members without such name clashes. Extension methods such as `Area()`, by contrast, do not collide with host properties.

## Considered Options

- `IReadOnlyList<T>` as mandatory (rejected: CS0411 on all gView geometries, plus `Count` next to `PointCount`)
- Parallel 3D hierarchy (rejected: doubles interfaces and overloads)
- `IHole2D` marker or a structured shell→holes model (rejected: presupposes host guarantees or fits no host)
- `IEnvelope2D` as an interface (rejected: result envelopes would need the factory)

Prototype: branch `prototype/interface-form`.
