using BenchmarkDotNet.Attributes;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>Layouts of the polygons that <c>UnionAll</c> dissolves.</summary>
public enum UnionLayout { Disjoint, Overlapping }

/// <summary>
/// <c>UnionAll</c> (ADR 0018) against the fold <c>Aggregate(Union)</c> and the NTS cascaded union: many squares that
/// are far apart, or squares that overlap their neighbours so that the result is one large area.
/// </summary>
[MemoryDiagnoser]
public class UnionAllBenchmarks
{
    private IPolygon2D[] _polygons = null!;
    private Geometry[] _nts = null!;

    [Params(UnionLayout.Disjoint, UnionLayout.Overlapping)]
    public UnionLayout Layout { get; set; }

    [Params(100, 1_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        _polygons = Create(Layout, N);
        _nts = _polygons.Select(p => Inputs.ToNts(p)).ToArray();
    }

    /// <summary>Squares on a grid; disjoint ones have gaps, overlapping ones reach into their neighbours.</summary>
    public static IPolygon2D[] Create(UnionLayout layout, int n)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(n));
        double step = 10, size = layout == UnionLayout.Disjoint ? 6 : 14;
        return Enumerable.Range(0, n).Select(i =>
        {
            double x = i % side * step, y = i / side * step;
            var ring = new Coordinate[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size), new(x, y) };
            return ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring)], null);
        }).ToArray();
    }

    [Benchmark(Baseline = true)]
    public IPolygon2D Fold() => _polygons.Aggregate((acc, p) => acc.Union(p));

    [Benchmark]
    public IPolygon2D UnionAll() => _polygons.UnionAll();

    [Benchmark]
    public Geometry? NtsCascaded() => CascadedPolygonUnion.Union(_nts.ToList());
}
