using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpatialNET.Tests.Playground;

/// <summary>The network view of the playground (ADR 0050): <c>/api/network/*</c> builds, describes and searches a network.</summary>
public class PlaygroundNetworkTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const int Trafo = 25, Switch = 17, Customer = 16, Cable = 11, Connector = 13;

    private readonly HttpClient _client = factory.CreateClient();

    private static object Point(double x, double y, int type) => new { geometry = new { type = "Point", coordinates = new[] { x, y } }, type };

    private static object Line(int type, params double[][] coordinates) => new { geometry = new { type = "LineString", coordinates }, type };

    /// <summary>The example of the concept page in local coordinates: a cable with a switch in the middle and a connector ending on it 4 mm beside it.</summary>
    private static object Concept(object? extra = null) => Request(
        points: [Point(0, 0, Trafo), Point(30, 0, Switch), Point(50, 30, Customer)],
        lines: [Line(Cable, [0, 0], [100, 0]), Line(Connector, [50, 30], [50, 0.004])],
        extra: extra);

    private static Dictionary<string, object?> Request(object[] points, object[] lines, object? extra = null)
    {
        var request = new Dictionary<string, object?> { ["points"] = points, ["lines"] = lines, ["tolerance"] = 0.01, ["local"] = true };
        if (extra is not null)
        {
            foreach (var p in extra.GetType().GetProperties())
            {
                request[p.Name] = p.GetValue(extra);
            }
        }

        return request;
    }

    private async Task<(HttpStatusCode Status, JsonObject Body)> Post(string url, object request)
    {
        var response = await _client.PostAsJsonAsync(url, request);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonObject>())!);
    }

    [Fact]
    public async Task Types_lists_the_fixed_node_and_edge_types()
    {
        var body = (await _client.GetFromJsonAsync<JsonObject>("/api/network/types"))!;

        Assert.Equal(["Trafo", "Kasten", "Schalter", "Kunde"], body["nodes"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
        Assert.Equal([11, 12, 13], body["edges"]!.AsArray().Select(t => t!["id"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Build_splits_the_cable_at_the_switch_and_the_T_junction()
    {
        var (status, body) = await Post("/api/network/build", Concept());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["ok"]!.GetValue<bool>());
        Assert.Equal((5, 4), (body["nodeCount"]!.GetValue<int>(), body["edgeCount"]!.GetValue<int>()));

        var nodes = body["nodes"]!.AsArray();
        Assert.Equal(3, nodes.Count(n => !n!["helper"]!.GetValue<bool>()));   // the points of the input are the real nodes ...
        Assert.Equal(2, nodes.Count(n => n!["helper"]!.GetValue<bool>()));    // ... the T junction and the end of the cable are helper nodes

        var edges = body["edges"]!.AsArray();
        var cable = edges.Where(e => e!["source"]!.GetValue<string>() == "L1").ToList();
        Assert.Equal(["L1#0", "L1#1", "L1#2"], cable.Select(e => e!["id"]!.GetValue<string>()));
        Assert.Equal([30.0, 50.0, 100.0], cable.Select(e => e!["toMeasure"]!.GetValue<double>()));
        Assert.All(cable, e => Assert.Equal("Kabel", e!["typeName"]!.GetValue<string>()));
        Assert.All(edges, e => Assert.Equal("LineString", e!["geometry"]!["type"]!.GetValue<string>()));   // every edge has its piece of the line
        Assert.Equal(20.0, cable[1]!["length"]!.GetValue<double>(), 6);
    }

    [Fact]
    public async Task Build_without_the_split_options_leaves_the_cable_whole()
    {
        var (_, body) = await Post("/api/network/build", Concept(new { splitAtNodes = false, splitAtLineEnds = false }));

        Assert.Equal(2, body["edges"]!.AsArray().Count(e => e!["source"]!.GetValue<string>() is "L1" or "L2"));
    }

    [Fact]
    public async Task Build_with_node_at_crossings_cuts_two_crossing_lines()
    {
        var crossing = Request([], [Line(Cable, [0, 0], [10, 10]), Line(Cable, [0, 10], [10, 0])]);

        var (_, without) = await Post("/api/network/build", crossing);
        crossing["nodeAtCrossings"] = true;
        var (_, with) = await Post("/api/network/build", crossing);

        Assert.Equal(2, without["edgeCount"]!.GetValue<int>());
        Assert.Equal(4, with["edgeCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Build_reports_the_conflict_of_two_real_nodes_within_the_tolerance_with_its_location()
    {
        var request = Request([Point(0, 0, Switch), Point(0.005, 0, Switch)], [Line(Cable, [0, 0], [10, 0])]);

        var (status, body) = await Post("/api/network/build", request);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body["ok"]!.GetValue<bool>());
        Assert.NotEmpty(body["error"]!.GetValue<string>());
        var conflict = body["conflicts"]!.AsArray().Single()!;
        Assert.Equal("RealNodesTooClose", conflict["kind"]!.GetValue<string>());
        Assert.Equal(["P1", "P2"], conflict["sources"]!.AsArray().Select(s => s!.GetValue<string>()).Order());
        Assert.Equal("Point", conflict["location"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Build_reports_an_isolated_node_as_a_diagnostic()
    {
        var request = Request([Point(0, 0, Switch), Point(500, 500, Customer)], [Line(Cable, [0, 0], [10, 0])]);

        var (_, body) = await Post("/api/network/build", request);

        var diagnostic = body["diagnostics"]!.AsArray().Single()!;   // and no note about shard regions, the playground has none
        Assert.Equal("IsolatedNode", diagnostic["kind"]!.GetValue<string>());
        Assert.Equal(500.0, diagnostic["location"]!["coordinates"]![0]!.GetValue<double>(), 6);
    }

    [Fact]
    public async Task Build_projects_a_map_input_to_UTM_and_returns_it_in_WGS84()
    {
        // About 100 m of cable near Graz with a switch in the middle; the tolerance of 0.05 is in metres (UTM 33N).
        var request = new
        {
            points = new[] { Point(15.44, 47.07, Trafo), Point(15.4406, 47.07, Switch) },
            lines = new[] { Line(Cable, [15.44, 47.07], [15.4406, 47.07], [15.4412, 47.07]) },
            tolerance = 0.05,
        };

        var (_, body) = await Post("/api/network/build", request);

        Assert.True(body["ok"]!.GetValue<bool>());
        Assert.Equal("EPSG:32633 (auto)", body["kbs"]!.GetValue<string>());
        Assert.Equal("m", body["unit"]!.GetValue<string>());
        Assert.Equal(2, body["edgeCount"]!.GetValue<int>());
        var first = body["edges"]![0]!["geometry"]!["coordinates"]![0]!;
        Assert.Equal(15.44, first[0]!.GetValue<double>(), 6);   // back in longitude and latitude
        Assert.Equal(47.07, first[1]!.GetValue<double>(), 6);
        Assert.InRange(body["edges"]![0]!["length"]!.GetValue<double>(), 40, 50);
    }

    [Fact]
    public async Task Path_follows_the_cable_and_the_connector_and_draws_the_way()
    {
        var (status, body) = await Post("/api/network/path", Concept(new { from = "P1", to = "P3" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["found"]!.GetValue<bool>());
        Assert.Equal(79.996, body["cost"]!.GetValue<double>(), 3);    // 50 m of cable and the connector of 29.996 m
        Assert.Equal(["L1#0", "L1#1", "L2#0"], body["edges"]!.AsArray().Select(e => e!.GetValue<string>()));
        var way = body["geometry"]!["coordinates"]!.AsArray();
        Assert.Equal([0.0, 0.0], way[0]!.AsArray().Select(v => v!.GetValue<double>()));
        Assert.Equal([50.0, 30.0], way[^1]!.AsArray().Select(v => v!.GetValue<double>()));
    }

    [Fact]
    public async Task Path_does_not_pass_a_blocked_node()
    {
        var (_, body) = await Post("/api/network/path", Concept(new { from = "P1", to = "P3", blocked = new[] { "P2" } }));

        Assert.True(body["ok"]!.GetValue<bool>());
        Assert.False(body["found"]!.GetValue<bool>());
        Assert.Null(body["geometry"]);
    }

    [Fact]
    public async Task Path_to_an_unknown_node_is_an_error_message()
    {
        var (status, body) = await Post("/api/network/path", Concept(new { from = "P1", to = "P99" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(body["error"]!.GetValue<string>()));
    }

    /// <summary>Two transformers feed one cable with customers between them and a switch in the middle (ADR 0051).</summary>
    private static Dictionary<string, object?> Street(object extra) => Request(
        points: [Point(0, 0, Trafo), Point(10, 0, Customer), Point(15, 0, Switch), Point(20, 0, Customer), Point(30, 0, Trafo), Point(40, 5, Customer)],
        lines: [Line(Cable, [0, 0], [30, 0])],
        extra: extra);

    private static Dictionary<string, List<string>> SupplyOf(JsonObject body) =>
        body["sinks"]!.AsArray().ToDictionary(
            s => s!["id"]!.GetValue<string>(),
            s => s!["sources"]!.AsArray().Select(x => x!.GetValue<string>()).ToList());

    [Fact]
    public async Task Supply_with_an_open_switch_gives_each_transformer_its_side_and_leaves_the_stray_customer_unsupplied()
    {
        var (status, body) = await Post("/api/network/supply", Street(new { sources = new[] { "P1", "P5" }, blocked = new[] { "P3" } }));

        Assert.Equal(HttpStatusCode.OK, status);
        var supply = SupplyOf(body);
        Assert.Equal(["P1"], supply["P2"]);
        Assert.Equal(["P5"], supply["P4"]);
        Assert.Empty(supply["P6"]);   // not connected to any line
        Assert.Equal(["P1", "P5"], body["sources"]!.AsArray().Select(s => s!["id"]!.GetValue<string>()));
        Assert.All(body["sinks"]!.AsArray(), s => Assert.Equal("Point", s!["geometry"]!["type"]!.GetValue<string>()));
        var firstArea = body["sources"]![0]!["edges"]!.AsArray().Select(e => e!.GetValue<string>()).ToList();
        Assert.Equal(["L1#0", "L1#1"], firstArea);   // the edge up to the switch is entered, the one behind it is not
    }

    [Fact]
    public async Task Supply_with_a_closed_switch_supplies_the_customers_between_the_transformers_twice()
    {
        var (_, body) = await Post("/api/network/supply", Street(new { sources = new[] { "P1", "P5" } }));

        var supply = SupplyOf(body);
        Assert.Equal(["P1", "P5"], supply["P2"]);
        Assert.Equal(["P1", "P5"], supply["P4"]);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unknown")]
    public async Task Supply_needs_known_sources(string problem)
    {
        var (status, body) = await Post("/api/network/supply", Street(problem == "none" ? new { sources = Array.Empty<string>() } : new { sources = new[] { "P99" } }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(body["error"]!.GetValue<string>()));
    }

    private static object RuleRow(string id, string subject, string subjectTypes, string countTypes, int min, int? max,
        string severity = "Error", string endFilter = "All", string? endTypes = null) =>
        new { id, severity, subject, subjectTypes, endFilter, endTypes, countTypes, min, max };

    [Fact]
    public async Task Validate_reports_each_violation_with_the_place_of_its_node()
    {
        // The concept network: the cable ends at a helper node (100, 0) with nothing behind it, and the switch has two cables, not one.
        var rules = new[]
        {
            RuleRow("LEITUNG-OFFEN", "Edge", "Kabel", "*", 1, null, severity: "Warning", endFilter: "NodeIs", endTypes: "untyped"),
            RuleRow("SCHALTER", "Node", "Schalter", "Kabel", 1, 1),
            RuleRow("KUNDE", "Node", "Kunde", "Kastenverbinder | Kabel", 1, 1),
        };

        var (status, body) = await Post("/api/network/validate", Concept(new { rules }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["ok"]!.GetValue<bool>());
        Assert.Equal(["LEITUNG-OFFEN", "SCHALTER", "KUNDE"], body["rules"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()));
        Assert.Equal("LEITUNG-OFFEN: edges Kabel, ends untyped, other edges * >= 1 (warning)", body["rules"]![0]!["text"]!.GetValue<string>());

        var violations = body["violations"]!.AsArray().Select(v => v!.AsObject()).ToList();
        Assert.Equal(2, violations.Count);
        var open = violations.Single(v => v["rule"]!.GetValue<string>() == "LEITUNG-OFFEN");
        Assert.Equal(("Warning", "Edge", "L1#2", 0), (open["severity"]!.GetValue<string>(), open["kind"]!.GetValue<string>(), open["edge"]!.GetValue<string>(), open["count"]!.GetValue<int>()));
        Assert.Equal([100.0, 0.0], open["geometry"]!["coordinates"]!.AsArray().Select(c => Math.Round(c!.GetValue<double>(), 3)));
        var cables = violations.Single(v => v["rule"]!.GetValue<string>() == "SCHALTER");
        Assert.Equal(("Node", "P2", 2, 1, 1), (cables["kind"]!.GetValue<string>(), cables["node"]!.GetValue<string>(), cables["count"]!.GetValue<int>(), cables["min"]!.GetValue<int>(), cables["max"]!.GetValue<int>()));
        Assert.Null(cables["edge"]);
    }

    [Theory]
    [InlineData("noRules")]
    [InlineData("badPattern")]
    [InlineData("endsOnNode")]
    [InlineData("range")]
    [InlineData("severity")]
    public async Task Validate_needs_valid_rules(string problem)
    {
        var (id, rules) = problem switch
        {
            "noRules" => ("", Array.Empty<object>()),
            "badPattern" => ("PATTERN", [RuleRow("PATTERN", "Node", "Gibtsnicht", "Kabel", 1, null)]),
            "endsOnNode" => ("ENDS", [RuleRow("ENDS", "Node", "Kasten", "Kabel", 1, null, endFilter: "NodeIs", endTypes: "Kasten")]),
            "range" => ("RANGE", [RuleRow("RANGE", "Node", "Kasten", "Kabel", 3, 2)]),
            _ => ("SEVERITY", [RuleRow("SEVERITY", "Node", "Kasten", "Kabel", 1, null, severity: "Fatal")]),
        };

        var (status, body) = await Post("/api/network/validate", Concept(new { rules }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        string message = body["error"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains(id, message);   // a row that cannot be a rule names its id
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("tolerance")]
    [InlineData("nodeType")]
    [InlineData("edgeType")]
    [InlineData("wrongGeometry")]
    public async Task Invalid_input_is_an_error_message(string problem)
    {
        var request = problem switch
        {
            "empty" => Request([], []),
            "tolerance" => Request([], [Line(Cable, [0, 0], [1, 0])], new { tolerance = 0 }),
            "nodeType" => Request([Point(0, 0, 99)], []),
            "edgeType" => Request([], [Line(99, [0, 0], [1, 0])]),
            _ => Request([Line(Cable, [0, 0], [1, 0])], []),   // a line where a point is expected
        };

        var (status, body) = await Post("/api/network/build", request);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(body["error"]!.GetValue<string>()));
    }
}
