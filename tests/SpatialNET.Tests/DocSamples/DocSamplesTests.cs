using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.DocSamples;

// Die Code-Beispiele aus README.md und docs/{en,de}/ (getting-started, host-integration).
// Ändert sich ein Beispiel, muss die Dokumentation mitgezogen werden – und umgekehrt.

[Collection(GlobalStateCollection.Name)]
public sealed class DocSamplesTests : IDisposable
{
    private readonly IGeometryFactory? _savedFactory = GeometryFactory.Default;

    public void Dispose() => GeometryFactory.Default = _savedFactory;

    [Fact]
    public void QuickStart()
    {
        // --- snippet: quickstart
        // Once at startup: the factory that algorithms use to create their results.
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

        IPolygon2D overlap = a.Intersection(b);

        Console.WriteLine(overlap.ToWkt());          // POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))
        Console.WriteLine(overlap.Area());           // 25
        Console.WriteLine(a.Intersects(b));          // True
        Console.WriteLine(a.Buffer(1).Area() > 100); // True
        // --- end

        Assert.Equal("POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))", overlap.ToWkt());
        Assert.Equal(25, overlap.Area());
        Assert.True(a.Intersects(b));
        Assert.True(a.Buffer(1).Area() > 100);
    }

    [Fact]
    public void FillRuleAndGrid()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

        // --- snippet: overlay-options
        // Grid size in map units (default: derived automatically from the coordinates).
        IPolygon2D union = a.Union(b, FillRule.NonZero, gridSize: 0.001);

        // Several expensive predicates against the same geometry: prepare it once.
        PreparedGeometry prepared = a.Prepare();
        bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

        // Full DE-9IM: matrix and pattern matching.
        string matrix = a.Relate(b).ToString(); // "212101212"
        // --- end

        Assert.Equal(175, union.Area());
        Assert.True(inside);
        Assert.Equal("212101212", matrix);
        Assert.True(a.Relate(b, "T*T***T**"));
    }

    [Fact]
    public void ReadingAndWriting()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: io
        IGeometry2D g = Wkt.Read("LINESTRING(0 0, 10 10)");
        string json = g.ToGeoJson();
        IReadOnlyList<IGeometry2D> all = GeoJson.ReadAll(json); // also FeatureCollection
        // --- end

        Assert.Single(all);
        Assert.IsAssignableFrom<IPolyline2D>(all[0]);
    }

    [Fact]
    public void HostIntegration()
    {
        // --- snippet: host-integration
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
        // --- end

        Assert.IsType<HostPolygon>(result);
        Assert.Single(result.AsList());
        Assert.Equal(25, result.Area());
    }
}

// --- snippet: host-classes
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
// --- end
