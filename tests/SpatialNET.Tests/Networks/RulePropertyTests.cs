using CsCheck;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// Random graphs and random rules against the brute-force reference of RuleReference (there is no NTS oracle for networks).
public class RulePropertyTests
{
    // Node types 1 (Box) and 2 (Trafo) and the untyped helper nodes 0; edge types 11 (Cable) and 12 (Line): what NetworkTestKit.Random makes.
    private static readonly TypeRegistry Registry = new(
        [new Facet("Class", new Dictionary<int, string> { [1] = "Box", [2] = "Trafo", [3] = "Cable", [4] = "Line" })],
        [
            new NetworkType(1, ElementKind.Node, [1]),
            new NetworkType(2, ElementKind.Node, [2]),
            new NetworkType(11, ElementKind.Edge, [3]),
            new NetworkType(12, ElementKind.Edge, [4]),
        ]);

    private static readonly string[] NodePatterns = ["Box", "Trafo", "Box | Trafo", "*", "untyped", "untyped | Box"];
    private static readonly string[] EdgePatterns = ["Cable", "Line", "Cable | Line", "*", "[Cable]"];

    private static List<NetworkRule> RandomRules(Random random)
    {
        var rules = new List<NetworkRule>();
        int count = random.Next(1, 7);
        for (int i = 0; i < count; i++)
        {
            bool node = random.Next(2) == 0;
            int min = random.Next(0, 3);
            int? max = random.Next(3) == 0 ? null : min + random.Next(0, 3);
            var filter = node ? RuleEndFilter.All : (RuleEndFilter)random.Next(3);
            string? endTypes = filter == RuleEndFilter.All ? null : NodePatterns[random.Next(NodePatterns.Length)];
            var definition = new NetworkRuleDefinition(
                $"R{i}",
                random.Next(4) == 0 ? RuleSeverity.Warning : RuleSeverity.Error,
                node ? RuleSubject.Node : RuleSubject.Edge,
                (node ? NodePatterns : EdgePatterns)[random.Next(node ? NodePatterns.Length : EdgePatterns.Length)],
                filter,
                endTypes,
                EdgePatterns[random.Next(EdgePatterns.Length)],
                min,
                max);
            rules.Add(NetworkRule.FromDefinition(definition, Registry));
        }

        return rules;
    }

    [Fact]
    public void ValidateNodes_equals_the_reference()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, _, _) = Random(seed);
            var rules = RandomRules(new Random(seed ^ 0x5bd1e995));
            var nodes = graph.Nodes.Select(n => n.Id).ToList();

            Assert.Equal(RuleReference.Nodes(graph, nodes, rules), graph.ValidateNodes(nodes, rules).ToList());
        });
    }

    [Fact]
    public void ValidateEdges_equals_the_reference()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, _, _) = Random(seed);
            var rules = RandomRules(new Random(seed ^ 0x5bd1e995));
            var edges = graph.Edges.ToList();

            Assert.Equal(RuleReference.Edges(graph, edges, rules), graph.ValidateEdges(edges, rules).ToList());
        });
    }

    [Fact]
    public void Validate_equals_ValidateNodes_plus_ValidateEdges_over_everything()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, _, _) = Random(seed);
            var rules = RandomRules(new Random(seed ^ 0x5bd1e995));
            var nodes = graph.Nodes.Select(n => n.Id).ToList();

            var separate = RuleReference.Sorted(graph.ValidateNodes(nodes, rules).Concat(graph.ValidateEdges(graph.Edges, rules)));
            var together = RuleReference.Sorted(graph.Validate(nodes, rules));

            Assert.Equal(separate, together);
            Assert.Equal(RuleReference.Everything(graph, rules), together);
        });
    }

    [Fact]
    public void A_shuffled_input_gives_the_same_set_of_violations()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, _, _) = Random(seed);
            var random = new Random(seed);
            var rules = RandomRules(new Random(seed ^ 0x5bd1e995));
            var nodes = graph.Nodes.Select(n => n.Id).OrderBy(_ => random.Next()).ToList();
            var edges = graph.Edges.OrderBy(_ => random.Next()).ToList();

            Assert.Equal(RuleReference.Everything(graph, rules), RuleReference.Sorted(graph.Validate(nodes, rules)));
            Assert.Equal(
                RuleReference.Sorted(RuleReference.Nodes(graph, nodes, rules)),
                RuleReference.Sorted(graph.ValidateNodes(nodes, rules)));
            Assert.Equal(
                RuleReference.Sorted(RuleReference.Edges(graph, edges, rules)),
                RuleReference.Sorted(graph.ValidateEdges(edges, rules)));
        });
    }

    [Fact]
    public void A_definition_round_trips()
    {
        Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            foreach (var rule in RandomRules(random))
            {
                var definition = rule.ToDefinition();
                var again = NetworkRule.FromDefinition(definition, Registry);

                Assert.Equal(definition, again.ToDefinition());
                Assert.Equal((rule.Id, rule.Severity, rule.Subject, rule.EndFilter, rule.Min, rule.Max),
                    (again.Id, again.Severity, again.Subject, again.EndFilter, again.Min, again.Max));
                Assert.Equal(rule.SubjectTypes.TypeIds, again.SubjectTypes.TypeIds);
                Assert.Equal(rule.CountTypes.TypeIds, again.CountTypes.TypeIds);
                Assert.Equal(rule.EndTypes?.TypeIds, again.EndTypes?.TypeIds);
                Assert.Equal(rule.ToString(), again.ToString());
            }
        });
    }

    [Fact]
    public void A_rule_built_in_code_and_its_definition_check_the_same()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, _, _) = Random(seed);
            var rules = RandomRules(new Random(seed ^ 0x5bd1e995));
            var rebuilt = rules.Select(r => NetworkRule.FromDefinition(r.ToDefinition(), Registry)).ToList();

            Assert.Equal(RuleReference.Sorted(graph.Validate(graph.Nodes.Select(n => n.Id), rules)),
                RuleReference.Sorted(graph.Validate(graph.Nodes.Select(n => n.Id), rebuilt)));
        });
    }
}
