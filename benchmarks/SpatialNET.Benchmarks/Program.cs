using System.Diagnostics;
using BenchmarkDotNet.Running;
using SpatialNET;
using SpatialNET.Benchmarks;
using SpatialNET.Imp;

// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks            → BenchmarkDotNet
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick → grobe Stoppuhr-Messung
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-unionall → UnionAll gegen das Falten von Union (ADR 0018)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-prepared → PreparedGeometry (ADR 0024)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-triangulation → Delaunay und ConcaveHull gegen NTS (ADR 0029)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-join → Join und SelfJoin gegen die Schleife aus Abfragen (ADR 0021)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-voronoi → Voronoi, Netz, Höhe, Höhenlinien, Polygon-Triangulierung (ADR 0036 bis 0038)
if (args.Contains("--quick-voronoi"))
{
    GeometryFactory.Default = ImpGeometryFactory.Instance;
    foreach (var layout in Enum.GetValues<PointLayout>())
    {
        foreach (int n in new[] { 10_000, 100_000 })
        {
            var (points, nts) = TriangulationBenchmarks.Create(layout, n);
            var terrain = TriangulationBenchmarks.CreateTerrain(layout, n);
            double voronoi = Measure(() => points.VoronoiDiagram());
            double ntsVoronoi = Measure(() =>
            {
                var b = new NetTopologySuite.Triangulate.VoronoiDiagramBuilder();
                b.SetSites(nts);
                b.GetDiagram(NetTopologySuite.Geometries.GeometryFactory.Default);
            });
            double edges = Measure(() => points.VoronoiEdges());
            double build = Measure(() => Triangulation.FromPoints(terrain));
            var tin = Triangulation.FromPoints(terrain);
            var envelope = terrain.Envelope();
            var random = new Random(7);
            var queries = Enumerable.Range(0, 100_000).Select(_ => new Coordinate(
                envelope.MinX + random.NextDouble() * envelope.Width, envelope.MinY + random.NextDouble() * envelope.Height)).ToArray();
            tin.Locate(queries[0]);    // builds the index
            double locate = Measure(() => { foreach (var q in queries) tin.Locate(q); });
            double interpolate = Measure(() => { foreach (var q in queries) tin.InterpolateZ(q); });
            double contours = Measure(() => { for (int level = -150; level <= 150; level += 30) tin.Contour(level); });
            Console.WriteLine($"{layout,-10} N={n,8}  Voronoi {voronoi,9:F1} ms  NTS {ntsVoronoi,9:F1} ms  Edges {edges,9:F1} ms  | FromPoints {build,8:F1} ms  100k Locate {locate,8:F1} ms  100k InterpolateZ {interpolate,8:F1} ms  11 Contours {contours,8:F1} ms");
        }
    }

    foreach (int n in new[] { 1_000, 10_000, 100_000 })
    {
        var random = new Random(5);
        var ring = Enumerable.Range(0, n).Select(i => { double a = 2 * Math.PI * i / n, r = 1000 + random.NextDouble() * 800; return new Coordinate(r * Math.Cos(a), r * Math.Sin(a)); }).ToList();
        ring.Add(ring[0]);
        var polygon = ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring.ToArray())], null);
        var ntsPolygon = new NetTopologySuite.Geometries.GeometryFactory().CreatePolygon(ring.Select(c => new NetTopologySuite.Geometries.Coordinate(c.X, c.Y)).ToArray());
        double ours = Measure(() => Triangulation.FromPolygon(polygon));
        double theirs = Measure(() => NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(ntsPolygon));
        Console.WriteLine($"Polygon    N={n,8}  FromPolygon {ours,9:F1} ms  NTS PolygonTriangulator {theirs,9:F1} ms");

        // a smooth ring, as a real boundary: short edges that follow each other
        var smooth = Enumerable.Range(0, n).Select(i => { double a = 2 * Math.PI * i / n, r = 1000 + 50 * Math.Sin(7 * a); return new Coordinate(r * Math.Cos(a), r * Math.Sin(a)); }).ToList();
        smooth.Add(smooth[0]);
        var smoothPolygon = ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(smooth.ToArray())], null);
        var ntsSmooth = new NetTopologySuite.Geometries.GeometryFactory().CreatePolygon(smooth.Select(c => new NetTopologySuite.Geometries.Coordinate(c.X, c.Y)).ToArray());
        double smoothOurs = Measure(() => Triangulation.FromPolygon(smoothPolygon));
        double smoothTheirs = Measure(() => NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(ntsSmooth));
        double smoothPoints = Measure(() => Triangulation.FromPoints(smoothPolygon));
        if (n <= 100_000)
        {
            double ntsDelaunay = Measure(() =>
            {
                var b = new NetTopologySuite.Triangulate.DelaunayTriangulationBuilder();
                b.SetSites(ntsSmooth);
                b.GetTriangles(NetTopologySuite.Geometries.GeometryFactory.Default);
            });
            Console.WriteLine($"   NTS Delaunay of the same vertices: {ntsDelaunay:F1} ms");
        }

        Console.WriteLine($"Smooth ring N={n,8}  FromPolygon {smoothOurs,9:F1} ms  NTS PolygonTriangulator {smoothTheirs,9:F1} ms  (the Delaunay triangulation of its vertices alone: {smoothPoints:F1} ms)");
    }

    foreach (int n in new[] { 10_000, 100_000 })
    {
        var (points, _) = TriangulationBenchmarks.Create(PointLayout.Uniform, n);
        var sorted = ((IMultiPoint2D)points);
        var chain = Enumerable.Range(0, sorted.PointCount).Select(i => new Coordinate(sorted[i].X, sorted[i].Y)).OrderBy(c => c.X).Where((_, i) => i % (n / 100) == 0).ToArray();
        var line = ImpGeometryFactory.Instance.CreatePolyline([chain], null);
        double plain = Measure(() => Triangulation.FromPoints(points));
        double constrained = Measure(() => Triangulation.FromPointsAndLines(points, line));
        Console.WriteLine($"Break line N={n,8}  FromPoints {plain,9:F1} ms  FromPointsAndLines (a line of {chain.Length} points) {constrained,9:F1} ms");
    }

    return;
}

if (args.Contains("--quick-triangulation"))
{
    GeometryFactory.Default = ImpGeometryFactory.Instance;
    foreach (var layout in Enum.GetValues<PointLayout>())
    {
        foreach (int n in new[] { 10_000, 100_000, 1_000_000 })
        {
            var (points, nts) = TriangulationBenchmarks.Create(layout, n);
            var builder = () =>
            {
                var b = new NetTopologySuite.Triangulate.DelaunayTriangulationBuilder();
                b.SetSites(nts);
                return b.GetTriangles(NetTopologySuite.Geometries.GeometryFactory.Default).NumGeometries;
            };
            double ours = Measure(() => points.DelaunayTriangles());
            double theirs = n <= 100_000 ? Measure(() => builder()) : double.NaN;
            double hull = Measure(() => points.ConcaveHullByRatio(0.3));
            Console.WriteLine($"{layout,-10} N={n,8}  DelaunayTriangles {ours,9:F1} ms  NTS {theirs,9:F1} ms  ConcaveHull {hull,9:F1} ms");
        }
    }

    return;
}

if (args.Contains("--quick-unionall"))
{
    GeometryFactory.Default = ImpGeometryFactory.Instance;
    foreach (var layout in Enum.GetValues<UnionLayout>())
    {
        foreach (int n in new[] { 100, 400, 1_000 })
        {
            var polygons = UnionAllBenchmarks.Create(layout, n);
            Console.WriteLine($"{layout,-12} N={n,5}  fold {Measure(() => polygons.Aggregate((acc, p) => acc.Union(p))),9:F1} ms  " +
                              $"UnionAll {Measure(() => polygons.UnionAll()),9:F1} ms");
        }
    }

    return;
}

if (args.Contains("--quick"))
{
    GeometryFactory.Default = ImpGeometryFactory.Instance;
    foreach (var c in Enum.GetValues<Case>())
    {
        foreach (int n in new[] { 1_000, 10_000 })
        {
            var (a, b) = Inputs.Create(c, n);
            Console.WriteLine($"{c,-10} N={n,6}  Intersection {Measure(() => a.Intersection(b)),8:F2} ms  " +
                              $"Union {Measure(() => a.Union(b)),8:F2} ms  Difference {Measure(() => a.Difference(b)),8:F2} ms");
        }
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(OverlayBenchmarks).Assembly).Run(args);

static double Measure(Action action)
{
    action();
    int runs = 0;
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 400 || runs < 3)
    {
        action();
        runs++;
    }

    return sw.Elapsed.TotalMilliseconds / runs;
}
