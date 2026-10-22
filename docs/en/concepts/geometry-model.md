# The geometry model

SpatialNET computes on a small set of read-only interfaces. This page describes what those geometries mean: how polygons are defined, what "valid" means, and what happens with empty or invalid input. The interfaces themselves are listed in [Host integration](../host-integration.md).

## Kinds of geometry

| Interface | Is | Corresponds to |
|---|---|---|
| `IPoint2D` | a point | Point |
| `IMultiPoint2D` | a set of points | MultiPoint |
| `IPolyline2D` | one or more paths | LineString / MultiLineString |
| `IPolygon2D` | a flat list of rings | Polygon / MultiPolygon |

Paths and rings are parts of a geometry and are not geometries themselves. There is no geometry collection: results are dimension-homogeneous. 3D exists only on points (`IPoint3D`); algorithms interpolate Z at new vertices if the points involved carry it. A point may also carry a measure (`IPointM`, M): it is carried and interpolated like Z but never influences a computation ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)).

## Polygons: a flat list of rings

An `IPolygon2D` does not mark which ring is a shell and which a hole. Its area is defined by the **even-odd rule** over all rings ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)):

- A point is inside the polygon if it lies inside an **odd** number of rings.
- For valid polygons this equals the nesting depth: even depth = shell, odd depth = hole of the nearest enclosing shell.
- An island inside a hole is inside again, so it is a shell of its own.
- **The ring orientation is ignored** on input, and the closing point of a ring (equal to the start point) may be repeated or omitted.

SpatialNET calls a shell together with the holes that lie directly in it a **part**. A polygon with several parts is a multipolygon. Results are handed to the factory as parts, so that the host does not have to recompute which ring is a hole of which shell. In results, shells run counter-clockwise and holes clockwise.

## What "valid" means

A polygon is valid if the rings do not cross. Following the OGC rule as NetTopologySuite checks it:

- A hole may touch the shell or another hole in **exactly one point**; a ring may not touch itself.
- Rings that cross or overlap along an edge are invalid. In particular, partially overlapping shells are invalid: even-odd turns the overlap into a hole, whereas many hosts would count the area twice.
- Degenerate rings (fewer than 3 distinct points, area 0) are ignored by the algorithms and reported by `IsValid()`.

A missing closing point and the ring orientation are not errors. See [Simplify, convex hull and validation](simplify-and-validation.md) for how to check validity.

## Valid and invalid input

| Operation | Valid input | Invalid input |
|---|---|---|
| Overlays, buffer, simplify, cut | valid, closed results | defined result by the even-odd rule or the chosen fill rule; no exception |
| Predicates (`Contains`, `Relate`, …) | exact DE-9IM result | deterministic, no exception, but the predicates are not guaranteed to be consistent with one another |
| Measures (`Area`, `Centroid`, …) | exact | deterministic but not guaranteed; empty or invalid geometries never throw |
| `IsValid` / `Validate` | – | reports the findings |

So: the algorithms never throw for geometrically invalid or empty input. They throw only for `null` arguments, invalid numeric parameters (`NaN`, negative tolerance, a grid size ≤ 0, …) and geometry types they do not know.

## Empty geometries

An empty geometry has 0 points, paths or rings. It is a valid argument everywhere:

- The factory creates empty results for empty input, never `null`.
- `Envelope()` of an empty geometry is `Envelope2D.Empty` (all values `NaN`, `IsEmpty` is true).
- `Centroid()` is `null`; `Distance` to an empty geometry is `NaN` (a deliberate deviation from NetTopologySuite, which returns 0 and so makes "touching" and "nothing there" indistinguishable).
- For predicates, only `Disjoint` is true when either geometry is empty.

## Where is a point? Locate

`Locate` tells where a point lies relative to a geometry: `Location.Inside`, `Location.Boundary` or `Location.Outside` ([ADR 0022](../../adr/0022-locate-envelope-helfer-und-clip.md)). It is exact, like the predicates, and agrees with them: for a polygon, `Inside` is `Contains`, `Outside` is `Disjoint`, and `Boundary` is the rest of `Intersects`.

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))");

Location a = parcel.Locate(new Coordinate(2, 2));    // Inside
Location b = parcel.Locate(new Coordinate(0, 10));   // Boundary
Location c = parcel.Locate(new Coordinate(10, 10));  // Outside: in the hole
```

- **Polygon:** even-odd over all rings; a hole is outside, every ring (also of a hole) is boundary. **Polyline:** boundary at an end point by the mod-2 rule, inside elsewhere on the line. **Points:** inside if equal, they have no boundary. An empty geometry is outside.
- For many points against the same geometry, `geometry.Prepare().Locate(point)` uses a segment index.
- A point with a `NaN` or infinite coordinate throws `ArgumentOutOfRangeException`.

## Envelopes

`Envelope2D` has helpers for the arithmetic every host writes itself: `Intersects`, `Contains` (an envelope or a point; the border counts), `Union`, `Intersection`, `Expand` (negative shrinks), `Center`. An empty envelope meets nothing and contains nothing. `ToPolygon(factory)` turns an envelope into a rectangle polygon.

## Measures

`Area`, `Length`, `Envelope`, `Centroid` and `Distance` are extension methods ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)). `Area` exists for polygons, `Length` for polylines and polygons (the perimeter); the general `IGeometry2D` overloads return 0 when a measure does not fit the dimension. `Centroid` is returned as a `Coordinate?` value without Z, not as a geometry, so no factory is needed.
