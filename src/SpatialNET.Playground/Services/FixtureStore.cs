using System.Text.Json.Nodes;
using ImpGeometryFactory = SpatialNET.Imp.ImpGeometryFactory;
using SpatialNET.IO;

namespace SpatialNET.Playground.Services;

/// <summary>
/// Delivers the fixtures from <c>samples/&lt;algorithm&gt;/</c> as example geometries (ADR 0008/0011).
/// With <c>crs</c> they are projected to WGS84, without <c>crs</c> they are local planar (L.CRS.Simple, ADR 0010).
/// </summary>
public sealed class FixtureStore(string root)
{
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "samples");

    /// <summary>
    /// The algorithms by M show the examples with the tag <c>m</c> of their length variants (whose lines carry M); the parameters
    /// of those examples (lengths) do not fit and are dropped, the defaults of the descriptor apply.
    /// </summary>
    private static readonly Dictionary<string, string> MAliases = new()
    {
        ["interpolateByM"] = "interpolate",
        ["projectToM"] = "project",
        ["substringByM"] = "substring",
        ["locateBetween"] = "substring",
        ["locateAlong"] = "interpolate",
    };

    /// <summary>
    /// Algorithms that share the example folder of another name, with the tag <c>playground</c> as usual (and their parameters): the
    /// triangulations and the terrain methods have no fixture folder of their own, since these are not oracle cases (ADR 0036 to 0038).
    /// </summary>
    private static readonly Dictionary<string, string> SharedFolders = new()
    {
        ["voronoiDiagram"] = "voronoi",
        ["voronoiEdges"] = "voronoi",
        ["delaunayEdges"] = "voronoi",
        ["triangulate"] = "triangulation",
        ["constrainedDelaunay"] = "triangulation",
        ["interpolateZ"] = "terrain",
        ["contour"] = "terrain",
        ["steepTriangles"] = "terrain",
        ["terrainVolume"] = "terrain",
        ["naturalNeighbour"] = "terrain",
        ["refine"] = "terrain",
        ["slopeZones"] = "terrain",
        ["contourBands"] = "terrain",
        ["lineOfSight"] = "terrain-sight",
        ["surfaceVolume"] = "terrain-compare",
        ["drape"] = "terrain-profile",
        ["locateEvents"] = "events",
        ["intersectEvents"] = "events",
    };

    /// <summary>The fixtures of an algorithm; without <paramref name="all"/> only those with the tag <c>playground</c>.</summary>
    public IReadOnlyList<JsonObject> For(string algorithm, bool all)
    {
        bool viaAlias = MAliases.TryGetValue(algorithm, out var source);
        var folder = Path.Combine(root, source ?? SharedFolders.GetValueOrDefault(algorithm) ?? algorithm);
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
            if (viaAlias ? !tags.Contains("m") : !all && !tags.Contains("playground"))
            {
                continue;
            }

            var dto = ToDto(algorithm, Path.GetFileNameWithoutExtension(file), node, tags);
            if (viaAlias)
            {
                dto["params"] = new JsonObject();
            }

            result.Add(dto);
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

        // WKT with M: GeoJSON cannot carry M, so the UI sends it as WKT as well (ADR 0015).
        string? WktWithM(string key)
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

            var text = g.ToWkt();
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Z]+ (ZM|M) ") ? text : null;
        }

        string? error = null;
        JsonNode? a = null, b = null;
        string? aWkt = null, bWkt = null;
        try
        {
            a = Geometry("a");
            b = Geometry("b");
            aWkt = WktWithM("a");
            bWkt = WktWithM("b");
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            // e.g. NaN coordinates: they cannot be shown as GeoJSON
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
            ["aWkt"] = aWkt,
            ["bWkt"] = bWkt,
            ["params"] = node["params"]?.DeepClone() ?? new JsonObject(),
            ["crs"] = crs,
            ["local"] = crs is null,
            ["error"] = error,
        };
    }
}
