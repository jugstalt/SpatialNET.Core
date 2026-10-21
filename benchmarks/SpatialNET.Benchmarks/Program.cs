using System.Diagnostics;
using BenchmarkDotNet.Running;
using SpatialNET;
using SpatialNET.Benchmarks;
using SpatialNET.Imp;

// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks            → BenchmarkDotNet
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick → grobe Stoppuhr-Messung
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-index → dasselbe für den räumlichen Index (ADR 0014)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-unionall → UnionAll gegen das Falten von Union (ADR 0018)
// dotnet run -c Release --project benchmarks/SpatialNET.Benchmarks -- --quick-join → Join und SelfJoin gegen die Schleife aus Abfragen (ADR 0021)
if (args.Contains("--quick-index"))
{
    foreach (var layout in Enum.GetValues<Layout>())
    {
        foreach (int n in new[] { 1_000, 100_000 })
        {
            var (segments, extent, queries) = IndexInputs.Segments(layout, n);
            var grid = new SpatialNET.Internal.SegmentIndex(segments, extent);
            var tree = new SpatialIndex<int>(segments.Select((s, i) =>
                (new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)));
            var list = new List<int>();
            Console.WriteLine($"{layout,-9} N={n,7}  build grid {Measure(() => new SpatialNET.Internal.SegmentIndex(segments, extent)),8:F2} ms  " +
                              $"tree {Measure(() => new SpatialIndex<int>(segments.Select((s, i) => (new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)))),8:F2} ms  " +
                              $"| {queries.Length} queries: grid {Measure(() => { foreach (var q in queries) grid.Query(q.MinX, q.MinY, q.MaxX, q.MaxY); }),8:F3} ms  " +
                              $"tree {Measure(() => { foreach (var q in queries) { list.Clear(); tree.Query(q, list); } }),8:F3} ms");
        }
    }

    return;
}

if (args.Contains("--quick-join"))
{
    foreach (var layout in Enum.GetValues<Layout>())
    {
        foreach (int n in new[] { 1_000, 100_000 })
        {
            var left = IndexInputs.Create(layout, n).Boxes;
            var right = IndexInputs.Create(layout, n).Boxes.Select(e => new Envelope2D(e.MinX + 37, e.MinY + 19, e.MaxX + 37, e.MaxY + 19)).ToArray();
            var leftIndex = new SpatialIndex<int>(left.Select((e, i) => (e, i)));
            var rightIndex = new SpatialIndex<int>(right.Select((e, i) => (e, i)));
            var results = new List<int>();
            var pairs = new List<(int, int)>();
            Console.WriteLine($"{layout,-9} N={n,7}  loop of queries {Measure(() => { foreach (var box in right) { results.Clear(); leftIndex.Query(box, results); } }),9:F2} ms  " +
                              $"Join {Measure(() => { pairs.Clear(); leftIndex.Join(rightIndex, pairs); }),9:F2} ms  " +
                              $"SelfJoin {Measure(() => { foreach (var _ in leftIndex.SelfJoin()) { } }),9:F2} ms  ({pairs.Count} pairs)");
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
