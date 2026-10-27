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

### Reading a pattern

A pattern has nine characters, one for each pair of the three parts of `a` and the three parts of `b`. The three parts of a geometry are its **interior** (I), its **boundary** (B) and its **exterior** (E): for an area the inside, the edge and the rest of the plane; for a line everything except its two end points, the end points, and the rest; for a point the point itself, nothing, and the rest. The nine positions, row by row:

| | interior of `b` | boundary of `b` | exterior of `b` |
|---|---|---|---|
| **interior of `a`** | 1st (`II`) | 2nd (`IB`) | 3rd (`IE`) |
| **boundary of `a`** | 4th (`BI`) | 5th (`BB`) | 6th (`BE`) |
| **exterior of `a`** | 7th (`EI`) | 8th (`EB`) | 9th (`EE`) |

Each character says what the two parts **share**:

| Character | Means |
|---|---|
| `T` | they share something (points, a line or an area; the dimension does not matter) |
| `F` | they share nothing (empty) |
| `*` | does not matter |
| `0`, `1`, `2` | they share exactly points (0), a line (1) or an area (2) |

`T` and `F` do not say that the predicate is true or false; the predicate is true if all nine positions fit its pattern. As an example, `T*F**F***` is the pattern of `Within`: `T` at II, the interiors share something; `F` at IE, the interior of `a` shares nothing with the exterior of `b`; `F` at BE, neither does the boundary of `a`; the rest does not matter. In words: the interiors meet, and nothing of `a` sticks out of `b`. The matrix `FF2101FF2` of the square and its edge above reads the same way: II `F` (the interiors share nothing), IE `2` (the inside of the square shares an area with the exterior of the edge), BI `1` (the boundary of the square and the interior of the edge share a line: the edge lies on it), BB `0` (the end points of the edge touch the boundary), and so on.

## The predicates

All predicates are extension methods on `IGeometry2D`:

| Predicate | Meaning |
|---|---|
| [`Intersects`](predicates/intersects.md) | the geometries share at least one point |
| [`Disjoint`](predicates/disjoint.md) | the geometries share no point (`FF*FF****`) |
| [`Contains(a, b)`](predicates/contains.md) | b lies in a and they share an interior point (`T*****FF*`) |
| [`Within(a, b)`](predicates/within.md) | a lies in b (`T*F**F***`) |
| [`Covers(a, b)`](predicates/covers.md) | no point of b lies outside a; boundary points count |
| [`CoveredBy(a, b)`](predicates/covered-by.md) | no point of a lies outside b; boundary points count |
| [`Touches`](predicates/touches.md) | the geometries meet only in boundary points |
| [`Crosses`](predicates/crosses.md) | the geometries cross (OGC; depends on the dimensions) |
| [`Overlaps`](predicates/overlaps.md) | geometries of equal dimension overlap partially |
| [`EqualsTopo`](predicates/equals-topo.md) | the geometries are topologically equal (`T*F**FFF*`); false if one is empty |

`Contains` versus `Covers` is the difference that surprises most people: a polygon does not *contain* a line that lies on its boundary (the line has no point in the interior), but it *covers* it. If you mean "lies inside, the boundary counts", use `Covers`/`CoveredBy`.

Rules that apply to all predicates:

- **Empty geometries.** If a geometry is empty, only `Disjoint` is true; all other predicates are false, and `Relate` returns a matrix of only `F`.
- **Area by even-odd.** Areas follow the even-odd rule ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)).
- **Line boundaries by mod-2.** The end points of a line are its boundary, closed paths have no boundary, and for a polyline with several paths only an odd number of path ends at a point counts.
- **Invalid input** is handled deterministically and without exceptions, but without a guarantee that the predicates are consistent with one another.

## The situations

The tables on the pages of the single predicates use the same 27 situations. `a` is the first, `b` the second geometry; the matrix is `a.Relate(b)` ([The intersection matrix](#the-intersection-matrix)). A cell shows `true` where the predicate holds.

| Situation | Matrix | [Intersects](predicates/intersects.md) | [Disjoint](predicates/disjoint.md) | [Contains](predicates/contains.md) | [Within](predicates/within.md) | [Covers](predicates/covers.md) | [CoveredBy](predicates/covered-by.md) | [Touches](predicates/touches.md) | [Crosses](predicates/crosses.md) | [Overlaps](predicates/overlaps.md) | [EqualsTopo](predicates/equals-topo.md) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A point inside an area | `0FFFFF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| A point on the edge of an area | `F0FFFF212` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| A point outside an area | `FF0FFF212` |  | **true** |  |  |  |  |  |  |  |  |
| A point in a hole of an area | `FF0FFF212` |  | **true** |  |  |  |  |  |  |  |  |
| A point on a line (not at its end) | `0FFFFF102` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| A point at the end of a line | `F0FFFF102` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| A point at a bend (a vertex) inside a line | `0FFFFF102` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| A point beside a line | `FF0FFF102` |  | **true** |  |  |  |  |  |  |  |  |
| Two equal points | `0FFFFFFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| Two lines that cross | `0F1FF0102` | **true** |  |  |  |  |  |  | **true** |  |  |
| Two lines that meet end to end | `FF1F00102` | **true** |  |  |  |  |  | **true** |  |  |  |
| A line that ends on another line (a T) | `FF10F0102` | **true** |  |  |  |  |  | **true** |  |  |  |
| Two lines that run together for a stretch | `1010F0102` | **true** |  |  |  |  |  |  |  | **true** |  |
| Two equal lines (one reversed) | `1FFF0FFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| A line inside an area | `1FF0FF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| A line that crosses the edge of an area | `1010F0212` | **true** |  |  |  |  |  |  | **true** |  |  |
| A line along the edge of an area | `F1FF0F212` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| A line outside an area that ends on its edge | `FF1F00212` | **true** |  |  |  |  |  | **true** |  |  |  |
| A line outside an area | `FF1FF0212` |  | **true** |  |  |  |  |  |  |  |  |
| A small area inside a large one | `2FF1FF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| An area inside another, touching its edge from inside | `2FF11F212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Two areas that share an edge | `FF2F11212` | **true** |  |  |  |  |  | **true** |  |  |  |
| Two areas that touch at a corner | `FF2F01212` | **true** |  |  |  |  |  | **true** |  |  |  |
| Two areas that overlap | `212111212` | **true** |  |  |  |  |  |  |  | **true** |  |
| Two equal areas (other start point and direction) | `2FFF1FFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| Two areas apart | `FF2FF1212` |  | **true** |  |  |  |  |  |  |  |  |
| An area inside a hole of another | `FF2FF1212` |  | **true** |  |  |  |  |  |  |  |  |

### The geometries

| Situation | `a` | `b` |
|---|---|---|
| A point inside an area | `POINT (5 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A point on the edge of an area | `POINT (10 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A point outside an area | `POINT (15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A point in a hole of an area | `POINT (5 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))` |
| A point on a line (not at its end) | `POINT (5 0)` | `LINESTRING (0 0, 10 0)` |
| A point at the end of a line | `POINT (0 0)` | `LINESTRING (0 0, 10 0)` |
| A point at a bend (a vertex) inside a line | `POINT (5 0)` | `LINESTRING (0 0, 5 0, 5 5)` |
| A point beside a line | `POINT (5 1)` | `LINESTRING (0 0, 10 0)` |
| Two equal points | `POINT (1 1)` | `POINT (1 1)` |
| Two lines that cross | `LINESTRING (0 0, 10 10)` | `LINESTRING (0 10, 10 0)` |
| Two lines that meet end to end | `LINESTRING (0 0, 5 5)` | `LINESTRING (5 5, 10 0)` |
| A line that ends on another line (a T) | `LINESTRING (5 0, 5 5)` | `LINESTRING (0 0, 10 0)` |
| Two lines that run together for a stretch | `LINESTRING (0 0, 10 0)` | `LINESTRING (5 0, 15 0)` |
| Two equal lines (one reversed) | `LINESTRING (0 0, 10 0)` | `LINESTRING (10 0, 0 0)` |
| A line inside an area | `LINESTRING (2 2, 8 8)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A line that crosses the edge of an area | `LINESTRING (5 5, 15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A line along the edge of an area | `LINESTRING (0 0, 10 0)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A line outside an area that ends on its edge | `LINESTRING (10 5, 15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A line outside an area | `LINESTRING (12 0, 12 10)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| A small area inside a large one | `POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| An area inside another, touching its edge from inside | `POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Two areas that share an edge | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))` |
| Two areas that touch at a corner | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 10, 20 10, 20 20, 10 20, 10 10))` |
| Two areas that overlap | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((5 0, 15 0, 15 10, 5 10, 5 0))` |
| Two equal areas (other start point and direction) | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 0, 10 10, 0 10, 0 0, 10 0))` |
| Two areas apart | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((20 0, 30 0, 30 10, 20 10, 20 0))` |
| An area inside a hole of another | `POLYGON ((4.5 4.5, 5.5 4.5, 5.5 5.5, 4.5 5.5, 4.5 4.5))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))` |
## Exactness versus overlays

Predicates compute exactly on the coordinates you pass. Overlays round to a grid ([Overlays, fill rules and the grid](overlay-and-grid.md)). For near-touches below the grid size, `a.Intersects(b)` can therefore be true while `a.Intersection(b)` is empty.

### Exactness in practice, and the alternatives with a tolerance

The predicates demand **numerical exactness**: `Intersects` is `true` only if the two geometries share a point *to the last bit* of their coordinates. This is what makes the results reliable and consistent with each other, and it is what NetTopologySuite and JTS do. With measured or computed data it is almost never met: a coordinate is a rounded `double`, a point that you place on a line by clicking, by projecting or by computing lies, say, 4e-16 beside it, and a boundary shared by two parcels is only as exact as the two surveys. A point on a **vertex** is exact, because it has the very same numbers; a point on a **segment** is practically never exact.

If "close enough" is what you mean, which for survey data it is, say so with a tolerance. The library has no global tolerance for the predicates; it has the tools to ask for one:

```csharp
double t = 0.01;                                    // the tolerance, in the unit of your coordinates (here 1 cm)

bool intersects = a.IsWithinDistance(b, t);         // Intersects: closer than t
bool disjoint = !a.IsWithinDistance(b, t);          // Disjoint: farther than t
bool contains = a.Buffer(t).Contains(b);            // Contains: b is in a, up to t over the edge
bool within = b.Buffer(t).Contains(a);              // Within
bool covers = a.Buffer(t).Covers(b);                // Covers
bool coveredBy = b.Buffer(t).Covers(a);             // CoveredBy
bool touches = a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b);          // Touches (areas): they meet, and overlap by less than t
bool overlaps = a.Buffer(-t).Intersects(b.Buffer(-t))                             // Overlaps (areas): they overlap by more than t,
    && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a);                          // and neither lies in the other
bool equal = a.HausdorffDistance(b) <= t;           // EqualsTopo: no vertex of one is farther than t from the other
```

| Predicate | With a tolerance `t` | Reads as |
|---|---|---|
| `Intersects` | `a.IsWithinDistance(b, t)` | the distance is at most `t` |
| `Disjoint` | `!a.IsWithinDistance(b, t)` | the distance is more than `t` |
| `Contains(a, b)` | `a.Buffer(t).Contains(b)` | b lies in a, or sticks out by at most `t` |
| `Within(a, b)` | `b.Buffer(t).Contains(a)` | a lies in b, or sticks out by at most `t` |
| `Covers(a, b)` | `a.Buffer(t).Covers(b)` | as `Contains`, boundary points count |
| `CoveredBy(a, b)` | `b.Buffer(t).Covers(a)` | as `Within`, boundary points count |
| `Touches` (areas) | `a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b)` | they meet, and overlap by less than `t` |
| `Overlaps` (areas) | `a.Buffer(-t).Intersects(b.Buffer(-t)) && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a)` | they overlap by more than `t`, neither lies in the other |
| `EqualsTopo` | `a.HausdorffDistance(b) <= t` | no vertex is farther than `t` from the other geometry (see below) |
| `Crosses`, `Relate` | none | see below |

Notes on the table:

- `IsWithinDistance` stops as soon as it knows the answer and checks the envelopes first, so it is cheap. For many queries against one geometry use `Prepare()` and `prepared.IsWithinDistance(b, t)`. None of these calls changes `a` or `b`; the buffers are new geometries.
- `Buffer` is the expensive part of the alternatives. For many tests against the same `a`, compute `a.Buffer(t)` (and `a.Buffer(-t)`) once and keep it.
- `Buffer(-t)` shrinks an area; it works for polygons, not for lines or points, so `Touches` and `Overlaps` as written are for areas. For a line or a point use the distance: they touch if `IsWithinDistance(b, t)`.
- `HausdorffDistance` is discrete (it looks at vertices and, with a `densifyFraction` below 1, at points in between) and a lower bound; pass `densifyFraction` (for example 0.1) if the geometries have long segments. A vertex on a vertex is the case it decides exactly.
- `Crosses` and `Relate` have no simple counterpart in distances: a crossing or a DE-9IM matrix is a statement about the interiors, and "almost crossing" has no single meaning. Ask for what you mean: `IsWithinDistance` for "they meet", and `NearestPoints` for where.
- **Or snap first, and ask the exact question** ([ADR 0046](../../adr/0046-fangen-an-ein-ziel.md)): `a.Touches(b.SnapTo(a, t, SnapMode.Vertices))` moves the vertices of `b` within `t` of `a` onto it and inserts the vertices of `a` into the segments of `b`, so that a shared boundary becomes exact. This works for every predicate and for `Relate`, keeps the meaning of the exact predicates, and is the step that also helps the overlays and `Cut`; it changes `b` by up to `t`.
- To make *data* exact instead of the question tolerant, round it once to a grid on import (`ReducePrecision(0.01)`). That makes vertices that lie within a cell identical, which helps where geometries meet at vertices, but it does not help a point on the middle of a segment ([Measures and constructions](measures-and-constructions.md)).

The [playground](../getting-started.md#the-playground) shows the distance next to every predicate for this reason, names the alternative with a tolerance below it, and says so when a `false` is only a rounding difference.

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

A `PreparedGeometry` copies a **snapshot** of the coordinates and builds a segment index (and a point-in-polygon structure) from it. Later changes to the host object have no effect on it, which matters because host geometries are mutable. It offers `Intersects`, `Disjoint`, `Contains`, `Covers`, `Within`, `CoveredBy` and `Touches`, always with the same results as the unprepared predicates; the fast paths are used for `Intersects`/`Disjoint` and point-in-area, everything else runs through the exact `Relate`. A `PreparedGeometry` is **thread-safe** after construction (ADR 0024): prepare it once and share it between threads. It also answers `Distance`, `IsWithinDistance` and `NearestPoints` against other geometries, with the same results as the unprepared methods (only among equally near pairs may the nearest points differ).
