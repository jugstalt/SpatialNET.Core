using System.Text.Json;
using System.Text.Json.Nodes;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Playground.Algorithms;

namespace SpatialNET.Playground.Services;

/// <summary>Anfrage an <c>POST /api/run</c>.</summary>
/// <param name="Algorithm">Id des Algorithmus.</param>
/// <param name="Inputs">Operanden als GeoJSON-Geometrien (WGS84, bei <paramref name="Local"/> lokal planar).</param>
/// <param name="Params">Parameterwerte; fehlende nehmen den Standard des Deskriptors.</param>
/// <param name="Kbs"><c>auto</c> (Auto-UTM, ADR 0010) oder ein EPSG-Code wie <c>EPSG:25833</c>.</param>
/// <param name="Local">Lokale Koordinaten (L.CRS.Simple): keine Projektion.</param>
/// <param name="Wkt">
/// Operanden als WKT (Länge/Breite bzw. lokal, mit Z und M); ein Wert hier gilt vor der GeoJSON-Eingabe desselben Operanden,
/// denn GeoJSON trägt kein M (ADR 0015).
/// </param>
public sealed record RunRequest(
    string Algorithm,
    Dictionary<string, JsonElement>? Inputs,
    Dictionary<string, JsonElement>? Params,
    string? Kbs = "auto",
    bool Local = false,
    Dictionary<string, string>? Wkt = null);

/// <summary>Anfrage an <c>POST /api/convert</c>: genau eines von beiden.</summary>
/// <param name="Geometry">Eine GeoJSON-Geometrie.</param>
/// <param name="Wkt">Ein WKT-Text.</param>
public sealed record ConvertRequest(JsonElement? Geometry, string? Wkt);

/// <summary>
/// Führt einen Algorithmus aus (ADR 0010): WGS84 → Rechen-KBS projizieren, rechnen, nur die Vertices zurückprojizieren.
/// Distanzen und Maße sind in Einheiten des Rechen-KBS.
/// </summary>
public sealed class RunService
{
    private static readonly IGeometryFactory Factory = ImpGeometryFactory.Instance;

    private readonly Dictionary<string, IPlaygroundAlgorithm> _algorithms =
        PlaygroundAlgorithm.All.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public JsonObject Run(RunRequest request)
    {
        if (!_algorithms.TryGetValue(request.Algorithm, out var algorithm))
        {
            throw new ArgumentException($"Unbekannter Algorithmus „{request.Algorithm}“.");
        }

        var raw = new Dictionary<string, IGeometry2D>();
        foreach (var input in algorithm.Inputs)
        {
            if (request.Wkt is not null && request.Wkt.TryGetValue(input.Key, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    raw[input.Key] = SpatialNET.IO.Wkt.Read(text, Factory);
                }
                catch (Exception e) when (e is FormatException or NotSupportedException)
                {
                    throw new ArgumentException($"Eingabe „{input.Label}“ ({input.Key}): {e.Message}", e);
                }

                continue;
            }

            if (request.Inputs is null || !request.Inputs.TryGetValue(input.Key, out var json) || json.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"Eingabe „{input.Label}“ ({input.Key}) fehlt.");
            }

            raw[input.Key] = GeoJson.Read(json.GetRawText(), Factory);
        }

        var warnings = new JsonArray();
        Projection? projection = null;
        string kbs = "lokal";
        if (!request.Local)
        {
            var requested = string.IsNullOrWhiteSpace(request.Kbs) ? "auto" : request.Kbs.Trim();
            var center = Center(raw.Values);
            var epsg = requested.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Projection.AutoUtm(center.X, center.Y) : requested.ToUpperInvariant();
            try
            {
                projection = new Projection(epsg);
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Unbekanntes oder nicht unterstütztes KBS „{epsg}“ ({e.Message}).", e);
            }

            kbs = requested.Equals("auto", StringComparison.OrdinalIgnoreCase) ? $"{epsg} (auto)" : epsg;
            if (projection.IsGeographic)
            {
                warnings.Add("Geographisches KBS: Distanzen und Flächen sind in Grad, nicht in Metern.");
            }
            else if (epsg == "EPSG:3857")
            {
                double scale = 1 / Math.Cos(center.Y * Math.PI / 180);
                warnings.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"EPSG:3857 ist nicht längentreu: am Ort (Breite {center.Y:0.##}°) ist 1 Einheit ≈ {1 / scale:0.###} m (Faktor {scale:0.###})."));
            }
        }

        var inputs = raw.ToDictionary(kv => kv.Key, kv => projection is null ? kv.Value : projection.ToProjected(kv.Value));
        var parameters = new PlaygroundParameters(request.Params ?? [], algorithm.Parameters);
        var value = algorithm.Run(inputs, parameters, Factory, null);

        var response = Describe(value, projection);
        response["algorithm"] = algorithm.Id;
        response["kbs"] = kbs;
        response["unit"] = projection is null ? null : projection.Unit;
        response["warnings"] = warnings;
        return response;
    }

    /// <summary>Mittelpunkt des gemeinsamen Envelopes aller Eingaben in WGS84; bestimmt die Auto-UTM-Zone (ADR 0010).</summary>
    private static Coordinate Center(IEnumerable<IGeometry2D> inputs)
    {
        var envelopes = inputs.Select(g => g.Envelope()).Where(e => !e.IsEmpty).ToList();
        if (envelopes.Count == 0)
        {
            return new Coordinate(9, 0); // leere Eingaben: beliebige Zone
        }

        return new Coordinate(
            (envelopes.Min(e => e.MinX) + envelopes.Max(e => e.MaxX)) / 2,
            (envelopes.Min(e => e.MinY) + envelopes.Max(e => e.MaxY)) / 2);
    }

    private static JsonObject Describe(object? value, Projection? projection)
    {
        JsonNode? Geo(IGeometry2D g) => JsonNode.Parse((projection is null ? g : projection.ToWgs84(g)).ToGeoJson());
        string AsWkt(IGeometry2D g) => (projection is null ? g : projection.ToWgs84(g)).ToWkt();

        switch (value)
        {
            case IGeometry2D g:
                return new JsonObject { ["kind"] = "geometry", ["geometry"] = Geo(g), ["wkt"] = AsWkt(g), ["stats"] = Stats([g]) };

            case IEnumerable<IGeometry2D> list:
                var parts = list.ToList();
                return new JsonObject
                {
                    ["kind"] = "geometries",
                    ["geometries"] = new JsonArray(parts.Select(Geo).ToArray()),
                    ["wkts"] = new JsonArray(parts.Select(p => (JsonNode)AsWkt(p)).ToArray()),
                    ["stats"] = Stats(parts),
                };

            case double d:
                return new JsonObject { ["kind"] = "number", ["value"] = double.IsFinite(d) ? d : null, ["text"] = Text(d) };

            case bool b:
                return new JsonObject { ["kind"] = "bool", ["value"] = b };

            case IntersectionMatrix m:
                return new JsonObject { ["kind"] = "matrix", ["value"] = m.ToString() };

            case Coordinate c:
                var point = Factory.CreatePoint(c, null);
                return new JsonObject
                {
                    ["kind"] = "geometry",
                    ["geometry"] = Geo(point),
                    ["wkt"] = AsWkt(point),
                    ["text"] = $"({Text(c.X)}, {Text(c.Y)}{(c.HasZ ? $", Z = {Text(c.Z)}" : "")}{(c.HasM ? $", M = {Text(c.M)}" : "")})",
                };

            case null:
                return new JsonObject { ["kind"] = "none", ["text"] = "kein Ergebnis (leere Eingabe)" };

            case Envelope2D e when e.IsEmpty:
                return new JsonObject { ["kind"] = "none", ["text"] = "leerer Envelope" };

            case Envelope2D e:
                var rectangle = Factory.CreatePolygon([new PolygonPart(new Coordinate[]
                {
                    new(e.MinX, e.MinY), new(e.MaxX, e.MinY), new(e.MaxX, e.MaxY), new(e.MinX, e.MaxY), new(e.MinX, e.MinY),
                })], null);
                return new JsonObject
                {
                    ["kind"] = "geometry",
                    ["geometry"] = Geo(rectangle),
                    ["text"] = $"[{Text(e.MinX)}, {Text(e.MinY)}] – [{Text(e.MaxX)}, {Text(e.MaxY)}]",
                };

            case ValidationResult v:
                return new JsonObject
                {
                    ["kind"] = "validation",
                    ["value"] = v.IsValid,
                    ["issues"] = new JsonArray(v.Issues.Select(i => (JsonNode)new JsonObject
                    {
                        ["kind"] = i.Kind.ToString(),
                        ["location"] = double.IsFinite(i.Location.X) && double.IsFinite(i.Location.Y)
                            ? Geo(Factory.CreatePoint(i.Location, null))
                            : null,
                    }).ToArray()),
                };

            default:
                throw new NotSupportedException($"Ergebnistyp {value.GetType().Name} wird nicht dargestellt.");
        }
    }

    /// <summary>Kennzahlen des Ergebnisses in Einheiten des Rechen-KBS.</summary>
    private static JsonObject Stats(IReadOnlyList<IGeometry2D> geometries) => new()
    {
        ["area"] = geometries.Sum(g => g.Area()),
        ["length"] = geometries.Sum(g => g.Length()),
        ["vertices"] = geometries.Sum(Vertices),
        ["parts"] = geometries.Sum(Parts),
    };

    private static int Vertices(IGeometry2D g) => g switch
    {
        IPoint2D => 1,
        IMultiPoint2D mp => mp.PointCount,
        IPolyline2D l => Enumerable.Range(0, l.PathCount).Sum(i => l[i].PointCount),
        IPolygon2D p => Enumerable.Range(0, p.RingCount).Sum(i => p[i].PointCount),
        _ => 0,
    };

    /// <summary>Punkte, Pfade bzw. Teilflächen (Ringe mit positiver Fläche, ADR 0003).</summary>
    private static int Parts(IGeometry2D g) => g switch
    {
        IPoint2D => 1,
        IMultiPoint2D mp => mp.PointCount,
        IPolyline2D l => l.PathCount,
        IPolygon2D p => Enumerable.Range(0, p.RingCount).Count(i => SignedArea(p[i]) > 0),
        _ => 0,
    };

    private static double SignedArea(IRing2D ring)
    {
        double sum = 0;
        for (int i = 0, n = ring.PointCount; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }

    private static string Text(double d) => double.IsNaN(d) ? "NaN" : d.ToString("G10", System.Globalization.CultureInfo.InvariantCulture);
}
