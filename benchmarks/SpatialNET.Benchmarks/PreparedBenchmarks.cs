using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>
/// <see cref="PreparedGeometry"/> (ADR 0013, ADR 0024): preparing a polygon of many vertices, and repeated predicates, point
/// lookups and distances against it, compared with the unprepared methods.
/// </summary>
[MemoryDiagnoser]
public class PreparedBenchmarks
{
    private IPolygon2D _polygon = null!;
    private PreparedGeometry _prepared = null!;
    private Coordinate[] _points = null!;
    private IPolyline2D[] _probes = null!;

    [Params(1_000, 100_000)]
    public int Vertices { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_polygon, _points, _probes) = PreparedInputs.Create(Vertices);
        _prepared = _polygon.Prepare();
    }

    [Benchmark]
    public PreparedGeometry Prepare() => _polygon.Prepare();

    [Benchmark]
    public int LocatePrepared()
    {
        int inside = 0;
        foreach (var p in _points) if (_prepared.Locate(p) == Location.Inside) inside++;
        return inside;
    }

    [Benchmark]
    public int IntersectsPrepared()
    {
        int hits = 0;
        foreach (var probe in _probes) if (_prepared.Intersects(probe)) hits++;
        return hits;
    }

    [Benchmark]
    public int IntersectsUnprepared()
    {
        int hits = 0;
        foreach (var probe in _probes) if (_polygon.Intersects(probe)) hits++;
        return hits;
    }
}

internal static class PreparedInputs
{
    /// <summary>A rough, star-shaped polygon with many vertices, points around it and short probe lines.</summary>
    public static (IPolygon2D Polygon, Coordinate[] Points, IPolyline2D[] Probes) Create(int vertices)
    {
        var rnd = new Random(42);
        var ring = new Coordinate[vertices + 1];
        for (int i = 0; i < vertices; i++)
        {
            double angle = 2 * Math.PI * i / vertices;
            double radius = 1000 * (0.85 + 0.15 * rnd.NextDouble());
            ring[i] = new Coordinate(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        ring[vertices] = ring[0];
        var polygon = ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring)], null);
        var points = Enumerable.Range(0, 2_000).Select(_ => new Coordinate(rnd.NextDouble() * 2200 - 1100, rnd.NextDouble() * 2200 - 1100)).ToArray();
        var probes = Enumerable.Range(0, 500).Select(_ =>
        {
            double x = rnd.NextDouble() * 2200 - 1100, y = rnd.NextDouble() * 2200 - 1100;
            return ImpGeometryFactory.Instance.CreatePolyline([new[] { new Coordinate(x, y), new Coordinate(x + 30, y + 15) }], null);
        }).ToArray();
        return (polygon, points, probes);
    }
}
