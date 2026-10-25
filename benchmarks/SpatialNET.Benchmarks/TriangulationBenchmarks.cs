using BenchmarkDotNet.Attributes;
using NetTopologySuite.Triangulate;
using SpatialNET.Imp;

namespace SpatialNET.Benchmarks;

/// <summary>The layout of the points of a triangulation benchmark.</summary>
public enum PointLayout
{
    Uniform,
    Grid,
    Clustered,
}

/// <summary>
/// <c>DelaunayTriangles</c> and <c>ConcaveHullByRatio</c> (ADR 0029) against the NTS <c>DelaunayTriangulationBuilder</c> and
/// <c>ConcaveHull</c>, for points spread evenly, on a regular grid (many points on a common circle) and in clusters.
/// </summary>
[MemoryDiagnoser]
public class TriangulationBenchmarks
{
    private IGeometry2D _points = null!;
    private NetTopologySuite.Geometries.Geometry _ntsPoints = null!;

    [Params(PointLayout.Uniform, PointLayout.Grid, PointLayout.Clustered)]
    public PointLayout Layout { get; set; }

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_points, _ntsPoints) = Create(Layout, Count);
        GeometryFactory.Default = ImpGeometryFactory.Instance;
    }

    [Benchmark]
    public int Triangles() => _points.DelaunayTriangles().Count;

    [Benchmark]
    public IGeometry2D ConcaveHullByRatio() => _points.ConcaveHullByRatio(0.3);

    [Benchmark]
    public int NtsTriangles()
    {
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(_ntsPoints);
        return builder.GetTriangles(NetTopologySuite.Geometries.GeometryFactory.Default).NumGeometries;
    }

    [Benchmark]
    public NetTopologySuite.Geometries.Geometry NtsConcaveHull() => NetTopologySuite.Algorithm.Hull.ConcaveHull.ConcaveHullByLengthRatio(_ntsPoints, 0.3);

    /// <summary>Points as a multipoint and as an NTS multipoint.</summary>
    public static (IGeometry2D Points, NetTopologySuite.Geometries.Geometry Nts) Create(PointLayout layout, int count)
    {
        var random = new Random(42);
        var points = new List<Coordinate>(count);
        switch (layout)
        {
            case PointLayout.Uniform:
                for (int i = 0; i < count; i++) points.Add(new Coordinate(random.NextDouble() * 10_000, random.NextDouble() * 10_000));
                break;
            case PointLayout.Grid:
                int side = (int)Math.Ceiling(Math.Sqrt(count));
                for (int i = 0; i < count; i++) points.Add(new Coordinate(i % side * 10, i / side * 10));
                break;
            default:
                for (int i = 0; i < count; i++)
                {
                    double cx = random.Next(20) * 500, cy = random.Next(20) * 500;
                    points.Add(new Coordinate(cx + Gaussian(random) * 30, cy + Gaussian(random) * 30));
                }

                break;
        }

        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        return (ImpGeometryFactory.Instance.CreateMultiPoint(points.ToArray(), null),
            factory.CreateMultiPointFromCoords(points.Select(c => new NetTopologySuite.Geometries.Coordinate(c.X, c.Y)).ToArray()));
    }

    /// <summary>Points with a height: a smooth terrain with some noise, in the layout of <see cref="Create"/>.</summary>
    public static IGeometry2D CreateTerrain(PointLayout layout, int count)
    {
        var points = ((IMultiPoint2D)Create(layout, count).Points);
        var terrain = new Coordinate[points.PointCount];
        var random = new Random(43);
        for (int i = 0; i < terrain.Length; i++)
        {
            double x = points[i].X, y = points[i].Y;
            terrain[i] = new Coordinate(x, y, 200 * Math.Sin(x / 900) * Math.Cos(y / 700) + random.NextDouble() * 3);
        }

        return ImpGeometryFactory.Instance.CreateMultiPoint(terrain, null);
    }

    private static double Gaussian(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
