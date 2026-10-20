using BenchmarkDotNet.Attributes;
using SpatialNET.Imp;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SpatialNET.Benchmarks;

/// <summary>Overlays von Polygonen; NTS (OverlayNG mit Fließkomma-Präzision) als Vergleich.</summary>
[MemoryDiagnoser]
public class OverlayBenchmarks
{
    private IPolygon2D _a = null!, _b = null!;
    private NtsGeometry _ntsA = null!, _ntsB = null!;

    [Params(Case.Overlap, Case.Disjoint, Case.Contained, Case.Edge, Case.Islands)]
    public Case Case { get; set; }

    [Params(1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        (_a, _b) = Inputs.Create(Case, N);
        _ntsA = Inputs.ToNts(_a);
        _ntsB = Inputs.ToNts(_b);
    }

    [Benchmark]
    public IPolygon2D Intersection() => _a.Intersection(_b);

    [Benchmark]
    public IPolygon2D Union() => _a.Union(_b);

    [Benchmark]
    public IPolygon2D Difference() => _a.Difference(_b);

    [Benchmark]
    public NtsGeometry NtsIntersection() => _ntsA.Intersection(_ntsB);

    [Benchmark]
    public NtsGeometry NtsUnion() => _ntsA.Union(_ntsB);

    [Benchmark]
    public NtsGeometry NtsDifference() => _ntsA.Difference(_ntsB);
}
