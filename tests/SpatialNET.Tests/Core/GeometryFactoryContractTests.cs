using SpatialNET.Imp;
using SpatialNET.Tests.Foreign;

namespace SpatialNET.Tests.Core;

/// <summary>
/// Vertragstest für Geometrie-Factories (ADR 0004/0008); läuft für jede Factory in den Tests.
/// Ringe werden ohne Schlusspunkt und ohne Richtung verglichen, weil Hosts beides frei wählen dürfen.
/// </summary>
public abstract class GeometryFactoryContractTests
{
    protected abstract IGeometryFactory Factory { get; }

    private static readonly Coordinate[] Square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)];
    private static readonly Coordinate[] Hole = [new(1, 1), new(1, 2), new(2, 2), new(2, 1), new(1, 1)];

    [Fact]
    public void Creates_a_point()
    {
        var p = Factory.CreatePoint(new Coordinate(1, 2), null);

        Assert.Equal((1d, 2d), (p.X, p.Y));
    }

    [Fact]
    public void Creates_empty_geometries_instead_of_null()
    {
        Assert.Equal(0, Factory.CreateMultiPoint([], null).PointCount);
        Assert.Equal(0, Factory.CreatePolyline([], null).PathCount);
        Assert.Equal(0, Factory.CreatePolygon([], null).RingCount);
    }

    [Fact]
    public void Creates_a_polyline_with_all_paths()
    {
        ReadOnlyMemory<Coordinate>[] paths = [new Coordinate[] { new(0, 0), new(1, 1) }, new Coordinate[] { new(2, 2), new(3, 3), new(4, 2) }];

        var line = Factory.CreatePolyline(paths, null);

        Assert.Equal(2, line.PathCount);
        Assert.Equal([(2d, 2d), (3d, 3d), (4d, 2d)], Coordinates(line[1]).AsEnumerable());
    }

    [Fact]
    public void Creates_a_polygon_with_shell_and_holes_as_flat_ring_list()
    {
        PolygonPart[] parts = [new(Square, new ReadOnlyMemory<Coordinate>[] { Hole })];

        var polygon = Factory.CreatePolygon(parts, null);

        Assert.Equal(2, polygon.RingCount);
        Assert.Equal(RingSet(Square), RingSet(polygon[0]));
        Assert.Equal(RingSet(Hole), RingSet(polygon[1]));
    }

    [Fact]
    public void Does_not_keep_references_to_passed_buffers()
    {
        var coordinates = new Coordinate[] { new(0, 0), new(1, 1) };
        var multiPoint = Factory.CreateMultiPoint(coordinates, null);
        var line = Factory.CreatePolyline(new ReadOnlyMemory<Coordinate>[] { coordinates }, null);

        coordinates[0] = new Coordinate(99, 99);

        Assert.Equal(0, multiPoint[0].X);
        Assert.Equal(0, line[0][0].X);
    }

    [Fact]
    public void Keeps_Z_when_Z_is_supported()
    {
        if (!Factory.SupportsZ)
        {
            return; // Z darf bei SupportsZ = false verworfen werden.
        }

        var p = Factory.CreatePoint(new Coordinate(1, 2, 3), null);

        Assert.Equal(3, Assert.IsAssignableFrom<IPoint3D>(p).Z);
    }

    [Fact]
    public void Is_thread_safe()
    {
        var results = new IMultiPoint2D[1000];

        Parallel.For(0, results.Length, i =>
            results[i] = Factory.CreateMultiPoint([new Coordinate(i, i), new Coordinate(i + 1, i)], null));

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(i, results[i][0].X);
            Assert.Equal(i + 1, results[i][1].X);
        }
    }

    private static (double, double)[] Coordinates(IPointCollection2D points) =>
        points.AsList().Select(p => (p.X, p.Y)).ToArray();

    private static (double, double)[] RingSet(IPointCollection2D ring) => RingSet(Coordinates(ring));

    private static (double, double)[] RingSet(Coordinate[] ring) => RingSet(ring.Select(c => (c.X, c.Y)).ToArray());

    private static (double, double)[] RingSet((double, double)[] ring)
    {
        var open = ring.Length > 1 && ring[0] == ring[^1] ? ring[..^1] : ring;
        return open.Order().ToArray();
    }
}

public sealed class ImpGeometryFactoryContractTests : GeometryFactoryContractTests
{
    protected override IGeometryFactory Factory => ImpGeometryFactory.Instance;

    [Fact]
    public void Without_Z_support_creates_points_without_Z()
    {
        var p = new ImpGeometryFactory(supportsZ: false).CreatePoint(new Coordinate(1, 2, 3), null);

        Assert.IsNotAssignableFrom<IPoint3D>(p);
    }
}

public sealed class ForeignGeometryFactoryContractTests : GeometryFactoryContractTests
{
    protected override IGeometryFactory Factory => ForeignGeometryFactory.Instance;

    [Fact]
    public void Copies_metadata_from_the_source_geometry()
    {
        var source = new Foreign.Point(0, 0) { SrsId = 31256 };

        var result = (Shape)Factory.CreatePoint(new Coordinate(1, 1), source);

        Assert.Equal(31256, result.SrsId);
    }

    [Fact]
    public void Stores_rings_open_and_with_shell_clockwise()
    {
        Coordinate[] ccw = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)];

        var ring = Factory.CreatePolygon([new PolygonPart(ccw)], null)[0];

        Assert.Equal(4, ring.PointCount);
        Assert.True(SignedArea(ring) < 0, "Außenring muss im Uhrzeigersinn gespeichert sein.");
    }

    private static double SignedArea(IRing2D ring)
    {
        double sum = 0;
        for (int i = 0, n = ring.PointCount; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }
}
