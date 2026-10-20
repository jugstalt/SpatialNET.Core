using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpatialNET.IO;

namespace SpatialNET.Tests.Fixtures;

/// <summary>Ein gespeicherter Testfall unter <c>samples/&lt;algorithmus&gt;/</c> (ADR 0008, Format in samples/README.md).</summary>
public sealed record Fixture
{
    [JsonIgnore] public string Algorithm { get; init; } = "";
    [JsonIgnore] public string Id { get; init; } = "";

    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Source { get; init; }
    public string[] Tags { get; init; } = [];
    public string A { get; init; } = "";
    public string? B { get; init; }
    public Dictionary<string, JsonElement> Params { get; init; } = [];
    public string? Crs { get; init; }
    public string? Expected { get; init; }
    public string? Divergence { get; init; }
    public Dictionary<string, double> Tolerance { get; init; } = [];

    public IGeometry2D ReadA(IGeometryFactory factory) => Wkt.Read(A, factory);

    public IGeometry2D ReadB(IGeometryFactory factory) =>
        Wkt.Read(B ?? throw new InvalidOperationException($"Fixture {Id} hat keinen Operanden b."), factory);

    public double Param(string name, double fallback) =>
        Params.TryGetValue(name, out var v) ? v.GetDouble() : fallback;

    public string? Param(string name) =>
        Params.TryGetValue(name, out var v) ? v.ToString() : null;

    public double ExpectedNumber() => double.Parse(Expected!, CultureInfo.InvariantCulture);

    public double ToleranceFor(string kind, double fallback) => Tolerance.GetValueOrDefault(kind, fallback);

    public override string ToString() => Id;
}

/// <summary>Lädt alle Fixtures und Tag-Ausnahmen aus dem Ausgabeverzeichnis.</summary>
public static class FixtureCatalog
{
    public static readonly string[] RequiredTags =
    [
        "empty", "degenerate-ring", "collinear-overlap", "vertex-touch", "hole-touch", "self-crossing",
        "near-coincident", "large-coords", "z", "multi", "nested",
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Lazy<Dictionary<string, Fixture>> All = new(Load);

    public static string Root => Path.Combine(AppContext.BaseDirectory, "samples");

    public static IReadOnlyCollection<Fixture> Fixtures => All.Value.Values;

    public static Fixture Get(string id) => All.Value[id];

    public static IEnumerable<Fixture> For(string algorithm) =>
        All.Value.Values.Where(f => f.Algorithm == algorithm).OrderBy(f => f.Id, StringComparer.Ordinal);

    /// <summary>Begründet ausgenommene Tags eines Algorithmus aus <c>_catalog.json</c>.</summary>
    public static IReadOnlyDictionary<string, string> NotApplicable(string algorithm)
    {
        var file = Path.Combine(Root, algorithm, "_catalog.json");
        if (!File.Exists(file))
        {
            return new Dictionary<string, string>();
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.GetProperty("n/a").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    /// <summary>Theory-Daten: jedes Fixture eines Algorithmus × jede Implementierung.</summary>
    public static TheoryData<string, string> Theory(string algorithm, Func<Fixture, bool>? where = null)
    {
        var data = new TheoryData<string, string>();
        foreach (var fixture in For(algorithm).Where(where ?? (_ => true)))
            foreach (var (implementation, _) in Oracle.Nts.Implementations)
                data.Add(fixture.Id, implementation);
        return data;
    }

    private static Dictionary<string, Fixture> Load()
    {
        var result = new Dictionary<string, Fixture>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith('_'))
            {
                continue;
            }

            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            // samples/<algorithmus>/… bzw. samples/external/<quelle>/<algorithmus>/…
            var algorithm = segments[0] == "external" ? segments[2] : segments[0];
            var id = relative[..^".json".Length];
            var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(file), Json)! with { Algorithm = algorithm, Id = id };
            if (fixture.Expected is not null && string.IsNullOrWhiteSpace(fixture.Divergence) && !fixture.Tags.Contains("external"))
            {
                throw new InvalidDataException($"Fixture {id}: 'expected' ohne 'divergence'-Begründung.");
            }

            result.Add(id, fixture);
        }

        return result;
    }
}

/// <summary>Markiert einen Test als Abdeckung einer öffentlichen Methode, für die es keine Fixtures gibt.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class CoversAttribute(string method) : Attribute
{
    public string Method { get; } = method;
}
