using CsCheck;
using SpatialNET.Network;
using SpatialNET.Network.Building;
using static SpatialNET.Tests.Networks.BuilderTestKit;

namespace SpatialNET.Tests.Networks;

// The host's connection rules in the builder (ADR 0055): helper types, Connect, Split and Partition.
public class ConnectionRuleTests
{
    // two tiers: medium voltage (MS) and low voltage (NS)
    private const int MsCable = 21, NsCable = 11;
    private const int MsTrafo = 41, MsJoint = 42, NsSwitch = 31, NsJoint = 32;
    private const int MsHelper = 91, NsHelper = 92;

    private static int Tier(int type) => type is MsCable or MsTrafo or MsJoint or MsHelper ? 1 : 2;

    /// <summary>Tiers kept apart: ends and nodes connect within their tier, NS cables also at a transformer.</summary>
    private static NetworkBuildOptions Tiers(NetworkBuildOptions o, ConnectionRule? split = null) => o with
    {
        HelperNodeType = edge => Tier(edge) == 1 ? MsHelper : NsHelper,
        Partition = Tier,
        Connect = (node, edge) => Tier(node) == Tier(edge) || (node == MsTrafo && edge == NsCable) ? ConnectionDecision.Connect : ConnectionDecision.Report,
        Split = split ?? ((node, edge) => Tier(node) == Tier(edge) ? ConnectionDecision.Connect : ConnectionDecision.Report),
    };

    [Fact]
    public void A_joint_of_another_tier_on_a_vertex_does_not_split_the_cable_and_is_reported()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Muffe.4435", "M", new Coordinate(50, 0), NsJoint);
        builder.AddLine("Leitung.4576", Line((0, 0), (50, 0), (100, 0)), MsCable);
        builder.AddLine("NS.1", Line((50, 0), (50, 40)), NsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("Leitung.4576#0/1", writer.Edges.Single(e => e.Source == "Leitung.4576").Edge.Id);
        Assert.Equal("M", writer.Edge("NS.1#0/1").Edge.From);
        var refused = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitRefused);
        Assert.Equal(["Muffe.4435", "Leitung.4576"], refused.Sources!);
    }

    [Fact]
    public void Ends_of_two_tiers_at_one_place_give_two_helper_nodes_of_their_types()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddLine("MS.1", Line((0, 0), (10, 0)), MsCable);
        builder.AddLine("MS.2", Line((10, 0), (20, 0)), MsCable);
        builder.AddLine("NS.1", Line((10, 0), (10, 10)), NsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("H(10,0):91", writer.Edge("MS.1#0/1").Edge.To);
        Assert.Equal("H(10,0):91", writer.Edge("MS.2#0/1").Edge.From);
        Assert.Equal("H(10,0):92", writer.Edge("NS.1#0/1").Edge.From);
        Assert.Equal(NsHelper, writer.Node("H(10,0):92").Node.TypeId);
        Assert.All(writer.Nodes, n => Assert.True(n.IsHelper));

        // the NS end lies on the inside of no line, and a T junction of a helper type is asked too
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitRefused);
    }

    [Fact]
    public void A_line_end_that_may_not_connect_forms_a_helper_node_and_is_reported()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (10, 0)), NsCable);
        builder.AddLine("MS.1", Line((10, 0), (10, 10)), MsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("M", writer.Edge("NS.1#0/1").Edge.To);
        Assert.Equal("H(10,0):91", writer.Edge("MS.1#0/1").Edge.From);
        var refused = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ConnectionRefused);
        Assert.Equal(["Muffe.1", "MS.1"], refused.Sources!);
    }

    [Fact]
    public void Ignore_refuses_without_a_note()
    {
        var builder = Builder(0.01, o => Tiers(o) with { Connect = (node, edge) => Tier(node) == Tier(edge) ? ConnectionDecision.Connect : ConnectionDecision.Ignore });
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (10, 0)), NsCable);
        builder.AddLine("MS.1", Line((10, 0), (10, 10)), MsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("H(10,0):91", writer.Edge("MS.1#0/1").Edge.From);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ConnectionRefused);
    }

    [Fact]
    public void Refused_connections_name_the_types_the_rule_was_asked_with()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0), NsJoint);
        builder.AddNode("Muffe.2", "M2", new Coordinate(50, 0), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (10, 0)), NsCable);
        builder.AddLine("MS.1", Line((10, 0), (10, 10)), MsCable);
        builder.AddLine("MS.2", Line((40, 0), (50, 0), (60, 0)), MsCable);

        var result = builder.Build(new RecordingWriter());

        var connection = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ConnectionRefused);
        Assert.Equal((NsJoint, MsCable), (connection.NodeTypeId, connection.EdgeTypeId));
        var split = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitRefused);
        Assert.Equal((NsJoint, MsCable), (split.NodeTypeId, split.EdgeTypeId));
    }

    [Fact]
    public void ConnectAndReport_connects_the_line_end_and_is_reported()
    {
        var builder = Builder(0.01, o => Tiers(o) with
        {
            Connect = (node, edge) => Tier(node) == Tier(edge) ? ConnectionDecision.Connect : ConnectionDecision.ConnectAndReport,
        });
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (10, 0)), NsCable);
        builder.AddLine("MS.1", Line((10, 0), (10, 10)), MsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("M", writer.Edge("MS.1#0/1").Edge.From);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ConnectionRefused);
        var reported = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.ConnectionReported);
        Assert.Equal(["Muffe.1", "MS.1"], reported.Sources!);
        Assert.Equal((NsJoint, MsCable), (reported.NodeTypeId, reported.EdgeTypeId));
        Assert.Equal(new Coordinate(10, 0), reported.Location);
    }

    [Fact]
    public void ConnectAndReport_splits_the_line_and_is_reported_once()
    {
        var builder = Builder(0.01, o => Tiers(o, split: (node, edge) => Tier(node) == Tier(edge) ? ConnectionDecision.Connect : ConnectionDecision.ConnectAndReport));
        builder.AddNode("Muffe.4435", "M", new Coordinate(50, 0), NsJoint);
        builder.AddLine("Leitung.4576", Line((0, 0), (50, 0), (100, 0)), MsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal(2, writer.Edges.Count(e => e.Source == "Leitung.4576"));
        Assert.Equal("M", writer.Edge("Leitung.4576#0/2").Edge.To);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitRefused);
        var reported = Assert.Single(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitReported);
        Assert.Equal(["Muffe.4435", "Leitung.4576"], reported.Sources!);
        Assert.Equal((NsJoint, MsCable), (reported.NodeTypeId, reported.EdgeTypeId));
    }

    [Fact]
    public void A_transformer_connects_both_tiers()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Trafo.1", "T", new Coordinate(10, 0), MsTrafo);
        builder.AddLine("MS.1", Line((0, 0), (10, 0)), MsCable);
        builder.AddLine("NS.1", Line((10, 0), (20, 0)), NsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal("T", writer.Edge("MS.1#0/1").Edge.To);
        Assert.Equal("T", writer.Edge("NS.1#0/1").Edge.From);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind is BuildDiagnosticKind.ConnectionRefused or BuildDiagnosticKind.SplitRefused);
    }

    [Fact]
    public void Real_nodes_of_two_partitions_at_one_place_are_no_conflict()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Muffe.MS", "MM", new Coordinate(10, 0), MsJoint);
        builder.AddNode("Muffe.NS", "MN", new Coordinate(10, 0.002), NsJoint);
        builder.AddLine("MS.1", Line((0, 0), (10, 0)), MsCable);
        builder.AddLine("NS.1", Line((10, 0), (20, 0)), NsCable);
        var writer = new RecordingWriter();

        builder.Build(writer);

        Assert.Equal("MM", writer.Edge("MS.1#0/1").Edge.To);
        Assert.Equal("MN", writer.Edge("NS.1#0/1").Edge.From);
    }

    [Fact]
    public void Real_nodes_of_one_partition_at_one_place_stay_a_conflict()
    {
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Muffe.1", "M1", new Coordinate(10, 0), NsJoint);
        builder.AddNode("Muffe.2", "M2", new Coordinate(10, 0.002), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (10, 0)), NsCable);

        var error = Assert.Throws<NetworkBuildException>(() => builder.Build(new RecordingWriter()));

        Assert.Equal(NodeConflictKind.RealNodesTooClose, Assert.Single(error.Conflicts).Kind);
    }

    [Fact]
    public void A_line_end_that_may_connect_to_nodes_of_two_partitions_joins_them_for_the_resolver()
    {
        // the transformer (MS) and an NS joint drawn on it; the NS cable may connect to both
        var builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Trafo.1", "T", new Coordinate(10, 0), MsTrafo);
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0.003), NsJoint);
        builder.AddLine("MS.1", Line((0, 0), (10, 0)), MsCable);
        builder.AddLine("NS.1", Line((10, 0), (20, 0)), NsCable);

        var error = Assert.Throws<NetworkBuildException>(() => builder.Build(new RecordingWriter()));
        Assert.Equal(["Trafo.1", "Muffe.1"], Assert.Single(error.Conflicts).Sources);

        builder = Builder(0.01, o => Tiers(o));
        builder.AddNode("Trafo.1", "T", new Coordinate(10, 0), MsTrafo);
        builder.AddNode("Muffe.1", "M", new Coordinate(10, 0.003), NsJoint);
        builder.AddLine("MS.1", Line((0, 0), (10, 0)), MsCable);
        builder.AddLine("NS.1", Line((10, 0), (20, 0)), NsCable);
        builder.ResolveCoincidentNodes = nodes => nodes.ToList().FindIndex(n => n.TypeId == MsTrafo);
        var writer = new RecordingWriter();

        builder.Build(writer);

        Assert.Equal("T", writer.Edge("NS.1#0/1").Edge.From);
        Assert.Equal("M", Assert.Single(writer.Node("T").Merged!).Id);
    }

    [Fact]
    public void Only_switches_split_and_t_junctions_are_reported()
    {
        // an ideal low voltage network: lines are cut at real nodes, only switches are set onto a line
        var builder = Builder(0.01, o => Tiers(o, split: (node, edge) => node == NsSwitch ? ConnectionDecision.Connect : ConnectionDecision.Report));
        builder.AddNode("Trenner.1", "S", new Coordinate(30, 0), NsSwitch);
        builder.AddNode("Muffe.1", "M", new Coordinate(60, 0), NsJoint);
        builder.AddLine("NS.1", Line((0, 0), (100, 0)), NsCable);
        builder.AddLine("NS.2", Line((60, 0), (60, 10)), NsCable);
        builder.AddLine("NS.3", Line((80, 10), (80, 0)), NsCable);
        var writer = new RecordingWriter();

        var result = builder.Build(writer);

        Assert.Equal(["NS.1#0/2", "NS.1#1/2"], writer.Edges.Where(e => e.Source == "NS.1").Select(e => e.Edge.Id));
        Assert.Equal("S", writer.Edge("NS.1#0/2").Edge.To);
        Assert.Equal("H(80,0):92", writer.Edge("NS.3#0/1").Edge.To);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Kind == BuildDiagnosticKind.SplitRefused));
        Assert.Contains(result.Diagnostics, d => d.Kind == BuildDiagnosticKind.SplitRefused && d.Sources![0] is null);
    }

    [Fact]
    public void A_rule_is_asked_once_per_pair_of_types()
    {
        int asked = 0;
        var builder = Builder(0.01, o => o with { Split = (_, _) => { asked++; return ConnectionDecision.Connect; } });
        for (int i = 1; i < 10; i++)
        {
            builder.AddNode($"Trenner.{i}", $"S{i}", new Coordinate(10 * i, 0), NsSwitch);
        }

        builder.AddLine("NS.1", Line((0, 0), (100, 0)), NsCable);
        var writer = new RecordingWriter();

        builder.Build(writer);

        Assert.Equal(10, writer.Edges.Count);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void An_unknown_decision_throws()
    {
        var builder = Builder(0.01, o => o with { Split = (_, _) => (ConnectionDecision)7 });
        builder.AddNode("Trenner.1", "S", new Coordinate(50, 0), NsSwitch);
        builder.AddLine("NS.1", Line((0, 0), (100, 0)), NsCable);

        Assert.Throws<InvalidOperationException>(() => builder.Build(new RecordingWriter()));
    }

    [Fact]
    public void Rules_that_always_connect_give_the_network_without_rules()
    {
        Gen.Int.Sample(seed =>
        {
            var input = RandomInput.Create(seed);
            var (a, ra) = input.Build();
            var (b, rb) = input.Build(configure: o => o with
            {
                HelperNodeType = _ => 0,
                Partition = _ => 0,
                Connect = (_, _) => ConnectionDecision.Connect,
                Split = (_, _) => ConnectionDecision.Connect,
            });

            Assert.Equal(a.Nodes, b.Nodes);
            Assert.Equal(a.Edges.Select(e => (e.Edge.Id, e.Edge.From, e.Edge.To, e.FromMeasure, e.ToMeasure)),
                         b.Edges.Select(e => (e.Edge.Id, e.Edge.From, e.Edge.To, e.FromMeasure, e.ToMeasure)));
            Assert.Equal(ra.Diagnostics, rb.Diagnostics);
        });
    }

    [Fact]
    public void Another_order_of_the_input_gives_the_same_network_with_rules()
    {
        // two tiers by parity: node types 1, 2 and line types 10, 11; nodes of type 2 connect everything
        static NetworkBuildOptions Rules(NetworkBuildOptions o) => o with
        {
            HelperNodeType = edge => 100 + edge % 2,
            Partition = node => node % 2,
            Connect = (node, edge) => node == 2 || node % 2 == edge % 2 ? ConnectionDecision.Connect : ConnectionDecision.Report,
            Split = (node, edge) => node % 2 == edge % 2 ? ConnectionDecision.Connect : ConnectionDecision.Ignore,
        };

        Gen.Select(Gen.Int, Gen.Int).Sample(t =>
        {
            var (seed, shuffle) = t;
            var input = RandomInput.Create(seed);
            var (a, ra) = input.Build(configure: Rules);
            var (b, rb) = input.Build(shuffle: true, seed: shuffle, configure: Rules);

            static IEnumerable<(string, double, double, string, string)> Topology(RecordingWriter w) =>
                w.Edges.Select(e => (e.Source, Math.Round(e.FromMeasure, 9), Math.Round(e.ToMeasure, 9), e.Edge.From, e.Edge.To)).Order();

            Assert.Equal(Topology(a), Topology(b));
            Assert.Equal(a.Nodes.Select(n => n.Node).OrderBy(n => n.Id, StringComparer.Ordinal), b.Nodes.Select(n => n.Node).OrderBy(n => n.Id, StringComparer.Ordinal));
            Assert.Equal(ra.Diagnostics.Count, rb.Diagnostics.Count);
        });
    }
}
