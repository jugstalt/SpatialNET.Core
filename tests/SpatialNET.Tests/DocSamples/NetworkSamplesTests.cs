using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Network;
using SpatialNET.Network.Building;

namespace SpatialNET.Tests.DocSamples;

// Die Beispiele aus docs/en/concepts/networks.md und docs/de/concepts/networks.md. Ändert sich ein Beispiel, muss die Dokumentation mitgezogen werden.
public sealed class NetworkSamplesTests
{
    [Fact]
    public void Searches()
    {
        // --- snippet: network-search
        // A small network in memory: a transformer, a box with a switch, two customers.
        var graph = new InMemoryGraph<int, int, string>();
        graph.Add(new GraphNode<int>(1, 25, new Coordinate(0, 0)));    // transformer
        graph.Add(new GraphNode<int>(2, 15, new Coordinate(40, 0)));   // box
        graph.Add(new GraphNode<int>(3, 16, new Coordinate(40, 30)));  // customer
        graph.Add(new GraphNode<int>(4, 16, new Coordinate(80, 0)));   // customer
        graph.Add(new GraphEdge<int, int>(101, 1, 2, 11, 40));         // cable, 40 m
        graph.Add(new GraphEdge<int, int>(102, 2, 3, 13, 30));         // connector, 30 m
        graph.Add(new GraphEdge<int, int>(103, 2, 4, 13, 40));         // connector, 40 m

        // Everything within 75 m of cable from the transformer, in order of cost.
        foreach (var hit in graph.Reach([1], new() { MaxCost = 75 }))
            Console.WriteLine($"{hit.Node} after {hit.Cost} m");       // 1 after 0 m, 2 after 40 m, 3 after 70 m

        var path = graph.ShortestPath(1, 4)!;
        Console.WriteLine(string.Join(" > ", path.Steps.Select(s => s.Edge)) + $" = {path.Cost} m"); // 101 > 103 = 80 m

        // The box is switched off today: it is still reached, but not passed.
        var today = new NetworkState<int, int>().Block(2);
        Console.WriteLine(graph.ShortestPath(1, 4, new() { State = today }) is null);              // True
        // --- end

        Assert.Equal(["1 after 0 m", "2 after 40 m", "3 after 70 m"],
            graph.Reach([1], new() { MaxCost = 75 }).Select(h => $"{h.Node} after {h.Cost} m"));
        Assert.Equal("101 > 103 = 80 m", string.Join(" > ", path.Steps.Select(s => s.Edge)) + $" = {path.Cost} m");
        Assert.Null(graph.ShortestPath(1, 4, new() { State = today }));
    }

    [Fact]
    public void Tracing()
    {
        // --- snippet: network-tracing
        // Two transformers feed one street: 1 - 2 - 3 - 4 - 5 - 6, customers 2 to 5, a separation point at 3.
        var graph = new InMemoryGraph<int, int, string>();
        for (int id = 1; id <= 6; id++)
            graph.Add(new GraphNode<int>(id, id is 1 or 6 ? 25 : 16, new Coordinate(id * 30, 0)));
        for (int id = 1; id < 6; id++)
            graph.Add(new GraphEdge<int, int>(100 + id, id, id + 1, 11, 30));

        int[] transformers = [1, 6];
        int[] customers = [2, 3, 4, 5];

        // Who supplies whom? With the separation point closed, every customer hangs on both transformers: a mistake to find.
        foreach (var supply in graph.Supply(transformers, customers))
            Console.WriteLine($"{supply.Sink}: {string.Join(", ", supply.Sources)}");       // 2: 1, 6 and so on

        // Open the separation point: the node is reached by both runs, but nobody passes it.
        var open = new NetworkState<int, int>().Block(3);
        foreach (var supply in graph.Supply(transformers, customers, new() { State = open }))
            Console.WriteLine($"{supply.Sink}: {string.Join(", ", supply.Sources)}");       // 2: 1   3:   4: 6   5: 6
        Console.WriteLine(string.Join(", ", graph.FindUnsupplied(transformers, customers, new() { State = open }))); // 3

        // The area of transformer 1 with the cable length from it, and as edges to draw.
        foreach (var hit in graph.Trace([1], new() { State = open }))
            Console.WriteLine($"{hit.Node} {hit.Kind} after {hit.Cost} m");                 // 1 Source after 0 m, 2 Reached after 30 m, 3 Blocked after 60 m
        Console.WriteLine(string.Join(", ", graph.TraceEdges([1], new() { State = open }).Select(t => t.Edge.Id))); // 101, 102
        // --- end

        Assert.Equal(["2: 1, 6", "3: 1, 6", "4: 1, 6", "5: 1, 6"],
            graph.Supply(transformers, customers).Select(s => $"{s.Sink}: {string.Join(", ", s.Sources)}"));
        Assert.Equal(["2: 1", "3: ", "4: 6", "5: 6"],
            graph.Supply(transformers, customers, new() { State = open }).Select(s => $"{s.Sink}: {string.Join(", ", s.Sources)}"));
        Assert.Equal([3], graph.FindUnsupplied(transformers, customers, new() { State = open }));
        Assert.Equal(["1 Source after 0 m", "2 Reached after 30 m", "3 Blocked after 60 m"],
            graph.Trace([1], new() { State = open }).Select(h => $"{h.Node} {h.Kind} after {h.Cost} m"));
        Assert.Equal([101, 102], graph.TraceEdges([1], new() { State = open }).Select(t => t.Edge.Id));
    }

    [Fact]
    public void Rules()
    {
        // --- snippet: network-rules
        var types = new TypeRegistry(
            [
                new Facet("Level", new Dictionary<int, string> { [1] = "Low", [2] = "Medium" }),
                new Facet("Object", new Dictionary<int, string> { [1] = "Line", [2] = "BoxConnector", [4] = "Box" }),
            ],
            [
                new NetworkType(11, ElementKind.Edge, [1, 1]),   // Low.Line
                new NetworkType(13, ElementKind.Edge, [1, 2]),   // Low.BoxConnector
                new NetworkType(15, ElementKind.Node, [1, 4]),   // Low.Box
            ]);

        // Two boxes (1, 2) and helper nodes (3, 4, 5): box 1 has two connectors, box 2 none, and the line at node 4 ends in the open.
        var graph = new InMemoryGraph<int, int, string>();
        foreach (var (id, type) in new[] { (1, 15), (2, 15), (3, 0), (4, 0), (5, 0) })
            graph.Add(new GraphNode<int>(id, type, new Coordinate(id * 10, 0)));
        graph.Add(new GraphEdge<int, int>(101, 1, 3, 13, 10));   // connector to node 3, where a line continues
        graph.Add(new GraphEdge<int, int>(102, 3, 4, 11, 10));   // line from node 3 to node 4
        graph.Add(new GraphEdge<int, int>(103, 1, 5, 13, 10));   // connector to node 5, where nothing continues

        // Rules as rows, as the GIS team keeps them in a table: patterns as text, several separated by "|".
        var rows = new NetworkRuleDefinition[]
        {
            new("BOX-CONNECTOR", RuleSeverity.Error, RuleSubject.Node, "[*, Box]", RuleEndFilter.All, null, "[*, BoxConnector]", 1, null),
            new("CONNECTOR-FREE-END", RuleSeverity.Error, RuleSubject.Edge, "[*, BoxConnector]", RuleEndFilter.NodeIsNot, "[*, Box]", "[*, Line]", 1, 1),
        };
        var rules = rows.Select(row => NetworkRule.FromDefinition(row, types)).ToList();

        // ... or in code, here as a warning: a line does not end in the open at a helper node.
        rules.Add(NetworkRule.ForEdges("LINE-OPEN-END", types.Edges("[*, Line]"))
            .AtEndsWhereNode(TypeSet.Untyped)
            .CountOtherEdges(types.Edges("*")).AtLeast(1)
            .AsWarning());

        Console.WriteLine(rules[1]);   // CONNECTOR-FREE-END: edges [*, BoxConnector], ends not [*, Box], other edges [*, Line] = 1

        // The nodes of one area and all edges at them; over the whole network use ValidateNodes and ValidateEdges.
        foreach (var violation in graph.Validate(graph.Nodes.Select(node => node.Id), rules))
            Console.WriteLine($"{violation.RuleId} ({violation.Severity}) at node {violation.Node}" +
                              (violation.Kind == ElementKind.Edge ? $", edge {violation.Edge}" : "") + $": {violation.Count} found");
        // CONNECTOR-FREE-END (Error) at node 5, edge 103: 0 found
        // BOX-CONNECTOR (Error) at node 2: 0 found
        // LINE-OPEN-END (Warning) at node 4, edge 102: 0 found
        // --- end

        Assert.Equal("CONNECTOR-FREE-END: edges [*, BoxConnector], ends not [*, Box], other edges [*, Line] = 1", rules[1].ToString());
        Assert.Equal(
            [
                "CONNECTOR-FREE-END (Error) at node 5, edge 103: 0 found",
                "BOX-CONNECTOR (Error) at node 2: 0 found",
                "LINE-OPEN-END (Warning) at node 4, edge 102: 0 found",
            ],
            graph.Validate(graph.Nodes.Select(node => node.Id), rules).Select(violation =>
                $"{violation.RuleId} ({violation.Severity}) at node {violation.Node}" +
                (violation.Kind == ElementKind.Edge ? $", edge {violation.Edge}" : "") + $": {violation.Count} found"));
    }

    [Fact]
    public void TypesAndPatterns()
    {
        // --- snippet: network-types
        var types = new TypeRegistry(
            [
                new Facet("Level", new Dictionary<int, string> { [1] = "Low", [2] = "Medium" }),
                new Facet("Object", new Dictionary<int, string> { [1] = "Line", [2] = "BoxConnector", [4] = "Box", [5] = "Transformer" }),
                new Facet("Build", new Dictionary<int, string> { [1] = "Cable", [2] = "Overhead" }),
            ],
            [
                new NetworkType(11, ElementKind.Edge, [1, 1, 1]),   // Low.Line.Cable
                new NetworkType(12, ElementKind.Edge, [1, 1, 2]),   // Low.Line.Overhead
                new NetworkType(13, ElementKind.Edge, [1, 2]),      // Low.BoxConnector
                new NetworkType(15, ElementKind.Node, [1, 4]),      // Low.Box
                new NetworkType(21, ElementKind.Edge, [2, 1, 1]),   // Medium.Line.Cable
                new NetworkType(23, ElementKind.Edge, [2, 2]),      // Medium.BoxConnector
                new NetworkType(25, ElementKind.Node, [2, 5]),      // Medium.Transformer
            ]);

        TypeSet connectors = types.Edges("[*, BoxConnector, *]");  // {13, 23}: every connector, on every level
        TypeSet low = types.Edges("Low.*");                        // {11, 12, 13}
        TypeSet cables = types.Edges("[*, Line, Cable]");          // {11, 21}
        TypeSet stations = types.Nodes("[*, Box, *] | [*, Transformer, *]"); // {15, 25}

        // Keep a search on the low voltage side of a transformer.
        var options = new TraversalOptions<int, int> { EdgeTypes = low };
        // --- end

        Assert.Equal([13, 23], connectors.TypeIds);
        Assert.Equal([11, 12, 13], low.TypeIds);
        Assert.Equal([11, 21], cables.TypeIds);
        Assert.Equal([15, 25], stations.TypeIds);
        Assert.Same(low, options.EdgeTypes);
    }

    // --- snippet: network-ids
    // The host's ids: a helper node from its location (stable between builds), an edge from its line and part.
    private sealed class GisIds : INetworkIdFactory<string, string, string>
    {
        public string NewHelperNode(Coordinate at) => FormattableString.Invariant($"H({at.X},{at.Y})");

        public string NewEdge(string source, int part, int partCount) => $"{source}#{part}";
    }
    // --- end

    [Fact]
    public void Building()
    {
        // --- snippet: network-build
        IPolyline2D Line(string wkt) => Wkt.Read<IPolyline2D>(wkt, ImpGeometryFactory.Instance);

        var builder = new NetworkBuilder<string, string, string>(new NetworkBuildOptions { Tolerance = 0.01 }, new GisIds());
        builder.AddNode("Trafo.1", "T", new Coordinate(0, 0), typeId: 25);     // the points of the GIS are the real nodes
        builder.AddNode("Switch.1", "S", new Coordinate(30, 0), typeId: 17);
        builder.AddLine("Cable.210", Line("LINESTRING(0 0, 100 0)"), typeId: 11);         // one cable ...
        builder.AddLine("Connector.1", Line("LINESTRING(50 30, 50 0.004)"), typeId: 13);  // ... and a connector 4 mm beside it

        var graph = new InMemoryGraph<string, string, string>();   // or the host's writer into its database
        BuildResult result = builder.Build(graph);
        Console.WriteLine($"{result.NodeCount} nodes, {result.EdgeCount} edges");        // 5 nodes, 4 edges

        // The cable was split at the switch and at the T junction: three edges with their range on the cable.
        foreach (var edge in graph.Edges.Where(e => e.Id.StartsWith("Cable.210")))
        {
            graph.TryGetSource(edge.Id, out _, out double from, out double to);
            Console.WriteLine($"{edge.Id}: {edge.From} -> {edge.To}, {from} to {to} m");
        }
        // Cable.210#0: T -> S, 0 to 30 m
        // Cable.210#1: S -> H(50,0.004), 30 to 50 m
        // Cable.210#2: H(50,0.004) -> H(100,0), 50 to 100 m
        // --- end

        Assert.Equal((5, 4), (result.NodeCount, result.EdgeCount));
        var cable = graph.Edges.Where(e => e.Id.StartsWith("Cable.210")).Select(e =>
        {
            graph.TryGetSource(e.Id, out _, out double from, out double to);
            return $"{e.Id}: {e.From} -> {e.To}, {from} to {to} m";
        });
        Assert.Equal(["Cable.210#0: T -> S, 0 to 30 m", "Cable.210#1: S -> H(50,0.004), 30 to 50 m", "Cable.210#2: H(50,0.004) -> H(100,0), 50 to 100 m"], cable);
    }
}
