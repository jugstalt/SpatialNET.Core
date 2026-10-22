using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SpatialNET.Playground.Algorithms;
using SpatialNET.Playground.Services;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Playground;

/// <summary>Jede öffentliche Methode von SpatialNET.Extensions hat einen Playground-Deskriptor (ADR 0011).</summary>
public class PlaygroundCompletenessTests
{
    /// <summary>Methoden ohne eigenen Eintrag, mit Begründung.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["AsList"] = "Adapter für IReadOnlyList, kein Algorithmus",
        ["Prepare"] = "Optimierung für wiederholte Prädikate; im Playground identisch zu den Prädikaten",
        ["ToSpatialIndex"] = "Datenstruktur (ADR 0014), kein Algorithmus mit Geometrie-Ergebnis",
        ["Query"] = "Abfrage auf einer Datenstruktur (ADR 0014), kein Algorithmus mit Geometrie-Ergebnis",
        ["Locate"] = "Die Antwort ist eine Kategorie (innen, Rand, außen), die die Oberfläche nicht darstellt (ADR 0022)",
        ["ToPolygon"] = "Umwandlung einer Envelope in ein Polygon, kein Algorithmus (ADR 0022)",
        ["UnionAll"] = "Vereinigung vieler Geometrien (ADR 0018); der Playground kennt nur zwei Operanden A und B",
        ["Transform"] = "Nimmt eine Funktion als Parameter, die die Oberfläche nicht darstellt (ADR 0025); Translate, Scale und Rotate decken sie ab",
    };

    [Theory]
    [MemberData(nameof(FixtureCompletenessTests.PublicMethods), MemberType = typeof(FixtureCompletenessTests))]
    public void Every_public_method_has_a_descriptor(string method)
    {
        if (Exempt.ContainsKey(method))
        {
            return;
        }

        var id = char.ToLowerInvariant(method[0]) + method[1..];
        Assert.Contains(PlaygroundAlgorithm.All, a => a.Id == id);
    }

    [Fact]
    public void Descriptors_are_unique_and_consistent()
    {
        Assert.Equal(PlaygroundAlgorithm.All.Count, PlaygroundAlgorithm.All.Select(a => a.Id).Distinct().Count());
        foreach (var a in PlaygroundAlgorithm.All)
        {
            Assert.NotEmpty(a.Inputs);
            foreach (var p in a.Parameters)
            {
                Assert.Contains(p.Type, new[] { "number", "int", "bool", "enum" });
                Assert.True(p.Type != "enum" || p.Values?.Contains((string)p.Default!) == true, $"{a.Id}.{p.Key}");
            }
        }
    }

    [Fact]
    public void Enum_parameters_match_the_library_enums()
    {
        var all = PlaygroundAlgorithm.All.SelectMany(a => a.Parameters).Where(p => p.Type == "enum").ToList();

        Assert.All(all.Where(p => p.Key == "join"), p => Assert.Equal(Enum.GetNames<JoinStyle>().Order(), p.Values!.Order()));
        Assert.All(all.Where(p => p.Key == "cap"), p => Assert.Equal(Enum.GetNames<CapStyle>().Order(), p.Values!.Order()));
        Assert.All(all.Where(p => p.Key == "fillRule"), p => Assert.Equal(Enum.GetNames<FillRule>().Order(), p.Values!.Order()));
    }

    /// <summary>Fixture-Parameter, die das Soll beschreiben statt den Aufruf (validate: erwartete Befunde).</summary>
    private static readonly string[] ExpectationParams = ["kinds"];

    [Fact]
    public void Fixture_parameters_are_known_to_the_descriptors()
    {
        foreach (var fixture in FixtureCatalog.Fixtures)
        {
            var algorithm = PlaygroundAlgorithm.All.SingleOrDefault(a => a.Id == fixture.Algorithm);
            if (algorithm is null) continue;
            foreach (var key in fixture.Params.Keys.Where(k => !ExpectationParams.Contains(k)))
            {
                Assert.True(algorithm.Parameters.Any(p => p.Key == key), $"{fixture.Id}: Parameter \"{key}\" is missing in the descriptor.");
            }
        }
    }
}

public class ProjectionTests
{
    [Theory]
    [InlineData(13.4, 52.5, "EPSG:32633")]
    [InlineData(-0.1, 51.5, "EPSG:32630")]
    [InlineData(151.2, -33.9, "EPSG:32756")]
    [InlineData(180, 0, "EPSG:32660")]
    [InlineData(-180, 0, "EPSG:32601")]
    public void Auto_UTM_picks_the_zone_of_the_center(double lon, double lat, string epsg)
    {
        Assert.Equal(epsg, Projection.AutoUtm(lon, lat));
    }

    [Fact]
    public void Round_trip_through_UTM_keeps_the_vertices()
    {
        var projection = new Projection("EPSG:32633");
        var line = Imp.ImpGeometryFactory.Instance.CreatePolyline([new Coordinate[] { new(13.4, 52.5), new(13.41, 52.51) }], null);

        var projected = (IPolyline2D)projection.ToProjected(line);
        var back = (IPolyline2D)projection.ToWgs84(projected);

        Assert.InRange(projected[0][0].X, 390_000, 400_000);
        Assert.InRange(projected[0][0].Y, 5_810_000, 5_830_000);
        Assert.Equal(13.4, back[0][0].X, 1e-9);
        Assert.Equal(52.51, back[0][1].Y, 1e-9);
        Assert.False(projection.IsGeographic);
        Assert.True(new Projection("EPSG:4326").IsGeographic);
    }
}

public class PlaygroundEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static JsonObject Square(double x, double y, double size) => JsonNode.Parse(string.Create(CultureInfo.InvariantCulture,
        $$"""{"type":"Polygon","coordinates":[[[{{x}},{{y}}],[{{x + size}},{{y}}],[{{x + size}},{{y + size}}],[{{x}},{{y + size}}],[{{x}},{{y}}]]]}"""))!.AsObject();

    private async Task<(HttpStatusCode Status, JsonObject Body)> Run(object request)
    {
        var response = await _client.PostAsJsonAsync("/api/run", request);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonObject>())!);
    }

    [Fact]
    public async Task Algorithms_lists_the_descriptors()
    {
        var list = await _client.GetFromJsonAsync<JsonArray>("/api/algorithms");

        var buffer = list!.Single(a => a!["id"]!.GetValue<string>() == "buffer")!;
        Assert.Equal("Construction", buffer["group"]!.GetValue<string>());
        Assert.Contains(buffer["parameters"]!.AsArray(), p => p!["key"]!.GetValue<string>() == "distance" && p["unit"]!.GetValue<string>() == "kbs");
    }

    [Fact]
    public async Task Fixtures_are_filtered_by_the_playground_tag_and_marked_local()
    {
        var playground = await _client.GetFromJsonAsync<JsonArray>("/api/fixtures?algorithm=buffer");
        var all = await _client.GetFromJsonAsync<JsonArray>("/api/fixtures?algorithm=buffer&all=true");

        Assert.NotEmpty(playground!);
        Assert.True(all!.Count > playground!.Count);
        Assert.All(playground, f => Assert.Contains(f!["tags"]!.AsArray(), t => t!.GetValue<string>() == "playground"));
        Assert.All(playground, f => Assert.True(f!["local"]!.GetValue<bool>()));
        Assert.All(playground, f => Assert.NotNull(f!["a"]!["type"]));
        Assert.Empty((await _client.GetFromJsonAsync<JsonArray>("/api/fixtures?algorithm=..%2Fsrc"))!);
    }

    [Fact]
    public async Task Buffer_in_auto_UTM_uses_meters()
    {
        // 0,001° Quadrat bei Berlin, 10 m Puffer
        var (status, body) = await Run(new
        {
            algorithm = "buffer",
            inputs = new { a = Square(13.4, 52.5, 0.001) },
            @params = new { distance = 10 },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("EPSG:32633 (auto)", body["kbs"]!.GetValue<string>());
        Assert.Equal("m", body["unit"]!.GetValue<string>());
        Assert.Equal("geometry", body["kind"]!.GetValue<string>());
        double area = body["stats"]!["area"]!.GetValue<double>();
        // ca. 68 m × 111 m plus Rand von 10 m
        Assert.InRange(area, 68 * 111 + 2 * 10 * (68 + 111), 68 * 111 + 2 * 10 * (68 + 111) + Math.PI * 100 + 500);
        var ring = body["geometry"]!["coordinates"]![0]![0]!;
        Assert.InRange(ring[0]!.GetValue<double>(), 13.39, 13.41);
    }

    [Fact]
    public async Task Local_mode_does_not_project()
    {
        var (status, body) = await Run(new
        {
            algorithm = "area",
            inputs = new { a = Square(0, 0, 10) },
            local = true,
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(100, body["value"]!.GetValue<double>(), 1e-9);
        Assert.Equal("local", body["kbs"]!.GetValue<string>());
    }

    [Fact]
    public async Task Value_results_are_described_by_kind()
    {
        async Task<JsonObject> Local(string algorithm, JsonObject a, JsonObject? b = null) =>
            (await Run(new { algorithm, inputs = b is null ? (object)new { a } : new { a, b }, local = true })).Body;

        Assert.Equal("212101212", (await Local("relate", Square(0, 0, 10), Square(5, 5, 10)))["value"]!.GetValue<string>());
        Assert.True((await Local("intersects", Square(0, 0, 10), Square(5, 5, 10)))["value"]!.GetValue<bool>());
        Assert.Equal("geometry", (await Local("centroid", Square(0, 0, 10)))["kind"]!.GetValue<string>());
        Assert.Equal("geometry", (await Local("envelope", Square(0, 0, 10)))["kind"]!.GetValue<string>());
        var validation = await Local("validate", JsonNode.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,10],[10,0],[0,10],[0,0]]]}""")!.AsObject());
        Assert.False(validation["value"]!.GetValue<bool>());
        Assert.NotEmpty(validation["issues"]!.AsArray());
        var cut = await Local("cut", Square(0, 0, 10), JsonNode.Parse("""{"type":"LineString","coordinates":[[5,-1],[5,11]]}""")!.AsObject());
        Assert.Equal(2, cut["geometries"]!.AsArray().Count);
    }

    [Fact]
    public async Task Errors_are_reported_as_bad_request()
    {
        var (status, body) = await Run(new { algorithm = "nope", inputs = new { }, local = true });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("nope", body["error"]!.GetValue<string>());

        (status, body) = await Run(new { algorithm = "buffer", inputs = new { }, local = true });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("missing", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Every_playground_fixture_runs()
    {
        var failures = new List<string>();
        foreach (var algorithm in PlaygroundAlgorithm.All)
        {
            var fixtures = await _client.GetFromJsonAsync<JsonArray>($"/api/fixtures?algorithm={algorithm.Id}");
            foreach (var fixture in fixtures!)
            {
                var inputs = new JsonObject { ["a"] = fixture!["a"]?.DeepClone() };
                if (fixture["b"] is { } b) inputs["b"] = b.DeepClone();
                // Fixtures mit M reichen ihr WKT mit (GeoJSON trägt kein M, ADR 0015)
                var wkt = new JsonObject();
                if (fixture["aWkt"] is { } aWkt) wkt["a"] = aWkt.DeepClone();
                if (fixture["bWkt"] is { } bWkt) wkt["b"] = bWkt.DeepClone();
                var (status, body) = await Run(new { algorithm = algorithm.Id, inputs, wkt, @params = fixture["params"], local = fixture["local"]!.GetValue<bool>() });
                if (status != HttpStatusCode.OK) failures.Add($"{fixture["id"]}: {body["error"]}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Web_Mercator_warns_with_the_local_scale()
    {
        var (status, body) = await Run(new { algorithm = "area", inputs = new { a = Square(13.4, 60, 0.001) }, kbs = "EPSG:3857" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("factor 2", body["warnings"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Unknown_KBS_is_a_bad_request()
    {
        var (status, body) = await Run(new { algorithm = "area", inputs = new { a = Square(13.4, 52.5, 0.001) }, kbs = "EPSG:999999" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("EPSG:999999", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_reads_all_geometries_of_a_file()
    {
        const string file = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[1,2]}},
              {"type":"Feature","properties":{},"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}]}
            """;

        var response = await _client.PostAsync("/api/import", new StringContent(file));
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Point", "LineString"], body!["geometries"]!.AsArray().Select(g => g!["type"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/import", new StringContent("""{"type":"Pointt"}"""))).StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/js/app.js")]
    [InlineData("/js/map.js")]
    [InlineData("/css/app.css")]
    [InlineData("/lib/leaflet/leaflet.js")]
    [InlineData("/lib/leaflet-geoman/leaflet-geoman.min.js")]
    public async Task Frontend_is_served_locally(string path)
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Explicit_geographic_KBS_warns()
    {
        var (status, body) = await Run(new { algorithm = "area", inputs = new { a = Square(13.4, 52.5, 0.001) }, kbs = "EPSG:4326" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(body["warnings"]!.AsArray());
        Assert.Equal(1e-6, body["value"]!.GetValue<double>(), 1e-12);
    }
}
