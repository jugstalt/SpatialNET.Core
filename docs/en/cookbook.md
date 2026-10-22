# Cookbook

Short recipes for what hosts do every day. Each one is a complete, runnable fragment (they are compiled and checked in the test project), and each names the page that explains the idea behind it. They assume the setup of [Host integration](host-integration.md): the host classes are in place and `GeometryFactory.Default` is set; here the bundled `SpatialNET.Imp` classes and WKT stand in for your data.

## Which parcel is under the click?

A spatial index finds candidates by envelope; the exact test decides. The same pattern answers "everything within a distance of a point": widen the envelope, then test exactly ([Spatial index](concepts/spatial-index.md)).

```csharp
var parcels = new Dictionary<string, IPolygon2D>
{
    ["A-1"] = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    ["A-2"] = Wkt.Read<IPolygon2D>("POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
};

// Build once (the index never changes); the items are the dictionary entries.
var index = parcels.ToSpatialIndex(p => p.Value.Envelope());

// Which parcel is under the click? The index returns envelope candidates, Locate decides.
var click = new Coordinate(14, 3);
var hit = index.Query(new Envelope2D(click.X, click.Y, click.X, click.Y))
    .First(p => p.Value.Locate(click) != Location.Outside);

Console.WriteLine(hit.Key); // A-2

// Everything within 6 map units of a well: widen the envelope, then test exactly.
var well = Wkt.Read<IPoint2D>("POINT(24 5)");
var near = index.Query(new Envelope2D(well.X - 6, well.Y - 6, well.X + 6, well.Y + 6))
    .Where(p => p.Value.IsWithinDistance(well, 6))
    .Select(p => p.Key);

Console.WriteLine(string.Join(",", near)); // A-2

```

## One polygon, many points

Testing thousands of points against one big polygon is what `Prepare()` is for. The prepared geometry is thread-safe, so build it once and let all threads use it ([Predicates](concepts/predicates.md), [ADR 0024](../adr/0024-prepared-geometry-thread-sicher-und-distanz.md)).

```csharp
var district = Wkt.Read<IPolygon2D>("POLYGON((0 0, 100 0, 100 100, 0 100, 0 0))");
var points = Enumerable.Range(0, 1000).Select(i => new Coordinate(i % 40 * 5, i / 40 * 5)).ToArray();

// Prepare once; a PreparedGeometry is thread-safe, so the threads share it.
PreparedGeometry prepared = district.Prepare();
int inside = points.AsParallel().Count(p => prepared.Locate(p) == Location.Inside);

Console.WriteLine(inside); // points strictly inside, not on the boundary

```

## Spatial join with area shares

`Join` pairs the items of two indexes whose envelopes meet, skipping whole subtrees that cannot meet; then you test exactly and compute what you need, here how much of each parcel lies in a flood zone ([Spatial index](concepts/spatial-index.md), [Overlays](concepts/overlay-and-grid.md)).

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
}.ToSpatialIndex();
var floodZones = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((5 -5, 15 -5, 15 5, 5 5, 5 -5))"),
}.ToSpatialIndex();

// Join walks both trees together and gives envelope pairs; the exact test and the overlay are yours.
var shares = parcels.Join(floodZones)
    .Where(pair => pair.Left.Intersects(pair.Right))
    .Select(pair => (Parcel: pair.Left, Share: pair.Left.Intersection(pair.Right).Area() / pair.Left.Area()))
    .ToList();

Console.WriteLine(shares.Count);   // 1: the second parcel is nowhere near the zone
Console.WriteLine(shares[0].Share); // 0.25

```

## Dissolve by attribute

Group first, then `UnionAll` each group. It merges many polygons in one go, not as a loop of pairwise unions ([Overlays](concepts/overlay-and-grid.md)).

```csharp
var landUse = new (string Use, string Wkt)[]
{
    ("forest", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    ("forest", "POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
    ("field", "POLYGON((0 20, 10 20, 10 30, 0 30, 0 20))"),
};

// One union per key; neighbours that share an edge become one polygon.
Dictionary<string, IPolygon2D> dissolved = landUse
    .GroupBy(x => x.Use)
    .ToDictionary(g => g.Key, g => g.Select(x => Wkt.Read<IPolygon2D>(x.Wkt)).UnionAll());

Console.WriteLine(dissolved["forest"].Area()); // 200
Console.WriteLine(dissolved["forest"].RingCount); // 1: one outline, the shared edge is gone

```

## Repair a drawn polygon

`Validate` says what is wrong and where; `MakeValid` repairs it under the even-odd rule. Check first and keep valid input as it is: `MakeValid` always returns a new polygon on the grid ([Simplify, convex hull and validation](concepts/simplify-and-validation.md)).

```csharp
// A user drew a bow tie: the outline crosses itself.
var drawn = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

ValidationResult check = drawn.Validate();
foreach (var issue in check.Issues)
{
    Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
}

// MakeValid resolves it under the even-odd rule: two triangles that touch in a point.
IPolygon2D repaired = check.IsValid ? drawn : drawn.MakeValid();
Console.WriteLine(repaired.IsValid()); // True
Console.WriteLine(repaired.Area());    // 50

```

## Split a parcel

`Cut` returns the pieces as a flat list. A piece that the line does not reach comes back whole ([Cut](concepts/cut.md)).

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var fence = Wkt.Read<IPolyline2D>("LINESTRING(4 -1, 4 11)");

// The pieces come as a flat list, in no left/right order.
IReadOnlyList<IPolygon2D> pieces = parcel.Cut(fence);

Console.WriteLine(string.Join(" and ", pieces.Select(p => p.Area()).Order())); // 40 and 60

```

## From lines to areas

`LineMerge` joins pieces that meet end to end into longer paths; `Polygonize` turns lines into the areas they enclose. Lines that only dangle enclose nothing and are dropped ([Overlays](concepts/overlay-and-grid.md), [Measures and constructions](concepts/measures-and-constructions.md)).

```csharp
// A road network digitised in pieces: the pieces only touch at their ends.
var pieces = Wkt.Read<IPolyline2D>("MULTILINESTRING((0 0, 5 0), (5 0, 10 5), (10 5, 20 5))");
IPolyline2D road = pieces.LineMerge();
Console.WriteLine(road.PathCount); // 1: three pieces joined into one path

// A closed outline with a line across it: the lines enclose two areas.
var fences = Wkt.Read<IPolyline2D>(
    "MULTILINESTRING((0 0, 10 0, 10 10, 0 10, 0 0), (5 -5, 5 15))");
IReadOnlyList<IPolygon2D> fields = fences.Polygonize();
Console.WriteLine(fields.Count); // 2: the ends of the crossing line outside the outline are dropped

```

## Stationing along a road

Position along a line, nearest position to a point, and the piece between two positions. With M values the same three exist by measure instead of by length ([Linear referencing](concepts/linear-referencing.md)).

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 100 0, 100 50)");

Coordinate? at = road.Interpolate(120);               // (100 20): 120 along the road
double chainage = road.Project(new Coordinate(40, 7)); // 40: where the nearest point of the road lies
IPolyline2D section = road.Substring(50, 120);          // from (50 0) via (100 0) to (100 20)

Console.WriteLine($"{at!.Value.X} {at.Value.Y}, {chainage}, {section.Length()}"); // 100 20, 40, 70

```

## For the map

Simplify for small scales, clip to the screen, and find a good place for the label ([Simplify, convex hull and validation](concepts/simplify-and-validation.md), [Overlays](concepts/overlay-and-grid.md), [Measures and constructions](concepts/measures-and-constructions.md)).

```csharp
// A wiggly shore with many vertices.
var shore = Wkt.Read<IPolygon2D>(
    "POLYGON((0 0, 25 0.2, 50 0, 75 -0.2, 100 0, 100 40, 60 40, 60 100, 40 100, 40 40, 0 40, 0 0))");

// Fewer vertices for a small scale, without crossings.
IPolygon2D light = shore.Simplify(1, preserveTopology: true);

// Only what is on screen.
IGeometry2D visible = shore.Clip(new Envelope2D(-10, -10, 120, 50));

// Where to put the name: inside, and as far from the edge as possible.
Coordinate? label = shore.Polylabel();

Console.WriteLine(light.AsList().Sum(r => r.PointCount) < shore.AsList().Sum(r => r.PointCount)); // True

```

## Exchange with databases and web clients

Write and read WKB for a database column and GeoJSON for a web client. Reading takes the factory that creates your own classes, so what you read is already a host geometry ([Getting started](getting-started.md)).

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

// Binary for a database column, JSON for a web client; reading takes the factory that creates your classes.
byte[] wkb = parcel.ToWkb();
string json = parcel.ToGeoJson();

IPolygon2D fromDatabase = Wkb.Read<IPolygon2D>(wkb, ImpGeometryFactory.Instance);
IPolygon2D fromClient = GeoJson.Read<IPolygon2D>(json, ImpGeometryFactory.Instance);

Console.WriteLine(fromDatabase.EqualsTopo(fromClient)); // True

```
