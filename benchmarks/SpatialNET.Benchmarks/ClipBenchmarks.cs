using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>
/// <see cref="Overlays.Clip(IPolygon2D, Envelope2D, IGeometryFactory?, double?)"/> of smooth, concave polygons with many vertices
/// (about 5 km radius) to a rectangle that cuts them in half; one ring, and one ring with holes.
/// </summary>
[MemoryDiagnoser]
public class ClipBenchmarks
{
    private IPolygon2D _single = null!, _holes = null!;
    private Envelope2D _box;

    [Params(1_000, 12_000, 100_000)]
    public int Vertices { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        (_single, _holes, _box) = Create(Vertices);
    }

    [Benchmark]
    public IPolygon2D ClipSingleRing() => _single.Clip(_box);

    [Benchmark]
    public IPolygon2D ClipWithHoles() => _holes.Clip(_box);

    public static (IPolygon2D Single, IPolygon2D Holes, Envelope2D Box) Create(int n)
    {
        var f = ImpGeometryFactory.Instance;
        const double cx = 533_000, cy = 5_212_000, radius = 5_000;
        var rnd = new Random(42);
        double ph1 = rnd.NextDouble() * 2 * Math.PI, ph2 = rnd.NextDouble() * 2 * Math.PI;
        Coordinate[] Ring(double scale, int count, double dx, double dy, bool closeIt = true)
        {
            var pts = new Coordinate[count + 1];
            for (int i = 0; i < count; i++)
            {
                double t = 2 * Math.PI * i / count;
                double r = scale * (1 + 0.15 * Math.Sin(3 * t + ph1) + 0.08 * Math.Sin(7 * t + ph2) + 0.002 * (rnd.NextDouble() - 0.5));
                pts[i] = new Coordinate(cx + dx + r * Math.Cos(t), cy + dy + r * Math.Sin(t));
            }

            pts[count] = pts[0];
            return pts;
        }

        var single = f.CreatePolygon([new PolygonPart(Ring(radius, n, 0, 0))], null);
        var holes = f.CreatePolygon([new PolygonPart(Ring(radius, n, 0, 0),
        new ReadOnlyMemory<Coordinate>[]
        {
            Ring(300, Math.Max(16, n / 10), -1500, 0), Ring(300, Math.Max(16, n / 10), 1500, 500), Ring(300, Math.Max(16, n / 10), 0, -1200),
            Ring(300, Math.Max(16, n / 10), 0, 1200),
        })], null);
        return (single, holes, new Envelope2D(cx, cy - 6_000, cx + 6_000, cy + 6_000));
    }
}
