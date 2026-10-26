# Host integration

SpatialNET works exclusively through interfaces. A **host** – an existing GIS application with its own geometry classes – integrates it in three steps, without converting its geometries:

1. Let the host geometry classes implement the **geometry interfaces** of `SpatialNET.Core`.
2. Provide an **`IGeometryFactory`** that creates result geometries as host classes from coordinates.
3. Set `GeometryFactory.Default` once at startup (or pass the factory to each call).

From then on, every algorithm in `SpatialNET.Extensions` works directly on the host geometries and returns host geometries.

## The geometry interfaces

All interfaces are read-only and minimal ([ADR 0002](../adr/0002-geometrie-interfaces-count-indexer-flache-ringe.md)):

| Interface | Members | Meaning |
|---|---|---|
| `IGeometry2D` | – | Empty base type of all geometries |
| `IPoint2D` | `X`, `Y` | A point |
| `IPoint3D : IPoint2D` | `Z` | A point with height; 3D exists only on points |
| `IPointM : IPoint2D` | `M` | A point with a measure; independent of `IPoint3D`, a point may implement both |
| `IPointCollection2D` | `PointCount`, indexer | Ordered points; base of the following three |
| `IPath2D : IPointCollection2D` | – | A path (line string), part of a polyline |
| `IRing2D : IPath2D` | – | A closed path, part of a polygon |
| `IMultiPoint2D : IGeometry2D, IPointCollection2D` | – | A set of points |
| `IPolyline2D : IGeometry2D` | `PathCount`, indexer | One or more paths (corresponds to MultiLineString) |
| `IPolygon2D : IGeometry2D` | `RingCount`, indexer | A flat list of rings (corresponds to MultiPolygon) |

Things to know:

- **Rings.** A polygon is a flat list of rings without a hole marker. Its area is defined by the even-odd rule over all rings; shells and holes follow from nesting, and the ring orientation is ignored on input. The closing point of a ring (equal to the start point) may be repeated or omitted ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)).
- **No `IReadOnlyList<T>` on purpose.** Point sequences use `PointCount` plus an indexer. This avoids a second `IEnumerable<T>` on classes that already enumerate their points, which would make LINQ ambiguous. For LINQ and `foreach`, `SpatialNET.Extensions` offers list views: `points.AsList()`, `polyline.AsList()`, `polygon.AsList()`. A host may additionally implement `IReadOnlyList<T>` and is then passed through directly.
- **Further members** such as envelope, area or `IsEmpty` are not part of the interfaces. They are extension methods, so they do not collide with members of the same name on the host classes.
- **Z.** Algorithms interpolate Z at new vertices if the points involved are `IPoint3D`. Hosts that always carry a height simply return `Z = 0`.
- **M.** Algorithms carry M and interpolate it at new vertices (like Z) if the points involved are `IPointM`; they never compute with it ([ADR 0015](../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)). A class that implements `IPointM` reports its `M` as valid, including 0; `NaN` means "no M". A host that cannot tell "no M" from 0 (for example a point class that always stores `M = 0`) must **not** implement `IPointM` on that class, but on an M-capable subclass or wrapper that it uses where the geometry has M.
- **Empty geometries** are geometries with 0 points, paths or rings.

Implementing the interfaces is usually a few lines per class: either directly on the host classes, or – if the host has its own interfaces – on those, using default interface members for indexers whose return type differs. A minimal host looks like this:

```csharp
// A minimal host: mutable classes that implement the SpatialNET interfaces.
public sealed class HostPoint(double x, double y) : IPoint3D
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Z { get; set; } // hosts that always carry a height simply return 0
}

public sealed class HostRing(params HostPoint[] points) : IRing2D
{
    public int PointCount => points.Length;

    public IPoint2D this[int index] => points[index];
}

public sealed class HostPolygon(params HostRing[] rings) : IPolygon2D
{
    public int RingCount => rings.Length;

    public IRing2D this[int index] => rings[index];
}
```

## The geometry factory

Algorithms never construct geometries themselves. They hand coordinates to the factory ([ADR 0004](../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md)):

| Member | Creates |
|---|---|
| `SupportsZ` | – (whether created geometries are meant to carry Z) |
| `SupportsM` | – (whether created geometries are meant to carry M; default member, `false`) |
| `CreatePoint(Coordinate, source)` | a point |
| `CreateMultiPoint(ReadOnlySpan<Coordinate>, source)` | a multipoint; empty without coordinates |
| `CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>>, source)` | a polyline from paths; empty without paths |
| `CreatePolygon(ReadOnlySpan<PolygonPart>, source)` | a polygon from parts; empty without parts |

A `PolygonPart` is a shell (closed, counter-clockwise) with the holes that lie directly in it (closed, clockwise). The result is already valid, so the host does not have to re-derive holes.

The contract for implementations:

- **Thread-safe or stateless.** `GeometryFactory.Default` is used by all threads.
- **Copy buffers.** Passed buffers must not be referenced after the call.
- **Empty means empty.** Empty input produces empty geometries, never `null`.
- **Z may be dropped.** If `SupportsZ` is `false`, algorithms produce no Z at all.
- **M may be dropped.** If `SupportsM` is `false` (the default of the interface), algorithms produce no M at all.
- **`source`** is the primary input geometry of the algorithm (or `null`). The host can take metadata such as the spatial reference from it, without SpatialNET knowing it.

Results are dimension-homogeneous, so there is no `CreateGeometryCollection`. A host without a class for some kind (here: multipoint and polyline) can throw `NotSupportedException` in the corresponding method; algorithms that would need it then fail clearly instead of returning something else.

```csharp
// The factory receives coordinates only and creates the host classes from them.
public sealed class HostGeometryFactory : IGeometryFactory
{
    public bool SupportsZ => false;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) =>
        new HostPoint(coordinate.X, coordinate.Y);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no multipoint class.");

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no polyline class.");

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<HostRing>();
        foreach (var part in parts)
        {
            rings.Add(ToRing(part.Shell.Span));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(ToRing(hole.Span));
            }
        }

        return new HostPolygon(rings.ToArray());
    }

    private static HostRing ToRing(ReadOnlySpan<Coordinate> coordinates)
    {
        var points = new HostPoint[coordinates.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new HostPoint(coordinates[i].X, coordinates[i].Y);
        }

        return new HostRing(points);
    }
}
```

## Putting it together

```csharp
// 1. Your own geometry classes implement the read-only interfaces ...
var square = new HostPolygon(
    new HostRing(new HostPoint(0, 0), new HostPoint(10, 0), new HostPoint(10, 10), new HostPoint(0, 10)));
var other = new HostPolygon(
    new HostRing(new HostPoint(5, 5), new HostPoint(15, 5), new HostPoint(15, 15), new HostPoint(5, 15)));

// 2. ... and a factory creates the results as your own classes.
GeometryFactory.Default = new HostGeometryFactory();

IPolygon2D result = square.Intersection(other);

Console.WriteLine(result is HostPolygon);                            // True
Console.WriteLine(result.AsList().Count);                            // 1 ring
```

`GeometryFactory.Default` is a plain static property that is meant to be set once at startup. To use a different factory for a single call, pass it explicitly: `square.Intersection(other, factory: myFactory)`.

## Threads and mutable geometries

Host geometries are usually mutable; the library treats them as values for the length of a call.

- **During a call** an algorithm reads your geometry. Do not change that instance from another thread while an algorithm is running on it. Reading the same instance from many threads is fine, as long as your own classes allow concurrent reads.
- **`PreparedGeometry` and `SpatialIndex<T>` copy** what they need (a snapshot of the coordinates, the envelopes), so later changes to the host object do not affect them; build a new one to see changes. Both are **thread-safe** after construction, so build them once and share them ([ADR 0014](../adr/0014-oeffentlicher-raeumlicher-index-statisch-str.md), [ADR 0024](../adr/0024-prepared-geometry-thread-sicher-und-distanz.md)).
- **The factory** is called from whatever thread runs the algorithm, so it must be thread-safe or stateless (see above).
- **Results are new objects.** An algorithm never returns an input instance and never changes its input, so you can keep the input and the result side by side.

## Units, coordinate systems and the grid

The algorithms are **planar**: they compute on the numbers X and Y, whatever they mean. Distances and areas come out in the units of the coordinates, so use a projected coordinate system (metres) for anything that measures: a buffer of `10` around longitude/latitude data is 10 degrees. Z and M never take part in a computation.

Overlays round their result to a **grid** ([ADR 0005](../adr/0005-overlay-winding-number-snap-rounding.md)). By default the grid size is derived from the largest coordinate of the input, which suits most data. Set `Precision.DefaultGridSize` once if your data has a natural resolution (for example 0.001 m), or pass `gridSize` to a single call; see [Options and precision](getting-started.md#options-and-precision). Predicates and measures compute on the exact input coordinates and use no grid.

## Errors and edge cases

All algorithms follow the same rules ([ADR 0012](../adr/0012-api-signaturen-typisierte-overloads.md)), so you can rely on them in one place:

| Situation | Result |
|---|---|
| An argument is `null` | `ArgumentNullException` |
| A parameter is out of range (a negative distance, a grid size that is not finite or not greater than 0, a `NaN` tolerance) | `ArgumentOutOfRangeException` |
| No factory passed and `GeometryFactory.Default` is not set | `InvalidOperationException` |
| An object implements `IGeometry2D` but none of the four kinds (point, multipoint, polyline, polygon) | `NotSupportedException` |
| A geometry is empty or invalid | never an exception: an empty result, 0 for area and length, `NaN` for a distance, `null` where there is no point, `false` for a predicate |
| A coordinate is `NaN` or infinite | `IsValid` and `Validate` report it; `MakeValid` removes it; other algorithms are not defined on it |
| An invalid polygon is the input | the result follows the even-odd rule over all rings; check `IsValid` where you need a guarantee ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)) |

## Choosing the right tool

| You do this many times | Use |
|---|---|
| Test one geometry against many others | `Prepare()`, then query the prepared geometry |
| Look up items by place in a large set | `SpatialIndex<T>`: `Query` for a rectangle, `Nearest` for the closest |
| Pair the items of two sets that meet | `Join` and `SelfJoin` on two indexes |
| Merge many polygons, lines or points | `UnionAll`, not a loop of `Union` |
| Show geometry at a small scale | `Simplify`, `Clip` |
| Keep user input clean | `Validate` and `MakeValid` |
| Give a road its kilometres, and ask by kilometre or by click | `CalibrateM` once, then `ProjectToM`, `LocateAlong`, `LocateBetween` |
| Combine tables of route events (speed limit, surface, bridges) | `LineEvents` and `PointEvents`, then `LocateEvents` to draw them |
| Heights, slopes, contour lines or a height profile | one `Triangulation`, asked many times |
| Which point is the nearest, as areas | `VoronoiDiagram`, cut to a boundary |

## Reading and writing

`SpatialNET.IO` reads and writes WKT, WKB (ISO and EWKB, with Z and M) and GeoJSON. Every `Read` takes an optional factory: pass yours, and what you read is already a host geometry, without a conversion step. For the use cases see the [Cookbook](cookbook.md).

## Checklist

- The host geometry classes implement the interfaces; `PointCount` and `RingCount` and the indexers are cheap (they are called in loops).
- `IPoint3D` only where Z is real; `IPointM` only where "no M" can be told from 0.
- A factory that creates your classes, thread-safe, copies its buffers, creates empty geometries for empty input.
- `GeometryFactory.Default` set once at startup.
- Coordinates in a projected system for measuring; `Precision.DefaultGridSize` set if your data has a natural resolution.
- `Prepare()` and `SpatialIndex<T>` where the same geometry or set is queried repeatedly.
- The foreign-geometry tests of the repository run against your classes (next section).

## Checking your integration

The repository contains a deliberately awkward host-style implementation in the test project (`tests/SpatialNET.Tests/Foreign/ForeignGeometries.cs`): class-based indexers, rings without closing point, shells clockwise, additionally `IReadOnlyList<T>`, and every point as `IPoint3D` with `Z = 0`. Every fixture test runs against it, which is how assumptions about concrete types are caught ([ADR 0008](../adr/0008-teststrategie-fixtures-nts-orakel.md)). It is a good model for what a real host looks like.
