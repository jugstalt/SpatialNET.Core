# Getting started

This page walks through the first program: installing, setting up the geometry factory, reading geometries, running an algorithm and writing the result.

## Installation

SpatialNET is a pre-release and is on NuGet. Install the packages you need with `--prerelease`:

```bash
dotnet add package SpatialNET.Extensions --prerelease
dotnet add package SpatialNET.Imp --prerelease
dotnet add package SpatialNET.IO --prerelease
```

Alternatively, clone the repository and reference the projects:

```bash
git clone https://github.com/jugstalt/SpatialNET.Core.git
dotnet add <your project> reference SpatialNET.Core/src/SpatialNET.Extensions/SpatialNET.Extensions.csproj
dotnet add <your project> reference SpatialNET.Core/src/SpatialNET.Imp/SpatialNET.Imp.csproj
dotnet add <your project> reference SpatialNET.Core/src/SpatialNET.IO/SpatialNET.IO.csproj
```

The target framework is `net10.0`. Which projects you need depends on your role:

| You are … | Reference |
|---|---|
| a host (GIS application with its own geometry classes) | `SpatialNET.Extensions` (brings `SpatialNET.Core`); see [Host integration](host-integration.md) |
| a user without geometry classes of your own | `SpatialNET.Extensions`, `SpatialNET.Imp` and, for text formats, `SpatialNET.IO` |

## A first program

```csharp
using SpatialNET;
using SpatialNET.Imp;
using SpatialNET.IO;

// Once at startup: the factory that algorithms use to create their results.
GeometryFactory.Default = ImpGeometryFactory.Instance;

var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

IPolygon2D overlap = a.Intersection(b);

Console.WriteLine(overlap.ToWkt());          // POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))
Console.WriteLine(overlap.Area());           // 25
Console.WriteLine(a.Intersects(b));          // True
Console.WriteLine(a.Buffer(1).Area() > 100); // True
```

What happens here:

- **Geometries are interfaces.** `IPolygon2D` is a read-only interface; `Wkt.Read<T>` creates the geometry through the factory, in this case as the classes of the reference implementation `SpatialNET.Imp`.
- **Algorithms are extension methods** on the interfaces (`Intersection`, `Buffer`, `Area`, `Intersects`, …). All of them live in the namespace `SpatialNET`.
- **Results come from the factory.** An algorithm creates its result geometries through `IGeometryFactory`. Every algorithm takes it as an optional `factory` parameter; if you pass `null` (the default), `GeometryFactory.Default` is used. If neither is set, the algorithm throws an `InvalidOperationException` with a clear message ([ADR 0004](../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md)).

## Options and precision

Optional parameters follow a fixed order: required parameters → domain options → `factory` → `gridSize`.

```csharp
// Grid size in map units (default: derived automatically from the coordinates).
IPolygon2D union = a.Union(b, FillRule.NonZero, gridSize: 0.001);

// Several expensive predicates against the same geometry: prepare it once.
PreparedGeometry prepared = a.Prepare();
bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

// Full DE-9IM: matrix and pattern matching.
string matrix = a.Relate(b).ToString(); // "212101212"
```

- **Fill rule.** The area of a polygon is defined by the even-odd rule over all its rings; shells and holes are derived from nesting, not marked ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)). Overlay operations accept a `FillRule` (`EvenOdd`, `NonZero`, `Positive`, `Negative`), for example to merge overlapping shells with `NonZero`.
- **Grid.** Overlays always round their results to a grid (snap rounding): result coordinates lie on the grid, and in return the topology is provably consistent and the result valid. The grid size is taken from the `gridSize` parameter, otherwise from `Precision.DefaultGridSize`, otherwise derived automatically from the largest coordinate of the inputs ([ADR 0005](../adr/0005-overlay-winding-number-snap-rounding.md)). Predicates such as `Intersects` and `Relate` compute exactly on the input coordinates, without a grid.
- **Strict results.** Results are dimension-homogeneous: touch remnants of lower dimension are dropped (two polygons touching along an edge intersect in an empty polygon, not in a line).

## Reading and writing

`SpatialNET.IO` reads and writes WKT, GeoJSON and WKB ([ADR 0009](../adr/0009-io-wkt-geojson-ueber-factory.md), [ADR 0016](../adr/0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.md)):

```csharp
IGeometry2D g = Wkt.Read("LINESTRING(0 0, 10 10)");
string json = g.ToGeoJson();
IReadOnlyList<IGeometry2D> all = GeoJson.ReadAll(json); // also FeatureCollection

byte[] wkb = g.ToWkb();                                 // ISO WKB, little endian
IGeometry2D fromWkb = Wkb.Read(wkb);                    // ISO and EWKB
```

Reading always goes through the factory (parameter `factory`, otherwise `GeometryFactory.Default`). Geometries that are syntactically correct but geometrically invalid are accepted unchanged.

## What is available

| Area | Methods (extension methods in the namespace `SpatialNET`) |
|---|---|
| Overlays | `Intersection`, `Union`, `UnionAll`, `Difference`, `SymDifference`, `Clip`, `Polygonize` |
| Position of a point | `Locate` (`Location`), also on `PreparedGeometry` |
| Derived geometries | `Buffer`, `OffsetCurve`, `ConvexHull`, `Simplify`, `Cut` |
| Predicates | `Intersects`, `Disjoint`, `Contains`, `Within`, `Covers`, `CoveredBy`, `Touches`, `Crosses`, `Overlaps`, `EqualsTopo`, `Relate`, `Prepare` |
| Spatial index | `SpatialIndex<T>`, `ToSpatialIndex`, `Query`, `Nearest`, `Join`, `SelfJoin` |
| Linear referencing | `Interpolate`, `Project`, `Substring`, `InterpolateByM`, `ProjectToM`, `SubstringByM` |
| Validity | `IsValid`, `Validate`, `MakeValid` |
| Measures | `Area`, `Length`, `Envelope`, `Centroid`, `Distance`, `NearestPoints`, `IsWithinDistance`, `PointOnSurface`, `HausdorffDistance`, `Polylabel` |
| Constructions | `Boundary`, `Densify`, `Reverse`, `LineMerge`, `Translate`, `Scale`, `Rotate`, `Transform`, `MinimumRotatedRectangle`, `MinimumBoundingCircle`, `ConcaveHull`, `ConcaveHullByRatio`, `DelaunayTriangles` |
| Clean-up | `RemoveHoles`, `RemoveSmallParts`, `SnapToGrid`, `ReducePrecision` |
| Checks | `IsSimple`, `IsClosed`, `IsRing`, `EqualsExact` |
| Conversion | `AsList` (LINQ-friendly views), `ToWkt`, `ToGeoJson`, `ToWkb` |

The signatures, parameters and exceptions of every method are in the API reference, which is generated from the XML comments.

## The playground

The playground is a small web application to try the algorithms on a map:

```bash
dotnet run --project src/SpatialNET.Playground
```

Open `http://localhost:5003`, press Ctrl+K to choose an algorithm, draw inputs (target A/B), upload GeoJSON or pick one of the sample geometries from the bar. The samples are the fixtures with the tag `playground` from the repository folder [`samples/`](https://github.com/jugstalt/SpatialNET.Core/tree/main/samples). Distances are given in the units of the computation CRS, which is chosen automatically (auto-UTM) by default ([ADR 0010](../adr/0010-playground-projektion-serverseitig-auto-utm.md)). Every input also has a WKT field (with Z and M) and every result shows its WKT, which is how the M algorithms get their data (GeoJSON has no M, [ADR 0015](../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)).

## Building the documentation

The documentation site (English and German) is built with [DocFX](https://dotnet.github.io/docfx/), which is pinned as a local tool:

```powershell
./docs/build.ps1          # builds docs/_site/en and docs/_site/de
./docs/build.ps1 -Serve   # additionally serves the result at http://localhost:8080
```

The API reference is generated from the XML comments of the four libraries and exists in English only.
