using SpatialNET.Imp;
using SpatialNET.Network;
using SpatialNET.Network.Building;
using static SpatialNET.Tests.Networks.BuilderTestKit;

namespace SpatialNET.Tests.Networks;

// Handgeschriebene Fälle zum Builder (ADR 0050).
public class NetworkBuilderTests
{
    /// <summary>The situation of the concept: a transformer, a switch in the middle of a cable, a box on a T junction, two customers.</summary>
    private static NetworkBuilder<string, string, string> Concept(double tolerance = 0.01)
    {
        var builder = Builder(tolerance);
        builder.AddNode("Trafo.1", "T", new Coordinate(0, 0), typeId: 25);
        builder.AddNode("Schalter.1", "S", new Coordinate(30, 0), typeId: 17);
        builder.AddNode("Kasten.1", "K", new Coordinate(50, 30), typeId: 15);
        builder.AddNode("Kunde.1", "C1", new Coordinate(90, 50), typeId: 16);
        builder.AddNode("Kunde.2", "C2", new Coordinate(100, -40), typeId: 16);
        builder.AddLine("AV.1", Line((0, 0), (10, 0)), typeId: 14);                            // transformer connector
        builder.AddLine("NS.210", Line((10, 0), (100, 0)), typeId: 11);                        // one cable, split twice
        builder.AddLine("NS.211", Line((100, 0), (100, -40)), typeId: 11);
        builder.AddLine("KV.1", Line((50, 30), (50, 0.004)), typeId: 13);                      // ends 4 mm beside the cable
        builder.AddLine("KV.2", Line((50, 30), (70, 50)), typeId: 13);
        builder.AddLine("NS.212", Line((70.005, 50), (90, 50)), typeId: 11);                   // starts 5 mm beside the connector
        return builder;
    }

    [Fact]
    public void The_concept_example_gives_eight_edges_and_four_helper_nodes()
    {
        var writer = new RecordingWriter();
        var result = Concept().Build(writer);

        Assert.Equal(8, result.EdgeCount);
        Assert.Equal(["T", "S", "K", "C1", "C2"], writer.Nodes.Take(5).Select(n => n.Node.Id));
        Assert.Equal(4, writer.Nodes.Count(n => n.IsHelper));
        Assert.All(writer.Nodes.Where(n => n.IsHelper), n => Assert.Equal(0, n.Node.TypeId));

        // the cable NS.210 runs helper(10,0) - S - helper - helper(100,0); the helper of the T junction lies at the end of the
        // connector, 4 mm beside the cable, and the cable is split at the foot of the perpendicular
        var cable = writer.Edges.Where(e => e.Source == "NS.210").OrderBy(e => e.FromMeasure).ToList();
        Assert.Equal(["NS.210#0/3", "NS.210#1/3", "NS.210#2/3"], cable.Select(e => e.Edge.Id));
        Assert.Equal(["H(10,0)", "S", "H(50,0.004)"], cable.Select(e => e.Edge.From));
        Assert.Equal([0.0, 20, 40], cable.Select(e => e.FromMeasure));
        Assert.Equal([20.0, 20, 50], cable.Select(e => e.Edge.Length));
        Assert.Equal("H(50,0.004)", writer.Edge("KV.1#0/1").Edge.To);
        Assert.Equal("H(70.0025,50)", writer.Edge("NS.212#0/1").Edge.From);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind != BuildDiagnosticKind.OutsideShardRegions);
    }

    [Fact]
    public void Without_splitting_the_cable_stays_one_edge_and_the_box_is_cut_off()
    {
        var builder = Builder(0.01, o => o with { SplitAtNodes = false, SplitAtLineEnds = false });
        builder.AddNode("Schalter.1", "S", new Coordinate(30, 0), typeId: 17);
        builder.AddLine("NS.210", Line((0, 0), (100, 0)), typeId: 11);
        builder.AddLine("KV.1", Line((50, 30), (50, 0)), typeId: 13);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal(["NS.210#0/1", "KV.1#0/1"], writer.Edges.Select(e => e.Edge.Id));
        Assert.Contains(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.IsolatedNode);
    }

    [Fact]
    public void Two_real_nodes_within_the_tolerance_stop_the_build_before_the_first_write()
    {
        var builder = Builder(0.01);
        builder.AddNode("Kasten.1", "K1", new Coordinate(0, 0), typeId: 15);
        builder.AddNode("Kasten.2", "K2", new Coordinate(0.005, 0), typeId: 15);
        builder.AddNode("Kasten.3", "K3", new Coordinate(50, 0), typeId: 15);
        builder.AddNode("Schalter.3", "S3", new Coordinate(50, 0.002), typeId: 17);
        builder.AddLine("NS.1", Line((0, 0), (50, 0)), typeId: 11);
        var writer = new RecordingWriter();

        var error = Assert.Throws<NetworkBuildException>(() => builder.Build(writer));

        Assert.Equal(2, error.Conflicts.Count);
        Assert.All(error.Conflicts, c => Assert.Equal(NodeConflictKind.RealNodesTooClose, c.Kind));
        Assert.Equal(["Kasten.1", "Kasten.2"], error.Conflicts[0].Sources);
        Assert.Empty(writer.Nodes);
        Assert.Empty(writer.Edges);
    }

    [Fact]
    public void A_chain_of_ends_wider_than_the_tolerance_is_one_node_and_reported()
    {
        var builder = Builder(1);
        builder.AddLine("A", Line((-10, 0), (0, 0)), typeId: 11);
        builder.AddLine("B", Line((0.9, 0), (0.9, 10)), typeId: 11);
        builder.AddLine("C", Line((1.8, 0), (10, 0)), typeId: 11);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal(1 + 3, writer.Nodes.Count);
        Assert.Single(writer.Edges.Select(e => e.Edge.To).Concat(writer.Edges.Select(e => e.Edge.From)).GroupBy(id => id), g => g.Count() == 3);
        Assert.Contains(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ClusterWiderThanTolerance);
    }

    [Fact]
    public void Crossings_connect_only_when_asked()
    {
        foreach (bool crossings in new[] { false, true })
        {
            var builder = Builder(0.01, o => o with { NodeAtCrossings = crossings });
            builder.AddLine("Freileitung", Line((0, 0), (10, 10)), typeId: 12);
            builder.AddLine("Kabel", Line((0, 10), (10, 0)), typeId: 11);
            var writer = new RecordingWriter();

            builder.Build(writer);

            Assert.Equal(crossings ? 4 : 2, writer.Edges.Count);
            Assert.Equal(crossings ? 5 : 4, writer.Nodes.Count);
            if (crossings)
            {
                Assert.Contains(writer.Nodes, n => n.Node.Id == "H(5,5)");
            }
        }
    }

    [Fact]
    public void A_line_that_crosses_itself_meets_the_crossing_twice()
    {
        var builder = Builder(0.01, o => o with { NodeAtCrossings = true });
        builder.AddLine("Acht", Line((0, 0), (10, 10), (10, 0), (0, 10)), typeId: 11);
        var writer = new RecordingWriter();

        builder.Build(writer);

        var edges = writer.Edges.OrderBy(e => e.FromMeasure).ToList();
        Assert.Equal(3, edges.Count);
        Assert.Equal("H(5,5)", edges[0].Edge.To);
        Assert.Equal("H(5,5)", edges[1].Edge.From);
        Assert.Equal("H(5,5)", edges[1].Edge.To);   // the loop through (10,10) and (10,0)
        Assert.Equal("H(5,5)", edges[2].Edge.From);
    }

    [Fact]
    public void Multi_part_lines_give_edges_per_part_with_measures_along_the_whole_line()
    {
        var builder = Builder();
        builder.AddLine("M", MultiLine([(0, 0), (10, 0)], [(20, 0), (20, 5)]), typeId: 11);
        var writer = new RecordingWriter();

        builder.Build(writer);

        Assert.Equal(["M#0/2", "M#1/2"], writer.Edges.Select(e => e.Edge.Id));
        Assert.Equal((10.0, 15.0), (writer.Edges[1].FromMeasure, writer.Edges[1].ToMeasure));
    }

    [Fact]
    public void A_closed_line_is_a_loop_and_a_line_shorter_than_the_tolerance_is_dropped()
    {
        var builder = Builder(0.1);
        builder.AddLine("Ring", Line((0, 0), (10, 0), (10, 10), (0, 0)), typeId: 11);
        builder.AddLine("Stummel", Line((50, 50), (50.05, 50)), typeId: 11);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        var ring = Assert.Single(writer.Edges);
        Assert.Equal(ring.Edge.From, ring.Edge.To);
        Assert.Contains(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ShortPartDropped);
        Assert.Single(writer.Nodes);
    }

    [Fact]
    public void Surveyed_lengths_and_absolute_weights_are_split_and_other_weights_copied()
    {
        var ferry = new WeightDefinition(1, "ferry", WeightKind.Absolute);
        var resistance = new WeightDefinition(2, "resistance", WeightKind.PerLength);
        var builder = Builder(0.01, o => o with { Weights = [ferry, resistance] });
        builder.AddNode("Schalter", "S", new Coordinate(25, 0), typeId: 17);
        builder.AddLine("L", Line((0, 0), (100, 0)), typeId: 11,
            new LineAttributes(EdgeDirection.Forward, Disabled: true, Length: 120, Weights: [new EdgeWeight(1, 40), new EdgeWeight(2, 0.3)]));
        var writer = new RecordingWriter();

        builder.Build(writer);

        var (first, second) = (writer.Edges[0].Edge, writer.Edges[1].Edge);
        Assert.Equal((30.0, 90.0), (first.Length, second.Length));
        Assert.Equal((10.0, 30.0), (first.Weights.Span[0].Value, second.Weights.Span[0].Value));
        Assert.Equal((0.3, 0.3), (first.Weights.Span[1].Value, second.Weights.Span[1].Value));
        Assert.True(first.Disabled);
        Assert.Equal(EdgeDirection.Forward, second.Direction);
    }

    [Fact]
    public void Shards_come_from_the_first_matching_region_with_types_for_helper_nodes()
    {
        var types = TypeSet.Of(11, 15);   // low voltage: cable 11, box 15
        var builder = Builder(0.01, o => o with
        {
            ShardRegions =
            [
                new ShardRegion(Square(-1, -1, 50, 50), 101, types),
                new ShardRegion(Square(40, -1, 100, 50), 102, types),
                new ShardRegion(Square(-1, -1, 200, 200), 900, TypeSet.Of(21)),   // medium voltage only
            ],
            DefaultShard = 0,
        });
        builder.AddNode("Kasten", "K", new Coordinate(45, 10), typeId: 15);             // in 101 and 102: the first wins
        builder.AddLine("NS", Line((45, 10), (90, 10)), typeId: 11);                    // helper (90,10) in 102
        builder.AddLine("MS", Line((150, 150), (160, 150)), typeId: 21);                // helpers in 900
        builder.AddLine("Fremd", Line((300, 300), (310, 300)), typeId: 11);             // outside: default shard
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal(101, writer.Node("K").Shard);
        Assert.Equal(102, writer.Node("H(90,10)").Shard);
        Assert.Equal(900, writer.Node("H(150,150)").Shard);
        Assert.Equal(0, writer.Node("H(300,300)").Shard);
        Assert.Equal((101L, 102L), (writer.Edges[0].FromShard, writer.Edges[0].ToShard));
        Assert.Contains(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.OverlappingShardRegions);
        var outside = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.OutsideShardRegions);
        Assert.Contains("2 node(s)", outside.Message);
    }

    [Fact]
    public void Without_a_default_shard_a_node_outside_gets_a_grid_cell()
    {
        var builder = Builder(0.01, o => o with { FallbackShardCellSize = 100 });
        builder.AddLine("L", Line((10, 10), (250, 10)), typeId: 11);
        var writer = new RecordingWriter();

        builder.Build(writer);

        Assert.NotEqual(writer.Node("H(10,10)").Shard, writer.Node("H(250,10)").Shard);
    }

    [Fact]
    public void Duplicate_ids_from_the_factory_are_conflicts()
    {
        var builder = new NetworkBuilder<string, string, string>(new NetworkBuildOptions { Tolerance = 0.01 }, new ConstantIds());
        builder.AddNode("Kasten", "X", new Coordinate(0, 0), typeId: 15);
        builder.AddLine("A", Line((0, 0), (10, 0)), typeId: 11);
        builder.AddLine("B", Line((10, 0), (20, 0)), typeId: 11);
        var writer = new RecordingWriter();

        var error = Assert.Throws<NetworkBuildException>(() => builder.Build(writer));

        Assert.Contains(error.Conflicts, c => c.Kind == NodeConflictKind.HelperIdTaken);
        Assert.Contains(error.Conflicts, c => c.Kind == NodeConflictKind.DuplicateEdgeId);
        Assert.Empty(writer.Nodes);
    }

    private sealed class ConstantIds : INetworkIdFactory<string, string, string>
    {
        public string NewHelperNode(Coordinate location) => "X";

        public string NewEdge(string source, int part, int partCount) => "E";
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder(1, o => o with { FallbackShardCellSize = 0 }));
        Assert.Throws<ArgumentNullException>(() => new NetworkBuilder<string, string, string>(null!, new StableIds()));

        var builder = Builder();
        builder.AddNode("a", "A", new Coordinate(0, 0), typeId: 1);
        Assert.Throws<ArgumentException>(() => builder.AddNode("b", "A", new Coordinate(5, 0), typeId: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddNode("c", "C", new Coordinate(5, 0), typeId: 0));
        Assert.Throws<ArgumentException>(() => builder.AddNode("d", "D", new Coordinate(double.NaN, 0), typeId: 1));
        Assert.Throws<ArgumentNullException>(() => builder.AddLine("e", null!, 11));
        Assert.Throws<ArgumentException>(() => builder.AddLine("f", MultiLine(), 11));
        Assert.Throws<ArgumentException>(() => builder.AddLine("g", Line((0, 0)), 11));
        Assert.Throws<ArgumentException>(() => builder.AddLine("h", Line((0, 0), (double.PositiveInfinity, 0)), 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddLine("i", Line((0, 0), (1, 0)), 11, new LineAttributes(Length: -1)));
        Assert.Throws<ArgumentException>(() => builder.AddLine("j", Line((0, 0), (1, 0)), 11, new LineAttributes(Weights: [new EdgeWeight(9, 1)])));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
    }

    [Fact]
    public void A_builder_is_used_once()
    {
        var builder = Concept();
        builder.Build(new RecordingWriter());

        Assert.Throws<InvalidOperationException>(() => builder.Build(new RecordingWriter()));
        Assert.Throws<InvalidOperationException>(() => builder.AddLine("x", Line((0, 0), (1, 0)), 11));
    }

    [Fact]
    public void Progress_goes_through_the_phases_in_order_and_ends_each_one()
    {
        var reports = new List<BuildProgress>();
        Concept().Build(new RecordingWriter(), new SynchronousProgress(reports.Add));

        var phases = reports.Select(r => r.Phase).Distinct().ToList();
        Assert.Equal(Enum.GetValues<BuildPhase>(), phases);
        Assert.Equal(phases.Order(), phases);
        Assert.All(phases, phase => Assert.Contains(reports, r => r.Phase == phase && r.Done == r.Total));
        Assert.All(reports, r => Assert.InRange(r.Done, 0, r.Total));
    }

    private sealed class SynchronousProgress(Action<BuildProgress> report) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => report(value);
    }

    [Fact]
    public void Cancellation_stops_the_build_before_anything_is_written()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var writer = new RecordingWriter();

        Assert.Throws<OperationCanceledException>(() => Concept().Build(writer, cancellationToken: source.Token));
        Assert.Empty(writer.Nodes);
    }

    [Fact]
    public void A_built_network_in_memory_can_be_searched_and_drawn()
    {
        var graph = new InMemoryGraph<string, string, string>();
        var builder = Concept();
        builder.Build(graph);
        var lines = new Dictionary<string, IPolyline2D>
        {
            ["AV.1"] = Line((0, 0), (10, 0)),
            ["NS.210"] = Line((10, 0), (100, 0)),
            ["NS.211"] = Line((100, 0), (100, -40)),
            ["KV.1"] = Line((50, 30), (50, 0.004)),
        };

        var path = graph.ShortestPath("K", "C2")!;
        var shape = path.ToPolyline(edge =>
        {
            Assert.True(graph.TryGetSource(edge, out var source, out double from, out double to));
            return new EdgeShape(lines[source], from, to);
        }, ImpGeometryFactory.Instance);

        Assert.Equal(["KV.1#0/1", "NS.210#2/3", "NS.211#0/1"], path.Steps.Select(s => s.Edge));
        Assert.Equal(1, shape.PathCount);
        var points = Enumerable.Range(0, shape[0].PointCount).Select(i => (shape[0][i].X, shape[0][i].Y)).ToList();
        Assert.Equal([(50.0, 30.0), (50, 0.004), (50, 0), (100, 0), (100, -40)], points);
    }
}
