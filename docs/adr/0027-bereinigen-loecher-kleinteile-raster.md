# Clean-up: remove holes and small parts, snap to a grid, reduce precision

Four operations that tidy a geometry: **`RemoveHoles`** and **`RemoveSmallParts`** drop what is too small to matter, **`SnapToGrid`** rounds the coordinates, and **`ReducePrecision`** does the same and keeps the result valid. The first three only filter or round; `ReducePrecision` goes through the snap-rounding overlay core.

*German version: [0027-bereinigen-loecher-kleinteile-raster.de.md](0027-bereinigen-loecher-kleinteile-raster.de.md)*

## Consequences

- **Placement:** all four in the new static class `Cleaning` in `SpatialNET.Extensions`, namespace `SpatialNET`. Results come from the factory and are restricted to what it supports (ADR 0019). For the package validation this is an addition only.
- **`RemoveHoles(polygon, minArea = double.PositiveInfinity, factory?)`:** removes the holes whose area is **smaller than** `minArea`; the others stay. With the default every hole is removed; `minArea = 0` keeps every hole. A part, an island and a shell are never touched. The rings are normalized (ADR 0003). Only for `IPolygon2D`: other kinds have no holes.
- **`RemoveSmallParts`:** polygon: removes the parts whose area (shell minus its holes) is smaller than the limit; polyline: removes the paths shorter than the limit; the overload for `IGeometry2D` dispatches (its parameter is called `minSize`: area for a polygon, length for a polyline) and copies points and multipoints, which have no size. A value equal to the limit stays.
- **Limits (ADR 0012):** `minArea`, `minLength` and `minSize` must not be negative or `NaN`, otherwise `ArgumentOutOfRangeException`; infinity is allowed.
- **`SnapToGrid(geometry, gridSize, factory?)`:** rounds X and Y of every vertex to the nearest multiple of the grid size (halves away from zero) and nothing else. Consecutive vertices that fall together are merged (Z and M of the first), a path with fewer than two and a ring with fewer than three distinct vertices vanish, and a part whose shell vanishes goes with its holes. The points of a multipoint all stay. The result **may be invalid**: rounding can make edges cross or touch. This is the cheap, predictable operation, and the name says so; for a valid result there is `ReducePrecision`. `ArgumentOutOfRangeException` for a grid size that is not finite or not greater than 0, `InvalidOperationException` if a rounded coordinate leaves the range of `double`.
- **`ReducePrecision(geometry, gridSize, factory?)`:** the geometry is snap-rounded to the grid by the overlay core and the result is **valid**: a polygon goes through `MakeValid` under even-odd (ADR 0020), so parts that collapse vanish and the result is a valid polygon; a polyline goes through `Union` with an empty one (ADR 0018), so it is noded and its duplicates collapse; a multipoint loses its duplicates the same way; a point is rounded. Every vertex of the result lies on the grid. The grid size is required here (no cascade): reducing precision without saying to what makes no sense.
- **Tests (ADR 0008):** `[Covers]` tests instead of fixtures. Hand cases for each method; property tests: `RemoveHoles` and `RemoveSmallParts` give the area of an independent computation on the NTS parts; `SnapToGrid` puts every vertex on the grid within half a cell diagonal of an input vertex and is idempotent; `ReducePrecision` of polygons is valid, on the grid and within a tolerance of the area of the NTS `GeometryPrecisionReducer`, of lines on the grid and within two cells (Hausdorff) of the original. A weekly stress test reduces dirty polygons and checks validity and the grid.
- **Playground and documentation:** descriptors for all four (ADR 0011), a section in the concept page for the simplifying and validating operations, glossary entries.

## Considered Options

- `RemoveHoles(minArea = 0)` meaning "all holes" (rejected: it reads as "holes of at least 0", which keeps them all; the limit is a threshold below which holes go, and no limit removes all)
- A single `Reduce` that decides between snapping and noding (rejected: the two differ in cost and in the guarantee of validity, and the caller should choose)
- `SnapToGrid` through the noder as well (rejected: it would be `ReducePrecision`)
- Removing small parts by the area of the shell alone (rejected: a ring of land around a lake would count as large; the net area is what the part covers)
- Removing duplicates in `SnapToGrid` for multipoints (rejected: `SnapToGrid` rounds and merges neighbours; set semantics are for `ReducePrecision` and `MakeValid` leaves duplicates as well, ADR 0020)
