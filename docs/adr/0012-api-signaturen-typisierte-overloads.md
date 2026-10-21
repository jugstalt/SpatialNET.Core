# API signatures: typed overloads, values instead of geometries for point-like measures, fixed exception rules

Algorithms have **typed overloads** whose return type follows from the strict semantics (ADR 0005), plus one general `IGeometry2D` overload each, which branches at run time. The compiler thus knows the result type without a cast. Point-like measures are **values instead of geometries**: `Centroid()` returns `Coordinate?` (`null` for empty input) and needs no factory. `Distance` with an empty geometry is `NaN`. This is a deliberate deviation from NTS, which returns 0 and thereby makes "touches" and "nothing there" indistinguishable.

*German version: [0012-api-signaturen-typisierte-overloads.de.md](0012-api-signaturen-typisierte-overloads.de.md)*

## Consequences

- **Binary operations:**
  - **Intersection:** polygon ∩ polygon → `IPolygon2D`. Polygon ∩ polyline and polyline ∩ polyline → `IPolyline2D`, overlaps only, pure crossing points are dropped under strict semantics. Multipoint or point ∩ anything → `IMultiPoint2D`.
  - **Union and SymDifference:** only for equal dimension.
  - **Difference:** returns the type of the first operand.
- **Measures:**
  - `Area` on `IPolygon2D`, `Length` on `IPolyline2D` and `IPolygon2D` (perimeter).
  - General overloads on `IGeometry2D` return 0 if the measure does not fit the dimension.
  - `Envelope()` returns `Envelope2D`, for empty input `Envelope2D.Empty` (NaN, `IsEmpty`).
- **ConvexHull** returns `IGeometry2D`. Depending on degeneracy this is a polygon, a polyline or a multipoint, as in JTS.
- **Simplify** returns the input type.
  - **Default:** Douglas-Peucker followed by normalization through the overlay (union with EvenOdd). The result is always valid (ADR 0003), collapsed rings are dropped.
  - **`preserveTopology: true`:** a topology-preserving simplifier after the pattern of JTS `TopologyPreservingSimplifier`. This is an **additional algorithm within the v1 scope**.
- **IsValid:** `IsValid()` returns `bool`, `Validate()` returns `ValidationResult` with findings consisting of kind and `Coordinate`. The kinds are `SelfIntersection`, `RingSelfTouch`, `DegenerateRing`, `InvalidCoordinate`, `TooFewPoints` and `OverlappingShells`, according to ADR 0003 and not 1:1 from NTS (a missing closing point is valid, for example).
- **Exceptions:**
  - `ArgumentNullException` for `null` arguments
  - `ArgumentOutOfRangeException` for invalid numeric parameters (`NaN`/`Infinity`, negative tolerance, `gridSize ≤ 0`, `QuadrantSegments < 1`)
  - `InvalidOperationException` when no factory is available (ADR 0004)
  - `NotSupportedException` for unknown `IGeometry2D` types or combinations that cannot be represented in general overloads
  - Invalid or empty geometries **never** throw.
- **Parameters:** an options object exists only for more than two domain-specific knobs (`BufferOptions`, `OffsetOptions`, ADR 0006), otherwise simple optional parameters. The order is always: required parameters → domain options → `factory` → `gridSize`.

## Considered Options

- Centroid as `IPoint2D` via the factory with exception or `TryCentroid` (rejected: allocation for a single value)
- Distance with an empty geometry = 0 as in NTS (rejected: ambiguous) or an exception
- ConvexHull always `IPolygon2D` (rejected: loses the degeneracy information)
- Simplify without a validity guarantee (rejected: contradicts ADR 0003)
