# Predicates and DE-9IM

Spatial predicates answer yes/no questions about the relation of two geometries: do they intersect, does one contain the other, do they only touch. All of them are based on the full **DE-9IM** model of OGC and NetTopologySuite ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.md)) and compute **exactly on the input coordinates**, without a grid.

## The intersection matrix

For two geometries a and b, `a.Relate(b)` returns an `IntersectionMatrix`: for each combination of interior (I), boundary (B) and exterior (E) of a and b, the dimension of their intersection (`F` = empty, 0 = points, 1 = lines, 2 = areas).

```csharp
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var edge = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)"); // lies on the boundary of the square

Console.WriteLine(square.Contains(edge)); // False - DE-9IM: no point of the edge lies in the interior
Console.WriteLine(square.Covers(edge));   // True  - boundary points count
Console.WriteLine(square.Touches(edge));  // True
Console.WriteLine(square.Relate(edge));   // FF2101FF2
```

The matrix is written row by row in the order I, B, E of a against I, B, E of b (`II IB IE BI BB BE EI EB EE`); the nine values are also available as properties (`II`, `IB`, …, `EE`). `a.Relate(b, pattern)` checks the matrix against a pattern of nine characters: `T` (not empty), `F` (empty), `*` (anything), `0`, `1`, `2` (exactly this dimension). For example `T*F**F***` is "within".

## The predicates

All predicates are extension methods on `IGeometry2D`:

| Predicate | Meaning |
|---|---|
| `Intersects` | the geometries share at least one point |
| `Disjoint` | the geometries share no point (`FF*FF****`) |
| `Contains(a, b)` | b lies in a and they share an interior point (`T*****FF*`) |
| `Within(a, b)` | a lies in b (`T*F**F***`) |
| `Covers(a, b)` | no point of b lies outside a; boundary points count |
| `CoveredBy(a, b)` | no point of a lies outside b; boundary points count |
| `Touches` | the geometries meet only in boundary points |
| `Crosses` | the geometries cross (OGC; depends on the dimensions) |
| `Overlaps` | geometries of equal dimension overlap partially |
| `EqualsTopo` | the geometries are topologically equal (`T*F**FFF*`); false if one is empty |

`Contains` versus `Covers` is the difference that surprises most people: a polygon does not *contain* a line that lies on its boundary (the line has no point in the interior), but it *covers* it. If you mean "lies inside, the boundary counts", use `Covers`/`CoveredBy`.

Rules that apply to all predicates:

- **Empty geometries.** If a geometry is empty, only `Disjoint` is true; all other predicates are false, and `Relate` returns a matrix of only `F`.
- **Area by even-odd.** Areas follow the even-odd rule ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)).
- **Line boundaries by mod-2.** The end points of a line are its boundary, closed paths have no boundary, and for a polyline with several paths only an odd number of path ends at a point counts.
- **Invalid input** is handled deterministically and without exceptions, but without a guarantee that the predicates are consistent with one another.

## Exactness versus overlays

Predicates compute exactly on the coordinates you pass. Overlays round to a grid ([Overlays, fill rules and the grid](overlay-and-grid.md)). For near-touches below the grid size, `a.Intersects(b)` can therefore be true while `a.Intersection(b)` is empty.

## Repeated predicates: `Prepare()`

If you test many geometries against the same one (for example a polygon against thousands of points), prepare it once:

```csharp
// a and b are the two polygons from the quick start.
// Several expensive predicates against the same geometry: prepare it once.
PreparedGeometry prepared = a.Prepare();
bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

// Full DE-9IM: matrix and pattern matching.
string matrix = a.Relate(b).ToString(); // "212101212"
```

A `PreparedGeometry` copies a **snapshot** of the coordinates and builds a segment index (and a point-in-polygon structure) from it. Later changes to the host object have no effect on it, which matters because host geometries are mutable. It offers `Intersects`, `Disjoint`, `Contains`, `Covers`, `Within`, `CoveredBy` and `Touches`, always with the same results as the unprepared predicates; the fast paths are used for `Intersects`/`Disjoint` and point-in-area, everything else runs through the exact `Relate`. A `PreparedGeometry` is **not thread-safe**; prepare one instance per thread.
