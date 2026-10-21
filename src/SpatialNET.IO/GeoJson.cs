using System.Text;
using System.Text.Json;
using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>Options for writing GeoJSON.</summary>
public sealed record GeoJsonOptions
{
    /// <summary>The default options.</summary>
    public static GeoJsonOptions Default { get; } = new();

    /// <summary>How Z is written; default <see cref="ZMode.Auto"/>.</summary>
    public ZMode ZMode { get; init; } = ZMode.Auto;

    /// <summary>Write indented.</summary>
    public bool Indented { get; init; }
}

/// <summary>
/// Reading and writing GeoJSON (RFC 7946, ADR 0009) via System.Text.Json. Reading goes through the
/// geometry factory; <c>properties</c> and a <c>crs</c> member are ignored, M values are dropped.
/// Writing uses the leanest type, with shells counter-clockwise and holes clockwise.
/// </summary>
public static class GeoJson
{
    // ---------- Lesen ----------

    /// <summary>Reads a geometry from a Geometry or Feature object.</summary>
    /// <param name="json">The GeoJSON text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">Not valid GeoJSON; the message names the JSON path.</exception>
    /// <exception cref="NotSupportedException">FeatureCollection or GeometryCollection (use <see cref="ReadAll(string, IGeometryFactory?)"/> instead).</exception>
    public static IGeometry2D Read(string json, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Read(Encoding.UTF8.GetBytes(json), factory);
    }

    /// <summary>Reads a geometry of an expected kind.</summary>
    /// <typeparam name="T">The expected geometry kind.</typeparam>
    /// <param name="json">The GeoJSON text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">Not valid GeoJSON or not a geometry of kind <typeparamref name="T"/>.</exception>
    public static T Read<T>(string json, IGeometryFactory? factory = null) where T : IGeometry2D =>
        Read(json, factory) is T geometry
            ? geometry
            : throw new FormatException($"The GeoJSON does not describe a geometry of kind {typeof(T).Name}.");

    /// <summary>Reads a geometry from UTF-8 bytes.</summary>
    /// <param name="utf8">The UTF-8 bytes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IGeometry2D Read(ReadOnlySpan<byte> utf8, IGeometryFactory? factory = null)
    {
        using var doc = Parse(utf8);
        return new Reader(GeometryFactory.Resolve(factory)).Single(doc.RootElement);
    }

    /// <summary>Reads a geometry from a stream (UTF-8).</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IGeometry2D Read(Stream stream, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var doc = Parse(stream);
        return new Reader(GeometryFactory.Resolve(factory)).Single(doc.RootElement);
    }

    /// <summary>Reads all geometries (Geometry, Feature, FeatureCollection; GeometryCollections are flattened).</summary>
    /// <param name="json">The GeoJSON text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IReadOnlyList<IGeometry2D> ReadAll(string json, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return ReadAll(Encoding.UTF8.GetBytes(json), factory);
    }

    /// <summary>Reads all geometries from UTF-8 bytes.</summary>
    /// <param name="utf8">The UTF-8 bytes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IReadOnlyList<IGeometry2D> ReadAll(ReadOnlySpan<byte> utf8, IGeometryFactory? factory = null)
    {
        using var doc = Parse(utf8);
        var result = new List<IGeometry2D>();
        new Reader(GeometryFactory.Resolve(factory)).All(doc.RootElement, "$", result);
        return result;
    }

    /// <summary>Reads all geometries from a stream (UTF-8).</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IReadOnlyList<IGeometry2D> ReadAll(Stream stream, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var doc = Parse(stream);
        var result = new List<IGeometry2D>();
        new Reader(GeometryFactory.Resolve(factory)).All(doc.RootElement, "$", result);
        return result;
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            return JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid JSON: {e.Message}", e);
        }
    }

    private static JsonDocument Parse(Stream stream)
    {
        try
        {
            return JsonDocument.Parse(stream);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid JSON: {e.Message}", e);
        }
    }

    private sealed class Reader(IGeometryFactory factory)
    {
        public IGeometry2D Single(JsonElement root)
        {
            string type = Type(root, "$");
            return type switch
            {
                "Feature" => Geometry(Member(root, "geometry", "$"), "$.geometry")
                             ?? throw Error("$.geometry", "Feature without geometry"),
                "FeatureCollection" or "GeometryCollection" => throw new NotSupportedException(
                    $"{type} is not a single geometry (ADR 0009); GeoJson.ReadAll reads it as a list."),
                _ => Geometry(root, "$")!,
            };
        }

        public void All(JsonElement element, string path, List<IGeometry2D> result)
        {
            switch (Type(element, path))
            {
                case "FeatureCollection":
                    var features = Member(element, "features", path);
                    if (features.ValueKind != JsonValueKind.Array) throw Error(path + ".features", "Array expected");
                    int i = 0;
                    foreach (var feature in features.EnumerateArray()) All(feature, $"{path}.features[{i++}]", result);
                    break;
                case "Feature":
                    if (element.TryGetProperty("geometry", out var g) && g.ValueKind != JsonValueKind.Null)
                        All(g, path + ".geometry", result);
                    break;
                case "GeometryCollection":
                    var geometries = Member(element, "geometries", path);
                    int k = 0;
                    foreach (var child in geometries.EnumerateArray()) All(child, $"{path}.geometries[{k++}]", result);
                    break;
                default:
                    result.Add(Geometry(element, path)!);
                    break;
            }
        }

        public IGeometry2D? Geometry(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Null) return null;
            string type = Type(element, path);
            if (type == "GeometryCollection")
            {
                throw new NotSupportedException("GeometryCollection is not a single geometry (ADR 0009); GeoJson.ReadAll reads it as a list.");
            }

            var c = Member(element, "coordinates", path);
            string cp = path + ".coordinates";
            switch (type)
            {
                case "Point":
                    if (c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 0) return factory.CreateMultiPoint([], null);
                    return factory.CreatePoint(Position(c, cp), null);
                case "MultiPoint":
                    return factory.CreateMultiPoint(Positions(c, cp), null);
                case "LineString":
                    var line = Positions(c, cp);
                    return factory.CreatePolyline(line.Length == 0 ? [] : [line], null);
                case "MultiLineString":
                    return factory.CreatePolyline(Array(c, cp).Select((p, i) => (ReadOnlyMemory<Coordinate>)Positions(p, $"{cp}[{i}]"))
                        .Where(p => p.Length > 0).ToArray(), null);
                case "Polygon":
                    var rings = Rings(c, cp);
                    return factory.CreatePolygon(rings.Length == 0 ? [] : [Part(rings)], null);
                case "MultiPolygon":
                    return factory.CreatePolygon(Array(c, cp).Select((p, i) => Rings(p, $"{cp}[{i}]"))
                        .Where(r => r.Length > 0).Select(Part).ToArray(), null);
                default:
                    throw Error(path + ".type", $"unknown geometry type '{type}'");
            }
        }

        private static PolygonPart Part(Coordinate[][] rings) =>
            new(rings[0], rings.Skip(1).Select(r => (ReadOnlyMemory<Coordinate>)r).ToArray());

        private static Coordinate[][] Rings(JsonElement c, string path) =>
            Array(c, path).Select((r, i) => Positions(r, $"{path}[{i}]")).Where(r => r.Length > 0).ToArray();

        private static Coordinate[] Positions(JsonElement c, string path) =>
            Array(c, path).Select((p, i) => Position(p, $"{path}[{i}]")).ToArray();

        private static Coordinate Position(JsonElement p, string path)
        {
            var values = Array(p, path).ToArray();
            if (values.Length < 2) throw Error(path, "Position with at least 2 numbers expected");
            double Number(int i) => values[i].ValueKind == JsonValueKind.Number ? values[i].GetDouble() : throw Error($"{path}[{i}]", "Number expected");
            return values.Length >= 3 ? new Coordinate(Number(0), Number(1), Number(2)) : new Coordinate(Number(0), Number(1));
        }

        private static IEnumerable<JsonElement> Array(JsonElement e, string path) =>
            e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw Error(path, "Array expected");

        private static string Type(JsonElement e, string path)
        {
            if (e.ValueKind != JsonValueKind.Object) throw Error(path, "Object expected");
            var type = Member(e, "type", path);
            return type.ValueKind == JsonValueKind.String ? type.GetString()! : throw Error(path + ".type", "String expected");
        }

        private static JsonElement Member(JsonElement e, string name, string path) =>
            e.TryGetProperty(name, out var value) ? value : throw Error(path, $"Member '{name}' is missing");

        private static FormatException Error(string path, string message) => new($"Invalid GeoJSON at {path}: {message}.");
    }

    // ---------- Schreiben ----------

    /// <summary>Writes the geometry as a GeoJSON Geometry object.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">Coordinates are <c>NaN</c>/infinite (not representable in JSON) or the geometry kind is unknown.</exception>
    public static string ToGeoJson(this IGeometry2D geometry, GeoJsonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        options ??= GeoJsonOptions.Default;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options.Indented }))
        {
            Write(writer, geometry, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the geometry as a GeoJSON Geometry object to a <see cref="Utf8JsonWriter"/>.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="writer">The writer.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
    public static void WriteTo(this IGeometry2D geometry, Utf8JsonWriter writer, GeoJsonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, geometry, options ?? GeoJsonOptions.Default);
    }

    /// <summary>Writes geometries as a FeatureCollection with empty <c>properties</c>.</summary>
    /// <param name="geometries">The geometries.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
    public static string ToFeatureCollection(IEnumerable<IGeometry2D> geometries, GeoJsonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        options ??= GeoJsonOptions.Default;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options.Indented }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            writer.WriteStartArray("features");
            foreach (var g in geometries)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "Feature");
                writer.WritePropertyName("geometry");
                Write(writer, g, options);
                writer.WriteStartObject("properties");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter w, IGeometry2D geometry, GeoJsonOptions options)
    {
        w.WriteStartObject();
        switch (geometry)
        {
            case IPoint2D p:
                var c = p.ToCoordinate();
                bool pz = WithZ(options.ZMode, [c]);
                w.WriteString("type", "Point");
                w.WritePropertyName("coordinates");
                Position(w, c, pz);
                break;
            case IMultiPoint2D mp:
                var points = Rings.Coordinates(mp);
                w.WriteString("type", "MultiPoint");
                w.WritePropertyName("coordinates");
                Sequence(w, points, WithZ(options.ZMode, points));
                break;
            case IPolyline2D line:
                var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(p => p.Length > 0).ToArray();
                bool lz = WithZ(options.ZMode, paths.SelectMany(x => x));
                w.WriteString("type", paths.Length == 1 || paths.Length == 0 ? "LineString" : "MultiLineString");
                w.WritePropertyName("coordinates");
                if (paths.Length <= 1) Sequence(w, paths.Length == 0 ? [] : paths[0], lz);
                else
                {
                    w.WriteStartArray();
                    foreach (var path in paths) Sequence(w, path, lz);
                    w.WriteEndArray();
                }

                break;
            case IPolygon2D polygon:
                var parts = PolygonStructure.Build(polygon);
                bool az = WithZ(options.ZMode, parts.SelectMany(part => part.Holes.Prepend(part.Shell)).SelectMany(r => r));
                w.WriteString("type", parts.Count <= 1 ? "Polygon" : "MultiPolygon");
                w.WritePropertyName("coordinates");
                if (parts.Count <= 1)
                {
                    PartArray(w, parts.Count == 0 ? null : parts[0], az);
                }
                else
                {
                    w.WriteStartArray();
                    foreach (var part in parts) PartArray(w, part, az);
                    w.WriteEndArray();
                }

                break;
            default:
                throw new NotSupportedException($"{geometry.GetType().FullName} is not a known geometry kind.");
        }

        w.WriteEndObject();
    }

    private static void PartArray(Utf8JsonWriter w, Part? part, bool z)
    {
        w.WriteStartArray();
        if (part is not null)
        {
            foreach (var ring in part.Holes.Prepend(part.Shell)) Sequence(w, ring, z);
        }

        w.WriteEndArray();
    }

    private static void Sequence(Utf8JsonWriter w, Coordinate[] coordinates, bool z)
    {
        w.WriteStartArray();
        foreach (var c in coordinates) Position(w, c, z);
        w.WriteEndArray();
    }

    private static void Position(Utf8JsonWriter w, Coordinate c, bool z)
    {
        if (!double.IsFinite(c.X) || !double.IsFinite(c.Y) || (z && c.HasZ && !double.IsFinite(c.Z)))
        {
            throw new NotSupportedException("NaN or infinite coordinates cannot be represented in GeoJSON.");
        }

        w.WriteStartArray();
        w.WriteNumberValue(c.X);
        w.WriteNumberValue(c.Y);
        if (z) w.WriteNumberValue(c.HasZ ? c.Z : 0);
        w.WriteEndArray();
    }

    private static bool WithZ(ZMode mode, IEnumerable<Coordinate> coordinates) => mode switch
    {
        ZMode.Always => true,
        ZMode.Never => false,
        _ => coordinates.Any(c => c.HasZ && c.Z != 0),
    };
}
