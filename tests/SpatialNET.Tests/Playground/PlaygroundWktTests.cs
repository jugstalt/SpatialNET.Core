using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Playground.Services;

namespace SpatialNET.Tests.Playground;

/// <summary>WKT in the playground: input, output, conversion, and M that survives the way through (ADR 0015, ADR 0011).</summary>
public class PlaygroundWktTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string RoadWithM = "LINESTRING ZM (0 0 0 0, 10 0 10 100, 10 10 30 250)";

    private async Task<(HttpStatusCode Status, JsonObject Body)> Post(string url, object request)
    {
        var response = await _client.PostAsJsonAsync(url, request);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonObject>())!);
    }

    [Fact]
    public async Task A_WKT_input_with_M_reaches_the_algorithm()
    {
        var (status, body) = await Post("/api/run", new { algorithm = "interpolateByM", wkt = new { a = RoadWithM }, @params = new { m = 150.0 }, local = true });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("M = 150", body["text"]!.GetValue<string>());
        Assert.StartsWith("POINT ZM (10 3.3", body["wkt"]!.GetValue<string>());
        Assert.Equal("Point", body["geometry"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task LocateBetween_and_LocateAlong_run_on_a_WKT_with_M()
    {
        var (status, between) = await Post("/api/run", new { algorithm = "locateBetween", wkt = new { a = RoadWithM }, @params = new { fromM = 50.0, toM = 150.0 }, local = true });
        var (_, along) = await Post("/api/run", new { algorithm = "locateAlong", wkt = new { a = RoadWithM }, @params = new { m = 150.0 }, local = true });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.StartsWith("LINESTRING ZM (", between["wkt"]!.GetValue<string>());
        Assert.StartsWith("MULTIPOINT ZM (", along["wkt"]!.GetValue<string>());
    }

    [Fact]
    public async Task WKT_wins_over_the_GeoJSON_of_the_same_input()
    {
        var geoJson = JsonNode.Parse("""{"type":"LineString","coordinates":[[0,0],[1,0]]}""");

        var (_, body) = await Post("/api/run", new { algorithm = "length", inputs = new { a = geoJson }, wkt = new { a = "LINESTRING (0 0, 5 0)" }, local = true });

        Assert.Equal(5, body["value"]!.GetValue<double>(), 12);
    }

    [Fact]
    public async Task WKT_results_are_returned_and_keep_M_when_projected()
    {
        // 13.4 52.5 .. in the map mode the geometry runs through the projection and back
        var (status, body) = await Post("/api/run", new
        {
            algorithm = "substringByM",
            wkt = new { a = "LINESTRING M (13.4 52.5 0, 13.41 52.5 100, 13.41 52.51 250)" },
            @params = new { fromM = 50.0, toM = 200.0 },
            kbs = "EPSG:32633",
        });

        Assert.Equal(HttpStatusCode.OK, status);
        var wkt = body["wkt"]!.GetValue<string>();
        Assert.StartsWith("LINESTRING M (", wkt);
        var result = Wkt.Read<IPolyline2D>(wkt, ImpGeometryFactory.Instance);
        var first = Assert.IsAssignableFrom<IPointM>(result[0][0]);
        Assert.Equal(50, first.M, 6);
        Assert.Equal(13.405, result[0][0].X, 6); // back in degrees
    }

    [Fact]
    public async Task A_list_of_results_comes_with_one_WKT_each()
    {
        var (status, body) = await Post("/api/run", new { algorithm = "cut", wkt = new { a = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", b = "LINESTRING (5 -1, 5 11)" }, local = true });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body["wkts"]!.AsArray().Count);
        Assert.All(body["wkts"]!.AsArray(), w => Assert.StartsWith("POLYGON", w!.GetValue<string>()));
    }

    [Fact]
    public async Task Invalid_WKT_is_a_bad_request_that_names_the_input()
    {
        var (status, body) = await Post("/api/run", new { algorithm = "area", wkt = new { a = "POLYGON ((0 0, 1" }, local = true });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("(a)", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Convert_goes_both_ways()
    {
        var (status, fromWkt) = await Post("/api/convert", new { wkt = RoadWithM });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("LineString", fromWkt["geometry"]!["type"]!.GetValue<string>());
        Assert.Equal(RoadWithM, fromWkt["wkt"]!.GetValue<string>());
        Assert.Equal(3, fromWkt["geometry"]!["coordinates"]!.AsArray()[0]!.AsArray().Count); // GeoJSON has X, Y and Z, no M

        var geoJson = JsonNode.Parse("""{"type":"Point","coordinates":[1,2]}""");
        var (_, fromGeoJson) = await Post("/api/convert", new { geometry = geoJson });
        Assert.Equal("POINT (1 2)", fromGeoJson["wkt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Convert_rejects_missing_and_invalid_input()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/convert", new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/convert", new { wkt = "POINTT (1 2)" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/convert", new { wkt = "GEOMETRYCOLLECTION (POINT (1 2))" })).Status);
    }

    [Fact]
    public async Task Fixtures_with_M_come_with_their_WKT_and_the_M_algorithms_get_them()
    {
        var fixtures = await _client.GetFromJsonAsync<JsonArray>("/api/fixtures?algorithm=interpolateByM");

        var fixture = Assert.Single(fixtures!)!;
        Assert.Equal(RoadWithM, fixture["aWkt"]!.GetValue<string>());
        Assert.Empty(fixture["params"]!.AsObject()); // the lengths of the other algorithm do not apply
        Assert.True(fixture["local"]!.GetValue<bool>());

        // fixtures without M carry no WKT
        var plain = await _client.GetFromJsonAsync<JsonArray>("/api/fixtures?algorithm=interpolate");
        Assert.All(plain!, f => Assert.Null(f!["aWkt"]));
    }

    private const string Line = "LINESTRING (0 0, 10 3)";

    [Fact]
    public async Task A_predicate_shows_the_distance_and_says_when_a_false_is_only_rounding()
    {
        // (3.3 0.99) is on the line mathematically, but its coordinates are rounded: the exact predicate says no
        var (_, nearly) = await Post("/api/run", new { algorithm = "intersects", wkt = new { a = "POINT (3.3 0.99)", b = Line }, local = true });
        Assert.False(nearly["value"]!.GetValue<bool>());
        Assert.InRange(nearly["distance"]!.GetValue<double>(), 1e-17, 1e-12);
        Assert.True(nearly["nearlyTouching"]!.GetValue<bool>());
        Assert.Null(nearly["withinTolerance"]);

        // snapping B to A first (ADR 0046) inserts the point into the line: now it meets
        var (_, snapped) = await Post("/api/run", new { algorithm = "intersects", wkt = new { a = "POINT (3.3 0.99)", b = Line }, @params = new { snapTolerance = 1e-9 }, local = true });
        Assert.True(snapped["value"]!.GetValue<bool>());
        Assert.Equal(0, snapped["distance"]!.GetValue<double>());
        Assert.Contains(snapped["warnings"]!.AsArray(), w => w!.GetValue<string>().StartsWith("Snap moved", StringComparison.Ordinal));

        // exactly on the line, and a vertex: true, distance 0, nothing to note
        var (_, exact) = await Post("/api/run", new { algorithm = "intersects", wkt = new { a = "POINT (5 1.5)", b = Line }, local = true });
        Assert.True(exact["value"]!.GetValue<bool>());
        Assert.Equal(0, exact["distance"]!.GetValue<double>());
        Assert.False(exact["nearlyTouching"]!.GetValue<bool>());

        var (_, far) = await Post("/api/run", new { algorithm = "intersects", wkt = new { a = "POINT (5 5)", b = Line }, local = true });
        Assert.False(far["value"]!.GetValue<bool>());
        Assert.False(far["nearlyTouching"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("intersects", "a.Intersects(b.SnapTo(a, t, SnapMode.Vertices))  or  a.IsWithinDistance(b, t)")]
    [InlineData("disjoint", "a.Disjoint(b.SnapTo(a, t, SnapMode.Vertices))  or  !a.IsWithinDistance(b, t)")]
    [InlineData("contains", "a.Buffer(t).Contains(b)")]
    [InlineData("within", "b.Buffer(t).Contains(a)")]
    [InlineData("covers", "a.Buffer(t).Covers(b)")]
    [InlineData("coveredBy", "b.Buffer(t).Covers(a)")]
    [InlineData("touches", "a.Touches(b.SnapTo(a, t, SnapMode.Vertices))  or  a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b)  (areas)")]
    [InlineData("crosses", "a.Crosses(b.SnapTo(a, t, SnapMode.Vertices))")]
    [InlineData("equalsTopo", "a.EqualsTopo(b.SnapTo(a, t, SnapMode.Vertices))  or  a.HausdorffDistance(b) <= t")]
    public async Task A_predicate_names_the_alternative_with_a_tolerance(string algorithm, string alternative)
    {
        var (_, body) = await Post("/api/run", new { algorithm, wkt = new { a = "POINT (3.3 0.99)", b = Line }, local = true });

        Assert.Equal(alternative, body["alternative"]!.GetValue<string>());
        Assert.False(body.ContainsKey("withinTolerance"));
    }

    [Fact]
    public async Task Touches_overlaps_and_crosses_name_their_alternative_too_and_empty_input_has_no_distance()
    {
        foreach (var algorithm in new[] { "touches", "overlaps", "crosses" })
        {
            var (_, body) = await Post("/api/run", new { algorithm, wkt = new { a = "POINT (3.3 0.99)", b = Line }, local = true });
            Assert.False(string.IsNullOrWhiteSpace(body["alternative"]!.GetValue<string>()), algorithm);
        }

        var (_, empty) = await Post("/api/run", new { algorithm = "intersects", wkt = new { a = "POINT EMPTY", b = Line }, local = true });
        Assert.Null(empty["distance"]);
        Assert.False(empty["nearlyTouching"]!.GetValue<bool>());
    }

    [Fact]
    public void The_projection_keeps_Z_and_M()
    {
        var projection = new Projection("EPSG:32633");
        var line = ImpGeometryFactory.Instance.CreatePolyline([new Coordinate[] { new(13.4, 52.5, 100, 7), new(13.41, 52.51, 110, 9) }], null);

        var back = (IPolyline2D)projection.ToWgs84(projection.ToProjected(line));

        var point = back[0][0];
        Assert.Equal(100, Assert.IsAssignableFrom<IPoint3D>(point).Z, 12);
        Assert.Equal(7, Assert.IsAssignableFrom<IPointM>(point).M, 12);
        Assert.Equal(13.4, point.X, 1e-9);
    }
}
