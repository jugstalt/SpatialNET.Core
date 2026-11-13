using SpatialNET.Imp;
using SpatialNET.Network;
using static SpatialNET.Tests.Networks.NetworkTestKit;

namespace SpatialNET.Tests.Networks;

// Handgeschriebene Fälle zu Reach und ShortestPath (ADR 0049).
public class SearchTests
{
    private static Dictionary<int, double> Costs(IGraph<int, int> graph, int start, TraversalOptions<int, int>? options = null) =>
        graph.Reach([start], options).ToDictionary(r => r.Node, r => r.Cost);

    [Fact]
    public void Reach_goes_in_order_of_cost_and_names_start_and_edge()
    {
        //   1 --(2)-- 2 --(2)-- 3
        //   1 --------(1)------ 3      3 is reached over edge 103, before 2
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 2), E(102, 2, 3, 2), E(103, 1, 3, 1)]);

        var reached = graph.Reach([1]).ToList();

        Assert.Equal([1, 3, 2], reached.Select(r => r.Node));
        Assert.Equal([0.0, 1, 2], reached.Select(r => r.Cost));
        Assert.True(reached[0].IsStart);
        Assert.Equal(103, reached[1].Via);
        Assert.All(reached, r => Assert.Equal(1, r.Start));
    }

    [Fact]
    public void Several_starts_label_each_node_with_its_nearest_start()
    {
        var graph = Chain(5);
        var reached = graph.Reach([1, 5, 1]).ToDictionary(r => r.Node);

        Assert.Equal(1, reached[2].Start);
        Assert.Equal(5, reached[4].Start);
        Assert.Equal(2, reached[3].Cost);
    }

    [Fact]
    public void A_blocked_node_is_reached_but_not_passed()
    {
        var graph = Graph([N(1), N(2, blocked: true), N(3)], [E(101, 1, 2, 1), E(102, 2, 3, 1)]);
        var reached = graph.Reach([1]).ToList();

        Assert.Equal([1, 2], reached.Select(r => r.Node));
        Assert.True(reached[1].Blocked);
    }

    [Fact]
    public void A_blocked_start_reaches_only_itself()
    {
        var graph = Graph([N(1, blocked: true), N(2)], [E(101, 1, 2, 1)]);

        var reached = Assert.Single(graph.Reach([1]));
        Assert.True(reached.Blocked);
        Assert.Null(graph.ShortestPath(1, 2));
    }

    [Fact]
    public void The_state_overrides_the_normal_state()
    {
        var graph = Graph([N(1), N(2, blocked: true), N(3), N(4)],
            [E(101, 1, 2, 1), E(102, 2, 3, 1), E(103, 3, 4, 1, disabled: true), E(104, 1, 4, 10)]);

        Assert.Equal([1, 2, 4], Costs(graph, 1).Keys.Order());

        var state = new NetworkState<int, int>().Unblock(2).Enable(103).Disable(104);
        Assert.Equal(new Dictionary<int, double> { [1] = 0, [2] = 1, [3] = 2, [4] = 3 }, Costs(graph, 1, new() { State = state }));

        state.Block(3);
        Assert.Equal([1, 2, 3], Costs(graph, 1, new() { State = state }).Keys.Order());
    }

    [Fact]
    public void Directions_are_respected_and_can_be_overridden()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 1, EdgeDirection.Forward), E(102, 3, 2, 1, EdgeDirection.Forward)]);

        Assert.Equal([1, 2], Costs(graph, 1).Keys.Order());
        Assert.Equal([2], Costs(graph, 2).Keys);

        var state = new NetworkState<int, int>().SetDirection(102, EdgeDirection.Backward).SetDirection(101, EdgeDirection.Both);
        Assert.Equal([1, 2, 3], Costs(graph, 2, new() { State = state }).Keys.Order());
    }

    [Fact]
    public void Edge_types_limit_the_edges_that_are_walked()
    {
        //  low voltage 11 on one side of the transformer 2, medium voltage 21 on the other
        var graph = Graph([N(1), N(2, type: 5), N(3)], [E(101, 1, 2, 1, type: 21), E(102, 2, 3, 1, type: 11)]);

        Assert.Equal([2, 3], Costs(graph, 2, new() { EdgeTypes = TypeSet.Of(11) }).Keys.Order());
    }

    [Fact]
    public void Node_types_limit_the_nodes_that_are_entered_except_helper_nodes_and_starts()
    {
        var graph = Graph([N(1, type: 7), N(2), N(3, type: 8), N(4, type: 7)],
            [E(101, 1, 2, 1), E(102, 2, 3, 1), E(103, 2, 4, 1)]);

        Assert.Equal([1, 2, 4], Costs(graph, 1, new() { NodeTypes = TypeSet.Of(7) }).Keys.Order());
        Assert.Equal([2, 3], Costs(graph, 3, new() { NodeTypes = TypeSet.Of(9) }).Keys.Order());
    }

    [Fact]
    public void MaxCost_ends_the_search()
    {
        Assert.Equal([1, 2, 3], Costs(Chain(10), 1, new() { MaxCost = 2 }).Keys.Order());
        Assert.Equal([1], Costs(Chain(10), 1, new() { MaxCost = 0 }).Keys);
    }

    [Fact]
    public void Parallel_edges_take_the_cheaper_and_loops_are_ignored()
    {
        var graph = Graph([N(1), N(2)], [E(101, 1, 1, 0), E(102, 1, 2, 5), E(103, 2, 1, 3)]);

        var path = graph.ShortestPath(1, 2)!;
        var step = Assert.Single(path.Steps);
        Assert.Equal(103, step.Edge);
        Assert.True(step.Reversed);
        Assert.Equal(3, path.Cost);
    }

    [Fact]
    public void The_kinds_of_weights_turn_values_into_costs()
    {
        var graph = Graph([N(1), N(2), N(3)],
        [
            E(101, 1, 2, 2000, weights: [new EdgeWeight(1, 15), new EdgeWeight(2, 0.25), new EdgeWeight(3, 50)]),
            E(102, 2, 3, 1000, weights: [new EdgeWeight(3, 80)]),
        ]);

        Assert.Equal(3000, graph.ShortestPath(1, 3)!.Cost);
        var absolute = new WeightDefinition(1, "ferry", WeightKind.Absolute, Fallback: 1);
        Assert.Equal(16, graph.ShortestPath(1, 3, new() { Weight = absolute })!.Cost);
        var perLength = new WeightDefinition(2, "resistance", WeightKind.PerLength, Fallback: 0.5);
        Assert.Equal(0.25 * 2000 + 0.5 * 1000, graph.ShortestPath(1, 3, new() { Weight = perLength })!.Cost);
        var time = new WeightDefinition(3, "time", WeightKind.LengthOverValue);
        Assert.Equal(2000.0 / 50 + 1000.0 / 80, graph.ShortestPath(1, 3, new() { Weight = time })!.Cost);
    }

    [Fact]
    public void A_missing_value_without_fallback_is_an_error_when_the_edge_is_reached()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 1, weights: new EdgeWeight(3, 50)), E(102, 2, 3, 1)]);
        var time = new WeightDefinition(3, "time", WeightKind.LengthOverValue);

        var options = new TraversalOptions<int, int> { Weight = time };
        Assert.Equal(2, graph.Reach([1], options).Take(2).Count());
        var error = Assert.Throws<InvalidGraphException>(() => graph.Reach([1], options).ToList());
        Assert.Contains("102", error.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_invalid_cost_is_an_error(double length)
    {
        var graph = Graph([N(1), N(2)], [E(101, 1, 2, length)]);
        Assert.Throws<InvalidGraphException>(() => graph.Reach([1]).ToList());
    }

    [Fact]
    public void A_value_of_zero_for_a_speed_is_an_error()
    {
        var graph = Graph([N(1), N(2)], [E(101, 1, 2, 10, weights: new EdgeWeight(3, 0))]);
        var time = new WeightDefinition(3, "time", WeightKind.LengthOverValue);

        Assert.Throws<InvalidGraphException>(() => graph.ShortestPath(1, 2, new() { Weight = time }));
    }

    [Fact]
    public void An_edge_to_a_node_the_graph_does_not_return_is_an_error()
    {
        var graph = Graph([N(1)], [E(101, 1, 2, 1)]);

        var error = Assert.Throws<InvalidGraphException>(() => graph.Reach([1]).ToList());
        Assert.Contains("101", error.Message);
        Assert.Contains(" 2", error.Message);
    }

    [Fact]
    public void Arguments_are_checked_before_and_start_nodes_while_enumerating()
    {
        var graph = Chain(3);

        Assert.Throws<ArgumentNullException>(() => ((IGraph<int, int>)null!).Reach([1]));
        Assert.Throws<ArgumentNullException>(() => graph.Reach(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Reach([1], new() { MaxCost = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Reach([1], new() { MaxCost = double.NaN }));
        Assert.Throws<ArgumentNullException>(() => graph.Reach([1], new() { Weight = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            graph.Reach([1], new() { Weight = new WeightDefinition(1, "w", WeightKind.Absolute, Fallback: -1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            graph.Reach([1], new() { Weight = new WeightDefinition(1, "w", WeightKind.LengthOverValue, Fallback: 0) }));

        var lazy = graph.Reach([99]);
        Assert.Throws<ArgumentException>(() => lazy.ToList());
        Assert.Throws<ArgumentException>(() => graph.ShortestPath(1, 99));
        Assert.Throws<ArgumentException>(() => graph.ShortestPath(99, 99));
    }

    [Fact]
    public void ShortestPath_to_itself_is_empty_and_without_a_way_null()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 4)]);

        var self = graph.ShortestPath(2, 2)!;
        Assert.Empty(self.Steps);
        Assert.Equal(0, self.Cost);
        Assert.Null(graph.ShortestPath(1, 3));
    }

    [Fact]
    public void ShortestPath_may_end_at_a_blocked_node()
    {
        var graph = Graph([N(1), N(2, blocked: true), N(3)], [E(101, 1, 2, 1), E(102, 2, 3, 1)]);

        Assert.Equal(1, graph.ShortestPath(1, 2)!.Cost);
        Assert.Null(graph.ShortestPath(1, 3));
    }

    [Fact]
    public void ShortestPath_steps_follow_the_way_and_name_reversed_edges()
    {
        var graph = Graph([N(1), N(2), N(3)], [E(101, 1, 2, 1), E(102, 3, 2, 1)]);

        var path = graph.ShortestPath(1, 3)!;

        Assert.Equal(
            [new PathStep<int, int>(101, 1, 2, false, 1), new PathStep<int, int>(102, 2, 3, true, 1)],
            path.Steps);
    }

    [Fact]
    public void Reach_is_lazy()
    {
        var counting = new CountingGraph(Chain(100));

        var first = counting.Reach([1]).First();

        Assert.Equal(1, first.Node);
        Assert.Equal(0, counting.EdgeCalls);
    }

    [Fact]
    public void Edges_are_asked_for_in_batches()
    {
        // a star: the centre and 3,000 leaves; after the centre, the leaves are loaded 1,024 at a time
        var graph = Graph(Enumerable.Range(0, 3001).Select(i => N(i)), Enumerable.Range(1, 3000).Select(i => E(i, 0, i, 1)));
        var counting = new CountingGraph(graph);

        Assert.Equal(3001, counting.Reach([0]).Count());
        Assert.Equal(1 + 3, counting.EdgeCalls);
        Assert.Equal(1024, counting.LargestEdgeBatch);
        Assert.Equal(2, counting.NodeCalls);
    }

    [Fact]
    public void Cancellation_stops_the_search()
    {
        using var source = new CancellationTokenSource();
        var reached = Chain(10).Reach([1], cancellationToken: source.Token);

        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => reached.ToList());
    }

    [Fact]
    public void Searches_may_run_in_parallel_on_one_graph()
    {
        var (graph, starts, options) = Random(42);
        var sharded = new FakeShardedGraph(graph, id => id % 3, maxCachedShards: 1);
        var expected = graph.Reach(starts, options).ToDictionary(r => r.Node, r => r.Cost);

        Parallel.For(0, 16, _ =>
        {
            Assert.Equal(expected, graph.Reach(starts, options).ToDictionary(r => r.Node, r => r.Cost));
            Assert.Equal(expected, sharded.Reach(starts, options).ToDictionary(r => r.Node, r => r.Cost));
        });
    }
}
