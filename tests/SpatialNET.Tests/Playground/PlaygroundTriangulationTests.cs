using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SpatialNET.Playground.Algorithms;

namespace SpatialNET.Tests.Playground;

/// <summary>The Voronoi, triangulation and terrain entries of the playground (ADR 0036 to 0038): every example runs, optional inputs work.</summary>
public class PlaygroundTriangulationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string> Ids() => new()
    {
        "voronoiDiagram", "voronoiEdges", "delaunayEdges", "triangulate", "constrainedDelaunay", "interpolateZ", "contour",
        "steepTriangles", "terrainVolume", "drape", "locateEvents", "intersectEvents", "naturalNeighbour", "refine", "slopeZones", "lineOfSight", "surfaceVolume",
    };

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Every_example_of_the_algorithm_runs(string id)
    {
        var fixtures = await _client.GetFromJsonAsync<JsonArray>($"/api/fixtures?algorithm={id}");
        Assert.NotEmpty(fixtures!);

        foreach (var fixture in fixtures!)
        {
            var descriptor = PlaygroundAlgorithm.All.Single(a => a.Id == id);
            var inputs = new JsonObject();
            foreach (var input in descriptor.Inputs)
            {
                if (fixture![input.Key] is { } geometry)
                {
                    inputs[input.Key] = geometry.DeepClone();
                }
            }

            // an example with M comes with its WKT, because GeoJSON carries no M
            var wkt = new JsonObject();
            foreach (var input in descriptor.Inputs)
            {
                if (fixture![input.Key + "Wkt"] is { } text)
                {
                    wkt[input.Key] = text.DeepClone();
                }
            }

            var response = await _client.PostAsJsonAsync("/api/run", new { algorithm = id, inputs, wkt, @params = fixture!["params"], local = true });
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{id}/{fixture["id"]}: {body}");
            if (id == "interpolateZ")
            {
                Assert.NotNull(body["value"]);   // the point lies inside the hull of the terrain points
            }
        }
    }

    [Fact]
    public async Task The_clip_area_of_the_Voronoi_diagram_is_optional()
    {
        var sites = JsonNode.Parse("""{"type":"MultiPoint","coordinates":[[0,0],[10,0],[5,8]]}""");

        var response = await _client.PostAsJsonAsync("/api/run", new { algorithm = "voronoiDiagram", inputs = new { a = sites }, local = true });
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body["geometries"]?.AsArray().Count ?? body["wkts"]!.AsArray().Count);

        var descriptors = await _client.GetFromJsonAsync<JsonArray>("/api/algorithms");
        var voronoi = descriptors!.Single(d => d!["id"]!.GetValue<string>() == "voronoiDiagram")!;
        Assert.False(voronoi["inputs"]![0]!["optional"]!.GetValue<bool>());
        Assert.True(voronoi["inputs"]![1]!["optional"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_missing_required_input_is_still_an_error()
    {
        var response = await _client.PostAsJsonAsync("/api/run", new { algorithm = "interpolateZ", inputs = new { a = JsonNode.Parse("""{"type":"MultiPoint","coordinates":[[0,0],[10,0],[5,8]]}""") }, local = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
