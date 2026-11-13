using CsCheck;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// Referenz: Bellman-Ford über alle Kanten (ADR 0049); es gibt kein NTS-Orakel für Netzwerke.
public class SearchPropertyTests
{
    [Fact]
    public void Reach_equals_Bellman_Ford()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, starts, options) = Random(seed);
            var expected = Reference(graph, starts, options);
            var reached = graph.Reach(starts, options).ToList();

            Assert.Equal(expected.Count, reached.Count);
            Assert.Equal(reached.Count, reached.Select(r => r.Node).Distinct().Count());
            foreach (var r in reached)
            {
                Assert.Equal(expected[r.Node], r.Cost);
                Assert.Equal(starts.Contains(r.Node), r.IsStart);
                Assert.Contains(r.Start, starts);
            }

            Assert.True(reached.Zip(reached.Skip(1)).All(p => p.First.Cost <= p.Second.Cost), "not in order of cost");
        });
    }

    [Fact]
    public void Reach_reports_blocked_nodes_as_blocked()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, starts, options) = Random(seed);
            var nodes = graph.Nodes.ToDictionary(n => n.Id);
            foreach (var r in graph.Reach(starts, options))
            {
                Assert.Equal(options.State?.IsBlocked(nodes[r.Node]) ?? nodes[r.Node].Blocked, r.Blocked);
            }
        });
    }

    [Fact]
    public void ShortestPath_has_the_reference_cost_and_is_a_valid_way()
    {
        Gen.Select(Gen.Int, Gen.Int[0, 30], Gen.Int[0, 30]).Sample(t =>
        {
            var (seed, a, b) = t;
            var (graph, _, options) = Random(seed);
            var ids = graph.Nodes.Select(n => n.Id).ToArray();
            int from = ids[a % ids.Length], to = ids[b % ids.Length];
            var expected = Reference(graph, [from], options);

            var path = graph.ShortestPath(from, to, options);

            if (!expected.TryGetValue(to, out double cost))
            {
                Assert.Null(path);
                return;
            }

            Assert.NotNull(path);
            Assert.Equal(cost, path.Cost);
            Assert.Equal(cost, path.Steps.Sum(s => s.Cost));
            var edges = graph.Edges.ToDictionary(e => e.Id);
            var nodes = graph.Nodes.ToDictionary(n => n.Id);
            int at = from;
            for (int i = 0; i < path.Steps.Count; i++)
            {
                var step = path.Steps[i];
                var edge = edges[step.Edge];
                Assert.Equal(at, step.From);
                Assert.Equal(step.Reversed ? (edge.To, edge.From) : (edge.From, edge.To), (step.From, step.To));
                Assert.False(options.State?.IsBlocked(nodes[step.From]) ?? nodes[step.From].Blocked, "a blocked node was passed");
                at = step.To;
            }

            Assert.Equal(to, at);
        });
    }

    [Fact]
    public void The_same_input_gives_the_same_output()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, starts, options) = Random(seed);
            Assert.Equal(graph.Reach(starts, options).ToList(), graph.Reach(starts, options).ToList());
        });
    }

    [Fact]
    public void A_sharded_graph_gives_the_same_costs_as_the_graph_in_memory()
    {
        Gen.Select(Gen.Int, Gen.Int[1, 4], Gen.Int[1, 5]).Sample(t =>
        {
            var (seed, shards, cache) = t;
            var (graph, starts, options) = Random(seed);
            var sharded = new FakeShardedGraph(graph, id => id % shards, cache);

            var expected = graph.Reach(starts, options).ToDictionary(r => r.Node, r => r.Cost);
            var actual = sharded.Reach(starts, options).ToDictionary(r => r.Node, r => r.Cost);

            Assert.Equal(expected, actual);
            Assert.True(sharded.CachedShardCount <= Math.Max(cache, shards));
        });
    }
}
