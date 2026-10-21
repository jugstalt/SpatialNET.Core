using BenchmarkDotNet.Attributes;
using NetTopologySuite.Index.Strtree;
using SpatialNET.Internal;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;

namespace SpatialNET.Benchmarks;

/// <summary>Layouts of the indexed envelopes: spread evenly, or concentrated in a few clusters.</summary>
public enum Layout { Uniform, Clustered }

/// <summary>
/// The public <see cref="SpatialIndex{T}"/> against brute force and the NTS STRtree (ADR 0014):
/// build time, range queries and k-nearest. Queries cover about 0.1 % of the extent.
/// </summary>
[MemoryDiagnoser]
public class SpatialIndexBenchmarks
{
    private Envelope2D[] _boxes = null!, _queries = null!;
    private Coordinate[] _points = null!;
    private SpatialIndex<int> _index = null!;
    private STRtree<int> _tree = null!;

    [Params(Layout.Uniform, Layout.Clustered)]
    public Layout Layout { get; set; }

    [Params(1_000, 100_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_boxes, _queries, _points) = IndexInputs.Create(Layout, N);
        _index = new SpatialIndex<int>(_boxes.Select((e, i) => (e, i)));
        _tree = BuildTree(_boxes);
    }

    [Benchmark]
    public SpatialIndex<int> Build() => new(_boxes.Select((e, i) => (e, i)));

    [Benchmark]
    public STRtree<int> NtsBuild() => BuildTree(_boxes);

    [Benchmark]
    public int Query()
    {
        int hits = 0;
        foreach (var q in _queries) hits += _index.Query(q).Count();
        return hits;
    }

    [Benchmark]
    public int QueryIntoList()
    {
        int hits = 0;
        var list = new List<int>();
        foreach (var q in _queries)
        {
            list.Clear();
            _index.Query(q, list);
            hits += list.Count;
        }

        return hits;
    }

    [Benchmark]
    public int NtsQuery()
    {
        int hits = 0;
        foreach (var q in _queries) hits += _tree.Query(new NtsEnvelope(q.MinX, q.MaxX, q.MinY, q.MaxY)).Count;
        return hits;
    }

    [Benchmark]
    public int BruteForceQuery()
    {
        int hits = 0;
        foreach (var q in _queries.Take(10)) // 10 queries only: linear scan
        {
            foreach (var b in _boxes)
            {
                if (b.MinX <= q.MaxX && q.MinX <= b.MaxX && b.MinY <= q.MaxY && q.MinY <= b.MaxY) hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public int Nearest10()
    {
        int hits = 0;
        foreach (var p in _points) hits += _index.Nearest(p, 10).Count;
        return hits;
    }

    private static STRtree<int> BuildTree(Envelope2D[] boxes)
    {
        var tree = new STRtree<int>();
        for (int i = 0; i < boxes.Length; i++) tree.Insert(new NtsEnvelope(boxes[i].MinX, boxes[i].MaxX, boxes[i].MinY, boxes[i].MaxY), i);
        tree.Build();
        return tree;
    }
}

/// <summary>
/// Decides whether the internal grid <c>SegmentIndex</c> of <c>PreparedGeometry</c> may be replaced by
/// <see cref="SpatialIndex{T}"/> (ADR 0014): the segments of a ring with N vertices, probed with small
/// envelopes as the predicates do.
/// </summary>
[MemoryDiagnoser]
public class SegmentIndexBenchmarks
{
    private (Coordinate A, Coordinate B)[] _segments = null!;
    private Envelope2D[] _queries = null!;
    private Envelope2D _extent;
    private SegmentIndex _grid = null!;
    private SpatialIndex<int> _tree = null!;

    [Params(Layout.Uniform, Layout.Clustered)]
    public Layout Layout { get; set; }

    [Params(1_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_segments, _extent, _queries) = IndexInputs.Segments(Layout, N);
        _grid = new SegmentIndex(_segments, _extent);
        _tree = Tree();
    }

    [Benchmark]
    public object GridBuild() => new SegmentIndex(_segments, _extent);

    [Benchmark]
    public SpatialIndex<int> TreeBuild() => Tree();

    [Benchmark]
    public int GridQuery()
    {
        int hits = 0;
        foreach (var q in _queries) hits += _grid.Query(q.MinX, q.MinY, q.MaxX, q.MaxY).Count;
        return hits;
    }

    [Benchmark]
    public int TreeQuery()
    {
        int hits = 0;
        var list = new List<int>();
        foreach (var q in _queries)
        {
            list.Clear();
            _tree.Query(q, list);
            hits += list.Count;
        }

        return hits;
    }

    private SpatialIndex<int> Tree() => new(_segments.Select((s, i) =>
        (new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)));
}

internal static class IndexInputs
{
    private const double Size = 10_000;

    private static (double X, double Y) Position(Random rnd, Layout layout, (double X, double Y)[] clusters)
    {
        if (layout == Layout.Uniform) return (rnd.NextDouble() * Size, rnd.NextDouble() * Size);
        var (cx, cy) = clusters[rnd.Next(clusters.Length)];
        double r = Size * 0.02 * Math.Abs(rnd.NextDouble() + rnd.NextDouble() - 1) * 3;
        double a = rnd.NextDouble() * 2 * Math.PI;
        return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    private static (double X, double Y)[] Clusters(Random rnd) =>
        Enumerable.Range(0, 8).Select(_ => (rnd.NextDouble() * Size, rnd.NextDouble() * Size)).ToArray();

    public static (Envelope2D[] Boxes, Envelope2D[] Queries, Coordinate[] Points) Create(Layout layout, int n)
    {
        var rnd = new Random(42);
        var clusters = Clusters(rnd);
        double side = Size / Math.Sqrt(n) * 0.5; // small items: the layouts differ in density, not in size
        var boxes = Enumerable.Range(0, n).Select(_ =>
        {
            var (x, y) = Position(rnd, layout, clusters);
            return new Envelope2D(x, y, x + rnd.NextDouble() * side, y + rnd.NextDouble() * side);
        }).ToArray();
        double q = Size * 0.03; // about 0.1 % of the extent
        var queries = Enumerable.Range(0, 200).Select(_ =>
        {
            var (x, y) = Position(rnd, layout, clusters);
            return new Envelope2D(x, y, x + q, y + q);
        }).ToArray();
        var points = Enumerable.Range(0, 200).Select(_ =>
        {
            var (x, y) = Position(rnd, layout, clusters);
            return new Coordinate(x, y);
        }).ToArray();
        return (boxes, queries, points);
    }

    public static ((Coordinate A, Coordinate B)[] Segments, Envelope2D Extent, Envelope2D[] Queries) Segments(Layout layout, int n)
    {
        var rnd = new Random(42);
        var clusters = Clusters(rnd);
        double step = Size / Math.Sqrt(n) * 0.5;
        var segments = new (Coordinate, Coordinate)[n];
        var (px, py) = Position(rnd, layout, clusters);
        for (int i = 0; i < n; i++)
        {
            var (x, y) = Position(rnd, layout, clusters);
            (x, y) = (px + Math.Clamp(x - px, -step, step), py + Math.Clamp(y - py, -step, step));
            segments[i] = (new Coordinate(px, py), new Coordinate(x, y));
            (px, py) = (x, y);
        }

        var extent = new Envelope2D(
            segments.Min(s => Math.Min(s.Item1.X, s.Item2.X)), segments.Min(s => Math.Min(s.Item1.Y, s.Item2.Y)),
            segments.Max(s => Math.Max(s.Item1.X, s.Item2.X)), segments.Max(s => Math.Max(s.Item1.Y, s.Item2.Y)));
        double q = step * 2;
        var queries = Enumerable.Range(0, 2000).Select(_ =>
        {
            var (x, y) = Position(rnd, layout, clusters);
            return new Envelope2D(x, y, x + q, y + q);
        }).ToArray();
        return (segments, extent, queries);
    }
}
