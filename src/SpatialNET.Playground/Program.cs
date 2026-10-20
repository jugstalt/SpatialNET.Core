using System.Text.Json;
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

app.Run();

/// <summary>Einstiegspunkt; öffentlich für Endpunkt-Tests.</summary>
public partial class Program;
