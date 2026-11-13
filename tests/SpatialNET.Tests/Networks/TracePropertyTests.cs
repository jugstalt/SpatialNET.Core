using CsCheck;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// Referenz: Breitensuche und Bellman-Ford je Quelle (ADR 0051); es gibt kein NTS-Orakel für Netzwerke.
public class TracePropertyTests
{
    [Fact]
    public void Trace_equals_the_reference_for_every_source()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var distinct = sources.Distinct().ToList();
            var sourceSet = distinct.ToHashSet();
            var hits = graph.Trace(sources, options).ToList();

            // one run per source, in the order of the sources
            Assert.Equal(distinct, hits.Select(h => h.Source).Distinct().ToList());
            Assert.Equal(hits.Select(h => h.Source).OrderBy(s => distinct.IndexOf(s)), hits.Select(h => h.Source));

            foreach (int source in distinct)
            {
                var run = hits.Where(h => h.Source == source).ToList();
                var costs = TraceReference.Costs(graph, source, sourceSet, options);

                Assert.Equal(costs.Count, run.Count);
                Assert.Equal(run.Count, run.Select(h => h.Node).Distinct().Count());
                foreach (var hit in run)
                {
                    Assert.Equal(costs[hit.Node], hit.Cost);
                    Assert.Equal(TraceReference.KindOf(graph, hit.Node, source, sourceSet, options), hit.Kind);
                }

                Assert.True(run.Zip(run.Skip(1)).All(p => p.First.Cost <= p.Second.Cost), "not in order of cost");
                Assert.Equal(source, run[0].Node);
            }
        });
    }

    [Fact]
    public void A_blocked_source_gives_only_its_own_hit()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var nodes = graph.Nodes.ToDictionary(n => n.Id);
            foreach (int source in sources.Distinct())
            {
                if (options.State?.IsBlocked(nodes[source]) ?? nodes[source].Blocked)
                {
                    var run = graph.Trace(sources, options).Where(h => h.Source == source).ToList();
                    var hit = Assert.Single(run);
                    Assert.Equal((source, TraceHitKind.Blocked, 0.0), (hit.Node, hit.Kind, hit.Cost));
                }
            }
        });
    }

    [Fact]
    public void A_run_does_not_pass_blocked_nodes_or_other_sources()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var edges = graph.Edges.ToDictionary(e => e.Id);
            var hits = graph.Trace(sources, options).ToList();
            foreach (var hit in hits.Where(h => h.Kind == TraceHitKind.Reached))
            {
                // the node before it on its way in was passed: the source or a reached node, never a blocked node or another source
                var edge = edges[hit.Via];
                int before = edge.From == hit.Node ? edge.To : edge.From;
                var kind = hits.Single(h => h.Source == hit.Source && h.Node == before).Kind;
                Assert.True(kind is TraceHitKind.Source or TraceHitKind.Reached, $"{hit.Node} was reached over a node of kind {kind}");
            }
        });
    }

    [Fact]
    public void Supply_equals_the_reference_for_every_sink()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            // every node as a sink, in descending order, with a few repeated
            var sinks = graph.Nodes.Select(n => n.Id).OrderByDescending(id => id).ToList();
            var expected = TraceReference.Supply(graph, sources, sinks, options);

            var supply = graph.Supply(sources, sinks.Concat(sinks.Take(2)), options).ToList();

            Assert.Equal(sinks, supply.Select(s => s.Sink).ToList());
            foreach (var s in supply)
            {
                Assert.Equal(expected[s.Sink], s.Sources.ToList());
            }

            Assert.Equal(supply.Where(s => s.Sources.Count == 0).Select(s => s.Sink), graph.FindUnsupplied(sources, sinks, options));
        });
    }

    [Fact]
    public void Supply_does_not_depend_on_MaxCost_and_Trace_does()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var sinks = graph.Nodes.Select(n => n.Id).ToList();
            var unlimited = new TraversalOptions<int, int> { State = options.State, EdgeTypes = options.EdgeTypes, NodeTypes = options.NodeTypes };
            Assert.Equal(
                graph.Supply(sources, sinks, unlimited).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList(),
                graph.Supply(sources, sinks, options).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList());
        });
    }

    [Fact]
    public void Multiple_supply_is_found()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var sourceSet = sources.ToHashSet();
            var sinks = graph.Nodes.Select(n => n.Id).ToList();
            var multiple = graph.Supply(sources, sinks, options).Where(s => s.Sources.Count > 1).Select(s => s.Sink).ToHashSet();

            // the hits of Trace say the same: reached and passed by more than one source (without MaxCost, which Supply ignores)
            var unlimited = new TraversalOptions<int, int> { State = options.State, EdgeTypes = options.EdgeTypes, NodeTypes = options.NodeTypes };
            var viaTrace = graph.Trace(sources, unlimited)
                .Where(h => h.Kind is TraceHitKind.Source or TraceHitKind.Reached)
                .GroupBy(h => h.Node)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();
            Assert.Equal(viaTrace, multiple);
        });
    }

    [Fact]
    public void TraceEdges_equals_the_edges_the_reference_walks_or_enters()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var distinct = sources.Distinct().ToList();
            var sourceSet = distinct.ToHashSet();
            var traced = graph.TraceEdges(sources, options).ToList();

            Assert.Equal(traced.Select(t => t.Source).OrderBy(x => distinct.IndexOf(x)), traced.Select(t => t.Source));
            foreach (int source in distinct)
            {
                var ids = traced.Where(t => t.Source == source).Select(t => t.Edge.Id).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                Assert.Equal(TraceReference.Edges(graph, source, sourceSet, options).OrderBy(e => e), ids.OrderBy(e => e));
            }
        });
    }

    [Fact]
    public void The_same_input_gives_the_same_output()
    {
        Gen.Int.Sample(seed =>
        {
            var (graph, sources, options) = Random(seed);
            var sinks = graph.Nodes.Select(n => n.Id).ToList();
            Assert.Equal(graph.Trace(sources, options).ToList(), graph.Trace(sources, options).ToList());
            Assert.Equal(
                graph.TraceEdges(sources, options).Select(t => (t.Source, t.Edge.Id)).ToList(),
                graph.TraceEdges(sources, options).Select(t => (t.Source, t.Edge.Id)).ToList());
            Assert.Equal(
                graph.Supply(sources, sinks, options).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList(),
                graph.Supply(sources, sinks, options).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList());
        });
    }

    [Fact]
    public void A_sharded_graph_gives_the_same_supply_as_the_graph_in_memory()
    {
        Gen.Select(Gen.Int, Gen.Int[1, 4], Gen.Int[1, 5]).Sample(t =>
        {
            var (seed, shards, cache) = t;
            var (graph, sources, options) = Random(seed);
            var sharded = new FakeShardedGraph(graph, id => id % shards, cache);
            var sinks = graph.Nodes.Select(n => n.Id).ToList();

            Assert.Equal(
                graph.Supply(sources, sinks, options).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList(),
                sharded.Supply(sources, sinks, options).Select(s => (s.Sink, string.Join(",", s.Sources))).ToList());
            Assert.Equal(graph.Trace(sources, options).ToList(), sharded.Trace(sources, options).ToList());
        });
    }
}
