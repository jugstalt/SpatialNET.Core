using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>
/// <see cref="Overlays.Clip(IPolygon2D, Envelope2D, IGeometryFactory?, double?)"/> of small polygons (as in a label renderer that
/// clips every feature to the map extent): the border of the polygon in the window, the window cutting it in half, and the
/// polygon completely visible. <c>Overlay</c> is the clip through the overlay without the rectangle fast path (the former path).
/// </summary>
[MemoryDiagnoser]
public class SmallClipBenchmarks
{
    private IPolygon2D _polygon = null!;
    private Envelope2D _box;

    [Params(8, 50, 200)]
    public int Vertices { get; set; }

    [Params("Border", "Half", "Visible")]
    public string Case { get; set; } = "Border";

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        (_polygon, _box) = Create(Vertices, Case);
    }

    [Benchmark(Baseline = true)]
    public IPolygon2D Overlay() => Case == "Visible" ? _polygon.Clip(_box) : Overlays.ClipByOverlay(_polygon, _box, ImpGeometryFactory.Instance);

    [Benchmark]
    public IPolygon2D Clip() => _polygon.Clip(_box);

    public static (IPolygon2D Polygon, Envelope2D Box) Create(int n, string @case)
    {
        const double cx = 533_000, cy = 5_212_000, radius = 100;
        var rnd = new Random(7);
        var pts = new Coordinate[n + 1];
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            double r = radius * (1 + 0.1 * Math.Sin(3 * t) + 0.02 * (rnd.NextDouble() - 0.5));
            pts[i] = new Coordinate(cx + r * Math.Cos(t), cy + r * Math.Sin(t));
        }

        pts[n] = pts[0];
        var polygon = ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(pts)], null);
        var box = @case switch
        {
            "Border" => new Envelope2D(cx + 60, cy - 30, cx + 200, cy + 50),     // only the right border region in the window
            "Half" => new Envelope2D(cx - 300, cy - 300, cx + 10, cy + 300),     // the window cuts the polygon in the middle
            _ => new Envelope2D(cx - 300, cy - 300, cx + 300, cy + 300),          // completely visible
        };
        return (polygon, box);
    }
}
