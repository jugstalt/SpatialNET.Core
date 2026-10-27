# SpatialNET

*[Deutsche Fassung](README.de.md)*

SpatialNET is a .NET library for planar spatial computations: overlays (intersection, union, difference), buffer and offset curves, cut, simplify, spatial predicates (DE-9IM), validity checks and measures, linear referencing, and triangulations (Delaunay, Voronoi, terrain models with contour lines, slope, volume and smoothing).

- **Own algorithms, no third-party engine.** Everything is implemented in-house; there is no dependency on NetTopologySuite, Clipper or similar ([ADR 0001](docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.md)). NetTopologySuite appears only in the test project, as a comparison oracle.
- **Interfaces instead of geometry classes.** The algorithms are extension methods on small read-only interfaces (`IPolygon2D`, `IPolyline2D`, …), so existing GIS software such as gView or WebGIS can use them through its own geometry classes. Results are created through a geometry factory that the host provides.
- **Robust by construction.** Overlays snap-round to a grid and use exact predicates; results are valid by construction ([ADR 0005](docs/adr/0005-overlay-winding-number-snap-rounding.md)).

## Packages

| Package | Contents |
|---|---|
| `SpatialNET.Core` | Geometry interfaces, value types (`Coordinate`, `Envelope2D`), geometry factory contract |
| `SpatialNET.Extensions` | The algorithms as extension methods: overlays, buffer, offset curve, cut, simplify, predicates and `Relate`, `IsValid`/`Validate`, `Prepare`, `SpatialIndex<T>`, measures, linear referencing and route events, triangulation, Voronoi and terrain analysis |
| `SpatialNET.Imp` | Reference implementation of the geometry interfaces and the factory, for tests, tools and hosts without geometries of their own |
| `SpatialNET.IO` | WKT, GeoJSON and WKB reading and writing through the geometry factory |

## Quick start

> SpatialNET is a pre-release and is on NuGet; install the packages with `--prerelease`, for example `dotnet add package SpatialNET.Extensions --prerelease` (plus `SpatialNET.Imp` and `SpatialNET.IO` as needed). You can also clone the repository and reference the projects.

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

Hosts implement the interfaces on their own geometry classes and provide their own factory, see [Host integration](docs/en/host-integration.md).

## Documentation

- [Getting started](docs/en/getting-started.md) and [host integration](docs/en/host-integration.md)
- [Concepts](docs/en/concepts/geometry-model.md): geometry model, [overlays and the grid](docs/en/concepts/overlay-and-grid.md), [predicates](docs/en/concepts/predicates.md), [buffer and offset](docs/en/concepts/buffer-and-offset.md), [simplify and validation](docs/en/concepts/simplify-and-validation.md), [cut](docs/en/concepts/cut.md), [spatial index](docs/en/concepts/spatial-index.md), [linear referencing](docs/en/concepts/linear-referencing.md), [event tables](docs/en/concepts/event-tables.md), [triangulation, Voronoi and terrain](docs/en/concepts/triangulation-and-voronoi.md), [measures and constructions](docs/en/concepts/measures-and-constructions.md)
- [Glossary](docs/en/glossary.md) (English ↔ German terms)
- API reference: generated from the XML comments with DocFX, build it with `docs/build.ps1` (see [docs/en/getting-started.md](docs/en/getting-started.md#building-the-documentation))
- [Architecture decision records](docs/adr/) – the reasoning behind the design (English, with German versions)
- [Research notes](research/) on the algorithm choices

## Playground

An interactive web application for trying the algorithms on a map (ASP.NET Core + Leaflet):

```bash
dotnet run --project src/SpatialNET.Playground
```

Then open `http://localhost:5003`. The sample geometries come from [samples/](samples/README.md), the same fixtures the tests use.

## Quality

The test suite (xUnit, about 7,800 tests) compares every algorithm against NetTopologySuite as an oracle on shared fixtures and runs each fixture against the reference implementation and against a deliberately awkward host-style implementation ([ADR 0008](docs/adr/0008-teststrategie-fixtures-nts-orakel.md)). Benchmarks are in [benchmarks/](benchmarks/).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, testing and what a pull request contains; releases are described in [RELEASING.md](RELEASING.md). Changes are listed in the [changelog](CHANGELOG.md).

## License

SpatialNET is licensed under the [Apache License 2.0](LICENSE). Third-party components, algorithm sources and test data are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
