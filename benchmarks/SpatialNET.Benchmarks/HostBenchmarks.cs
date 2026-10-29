using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>
/// The cases of a host benchmark (gView GIS, classic engine against SpatialNET), in UTM metres: a prepared filter polygon against
/// 3,000 features (points, lines of 20 vertices, polygons of 30), as a data source checks a spatial filter.
/// </summary>
[MemoryDiagnoser]
public class HostPredicateBenchmarks
{
    private HostInputs _inputs = null!;
    private PreparedGeometry _filter = null!;

    /// <summary>A simple and a detailed filter polygon (a drawn selection, a district boundary).</summary>
    [Params(50, 2_000)]
    public int FilterVertices { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        _inputs = HostInputs.Create(FilterVertices);
        _filter = _inputs.Filter.Prepare();
    }

    [Benchmark]
    public int ContainsPrepared() => HostInputs.Contains(_filter, _inputs.Features);

    [Benchmark]
    public int IntersectsPrepared() => HostInputs.Intersects(_filter, _inputs.Features);
}

/// <summary>
/// The overlay cases of the host benchmark: <c>UnionAll</c> of overlapping blobs (32 vertices), <c>Clip</c> of a polygon of 5,000
/// vertices and of 2,000 small ones (20 vertices) to a map extent, and the buffer of 100 single points (25 m, 16 segments per quadrant).
/// </summary>
[MemoryDiagnoser]
public class HostOverlayBenchmarks
{
    private HostInputs _inputs = null!;

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        _inputs = HostInputs.Create(50);
    }

    [Benchmark]
    public int UnionAll100() => _inputs.Union100.UnionAll().RingCount;

    [Benchmark]
    public int UnionAll1000() => _inputs.Union1000.UnionAll().RingCount;

    [Benchmark]
    public int ClipLarge() => _inputs.Large.Clip(_inputs.LargeEnvelope).RingCount;

    [Benchmark]
    public int ClipManySmall() => HostInputs.ClipAll(_inputs.Small, _inputs.MapEnvelope);

    [Benchmark]
    public int BufferPoints() => HostInputs.BufferAll(_inputs.Points);
}

/// <summary>The inputs of <see cref="HostPredicateBenchmarks"/> and <see cref="HostOverlayBenchmarks"/>, built like the host's benchmark (same random sequence).</summary>
public sealed class HostInputs
{
    public const double OriginX = 533_000, OriginY = 5_212_000;

    /// <summary>The buffer options of the host: 16 segments per quarter circle.</summary>
    public static readonly BufferOptions BufferSettings = new() { QuadrantSegments = 16 };

    public IPolygon2D Filter { get; private init; } = null!;
    public IGeometry2D[] Features { get; private init; } = null!;
    public IPolygon2D[] Union100 { get; private init; } = null!;
    public IPolygon2D[] Union1000 { get; private init; } = null!;
    public IPolygon2D Large { get; private init; } = null!;
    public Envelope2D LargeEnvelope { get; private init; }
    public IPolygon2D[] Small { get; private init; } = null!;
    public Envelope2D MapEnvelope { get; private init; }
    public IPoint2D[] Points { get; private init; } = null!;

    public static HostInputs Create(int filterVertices)
    {
        const double extent = 10_000;
        var random = new Random(42);
        var filter = Blob(random, OriginX + extent / 2, OriginY + extent / 2, extent * 0.35, filterVertices);
        var features = new List<IGeometry2D>();
        for (int i = 0; i < 1000; i++)
        {
            features.Add(ImpGeometryFactory.Instance.CreatePoint(RandomPoint(random, extent), null));
            var start = RandomPoint(random, extent);
            features.Add(Walk(random, start.X, start.Y, 15, 20));
            var center = RandomPoint(random, extent);
            features.Add(Blob(random, center.X, center.Y, 60, 30));
        }

        random = new Random(42);
        var large = Blob(random, OriginX, OriginY, 5000, 5000);
        var small = Enumerable.Range(0, 2000).Select(_ =>
        {
            var center = RandomPoint(random, extent);
            return Blob(random, center.X, center.Y, 80, 20);
        }).ToArray();

        random = new Random(42);
        var points = Enumerable.Range(0, 100).Select(_ => ImpGeometryFactory.Instance.CreatePoint(RandomPoint(random, extent), null)).ToArray();

        return new HostInputs
        {
            Filter = filter,
            Features = features.ToArray(),
            Union100 = Blobs(100),
            Union1000 = Blobs(1000),
            Large = large,
            LargeEnvelope = new Envelope2D(OriginX - 2000, OriginY - 2000, OriginX + 6000, OriginY + 6000),
            Small = small,
            MapEnvelope = new Envelope2D(OriginX + 2000, OriginY + 2000, OriginX + 8000, OriginY + 8000),
            Points = points,
        };
    }

    public static int Contains(PreparedGeometry filter, IGeometry2D[] features) => features.Count(filter.Contains);

    public static int Intersects(PreparedGeometry filter, IGeometry2D[] features) => features.Count(filter.Intersects);

    public static int ClipAll(IPolygon2D[] polygons, Envelope2D envelope) => polygons.Sum(p => p.Clip(envelope).RingCount);

    public static int BufferAll(IPoint2D[] points) => points.Sum(p => p.Buffer(25, BufferSettings).RingCount);

    /// <summary>Overlapping blobs; the extent grows with the count, so that the density stays the same.</summary>
    private static IPolygon2D[] Blobs(int count)
    {
        var random = new Random(42);
        double extent = 100 * Math.Sqrt(count);
        return Enumerable.Range(0, count).Select(_ =>
        {
            var center = RandomPoint(random, extent);
            return Blob(random, center.X, center.Y, 40, 32);
        }).ToArray();
    }

    /// <summary>A star-shaped, irregular polygon around a centre.</summary>
    private static IPolygon2D Blob(Random random, double centerX, double centerY, double radius, int vertices)
    {
        var ring = new Coordinate[vertices + 1];
        for (int i = 0; i < vertices; i++)
        {
            double angle = 2 * Math.PI * i / vertices;
            double r = radius * (0.7 + 0.3 * random.NextDouble());
            ring[i] = new Coordinate(centerX + r * Math.Cos(angle), centerY + r * Math.Sin(angle));
        }

        ring[vertices] = ring[0];
        return ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring)], null);
    }

    /// <summary>A random walk.</summary>
    private static IPolyline2D Walk(Random random, double startX, double startY, double step, int vertices)
    {
        var path = new Coordinate[vertices];
        double x = startX, y = startY, heading = 0;
        for (int i = 0; i < vertices; i++)
        {
            path[i] = new Coordinate(x, y);
            heading += (random.NextDouble() - 0.5) * 1.2;
            x += step * Math.Cos(heading);
            y += step * Math.Sin(heading);
        }

        return ImpGeometryFactory.Instance.CreatePolyline([path], null);
    }

    private static Coordinate RandomPoint(Random random, double extent) =>
        new(OriginX + random.NextDouble() * extent, OriginY + random.NextDouble() * extent);
}
