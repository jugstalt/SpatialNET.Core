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

// Descriptors of all algorithms; the UI is built from them (ADR 0011).
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
        // The playground shows every error of the algorithm or the projection as a message (ADR 0011).
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

// GeoJSON upload: the client sends the file, it is read with GeoJson.ReadAll (ADR 0009/0011).
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

// WKT <-> GeoJSON: the UI shows and reads WKT (with Z and M), the map draws GeoJSON (ADR 0015/0011).
app.MapPost("/api/convert", (ConvertRequest request) =>
{
    try
    {
        IGeometry2D geometry = !string.IsNullOrWhiteSpace(request.Wkt)
            ? Wkt.Read(request.Wkt, ImpGeometryFactory.Instance)
            : request.Geometry is { ValueKind: JsonValueKind.Object } json
                ? GeoJson.Read(json.GetRawText(), ImpGeometryFactory.Instance)
                : throw new ArgumentException("Neither \"wkt\" nor \"geometry\" was given.");
        return Results.Json(new { geometry = JsonNode.Parse(geometry.ToGeoJson()), wkt = geometry.ToWkt() });
    }
    catch (Exception e) when (e is FormatException or NotSupportedException or ArgumentException or JsonException)
    {
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

app.Run();

/// <summary>Entry point; public for the endpoint tests.</summary>
public partial class Program;
