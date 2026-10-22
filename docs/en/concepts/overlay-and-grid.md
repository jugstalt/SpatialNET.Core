# Overlays, fill rules and the grid

Overlays combine two geometries: `Intersection`, `Union`, `Difference` and `SymDifference`. All of them run through **one** core, which is why they behave consistently, why they accept a fill rule, and why their results lie on a grid. This page explains how that works and what it means for you.

## How an overlay works

The core follows the pattern of the JTS OverlayNG pipeline ([ADR 0005](../../adr/0005-overlay-winding-number-snap-rounding.md)):

1. **Noding with snap rounding.** All edges of both inputs are cut at their intersection points, and the points are rounded to a grid.
2. **Planar graph.** The noded edges form a planar graph whose faces are the candidate result areas.
3. **Winding numbers.** Every face gets one winding number per operand: how often the boundary of that input winds around it.
4. **Fill rule.** The fill rule turns each winding number into inside or outside.
5. **Selection and assembly.** Faces are selected according to the operation, and the result is assembled as parts (shell plus holes), paths or points.

The result is always valid and closed; shells run counter-clockwise, holes clockwise.

## Fill rules

A fill rule decides from the winding number of a point whether it belongs to the area of an input. Every overlay takes a `FillRule` parameter:

| Fill rule | Inside if the winding number is … | Typical use |
|---|---|---|
| `EvenOdd` (default) | odd | Standard for all inputs; matches how SpatialNET defines polygon areas ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)) |
| `NonZero` | not 0 | Merging overlapping shells, e.g. from host data |
| `Positive` | greater than 0 | The basis of the buffer |
| `Negative` | less than 0 | Rarely needed |

```csharp
// Two partially overlapping shells in one polygon: invalid by the OGC rules.
var overlapping = Wkt.Read<IPolygon2D>(
    "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0),(5 5, 15 5, 15 15, 5 15, 5 5))");

Console.WriteLine(overlapping.IsValid());                            // False
Console.WriteLine(overlapping.Union(overlapping).Area());            // 150 - even-odd: the overlap becomes a hole
Console.WriteLine(overlapping.Union(overlapping, FillRule.NonZero).Area()); // 175 - the overlap is merged
```

Because overlays evaluate the fill rule anyway, they deliver a defined result even for self-crossing or overlapping input. A union with the right fill rule therefore doubles as a repair step for such input.

## Why there is always a grid

The intersection point of two segments is in general not representable as a pair of `double` values. If it is rounded naively, the rounded point no longer lies exactly on either segment, and subsequent decisions can contradict each other: the topology becomes inconsistent, and algorithms produce missing edges, invalid rings or exceptions.

SpatialNET avoids this with **snap rounding**: every constructed intersection point and every vertex is moved to a point of a regular grid, and every segment that passes through such a grid cell is routed through its center. Combined with exact predicates (Shewchuk's `orient2d`, which never gives a wrong sign), the result is a correctly noded arrangement and a result that is valid by construction.

The price is that **result coordinates lie on the grid** and may differ from the exact value by up to half a grid unit. Unmodified input vertices that already lie on the grid are unchanged.

### Choosing the grid size

The grid size is resolved in a cascade (the same pattern as for the factory):

1. the `gridSize` parameter of the call, if not `null`,
2. otherwise `Precision.DefaultGridSize`, if set (initially `null`),
3. otherwise automatically: the largest absolute coordinate of all inputs times 1e-12, rounded down to a power of ten. That is about 12 significant digits; for UTM coordinates around 5,000,000 the grid is 1e-6.

```csharp
var strip = Wkt.Read<IPolygon2D>("POLYGON((0 1, 10 1, 10 5, 0 5, 0 1))");
var triangle = Wkt.Read<IPolygon2D>("POLYGON((0 0, 1 3, -1 3, 0 0))");

// Automatic grid (here 1e-11): the intersection point (1/3, 1) is rounded very finely.
Console.WriteLine(strip.Intersection(triangle).ToWkt());                  // POLYGON ((0 1, 0.33333333333 1, 1 3, 0 3, 0 1))

// Explicit grid of 0.01 map units.
Console.WriteLine(strip.Intersection(triangle, gridSize: 0.01).ToWkt());  // POLYGON ((0 1, 0.33 1, 1 3, 0 3, 0 1))

// Global default for all overlays that do not pass a grid size.
Precision.DefaultGridSize = 0.01;
```

The automatic grid is derived from all inputs together, so `a.Intersection(b)` and `b.Intersection(a)` use the same grid. Choose an explicit grid size when you deliberately want coarser results, for example to clean up noisy data. `gridSize` must be finite and greater than 0; a grid that is too small for the coordinates (the coordinate no longer fits into the grid's integer range) throws an `ArgumentOutOfRangeException`.

## Uniting many geometries

`Union` takes two operands. To dissolve many geometries, use `UnionAll`; a loop over `Union` is quadratic, because each step processes the whole accumulated result again ([ADR 0018](../../adr/0018-unionall-kaskadiert-gemeinsames-gitter.md)).

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((5 0, 15 0, 15 10, 5 10, 5 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((50 50, 60 50, 60 60, 50 60, 50 50))"),
};

IPolygon2D all = parcels.UnionAll();   // two parts: the merged row and the separate square

// "Dissolve by key": group, then unite each group.
var byOwner = parcels.GroupBy(p => p.Envelope().MinX < 20 ? "west" : "east").Select(g => g.UnionAll());
```

`UnionAll` exists for polygons (with a `FillRule`), polylines and multipoints, and for geometries of equal dimension in general. Each input is read under the fill rule as in `Union`, empty inputs are skipped, and mixed dimensions throw `NotSupportedException`. It orders the inputs by their envelopes and unites them in small groups, level by level, so that neighbours meet early. **All steps use one grid**, resolved once over all inputs, so the grouping can change the result only by about one grid cell. The same sequence always gives the identical result; a different order gives the same area up to that tolerance.

## Clipping to a rectangle

`Clip` cuts a geometry to a rectangle, the case of tiles and viewports ([ADR 0022](../../adr/0022-locate-envelope-helfer-und-clip.md)). It is the intersection with the **closed** rectangle (the border belongs to it), with two fast paths:

```csharp
var tile = new Envelope2D(0, 0, 256, 256);

IPolygon2D inTile = parcel.Clip(tile);    // cut at the border
IPolyline2D road = roadLine.Clip(tile);   // the parts inside or on the border
IMultiPoint2D hits = points.Clip(tile);   // filtered, never rounded
```

- A geometry that lies **completely in the rectangle** is returned as a copy **without rounding** (a polygon as a normalised copy), and one that **does not meet it** gives an empty geometry. Only where the border cuts, the overlay runs and **only those results lie on the grid**.
- Z and M are carried like in `Intersection`; the corners of the rectangle that become vertices have none.
- A rectangle without area (zero width or height) gives an empty polygon or polyline; an invalid envelope (not finite, or `Min > Max`) throws.

## What else you should know

- **Strict semantics.** Results are dimension-homogeneous. Touch and collapse remnants of lower dimension are dropped: two polygons that only touch along an edge intersect in an *empty polygon*, not in a line. Where a result type is not unique, the typed overloads fix it: polygon ∩ polygon is an `IPolygon2D`; polygon ∩ polyline and polyline ∩ polyline are `IPolyline2D` (overlaps only, pure crossing points are dropped); point or multipoint ∩ anything is an `IMultiPoint2D`. `Difference` returns the type of the first operand. `Union` and `SymDifference` exist only for operands of equal dimension.
- **Predicates are exact, overlays are rounded.** `Intersects`, `Relate` and the other predicates compute exactly on the input coordinates without a grid. For near-touches below the grid size, `Intersects` can therefore be true while `Intersection` is empty. This is expected and the same in NetTopologySuite.
- **Z.** At new vertices Z is interpolated on the unrounded original segment. At crossings the Z of the first operand applies. M is handled the same way (carried, interpolated, first operand wins at crossings, dropped if the factory does not support M) and never influences a result's X/Y ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)). Results are 2D if the factory does not support Z ([ADR 0004](../../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md)).
- **Invalid input never throws.** Overlays interpret invalid input by the fill rule; they throw only for `null` arguments, invalid numeric parameters and unsupported geometry kinds.
