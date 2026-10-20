using System.Diagnostics;
using BenchmarkDotNet.Running;
using SpatialNET;
using SpatialNET.Benchmarks;
using SpatialNET.Imp;

// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks            → BenchmarkDotNet
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick → grobe Stoppuhr-Messung
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
