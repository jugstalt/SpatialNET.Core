using System.Text.Json.Nodes;
using ImpGeometryFactory = SpatialNET.Imp.ImpGeometryFactory;
using SpatialNET.IO;

namespace SpatialNET.Playground.Services;

/// <summary>
/// Liefert die Fixtures aus <c>samples/&lt;algorithmus&gt;/</c> als Beispielgeometrien (ADR 0008/0011).
/// Mit <c>crs</c> werden sie nach WGS84 projiziert, ohne <c>crs</c> sind sie lokal planar (L.CRS.Simple, ADR 0010).
/// </summary>
public sealed class FixtureStore(string root)
{
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "samples");

    /// <summary>Die Fixtures eines Algorithmus; ohne <paramref name="all"/> nur die mit Tag <c>playground</c>.</summary>
    public IReadOnlyList<JsonObject> For(string algorithm, bool all)
    {
        var folder = Path.Combine(root, algorithm);
        if (!Directory.Exists(folder) || algorithm.Contains("..") || algorithm.IndexOfAny(['/', '\\']) >= 0)
        {
            return [];
        }

        var result = new List<JsonObject>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).StartsWith('_'))
            {
                continue;
            }

            var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var tags = node["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToArray() ?? [];
            if (!all && !tags.Contains("playground"))
            {
                continue;
            }

            result.Add(ToDto(algorithm, Path.GetFileNameWithoutExtension(file), node, tags));
        }

        return result;
    }

    private static JsonObject ToDto(string algorithm, string id, JsonObject node, string[] tags)
    {
        var crs = node["crs"]?.GetValue<string>();
        var projection = crs is null ? null : new Projection(crs);

        JsonNode? Geometry(string key)
        {
            var wkt = node[key]?.GetValue<string>();
            if (wkt is null)
            {
                return null;
            }

            var g = Wkt.Read(wkt, ImpGeometryFactory.Instance);
            if (projection is not null)
            {
                g = projection.ToWgs84(g);
            }

            return JsonNode.Parse(g.ToGeoJson());
        }

        string? error = null;
        JsonNode? a = null, b = null;
        try
        {
            a = Geometry("a");
            b = Geometry("b");
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            // z. B. NaN-Koordinaten: nicht als GeoJSON darstellbar
            error = e.Message;
        }

        return new JsonObject
        {
            ["id"] = $"{algorithm}/{id}",
            ["name"] = node["name"]?.GetValue<string>() ?? id,
            ["description"] = node["description"]?.DeepClone(),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray()),
            ["a"] = a,
            ["b"] = b,
            ["params"] = node["params"]?.DeepClone() ?? new JsonObject(),
            ["crs"] = crs,
            ["local"] = crs is null,
            ["error"] = error,
        };
    }
}
