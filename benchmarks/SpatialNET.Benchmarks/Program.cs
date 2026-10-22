using System.Diagnostics;
using BenchmarkDotNet.Running;
using SpatialNET;
using SpatialNET.Benchmarks;
using SpatialNET.Imp;

// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks            → BenchmarkDotNet
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick → grobe Stoppuhr-Messung
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-unionall → UnionAll gegen das Falten von Union (ADR 0018)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-prepared → PreparedGeometry (ADR 0024)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-join → Join und SelfJoin gegen die Schleife aus Abfragen (ADR 0021)
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
