using SpatialNET.Imp;
using SpatialNET.Network;

namespace SpatialNET.Tests.Networks;

/// <summary>
/// The reference for network rules (ADR 0052): counts by brute force over every edge of an <see cref="InMemoryGraph{TNodeId, TEdgeId, TSource}"/>,
/// with none of the batching, blocks or caches of <see cref="NetworkValidation"/>.
/// </summary>
internal static class RuleReference
{
    public static List<RuleViolation<int, int>> Nodes(InMemoryGraph<int, int, string> graph, IEnumerable<int> nodeIds, IEnumerable<NetworkRule> rules)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var result = new List<RuleViolation<int, int>>();
        foreach (int id in nodeIds)
        {
            var node = nodes[id];
            foreach (var rule in rules.Where(r => r.Subject == RuleSubject.Node && r.SubjectTypes.Contains(node.TypeId)))
            {
                int count = graph.Edges.Count(e => (e.From == id || e.To == id) && rule.CountTypes.Contains(e.TypeId));
                if (Violates(rule, count))
                {
                    result.Add(new(rule.Id, rule.Severity, ElementKind.Node, id, default, node.TypeId, count, rule.Min, rule.Max, node.Location));
                }
            }
        }

        return result;
    }

    public static List<RuleViolation<int, int>> Edges(InMemoryGraph<int, int, string> graph, IEnumerable<GraphEdge<int, int>> edges, IEnumerable<NetworkRule> rules)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var result = new List<RuleViolation<int, int>>();
        foreach (var edge in edges)
        {
            foreach (var rule in rules.Where(r => r.Subject == RuleSubject.Edge && r.SubjectTypes.Contains(edge.TypeId)))
            {
                foreach (int end in new[] { edge.From, edge.To })
                {
                    var node = nodes[end];
                    bool listed = rule.EndTypes?.Contains(node.TypeId) ?? false;
                    if ((rule.EndFilter == RuleEndFilter.NodeIs && !listed) || (rule.EndFilter == RuleEndFilter.NodeIsNot && listed))
                    {
                        continue;
                    }

                    int count = graph.Edges.Count(e => e.Id != edge.Id && (e.From == end || e.To == end) && rule.CountTypes.Contains(e.TypeId));
                    if (Violates(rule, count))
                    {
                        result.Add(new(rule.Id, rule.Severity, ElementKind.Edge, end, edge.Id, edge.TypeId, count, rule.Min, rule.Max, node.Location));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>The node rules for every node and the edge rules for every edge, in a fixed order, for comparing sets.</summary>
    public static List<RuleViolation<int, int>> Everything(InMemoryGraph<int, int, string> graph, IEnumerable<NetworkRule> rules) =>
        Sorted(Nodes(graph, graph.Nodes.Select(n => n.Id), rules).Concat(Edges(graph, graph.Edges, rules)));

    public static List<RuleViolation<int, int>> Sorted(IEnumerable<RuleViolation<int, int>> violations) =>
        violations.OrderBy(v => v.RuleId, StringComparer.Ordinal).ThenBy(v => v.Kind).ThenBy(v => v.Node).ThenBy(v => v.Edge).ToList();

    private static bool Violates(NetworkRule rule, int count) => count < rule.Min || (rule.Max is { } max && count > max);
}
