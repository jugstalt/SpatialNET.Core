using SpatialNET.Imp;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

public class TraceTests
{
    // 1 (transformer) – 2 – 3 (separation point) – 4 – 5 – 6 (transformer), the separation point as a node
    private static InMemoryGraph<int, int, string> Feeder() => Chain(6);

    [Fact]
    public void Two_transformers_with_an_open_separation_point_supply_one_side_each()
    {
        var graph = Feeder();
        var state = new NetworkState<int, int>();
        state.Block(3);

        var supply = graph.Supply([1, 6], [2, 3, 4, 5], new() { State = state }).ToDictionary(s => s.Sink, s => s.Sources.ToList());

        Assert.Equal([1], supply[2]);
        Assert.Empty(supply[3]); // the open separation point itself is reached, but not supplied
        Assert.Equal([6], supply[4]);
        Assert.Equal([6], supply[5]);
        Assert.Equal([3], graph.FindUnsupplied([1, 6], [2, 3, 4, 5], new() { State = state }).ToList());

        var hits = graph.Trace([1, 6], new() { State = state }).ToList();
        Assert.Equal([(1, TraceHitKind.Source), (2, TraceHitKind.Reached), (3, TraceHitKind.Blocked)],
            hits.Where(h => h.Source == 1).Select(h => (h.Node, h.Kind)));
        Assert.Equal([(6, TraceHitKind.Source), (5, TraceHitKind.Reached), (4, TraceHitKind.Reached), (3, TraceHitKind.Blocked)],
            hits.Where(h => h.Source == 6).Select(h => (h.Node, h.Kind)));
    }

    [Fact]
    public void Two_transformers_with_a_closed_separation_point_supply_everything_twice()
    {
        var graph = Feeder();

        var supply = graph.Supply([1, 6], [2, 3, 4, 5]).ToList();

        Assert.All(supply, s => Assert.Equal([1, 6], s.Sources));
        Assert.Empty(graph.FindUnsupplied([1, 6], [2, 3, 4, 5]));

        // each run stops at the other source and reports it
        var run = graph.Trace([1, 6]).Where(h => h.Source == 1).ToList();
        Assert.Equal(6, run.Count);
        Assert.Equal((6, TraceHitKind.OtherSource, 5.0), (run[^1].Node, run[^1].Kind, run[^1].Cost));
    }

    [Fact]
    public void An_open_edge_as_separation_point_works_like_an_open_node()
    {
        var graph = Feeder();
        var state = new NetworkState<int, int>();
        state.Disable(103); // between 3 and 4

        var supply = graph.Supply([1, 6], [2, 3, 4, 5], new() { State = state }).ToDictionary(s => s.Sink, s => s.Sources.ToList());

        Assert.Equal([1], supply[2]);
        Assert.Equal([1], supply[3]);
        Assert.Equal([6], supply[4]);
        Assert.Equal([6], supply[5]);
    }

    [Fact]
    public void A_ring_fed_from_both_sides_is_supplied_twice_with_the_cost_from_each_source()
    {
        // ring 1-2-3-4-5-6-1, fed at 1 and 4
        var graph = Graph(Enumerable.Range(1, 6).Select(i => N(i)), Enumerable.Range(1, 6).Select(i => E(100 + i, i, i % 6 + 1, 1)));

        var hits = graph.Trace([1, 4]).ToList();

        var from1 = hits.Where(h => h.Source == 1).ToDictionary(h => h.Node);
        Assert.Equal(new[] { 1.0, 2.0, 2.0 }, new[] { from1[2].Cost, from1[3].Cost, from1[5].Cost });
        Assert.Equal(TraceHitKind.OtherSource, from1[4].Kind);
        Assert.Equal(3, from1[4].Cost);
        Assert.Equal(6, hits.Count(h => h.Source == 4));

        var supply = graph.Supply([1, 4], [2, 3, 5, 6]).ToList();
        Assert.All(supply, s => Assert.Equal([1, 4], s.Sources));

        // the closing edges of the mesh are in the supply area: both runs together pass or enter all six edges, each once
        var edges = graph.TraceEdges([1, 4]).ToList();
        Assert.Equal(6, edges.Count(e => e.Source == 1));
        Assert.Equal(6, edges.Count(e => e.Source == 4));
    }

    [Fact]
    public void Directed_edges_are_respected()
    {
        // 1 → 2 → 3 and 4 → 3, the arrows along the digitizing direction
        var graph = Graph(Enumerable.Range(1, 4).Select(i => N(i)),
        [
            E(101, 1, 2, 1, EdgeDirection.Forward),
            E(102, 2, 3, 1, EdgeDirection.Forward),
            E(103, 4, 3, 1, EdgeDirection.Forward),
        ]);

        var supply = graph.Supply([1, 4], [2, 3]).ToDictionary(s => s.Sink, s => s.Sources.ToList());

        Assert.Equal([1], supply[2]);
        Assert.Equal([1, 4], supply[3]);
        Assert.Equal([(1, TraceHitKind.Source), (2, TraceHitKind.Reached), (3, TraceHitKind.OtherSource)],
            graph.Trace([1, 3]).Where(h => h.Source == 1).Select(h => (h.Node, h.Kind)));
        Assert.Single(graph.Trace([3])); // upstream is not walked
    }

    [Fact]
    public void Duplicate_sources_and_sinks_are_run_and_reported_once()
    {
        var graph = Chain(3);

        Assert.Equal(graph.Trace([1]).ToList(), graph.Trace([1, 1, 1]).ToList());
        var supply = graph.Supply([1, 1], [3, 3, 2]).ToList();
        Assert.Equal([3, 2], supply.Select(s => s.Sink));
        Assert.All(supply, s => Assert.Equal([1], s.Sources));
        Assert.Equal(2, graph.TraceEdges([1, 1]).Count());
    }

    [Fact]
    public void A_blocked_source_supplies_nothing()
    {
        var graph = Graph([N(1, blocked: true), N(2), N(3)], [E(101, 1, 2, 1), E(102, 2, 3, 1)]);

        var hit = Assert.Single(graph.Trace([1]));
        Assert.Equal((1, TraceHitKind.Blocked, 0.0), (hit.Node, hit.Kind, hit.Cost));
        Assert.Empty(graph.TraceEdges([1]));
        Assert.Equal([1, 2, 3], graph.FindUnsupplied([1], [1, 2, 3]));
    }

    [Fact]
    public void A_blocked_sink_and_a_sink_that_is_another_source_are_not_supplied_by_that_run()
    {
        var graph = Graph([N(1), N(2, blocked: true), N(3)], [E(101, 1, 2, 1), E(102, 2, 3, 1)]);

        Assert.Equal([2, 3], graph.FindUnsupplied([1], [1, 2, 3]));
        Assert.Equal([3], graph.Supply([1, 3], [3]).Single().Sources); // 3 is its own source only
    }

    [Fact]
    public void Costs_follow_the_chosen_weight_for_Trace_but_Supply_needs_length()
    {
        var graph = Graph(Enumerable.Range(1, 3).Select(i => N(i)),
        [
            E(101, 1, 2, 100, weights: [new EdgeWeight(7, 5)]),
            E(102, 2, 3, 100, weights: [new EdgeWeight(7, 6)]),
        ]);
        var weight = new WeightDefinition(7, "ferry", WeightKind.Absolute);

        Assert.Equal([0.0, 5.0, 11.0], graph.Trace([1], new() { Weight = weight }).Select(h => h.Cost));
        Assert.Throws<ArgumentException>(() => graph.Supply([1], [2], new() { Weight = weight }));
        Assert.Throws<ArgumentException>(() => graph.FindUnsupplied([1], [2], new() { Weight = weight }));
    }

    [Fact]
    public void Supply_ignores_MaxCost_and_Trace_respects_it()
    {
        var graph = Chain(4);

        Assert.Equal([1, 2], graph.Trace([1], new() { MaxCost = 1 }).Select(h => h.Node));
        Assert.Equal([1], graph.Supply([1], [4], new() { MaxCost = 1 }).Single().Sources);
    }

    [Fact]
    public void Unknown_sources_and_sinks_are_an_ArgumentException()
    {
        var graph = Chain(3);

        Assert.Throws<ArgumentException>(() => graph.Trace([1, 99]).ToList());
        Assert.Throws<ArgumentException>(() => graph.TraceEdges([99]).ToList());
        Assert.Throws<ArgumentException>(() => graph.Supply([99], [1]).ToList());
        Assert.Throws<ArgumentException>(() => graph.Supply([1], [2, 99]).ToList());
        Assert.Throws<ArgumentException>(() => graph.FindUnsupplied([1], [99]).ToList());
        Assert.Throws<ArgumentNullException>(() => graph.Trace(null!));
        Assert.Throws<ArgumentNullException>(() => graph.Supply([1], null!));
    }

    [Fact]
    public void A_cancelled_token_stops_the_runs()
    {
        var graph = Chain(50);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => graph.Trace([1], cancellationToken: cts.Token).ToList());
        Assert.Throws<OperationCanceledException>(() => graph.Supply([1], [50], cancellationToken: cts.Token).ToList());
    }

    [Fact]
    public void Supply_reports_the_runs_done_and_the_total_at_the_end()
    {
        var graph = Chain(10);
        var reports = new List<SupplyProgress>();

        graph.Supply([1, 5, 10], [2], progress: new SyncProgress(reports.Add)).ToList();

        Assert.NotEmpty(reports);
        Assert.Equal(new SupplyProgress(3, 3), reports[^1]);
        Assert.All(reports, r => Assert.Equal(3, r.SourcesTotal));
        Assert.True(reports.Zip(reports.Skip(1)).All(p => p.First.SourcesDone <= p.Second.SourcesDone));
    }

    [Fact]
    public void The_edges_of_a_run_are_the_edges_of_its_supply_area()
    {
        var graph = Feeder();
        var state = new NetworkState<int, int>();
        state.Block(3);

        var edges = graph.TraceEdges([1, 6], new() { State = state }).ToList();

        // each run also enters the edge to the blocked node
        Assert.Equal([101, 102], edges.Where(e => e.Source == 1).Select(e => e.Edge.Id).OrderBy(e => e));
        Assert.Equal([103, 104, 105], edges.Where(e => e.Source == 6).Select(e => e.Edge.Id).OrderBy(e => e));
    }

    [Fact]
    public void Supply_over_a_sharded_graph_loads_each_shard_once_when_the_sources_come_sorted_by_shard()
    {
        // 12 areas of 5 nodes in a row, each fed at its first node; shard = area
        int areas = 12;
        var nodes = Enumerable.Range(0, areas * 5).Select(i => N(i + 1)).ToList();
        var edges = new List<GraphEdge<int, int>>();
        for (int a = 0; a < areas; a++)
        {
            for (int k = 0; k < 4; k++)
            {
                edges.Add(E(1000 + a * 10 + k, a * 5 + k + 1, a * 5 + k + 2, 1));
            }
        }

        var graph = Graph(nodes, edges);
        var sharded = new FakeShardedGraph(graph, id => (id - 1) / 5, maxCachedShards: 2);
        var sources = Enumerable.Range(0, areas).Select(a => a * 5 + 1).ToList();
        var sinks = nodes.Select(n => n.Id).ToList();

        var supply = sharded.Supply(sources, sinks).ToList();

        Assert.All(supply, s => Assert.Single(s.Sources));
        Assert.Equal(areas, sharded.Loads.Count);
    }

    private sealed class SyncProgress(Action<SupplyProgress> report) : IProgress<SupplyProgress>
    {
        public void Report(SupplyProgress value) => report(value);
    }
}
