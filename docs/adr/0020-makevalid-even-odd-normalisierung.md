# MakeValid: even-odd normalisation through the overlay core, no mixed dimensions

SpatialNET gets **`MakeValid`**, which turns an invalid geometry into a valid one. This reverses the exclusion of "MakeValid/Clean" from the charter (map #1), because dirty data from hosts is the rule, not the exception, and the pieces are already there. The meaning is **narrow and consistent with the rest of the library**: a polygon is repaired by **even-odd normalisation** through the overlay core, the same self-union that `Simplify` already runs, so the area that the input has under the even-odd rule (ADR 0003) is exactly the area of the result. It is **not** the OGC/GEOS `ST_MakeValid`, which keeps line and point remnants and returns mixed collections: results of SpatialNET are dimension-homogeneous (ADR 0002, ADR 0005), and there is no geometry collection.

*German version: [0020-makevalid-even-odd-normalisierung.de.md](0020-makevalid-even-odd-normalisierung.de.md)*

## Consequences

- **Placement and API:** `MakeValid` in the static class `Overlays` (it runs the overlay core), `SpatialNET.Extensions`, namespace `SpatialNET`. Typed overloads return the input type: `MakeValid(this IPolygon2D, FillRule = EvenOdd, factory, gridSize)` returns `IPolygon2D`, `MakeValid(this IPolyline2D, factory)` returns `IPolyline2D`, `MakeValid(this IMultiPoint2D, factory)` returns `IMultiPoint2D`, and `MakeValid(this IGeometry2D, FillRule, factory, gridSize)` branches at run time (a point that is not finite becomes an empty multipoint, a finite one a copy). For the package validation this is an addition only.
- **Polygons:**
  - Each ring is read under the `FillRule` (default EvenOdd) and the polygon is united with itself by the overlay core, exactly as `Simplify` normalises its result. Self-intersecting and overlapping rings, touching rings, inverted holes and duplicate rings all dissolve; holes, islands and parts arise from the overlay.
  - Rings without area are dropped, the result is valid, rings are closed, shells counter-clockwise and holes clockwise (ADR 0003). A polygon without area becomes an empty polygon.
  - The repair runs on the grid (ADR 0005): `gridSize`, otherwise `Precision.DefaultGridSize`, otherwise automatic. So coordinates of the result lie on the grid, **also those of parts that were valid before**. A caller who wants to leave valid input untouched checks `IsValid()` first.
  - Z and M are carried and interpolated like in `Union` (ADR 0005, ADR 0015).
- **Polylines:** a polyline is valid if every path has at least two distinct points and all coordinates are finite (crossing lines are valid). `MakeValid` removes non-finite points and consecutive duplicate points and drops paths with fewer than two distinct points. Other coordinates are **not rounded**: no overlay runs for lines.
- **Points:** non-finite points are removed. Duplicates stay, because `MakeValid` repairs and does not turn a list into a set (`Union` has set semantics).
- **Non-finite coordinates** (NaN or infinite) are removed before anything else for every kind, because they cannot be put on a grid. A ring that loses points this way is repaired like any other ring.
- **Valid input is copied:** the result is always a new geometry from the factory (never the input instance), whether or not the input was valid. This keeps the return type and the properties predictable for every host.
- **Guarantees:** the result is valid (`IsValid()`), it is idempotent (`MakeValid(MakeValid(g))` has the same area and vertices as `MakeValid(g)` on the same grid; the start vertex of a ring and the order of the parts may differ), and for a polygon its area equals the area of the input under the fill rule, up to the grid.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for an invalid `gridSize`, `NotSupportedException` for an unknown geometry kind, `InvalidOperationException` without a factory. Invalid or empty geometries never throw.
- **Tests (ADR 0008):**
  - NTS `GeometryFixer` has the OGC meaning, so it is **not** the oracle. The oracle is an independent even-odd area: the input rings are noded and polygonised with NTS, and a face counts if an odd number of input rings contain it. The test compares the area of the result with that, and requires validity. Fixtures are derived from those of `validate`, so the mandatory tag catalog applies, with the justified exceptions of `validate`.
  - Property tests (CsCheck): the result is always valid, for random rings with self-intersections and duplicates; valid input keeps its area up to the grid; idempotence; the area equals the even-odd area of a random set of rectangles computed independently (inclusion and exclusion by symmetric difference); `FillRule.NonZero` merges overlapping shells.
  - Hand-written cases: lines and points, non-finite coordinates, empty geometries, `null`, mixed Z and M.
- **Playground and documentation:** a descriptor (ADR 0011), a section in the concept page "Simplify, convex hull and validation", glossary entry.

## Considered Options

- The OGC/GEOS `MakeValid` (rejected: it returns mixed dimensions and geometry collections, which the model does not have, and its rules are far larger than what hosts need)
- `Buffer(0)` as the implementation (rejected: it is not defined for lines and points, rounds through offset curves, and drops thin parts)
- Naming it `Normalize` (rejected: for JTS that means the order of coordinates)
- Returning a valid input unchanged, as the same instance (rejected: the type and properties of the returned object would depend on the host; callers who want it check `IsValid()` first)
- Rounding only the invalid regions to the grid (rejected: complicated and fragile; one rule for the whole polygon is easy to state)
- Rounding lines to the grid as well (rejected: lines have nothing to repair that needs it, and rounding would change valid data)
- Removing duplicate points of a multipoint (rejected: that is set semantics, which `Union` already provides)
- Keeping the collapsed remnants of a polygon as lines (rejected: results are dimension-homogeneous)
