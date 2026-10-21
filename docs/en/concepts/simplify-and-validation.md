# Simplify, convex hull and validation

## Simplify

`Simplify` reduces the number of vertices with the Douglas-Peucker algorithm ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)). A vertex is kept if it is farther than `tolerance` from the chord between its neighbors.

```csharp
var noisy = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 1 0.1, 2 -0.1, 3 0.05, 4 0)");
Console.WriteLine(noisy.Simplify(0.5).ToWkt()); // LINESTRING (0 0, 4 0)
```

- **Polylines:** each path is simplified; the start and end point stay, and the remaining vertices are unchanged (including Z).
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

Not errors: a **missing closing point**, the **ring orientation**, and a **ring inside a hole** (that is an island). To repair invalid input, run it through an overlay with the right fill rule, for example `Union` with `FillRule.NonZero` ([Overlays, fill rules and the grid](overlay-and-grid.md)).
