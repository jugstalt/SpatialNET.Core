using CsCheck;
using static SpatialNET.Tests.Networks.BuilderTestKit;

namespace SpatialNET.Tests.Networks;

// Referenz: rohe Gewalt über alle Paare von Enden und alle Linien gegen alle Knoten (ADR 0050); es gibt kein NTS-Orakel.
public class BuilderPropertyTests
{
    private static Dictionary<(string Source, double From, double To), (string From, string To)> Topology(RecordingWriter writer) =>
        writer.Edges.ToDictionary(e => (e.Source, Math.Round(e.FromMeasure, 9), Math.Round(e.ToMeasure, 9)), e => (e.Edge.From, e.Edge.To));

    private static double Length((double X, double Y)[] points) =>
        points.Zip(points.Skip(1)).Sum(p => Math.Sqrt(Math.Pow(p.Second.X - p.First.X, 2) + Math.Pow(p.Second.Y - p.First.Y, 2)));

    [Fact]
    public void Another_order_of_the_input_gives_the_same_network()
    {
        Gen.Select(Gen.Int, Gen.Int, Gen.Bool).Sample(t =>
        {
            var (seed, shuffle, crossings) = t;
            var input = RandomInput.Create(seed);
            var (a, _) = input.Build(crossings: crossings);
            var (b, _) = input.Build(shuffle: true, seed: shuffle, crossings: crossings);

            var (ta, tb) = (Topology(a), Topology(b));
            var onlyA = ta.Where(kv => !tb.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => $"{kv.Key} -> {kv.Value}");
            var onlyB = tb.Where(kv => !ta.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => $"{kv.Key} -> {kv.Value}");
            Assert.True(ta.Count == tb.Count && !onlyA.Any(), $"in order: {string.Join("; ", onlyA)} | shuffled: {string.Join("; ", onlyB)}");
            Assert.Equal(a.Nodes.Select(n => n.Node).OrderBy(n => n.Id, StringComparer.Ordinal), b.Nodes.Select(n => n.Node).OrderBy(n => n.Id, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void The_same_input_gives_the_same_writes()
    {
        Gen.Int.Sample(seed =>
        {
            var input = RandomInput.Create(seed);
            var (a, ra) = input.Build();
            var (b, rb) = input.Build();

            Assert.Equal(a.Nodes, b.Nodes);
            Assert.Equal(a.Edges.Select(e => (e.Edge.Id, e.Edge.From, e.Edge.To, e.Edge.Length, e.FromMeasure, e.ToMeasure)),
                         b.Edges.Select(e => (e.Edge.Id, e.Edge.From, e.Edge.To, e.Edge.Length, e.FromMeasure, e.ToMeasure)));
            Assert.Equal(ra.Diagnostics, rb.Diagnostics);
        });
    }

    [Fact]
    public void Ends_and_points_join_exactly_as_the_brute_force_groups()
    {
        Gen.Int.Sample(seed =>
        {
            var input = RandomInput.Create(seed);
            var (writer, _) = input.Build();

            // every point: the real nodes and both ends of every line whose edges were not all dropped
            var points = new List<(double X, double Y, string Node)>();
            foreach (var (id, at, _) in input.Nodes)
            {
                points.Add((at.X, at.Y, id));
            }

            foreach (var (source, line, _, _, _) in input.Lines)
            {
                var edges = writer.Edges.Where(e => e.Source == source).OrderBy(e => e.FromMeasure).ToList();
                if (edges.Count > 0 && edges[0].FromMeasure == 0)
                {
                    points.Add((line[0].X, line[0].Y, edges[0].Edge.From));
                }

                if (edges.Count > 0 && Math.Abs(edges[^1].ToMeasure - Length(line)) < 1e-9)
                {
                    points.Add((line[^1].X, line[^1].Y, edges[^1].Edge.To));
                }
            }

            // the groups by brute force: union of every pair within the tolerance
            var parent = Enumerable.Range(0, points.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2) <= input.Tolerance * input.Tolerance)
                    {
                        parent[Find(i)] = Find(j);
                    }
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    Assert.Equal(Find(i) == Find(j), points[i].Node == points[j].Node);
                }
            }
        });
    }

    [Fact]
    public void Every_node_on_the_inside_of_a_line_splits_it()
    {
        Gen.Int.Sample(seed =>
        {
            var input = RandomInput.Create(seed);
            var (writer, _) = input.Build();

            foreach (var (source, line, _, _, _) in input.Lines)
            {
                var edges = writer.Edges.Where(e => e.Source == source).ToList();
                if (edges.Count == 0)
                {
                    continue;
                }

                string start = edges.MinBy(e => e.FromMeasure).Edge.From, end = edges.MaxBy(e => e.ToMeasure).Edge.To;
                var stops = edges.SelectMany(e => new[] { e.Edge.From, e.Edge.To }).ToHashSet();
                foreach (var node in writer.Nodes)
                {
                    var (distance, measure) = Project(line, node.Node.Location);
                    if (distance <= input.Tolerance && measure > 1e-9 && measure < Length(line) - 1e-9 && node.Node.Id != start && node.Node.Id != end)
                    {
                        Assert.Contains(node.Node.Id, stops);
                    }
                }
            }
        });
    }

    [Fact]
    public void The_parts_of_a_line_cover_it_and_share_its_lengths_and_absolute_weights()
    {
        Gen.Int.Sample(seed =>
        {
            var input = RandomInput.Create(seed);
            var (writer, result) = input.Build();
            if (result.Diagnostics.Any(d => d.Kind == Network.Building.BuildDiagnosticKind.ShortPartDropped))
            {
                return; // a dropped part leaves a gap shorter than the tolerance
            }

            foreach (var (source, line, _, surveyed, value) in input.Lines)
            {
                var edges = writer.Edges.Where(e => e.Source == source).OrderBy(e => e.FromMeasure).ToList();
                double length = Length(line);
                Assert.Equal(0, edges[0].FromMeasure);
                Assert.Equal(length, edges[^1].ToMeasure, 9);
                for (int i = 1; i < edges.Count; i++)
                {
                    Assert.Equal(edges[i - 1].ToMeasure, edges[i].FromMeasure, 9);
                }

                Assert.Equal(surveyed ?? length, edges.Sum(e => e.Edge.Length), 6);
                Assert.Equal(value, edges.Sum(e => e.Edge.Weights.Span[0].Value), 6);
                Assert.Equal(Enumerable.Range(0, edges.Count).Select(i => $"{source}#{i}/{edges.Count}"), edges.Select(e => e.Edge.Id));
            }
        });
    }

    [Fact]
    public void All_nodes_are_written_before_the_first_edge_and_every_end_is_a_written_node()
    {
        Gen.Int.Sample(seed =>
        {
            var (writer, result) = RandomInput.Create(seed).Build(crossings: true);
            var ids = writer.Nodes.Select(n => n.Node.Id).ToHashSet();

            Assert.False(writer.EdgeBeforeAllNodes);
            Assert.Equal(writer.Nodes.Count, ids.Count);
            Assert.All(writer.Edges, e => Assert.True(ids.Contains(e.Edge.From) && ids.Contains(e.Edge.To)));
            Assert.Equal((result.NodeCount, result.EdgeCount), (writer.Nodes.Count, writer.Edges.Count));
        });
    }

    private static (double Distance, double Measure) Project((double X, double Y)[] line, Coordinate at)
    {
        double best = double.PositiveInfinity, measure = 0, along = 0;
        for (int i = 1; i < line.Length; i++)
        {
            double dx = line[i].X - line[i - 1].X, dy = line[i].Y - line[i - 1].Y, l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Clamp(((at.X - line[i - 1].X) * dx + (at.Y - line[i - 1].Y) * dy) / l2, 0, 1) : 0;
            double d = Math.Pow(at.X - line[i - 1].X - t * dx, 2) + Math.Pow(at.Y - line[i - 1].Y - t * dy, 2);
            if (d < best)
            {
                best = d;
                measure = along + t * Math.Sqrt(l2);
            }

            along += Math.Sqrt(l2);
        }

        return (Math.Sqrt(best), measure);
    }
}
