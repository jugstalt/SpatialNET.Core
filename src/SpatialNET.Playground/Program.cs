using System.Text.Json;
using System.Text.Json.Nodes;
using SpatialNET;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Playground.Algorithms;
using SpatialNET.Playground.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new FixtureStore(FixtureStore.DefaultRoot));
builder.Services.AddSingleton<RunService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Deskriptoren aller Algorithmen; die UI entsteht daraus (ADR 0011).
app.MapGet("/api/algorithms", () => PlaygroundAlgorithm.All.Select(a => new
{
    a.Id,
    a.Name,
    a.Group,
    a.Inputs,
    a.Parameters,
}));

app.MapGet("/api/fixtures", (string algorithm, bool? all, FixtureStore store) => Results.Json(store.For(algorithm, all ?? false)));

app.MapPost("/api/run", (RunRequest request, RunService service) =>
{
    try
    {
        return Results.Json(service.Run(request));
    }
    catch (Exception e)
    {
        // Das Playground zeigt jeden Fehler des Algorithmus bzw. der Projektion als Meldung an (ADR 0011).
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

// GeoJSON-Upload: der Client schickt die Datei, gelesen wird mit GeoJson.ReadAll (ADR 0009/0011).
app.MapPost("/api/import", async (HttpRequest request) =>
{
    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body);
    try
    {
        var geometries = GeoJson.ReadAll(body.ToArray(), ImpGeometryFactory.Instance);
        return Results.Json(new { geometries = geometries.Select(g => JsonNode.Parse(g.ToGeoJson())).ToList() });
    }
    catch (Exception e) when (e is FormatException or NotSupportedException or JsonException)
    {
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

// WKT <-> GeoJSON: die Oberfläche zeigt und liest WKT (mit Z und M), die Karte zeichnet GeoJSON (ADR 0015/0011).
app.MapPost("/api/convert", (ConvertRequest request) =>
{
    try
    {
        IGeometry2D geometry = !string.IsNullOrWhiteSpace(request.Wkt)
            ? Wkt.Read(request.Wkt, ImpGeometryFactory.Instance)
            : request.Geometry is { ValueKind: JsonValueKind.Object } json
                ? GeoJson.Read(json.GetRawText(), ImpGeometryFactory.Instance)
                : throw new ArgumentException("Weder „wkt“ noch „geometry“ angegeben.");
        return Results.Json(new { geometry = JsonNode.Parse(geometry.ToGeoJson()), wkt = geometry.ToWkt() });
    }
    catch (Exception e) when (e is FormatException or NotSupportedException or ArgumentException or JsonException)
    {
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

app.Run();

/// <summary>Einstiegspunkt; öffentlich für Endpunkt-Tests.</summary>
public partial class Program;
