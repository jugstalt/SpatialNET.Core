# Buffer and offset curves

`Buffer` grows or shrinks a geometry by a distance. `OffsetCurve` returns a line parallel to a polyline or to the boundary of a polygon. Both are built on the overlay core ([Overlays, fill rules and the grid](overlay-and-grid.md)); defaults and parameters are deliberately compatible with JTS/NetTopologySuite, so results stay close to the oracle the tests compare against ([ADR 0006](../../adr/0006-buffer-roh-offset-union-positive-offsetcurve-jts.md)).

## Buffer

```csharp
var line = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)");
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

// A buffer around a line with flat ends: a 10 x 4 rectangle.
Console.WriteLine(line.Buffer(2, new BufferOptions { CapStyle = CapStyle.Flat }).Area()); // 40

// Round corners (default) versus mitre corners.
Console.WriteLine(Math.Round(square.Buffer(1).Area(), 2));                                         // 143.12
Console.WriteLine(square.Buffer(1, new BufferOptions { JoinStyle = JoinStyle.Mitre }).Area());      // 144

// A negative distance shrinks a polygon.
Console.WriteLine(square.Buffer(-2).ToWkt()); // POLYGON ((2 2, 8 2, 8 8, 2 8, 2 2))
```

`Buffer(this IGeometry2D, double distance, BufferOptions? options = null, IGeometryFactory? factory = null, double? gridSize = null)` always returns an `IPolygon2D`. The distance is in map units; buffering in geographic coordinates (degrees) is distorted, so project first.

### How it is built

The buffer is built from the **raw offset** of each ring or line (with joins and caps). Because the raw offset overlaps itself at concave corners and narrow places, it is cleaned up by a single `Union(FillRule.Positive)` in the overlay core. There is no trimming logic of its own for self-intersections.

### Options

| Option | Default | Meaning |
|---|---|---|
| `JoinStyle` | `Round` | Corners on the outside: `Round` (arc), `Mitre` (extended edges, limited by `MitreLimit`), `Bevel` (direct connection) |
| `CapStyle` | `Round` | Ends of lines: `Round` (semicircle), `Flat` (cut off at the end point), `Square` (extended by the distance) |
| `QuadrantSegments` | 8 | Segments per quarter circle |
| `ArcTolerance` | `null` | Maximum chord deviation of the arcs in map units; if set, it takes precedence over `QuadrantSegments` |
| `MitreLimit` | 5 | Largest ratio of mitre distance to distance; beyond it the tip is truncated |
| `SimplifyFactor` | 0.01 | Factor (× \|distance\|) of the input simplification that removes tiny indentations; 0 = off |
| `SingleSided` | `false` | Buffer only one side of lines (positive left, negative right), with flat ends |

`BufferOptions` and `OffsetOptions` are records with `init` properties and a `Default` instance.

### Negative distances and edge cases

- A negative distance on a polygon buffers **inwards**: the polygon shrinks by \|d\|. Narrow parts may disappear or the polygon may split into several parts.
- A polygon that vanishes completely gives an empty polygon.
- A point or line with a distance ≤ 0 gives an empty polygon (except `SingleSided`).
- A polygon with distance 0 returns the input normalized by even-odd.
- Invalid input is interpreted by even-odd, without an exception. Exceptions are thrown only for a distance that is `NaN` or infinite, and for invalid options.
- Buffer results are 2D.

## Offset curves

```csharp
// Offset curves: positive distance to the left, negative to the right of the direction of travel.
Console.WriteLine(line.OffsetCurve(2).ToWkt());  // LINESTRING (0 2, 10 2)
Console.WriteLine(line.OffsetCurve(-2).ToWkt()); // LINESTRING (0 -2, 10 -2)
```

`OffsetCurve` exists for `IPolyline2D` and `IPolygon2D` and always returns an `IPolyline2D`.

- **Polylines:** a positive distance is to the left in the direction of travel, a negative distance to the right.
- **Polygons:** a positive distance is outwards and a negative distance inwards, **regardless of ring orientation**. The result is the boundary of `Buffer(d)` as closed paths.
- **Guarantee:** the result lies on the boundary of the buffer. This follows the JTS `OffsetCurve` approach: a buffer with flat ends, the raw offset of the desired side, and a match against the buffer boundary. Where the offset has several disjoint sections (narrow places, self-intersections of the input), the result is a polyline with several paths.
- **`Joined`:** `OffsetOptions.Joined` connects the pieces into one path; that path may then intersect itself.
- **Z:** offset curves interpolate Z on the corresponding source segment if the factory supports Z.

`OffsetOptions` has the same join, arc, mitre and simplify options as `BufferOptions`, plus `Joined`.
