using BenchmarkDotNet.Attributes;
using NetTopologySuite.Index.Strtree;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;

namespace SpatialNET.Benchmarks;

/// <summary>
/// <c>SpatialIndex&lt;T&gt;.Join</c> and <c>SelfJoin</c> (ADR 0021) against the loop of queries (one index, one query per item of
/// the other), the same loop with the NTS STRtree, and brute force over all pairs (small sizes only).
/// </summary>
[MemoryDiagnoser]
public class SpatialJoinBenchmarks
{
    private Envelope2D[] _left = null!, _right = null!;
    private SpatialIndex<int> _leftIndex = null!, _rightIndex = null!;
    private STRtree<int> _tree = null!;

    [Params(Layout.Uniform, Layout.Clustered)]
    public Layout Layout { get; set; }

    [Params(1_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _left = IndexInputs.Create(Layout, N).Boxes;
        _right = IndexInputs.Create(Layout, N).Boxes.Select(e => new Envelope2D(e.MinX + 37, e.MinY + 19, e.MaxX + 37, e.MaxY + 19)).ToArray();
        _leftIndex = new SpatialIndex<int>(_left.Select((e, i) => (e, i)));
        _rightIndex = new SpatialIndex<int>(_right.Select((e, i) => (e, i)));
        _tree = new STRtree<int>();
        for (int i = 0; i < _left.Length; i++) _tree.Insert(new NtsEnvelope(_left[i].MinX, _left[i].MaxX, _left[i].MinY, _left[i].MaxY), i);
    }

    [Benchmark(Baseline = true)]
    public int LoopOfQueries()
    {
        int pairs = 0;
        var results = new List<int>();
        foreach (var box in _right)
        {
            results.Clear();
            _leftIndex.Query(box, results);
            pairs += results.Count;
        }

        return pairs;
    }

    [Benchmark]
    public int Join()
    {
        var pairs = new List<(int, int)>();
        _leftIndex.Join(_rightIndex, pairs);
        return pairs.Count;
    }

    [Benchmark]
    public int SelfJoin()
    {
        int pairs = 0;
        foreach (var _ in _leftIndex.SelfJoin()) pairs++;
        return pairs;
    }

    [Benchmark]
    public int NtsLoopOfQueries()
    {
        int pairs = 0;
        foreach (var box in _right) pairs += _tree.Query(new NtsEnvelope(box.MinX, box.MaxX, box.MinY, box.MaxY)).Count;
        return pairs;
    }

    [Benchmark]
    public int BruteForce()
    {
        if (N > 1_000) return -1; // quadratic: only for the small size
        int pairs = 0;
        foreach (var a in _left)
            foreach (var b in _right)
                if (a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY) pairs++;
        return pairs;
    }
}
