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
