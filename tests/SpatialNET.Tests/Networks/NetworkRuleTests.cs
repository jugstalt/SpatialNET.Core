using SpatialNET.Imp;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// Network rules (ADR 0052): the four rules of the concept, counting details, rows and errors, with the types of TypeRegistryTests.
// Nodes: 15 box, 25 transformer, 16 customer, 0 helper node. Edges: 11 low-voltage cable, 12 low-voltage overhead line,
// 13 low-voltage connector, 21 medium-voltage cable.
public class NetworkRuleTests
{
    private static readonly TypeRegistry Types = new(TypeRegistryTests.Facets, TypeRegistryTests.Types);

    private const int Box = 15, Trafo = 25, Customer = 16, Helper = 0;
    private const int Cable = 11, Overhead = 12, Connector = 13, MediumCable = 21;

    private static NetworkRule BoxNeedsConnector() =>
        NetworkRule.ForNodes("KASTEN-ABGANG", Types.Nodes("[*, Kasten, *]"))
            .CountEdges(Types.Edges("[*, Kastenverbinder, *]")).AtLeast(1);

    private static NetworkRule ConnectorFreeEnd() =>
        NetworkRule.ForEdges("KV-FREIES-ENDE", Types.Edges("[*, Kastenverbinder, *]"))
            .AtEndsWhereNodeIsNot(Types.Nodes("[*, Kasten, *]"))
            .CountOtherEdges(Types.Edges("[*, Leitung, *]")).Exactly(1);

    private static NetworkRule VoltageOnlyAtTransformer() =>
        NetworkRule.ForEdges("SPANNUNGSEBENEN", Types.Edges("Niederspannung.*"))
            .AtEndsWhereNodeIsNot(Types.Nodes("[*, Trafo, *]"))
            .CountOtherEdges(Types.Edges("Mittelspannung.*")).None();

    private static NetworkRule NoOpenLineEnd() =>
        NetworkRule.ForEdges("LEITUNG-OFFEN", Types.Edges("[*, Leitung, *]"))
            .AtEndsWhereNode(TypeSet.Untyped)
            .CountOtherEdges(Types.Edges("*")).AtLeast(1);

    private static List<string> Ids(IEnumerable<RuleViolation<int, int>> violations) =>
        violations.Select(v => v.Kind == ElementKind.Node ? $"{v.RuleId}@{v.Node}" : $"{v.RuleId}@{v.Node}/{v.Edge}").ToList();

    [Fact]
    public void A_box_needs_a_connector()
    {
        var graph = Graph([N(1, Box), N(2, Box), N(3, Customer)], [E(101, 1, 3, 5, type: Connector)]);

        var violations = graph.ValidateNodes([1, 2, 3], [BoxNeedsConnector()]).ToList();

        var violation = Assert.Single(violations);
        Assert.Equal(("KASTEN-ABGANG", RuleSeverity.Error, ElementKind.Node, 2, default(int), Box, 0, 1, (int?)null, new Coordinate(2, 0)),
            (violation.RuleId, violation.Severity, violation.Kind, violation.Node, violation.Edge, violation.TypeId, violation.Count, violation.Min, violation.Max, violation.Location));
    }

    [Fact]
    public void The_free_end_of_a_connector_needs_exactly_one_line()
    {
        // Box 1 with three connectors; the end at the box is never checked. Free end 2 has one cable (fine), free end 4 has a cable and
        // an overhead line (two), free end 5 has nothing (zero).
        var graph = Graph(
            [N(1, Box), N(2), N(3, Customer), N(4), N(5), N(6, Customer), N(7, Customer)],
            [
                E(101, 1, 2, 5, type: Connector), E(102, 2, 3, 5, type: Cable),
                E(103, 1, 4, 5, type: Connector), E(104, 4, 6, 5, type: Cable), E(105, 4, 7, 5, type: Overhead),
                E(106, 1, 5, 5, type: Connector),
            ]);

        var violations = graph.ValidateEdges(graph.Edges, [ConnectorFreeEnd()]).ToList();

        Assert.Equal(["KV-FREIES-ENDE@4/103", "KV-FREIES-ENDE@5/106"], Ids(violations));
        Assert.Equal([2, 0], violations.Select(v => v.Count));
    }

    [Fact]
    public void Low_and_medium_voltage_meet_only_at_a_transformer()
    {
        // Transformer 1 takes a low-voltage and a medium-voltage line: allowed. The helper node 4 does the same: a violation, once for the low-voltage line.
        var graph = Graph(
            [N(1, Trafo), N(2, Customer), N(3, Customer), N(4), N(5, Customer), N(6, Customer)],
            [
                E(101, 1, 2, 5, type: Cable), E(102, 1, 3, 5, type: MediumCable),
                E(103, 4, 5, 5, type: Cable), E(104, 4, 6, 5, type: MediumCable),
            ]);

        var violations = graph.ValidateEdges(graph.Edges, [VoltageOnlyAtTransformer()]).ToList();

        // The low-voltage line 103 at node 4 has one medium-voltage neighbour; at its other end, node 5, none. The medium-voltage line is not a subject.
        var violation = Assert.Single(violations);
        Assert.Equal(("SPANNUNGSEBENEN", 4, 103, 1), (violation.RuleId, violation.Node, violation.Edge, violation.Count));
    }

    [Fact]
    public void A_line_does_not_end_in_the_open()
    {
        // 1 - 2 - 3 along cables, node 1 and 3 are customers (typed, so not checked), node 2 is a helper node with two lines.
        // The line 103 ends at the helper node 4, where nothing else is attached.
        var graph = Graph(
            [N(1, Customer), N(2), N(3, Customer), N(4), N(5, Customer)],
            [E(101, 1, 2, 5, type: Cable), E(102, 2, 3, 5, type: Cable), E(103, 5, 4, 5, type: Cable)]);

        var violations = graph.ValidateEdges(graph.Edges, [NoOpenLineEnd()]).ToList();

        var violation = Assert.Single(violations);
        Assert.Equal((4, 103, 0), (violation.Node, violation.Edge, violation.Count));
    }

    [Fact]
    public void Parallel_edges_count_one_by_one()
    {
        var graph = Graph([N(1, Box), N(2, Customer)], [E(101, 1, 2, 5, type: Connector), E(102, 1, 2, 5, type: Connector)]);

        var exactlyOne = NetworkRule.ForNodes("ONE", Types.Nodes("[*, Kasten, *]")).CountEdges(Types.Edges("[*, Kastenverbinder, *]")).Exactly(1);
        var node = Assert.Single(graph.ValidateNodes([1, 2], [exactlyOne]));
        Assert.Equal(2, node.Count);

        // each of the two parallel connectors sees the other one at each of its ends
        var next = NetworkRule.ForEdges("NEIGHBOUR", Types.Edges("[*, Kastenverbinder, *]")).CountOtherEdges(Types.Edges("*")).None();
        Assert.Equal([1, 1, 1, 1], graph.ValidateEdges(graph.Edges, [next]).Select(v => v.Count));
    }

    [Fact]
    public void A_loop_counts_once_and_never_counts_itself()
    {
        var graph = Graph([N(1, Box)], [E(101, 1, 1, 5, type: Connector)]);

        var atLeastOne = NetworkRule.ForNodes("A", Types.Nodes("[*, Kasten, *]")).CountEdges(Types.Edges("*")).Exactly(1);
        Assert.Empty(graph.ValidateNodes([1], [atLeastOne]));

        // the loop is checked at both of its ends, and its own record does not count at either
        var other = NetworkRule.ForEdges("B", Types.Edges("*")).CountOtherEdges(Types.Edges("*")).AtLeast(1);
        var violations = graph.ValidateEdges(graph.Edges, [other]).ToList();
        Assert.Equal([(1, 101, 0), (1, 101, 0)], violations.Select(v => (v.Node, v.Edge, v.Count)));
    }

    [Fact]
    public void A_disabled_edge_counts_and_the_switching_state_does_not_matter()
    {
        var graph = Graph([N(1, Box, blocked: true)], [E(101, 1, 1, 5, disabled: true, type: Connector)]);
        graph.Add(N(2, Customer));
        graph.Add(E(102, 1, 2, 5, disabled: true, type: Connector));

        var rule = NetworkRule.ForNodes("A", Types.Nodes("[*, Kasten, *]")).CountEdges(Types.Edges("[*, Kastenverbinder, *]")).Exactly(2);

        Assert.Empty(graph.ValidateNodes([1], [rule]));
    }

    [Fact]
    public void Several_patterns_with_a_bar_add_up()
    {
        var graph = Graph([N(1, Box), N(2, Trafo), N(3, Customer)], [E(101, 1, 3, 5, type: Cable), E(102, 2, 3, 5, type: Overhead), E(103, 3, 1, 5, type: Connector)]);
        var rule = NetworkRule.FromDefinition(new NetworkRuleDefinition(
            "BAR", RuleSeverity.Error, RuleSubject.Node,
            "[*, Kasten, *] | [*, Trafo, *]", RuleEndFilter.All, null,
            "[*, Leitung, Kabel] | [*, Leitung, Freileitung]", 2, 2), Types);

        var violations = graph.ValidateNodes([1, 2, 3], [rule]).ToList();

        // the box has one line (the connector does not match), the transformer has one, the customer is not a subject
        Assert.Equal(["BAR@1", "BAR@2"], Ids(violations));
        Assert.Equal([1, 1], violations.Select(v => v.Count));
    }

    [Fact]
    public void An_edge_rule_can_look_only_at_ends_with_a_given_node()
    {
        var graph = Graph([N(1, Box), N(2), N(3, Customer)], [E(101, 1, 2, 5, type: Connector), E(102, 2, 3, 5, type: Cable)]);
        var atBox = NetworkRule.ForEdges("AT-BOX", Types.Edges("[*, Kastenverbinder, *]"))
            .AtEndsWhereNode(Types.Nodes("[*, Kasten, *]")).CountOtherEdges(Types.Edges("[*, Leitung, *]")).AtLeast(1);

        var violation = Assert.Single(graph.ValidateEdges(graph.Edges, [atBox]));
        Assert.Equal((1, 101), (violation.Node, violation.Edge));
    }

    [Fact]
    public void AsWarning_lowers_the_severity()
    {
        var graph = Graph([N(1, Box)], []);
        var rule = BoxNeedsConnector().AsWarning();

        Assert.Equal(RuleSeverity.Warning, Assert.Single(graph.ValidateNodes([1], [rule])).Severity);
        Assert.Equal(RuleSeverity.Error, BoxNeedsConnector().Severity);
    }

    [Fact]
    public void Each_method_applies_only_the_rules_of_its_kind()
    {
        var graph = Graph([N(1, Box), N(2)], [E(101, 1, 2, 5, type: Cable)]);
        NetworkRule[] rules = [BoxNeedsConnector(), NoOpenLineEnd()];

        Assert.Equal(["KASTEN-ABGANG@1"], Ids(graph.ValidateNodes([1, 2], rules)));
        Assert.Equal(["LEITUNG-OFFEN@2/101"], Ids(graph.ValidateEdges(graph.Edges, rules)));
        Assert.Equal(["KASTEN-ABGANG@1", "LEITUNG-OFFEN@2/101"], Ids(graph.Validate([1, 2], rules)));
    }

    [Fact]
    public void Violations_come_in_input_order_and_rule_order_with_From_before_To()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 5, type: Cable), E(102, 3, 2, 5, type: Cable)]);
        var first = NetworkRule.ForEdges("B", Types.Edges("*")).CountOtherEdges(Types.Edges("*")).None();
        var second = NetworkRule.ForEdges("A", Types.Edges("*")).CountOtherEdges(Types.Edges("*")).Exactly(5);

        var violations = graph.ValidateEdges([graph.Edges.ElementAt(1), graph.Edges.ElementAt(0)], [first, second]);

        Assert.Equal(["B@2/102", "A@3/102", "A@2/102", "B@2/101", "A@1/101", "A@2/101"], Ids(violations));
    }

    [Fact]
    public void An_element_passed_twice_is_checked_twice_by_ValidateNodes_but_once_by_Validate()
    {
        var graph = Graph([N(1, Box)], []);

        Assert.Equal(2, graph.ValidateNodes([1, 1], [BoxNeedsConnector()]).Count());
        Assert.Single(graph.Validate([1, 1], [BoxNeedsConnector()]));
    }

    [Fact]
    public void Validate_checks_each_edge_once_and_treats_the_outside_as_context()
    {
        // Area: nodes 1 and 2. The edge 102 leaves the area to node 3 (a helper node outside): its end at 2 is checked, the end at 3 as well,
        // because the edge is checked as a whole; node 3 itself is not.
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 5, type: Cable), E(102, 2, 3, 5, type: Cable)]);
        var rule = NetworkRule.ForEdges("E", Types.Edges("*")).CountOtherEdges(Types.Edges("*")).AtLeast(1);
        var nodeRule = NetworkRule.ForNodes("N", TypeSet.Untyped).CountEdges(Types.Edges("*")).AtLeast(2);

        Assert.Equal(["N@1", "E@1/101", "E@3/102"], Ids(graph.Validate([1, 2], [rule, nodeRule])));
    }

    [Fact]
    public void A_node_that_is_not_in_the_graph_is_an_invalid_graph()
    {
        var graph = Graph([N(1, Box)], []);

        Assert.Throws<InvalidGraphException>(() => graph.ValidateNodes([1, 9], [BoxNeedsConnector()]).ToList());
        Assert.Throws<InvalidGraphException>(() => graph.Validate([9], [BoxNeedsConnector()]).ToList());

        var dangling = E(101, 1, 9, 5, type: Cable);
        Assert.Throws<InvalidGraphException>(() => graph.ValidateEdges([dangling], [NoOpenLineEnd()]).ToList());
    }

    [Fact]
    public void Rule_ids_must_be_unique_and_the_check_throws_at_the_call()
    {
        var graph = Graph([N(1, Box)], []);

        // thrown at the call, not at the first element: nothing is enumerated
        Assert.Throws<ArgumentException>(() => graph.ValidateNodes([1], [BoxNeedsConnector(), BoxNeedsConnector()]));
        Assert.Throws<ArgumentException>(() => graph.ValidateEdges([], [NoOpenLineEnd(), NoOpenLineEnd()]));
        Assert.Throws<ArgumentException>(() => graph.Validate([1], [BoxNeedsConnector(), NoOpenLineEnd(), NoOpenLineEnd()]));
        Assert.Throws<ArgumentNullException>(() => graph.ValidateNodes(null!, [BoxNeedsConnector()]));
        Assert.Throws<ArgumentNullException>(() => graph.ValidateNodes([1], null!));
    }

    [Fact]
    public void A_cancelled_token_stops_the_check()
    {
        var graph = Graph([N(1, Box)], []);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => graph.ValidateNodes([1], [BoxNeedsConnector()], cts.Token).ToList());
    }

    [Fact]
    public void Large_input_is_read_in_blocks_of_about_a_thousand_and_asked_in_batches()
    {
        var graph = Chain(2500);
        var counting = new CountingGraph(graph);
        var rule = NetworkRule.ForNodes("DEGREE", TypeSet.Untyped).CountEdges(Types.Edges("*")).Between(1, 2);

        var violations = counting.ValidateNodes(Enumerable.Range(1, 2500), [rule]).ToList();

        Assert.Empty(violations);
        Assert.Equal(3, counting.EdgeCalls);
        Assert.Equal(3, counting.NodeCalls);
        Assert.True(counting.LargestEdgeBatch <= 1000);
    }

    [Fact]
    public void ToString_reads_like_the_rule()
    {
        Assert.Equal("KV-FREIES-ENDE: edges [*, Kastenverbinder, *], ends not [*, Kasten, *], other edges [*, Leitung, *] = 1", ConnectorFreeEnd().ToString());
        Assert.Equal("KASTEN-ABGANG: nodes [*, Kasten, *], edges [*, Kastenverbinder, *] >= 1", BoxNeedsConnector().ToString());
        Assert.Equal("SPANNUNGSEBENEN: edges Niederspannung.*, ends not [*, Trafo, *], other edges Mittelspannung.* = 0", VoltageOnlyAtTransformer().ToString());
        Assert.EndsWith("(warning)", BoxNeedsConnector().AsWarning().ToString());
        Assert.EndsWith("between 1 and 3", NetworkRule.ForNodes("X", Types.Nodes("*")).CountEdges(Types.Edges("*")).Between(1, 3).ToString());
        Assert.EndsWith("<= 3", NetworkRule.ForNodes("X", Types.Nodes("*")).CountEdges(Types.Edges("*")).Between(0, 3).ToString());
    }

    [Fact]
    public void A_rule_gives_its_row_back_and_the_row_gives_the_rule()
    {
        var rule = ConnectorFreeEnd().AsWarning();

        var definition = rule.ToDefinition();

        Assert.Equal(new NetworkRuleDefinition(
            "KV-FREIES-ENDE", RuleSeverity.Warning, RuleSubject.Edge, "[*, Kastenverbinder, *]",
            RuleEndFilter.NodeIsNot, "[*, Kasten, *]", "[*, Leitung, *]", 1, 1), definition);
        Assert.Equal(rule.ToString(), NetworkRule.FromDefinition(definition, Types).ToString());
    }

    [Fact]
    public void A_rule_made_from_type_ids_has_no_pattern_text_to_give_back()
    {
        var rule = NetworkRule.ForNodes("X", TypeSet.Of(15)).CountEdges(TypeSet.Of(13)).AtLeast(1);

        var exception = Assert.Throws<InvalidOperationException>(() => rule.ToDefinition());
        Assert.Contains("'X'", exception.Message);
        Assert.Equal("X: nodes {15}, edges {13} >= 1", rule.ToString());
    }

    private static NetworkRuleDefinition Row(
        string id = "R", RuleSubject subject = RuleSubject.Node, string subjectTypes = "[*, Kasten, *]",
        RuleEndFilter endFilter = RuleEndFilter.All, string? endTypes = null, string countTypes = "[*, Kastenverbinder, *]",
        int min = 1, int? max = null) =>
        new(id, RuleSeverity.Error, subject, subjectTypes, endFilter, endTypes, countTypes, min, max);

    [Fact]
    public void A_valid_row_resolves()
    {
        var rule = NetworkRule.FromDefinition(Row(min: 1, max: 3), Types);

        Assert.Equal(("R", RuleSubject.Node, 1, (int?)3), (rule.Id, rule.Subject, rule.Min, rule.Max));
        Assert.Equal([15], rule.SubjectTypes.TypeIds);
        Assert.Equal([13, 23], rule.CountTypes.TypeIds);
    }

    [Fact]
    public void Definition_errors_name_the_rule()
    {
        // a pattern without a match
        var noMatch = Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(Row(id: "NOMATCH", subjectTypes: "[*, Gibtsnicht, *]"), Types));
        Assert.Contains("NOMATCH", noMatch.Message);
        Assert.Contains("Gibtsnicht", noMatch.Message);

        // a pattern of the wrong kind: boxes are nodes, so as the count types of a node rule (edges) they match nothing
        var wrongCount = Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(Row(id: "WRONG1", countTypes: "[*, Kasten, *]"), Types));
        Assert.Contains("WRONG1", wrongCount.Message);

        // ... and a line as the subject of a node rule
        var wrongSubject = Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(Row(id: "WRONG2", subjectTypes: "[*, Leitung, *]"), Types));
        Assert.Contains("WRONG2", wrongSubject.Message);

        // an end filter on a node rule
        var ends = Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(
            Row(id: "ENDS", endFilter: RuleEndFilter.NodeIs, endTypes: "[*, Kasten, *]"), Types));
        Assert.Contains("ENDS", ends.Message);

        // an end filter without end types, end types without an end filter, an empty pattern, a missing id
        Assert.Contains("ENDS2", Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(
            Row(id: "ENDS2", subject: RuleSubject.Edge, subjectTypes: "*", endFilter: RuleEndFilter.NodeIs), Types)).Message);
        Assert.Contains("ENDS3", Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(
            Row(id: "ENDS3", subject: RuleSubject.Edge, subjectTypes: "*", endTypes: "[*, Kasten, *]"), Types)).Message);
        Assert.Contains("EMPTY", Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(Row(id: "EMPTY", countTypes: " "), Types)).Message);
        Assert.Throws<ArgumentException>(() => NetworkRule.FromDefinition(Row(id: " "), Types));
        Assert.Throws<ArgumentNullException>(() => NetworkRule.FromDefinition(null!, Types));

        // a count range that cannot be
        var negative = Assert.Throws<ArgumentOutOfRangeException>(() => NetworkRule.FromDefinition(Row(id: "NEG", min: -1), Types));
        Assert.Contains("NEG", negative.Message);
        var inverted = Assert.Throws<ArgumentOutOfRangeException>(() => NetworkRule.FromDefinition(Row(id: "INV", min: 3, max: 2), Types));
        Assert.Contains("INV", inverted.Message);
    }

    [Fact]
    public void The_builder_checks_its_arguments()
    {
        Assert.Throws<ArgumentException>(() => NetworkRule.ForNodes("", TypeSet.Untyped));
        Assert.Throws<ArgumentNullException>(() => NetworkRule.ForNodes("X", null!));
        Assert.Throws<ArgumentNullException>(() => NetworkRule.ForEdges("X", TypeSet.Untyped).CountOtherEdges(null!));
        var count = NetworkRule.ForNodes("X", TypeSet.Untyped).CountEdges(TypeSet.Of(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => count.AtLeast(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => count.Between(2, 1));
        Assert.Equal((0, (int?)0), (count.None().Min, count.None().Max));
        Assert.Equal((2, (int?)2), (count.Exactly(2).Min, count.Exactly(2).Max));
    }
}
