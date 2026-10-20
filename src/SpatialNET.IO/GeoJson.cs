using System.Text;
using System.Text.Json;
using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>Optionen für das Schreiben von GeoJSON.</summary>
public sealed record GeoJsonOptions
{
    /// <summary>Die Standard-Optionen.</summary>
    public static GeoJsonOptions Default { get; } = new();

    /// <summary>Wie Z geschrieben wird; Standard <see cref="ZMode.Auto"/>.</summary>
    public ZMode ZMode { get; init; } = ZMode.Auto;

    /// <summary>Eingerückt schreiben.</summary>
    public bool Indented { get; init; }
}

/// <summary>
/// Lesen und Schreiben von GeoJSON (RFC 7946, ADR 0009) über System.Text.Json. Gelesen wird über die
/// Geometrie-Factory; <c>properties</c> und ein <c>crs</c>-Member werden ignoriert, M-Werte verworfen.
/// Geschrieben wird der schlankste Typ mit Außenringen gegen und Löchern im Uhrzeigersinn.
/// </summary>
public static class GeoJson
{
    // ---------- Lesen ----------

    /// <summary>Liest eine Geometrie aus einem Geometry- oder Feature-Objekt.</summary>
    /// <param name="json">Der GeoJSON-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> ist <c>null</c>.</exception>
    /// <exception cref="FormatException">Kein gültiges GeoJSON; die Meldung nennt den JSON-Pfad.</exception>
    /// <exception cref="NotSupportedException">FeatureCollection bzw. GeometryCollection (dafür <see cref="ReadAll(string, IGeometryFactory?)"/>).</exception>
    public static IGeometry2D Read(string json, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Read(Encoding.UTF8.GetBytes(json), factory);
    }

    /// <summary>Liest eine Geometrie einer erwarteten Art.</summary>
    /// <typeparam name="T">Die erwartete Geometrie-Art.</typeparam>
    /// <param name="json">Der GeoJSON-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">Kein gültiges GeoJSON oder keine Geometrie der Art <typeparamref name="T"/>.</exception>
    public static T Read<T>(string json, IGeometryFactory? factory = null) where T : IGeometry2D =>
        Read(json, factory) is T geometry
            ? geometry
            : throw new FormatException($"Das GeoJSON beschreibt keine Geometrie der Art {typeof(T).Name}.");

    /// <summary>Liest eine Geometrie aus UTF-8-Bytes.</summary>
    /// <param name="utf8">Die UTF-8-Bytes.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IGeometry2D Read(ReadOnlySpan<byte> utf8, IGeometryFactory? factory = null)
    {
        using var doc = Parse(utf8);
        return new Reader(GeometryFactory.Resolve(factory)).Single(doc.RootElement);
    }

    /// <summary>Liest eine Geometrie aus einem Stream (UTF-8).</summary>
    /// <param name="stream">Der Stream.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IGeometry2D Read(Stream stream, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var doc = Parse(stream);
        return new Reader(GeometryFactory.Resolve(factory)).Single(doc.RootElement);
    }

    /// <summary>Liest alle Geometrien (Geometry, Feature, FeatureCollection; GeometryCollections flachgeklopft).</summary>
    /// <param name="json">Der GeoJSON-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IReadOnlyList<IGeometry2D> ReadAll(string json, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return ReadAll(Encoding.UTF8.GetBytes(json), factory);
    }

    /// <summary>Liest alle Geometrien aus UTF-8-Bytes.</summary>
    /// <param name="utf8">Die UTF-8-Bytes.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    public static IReadOnlyList<IGeometry2D> ReadAll(ReadOnlySpan<byte> utf8, IGeometryFactory? factory = null)
    {
        using var doc = Parse(utf8);
        var result = new List<IGeometry2D>();
        new Reader(GeometryFactory.Resolve(factory)).All(doc.RootElement, "$", result);
        return result;
    }

    /// <summary>Liest alle Geometrien aus einem Stream (UTF-8).</summary>
    /// <param name="stream">Der Stream.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
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
            throw new FormatException($"Ungültiges JSON: {e.Message}", e);
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
            throw new FormatException($"Ungültiges JSON: {e.Message}", e);
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
                             ?? throw Error("$.geometry", "Feature ohne Geometrie"),
                "FeatureCollection" or "GeometryCollection" => throw new NotSupportedException(
                    $"{type} ist keine Einzelgeometrie (ADR 0009); GeoJson.ReadAll liest sie als Liste."),
                _ => Geometry(root, "$")!,
            };
        }

        public void All(JsonElement element, string path, List<IGeometry2D> result)
        {
            switch (Type(element, path))
            {
                case "FeatureCollection":
                    var features = Member(element, "features", path);
                    if (features.ValueKind != JsonValueKind.Array) throw Error(path + ".features", "Array erwartet");
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
                throw new NotSupportedException("GeometryCollection ist keine Einzelgeometrie (ADR 0009); GeoJson.ReadAll liest sie als Liste.");
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
                    throw Error(path + ".type", $"unbekannter Geometrie-Typ '{type}'");
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
            if (values.Length < 2) throw Error(path, "Position mit mindestens 2 Zahlen erwartet");
            double Number(int i) => values[i].ValueKind == JsonValueKind.Number ? values[i].GetDouble() : throw Error($"{path}[{i}]", "Zahl erwartet");
            return values.Length >= 3 ? new Coordinate(Number(0), Number(1), Number(2)) : new Coordinate(Number(0), Number(1));
        }

        private static IEnumerable<JsonElement> Array(JsonElement e, string path) =>
            e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw Error(path, "Array erwartet");

        private static string Type(JsonElement e, string path)
        {
            if (e.ValueKind != JsonValueKind.Object) throw Error(path, "Objekt erwartet");
            var type = Member(e, "type", path);
            return type.ValueKind == JsonValueKind.String ? type.GetString()! : throw Error(path + ".type", "Zeichenkette erwartet");
        }

        private static JsonElement Member(JsonElement e, string name, string path) =>
            e.TryGetProperty(name, out var value) ? value : throw Error(path, $"Member '{name}' fehlt");

        private static FormatException Error(string path, string message) => new($"Ungültiges GeoJSON bei {path}: {message}.");
    }

    // ---------- Schreiben ----------

    /// <summary>Schreibt die Geometrie als GeoJSON-Geometry-Objekt.</summary>
    /// <param name="geometry">Die Geometrie.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> ist <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">Koordinaten sind <c>NaN</c>/unendlich (in JSON nicht darstellbar) oder unbekannte Geometrie-Art.</exception>
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

    /// <summary>Schreibt die Geometrie als GeoJSON-Geometry-Objekt in einen <see cref="Utf8JsonWriter"/>.</summary>
    /// <param name="geometry">Die Geometrie.</param>
    /// <param name="writer">Der Writer.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
    public static void WriteTo(this IGeometry2D geometry, Utf8JsonWriter writer, GeoJsonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, geometry, options ?? GeoJsonOptions.Default);
    }

    /// <summary>Schreibt Geometrien als FeatureCollection mit leeren <c>properties</c>.</summary>
    /// <param name="geometries">Die Geometrien.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="GeoJsonOptions.Default"/>.</param>
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
                throw new NotSupportedException($"{geometry.GetType().FullName} ist keine bekannte Geometrie-Art.");
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
            throw new NotSupportedException("NaN bzw. unendliche Koordinaten sind in GeoJSON nicht darstellbar.");
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
