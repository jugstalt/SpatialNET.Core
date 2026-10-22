# Simplify, convex hull and validation

## Simplify

`Simplify` reduces the number of vertices with the Douglas-Peucker algorithm ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)). A vertex is kept if it is farther than `tolerance` from the chord between its neighbors.

```csharp
var noisy = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 1 0.1, 2 -0.1, 3 0.05, 4 0)");
Console.WriteLine(noisy.Simplify(0.5).ToWkt()); // LINESTRING (0 0, 4 0)
```

- **Polylines:** each path is simplified; the start and end point stay, and the remaining vertices are unchanged (including Z and M).
- **Polygons:** each ring is simplified; the start point of a ring may be dropped as well, and rings that end up with fewer than 4 points are dropped. The result is normalized by the overlay and is therefore **always valid** (see [The geometry model](geometry-model.md)).
- **`preserveTopology: true`** uses a topology-preserving variant after the pattern of JTS `TopologyPreservingSimplifier`: no new crossings arise between or within the paths, and no rings are lost.
- `tolerance` is in map units and must be finite and ≥ 0.

The signatures are `Simplify(this IPolyline2D, double tolerance, bool preserveTopology = false, IGeometryFactory? factory = null)` and, for polygons, an additional `gridSize` for the normalization.

## Convex hull

`ConvexHull` returns the convex hull of all points of a geometry as an `IGeometry2D`. Depending on the input it is a polygon (counter-clockwise), a polyline between the two extreme points (all points collinear) or a point; for empty input it is empty.

## Validation

```csharp
var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");
Console.WriteLine(bowtie.IsValid()); // False

foreach (var issue in bowtie.Validate().Issues)
{
    Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
}
```

`IsValid()` returns a `bool`. `Validate()` returns a `ValidationResult` whose `Issues` list every finding with its kind and location (`ValidationIssue(Kind, Location)`); it is empty for a valid geometry. Validity follows [ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md): the area is read by even-odd, and the OGC rules apply for crossings and touches. All decisions are exact.

| `ValidationIssueKind` | Meaning |
|---|---|
| `SelfIntersection` | Rings cross each other, or a ring crosses or overlaps itself |
| `RingSelfTouch` | A ring touches itself at a point (for example an "inverted hole") |
| `DegenerateRing` | A ring without area: fewer than 3 distinct points or all points collinear |
| `InvalidCoordinate` | A coordinate is `NaN` or infinite |
| `TooFewPoints` | A path with fewer than 2 distinct points |
| `OverlappingShells` | Two rings overlap along an edge |
| `DisconnectedInterior` | Rings touch in such a way that the area falls apart into separate parts (for example two touch points of the same ring pair) |

Not errors: a **missing closing point**, the **ring orientation**, and a **ring inside a hole** (that is an island). To repair invalid input, use `MakeValid` (below).

## Repair: MakeValid

`MakeValid` turns an invalid geometry into a valid one ([ADR 0020](../../adr/0020-makevalid-even-odd-normalisierung.md)). For a polygon it is **even-odd normalisation** through the overlay core, the same self-union that `Simplify` runs: the area the input has under the fill rule is exactly the area of the result. Crossing, overlapping and touching rings dissolve, holes, islands and parts arise from the overlay, rings without area are dropped.

```csharp
var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

bowtie.IsValid();                  // false
IPolygon2D valid = bowtie.MakeValid();  // two triangles, valid
```

- **It is not the OGC `ST_MakeValid`.** Results are dimension-homogeneous, and there is no geometry collection, so collapsed remnants are not kept as lines.
- **It runs on the grid** ([overlays](overlay-and-grid.md)): the result lies on the grid, also the parts that were valid before. To leave valid input untouched, check `IsValid()` first. `FillRule.NonZero` merges overlapping shells instead of making the overlap a hole.
- **Polylines** lose non-finite points, consecutive duplicate points and paths with fewer than two distinct points; crossing lines are valid. **Points** lose non-finite ones; duplicates stay. Lines and points are not rounded.
- The result is always a new geometry from the factory, and `MakeValid(MakeValid(g))` is the same geometry again up to the grid (running it twice can bend an edge of a very thin sliver by about one grid cell).

