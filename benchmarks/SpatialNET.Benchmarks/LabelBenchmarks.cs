using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>
/// Label points for map labelling: <see cref="Shapes.FastPolygonLabel"/> and <see cref="Shapes.FastAlternativePolygonLabelsLazy"/>
/// on smooth, concave polygons (the shape of the gView label benchmark), compared with the centroid plus an inside test and with
/// <see cref="Shapes.Polylabel"/> (the cost of a best-first search over quadtree cells by distance to the boundary).
/// </summary>
[MemoryDiagnoser]
public class LabelBenchmarks
{
    private IPolygon2D _blob = null!, _holed = null!;
    private Coordinate _seed;
    private AlternativeLabelOptions _options = null!;

    [Params(1_000, 12_000, 100_000)]
    public int Vertices { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        (_blob, _holed) = Create(Vertices);
        _seed = _blob.FastPolygonLabel()!.Value;
        _options = new AlternativeLabelOptions { Separation = 50, MinClearance = 0 };
    }

    [Benchmark(Baseline = true)]
    public Coordinate? FastLabel() => _blob.FastPolygonLabel();

    /// <summary>The centroid lies in a hole: the scanline fallback.</summary>
    [Benchmark]
    public Coordinate? FastLabelCentroidInHole() => _holed.FastPolygonLabel();

    [Benchmark]
    public bool CentroidAndLocate() => _blob.Centroid() is { } c && _blob.Locate(c) == Location.Inside;

    [Benchmark]
    public Coordinate AlternativeFirst() => _blob.FastAlternativePolygonLabelsLazy(_seed, _options).First();

    [Benchmark]
    public int AlternativeFirstFour()
    {
        int n = 0;
        foreach (var _ in _blob.FastAlternativePolygonLabelsLazy(_seed, _options))
        {
            if (++n == 4) break;
        }

        return n;
    }

    [Benchmark]
    public Coordinate? Polylabel() => _blob.Polylabel();

    public static (IPolygon2D Blob, IPolygon2D Holed) Create(int n)
    {
        var f = ImpGeometryFactory.Instance;
        const double cx = 533_000, cy = 5_212_000;
        var random = new Random(42);
        double ph1 = random.NextDouble() * 2 * Math.PI, ph2 = random.NextDouble() * 2 * Math.PI;
        var pts = new Coordinate[n + 1];
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            double r = 5000 * (1 + 0.15 * Math.Sin(3 * t + ph1) + 0.08 * Math.Sin(7 * t + ph2) + 0.002 * (random.NextDouble() - 0.5));
            pts[i] = new Coordinate(cx + r * Math.Cos(t), cy + r * Math.Sin(t));
        }

        pts[n] = pts[0];
        var blob = f.CreatePolygon([new PolygonPart(pts)], null);
        var c = blob.Centroid()!.Value;
        var hole = new Coordinate[65];
        for (int i = 0; i < 64; i++)
        {
            double t = -2 * Math.PI * i / 64;
            hole[i] = new Coordinate(c.X + 800 * Math.Cos(t), c.Y + 800 * Math.Sin(t));
        }

        hole[64] = hole[0];
        return (blob, f.CreatePolygon([new PolygonPart(pts, new ReadOnlyMemory<Coordinate>[] { hole })], null));
    }
}
