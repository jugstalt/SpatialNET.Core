// Korpus-Konverter (ADR 0008, #23): übernimmt die JTS-XML-Testsuites (EDL 1.0) und die End-to-End-Fälle
// von polygon-clipping (MIT) als Fixtures nach samples/external/<quelle>/<algorithmus>/.
// Erwartete Werte werden nicht übernommen – das Soll berechnet weiterhin das NTS-Orakel zur Laufzeit.
//
// Aufruf (Quellen vorher z. B. per Sparse-Clone holen):
//   dotnet run --project tools/SpatialNET.CorpusConverter -- \
//     --jts <jts>/modules/tests/src/test/resources/testxml/general \
//     --polygon-clipping <polygon-clipping>/test/end-to-end \
//     --out samples/external

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImpGeometryFactory = SpatialNET.Imp.ImpGeometryFactory;
using SpatialNET.IO;

var options = args.Chunk(2).ToDictionary(p => p[0], p => p[1]);
var output = options.GetValueOrDefault("--out", "samples/external");
int written = 0, skipped = 0;
var seen = new HashSet<string>();

if (options.TryGetValue("--jts", out var jtsDir)) ConvertJts(jtsDir);
if (options.TryGetValue("--polygon-clipping", out var pcDir)) ConvertPolygonClipping(pcDir);
Console.WriteLine($"{written} Fixtures geschrieben, {skipped} Fälle übersprungen.");

void ConvertJts(string dir)
{
    foreach (var file in Directory.GetFiles(dir, "*.xml").Order(StringComparer.Ordinal))
    {
        var run = XElement.Load(file);
        var scale = run.Element("precisionModel")?.Attribute("scale")?.Value;
        double? gridSize = scale is null ? null : 1 / double.Parse(scale, CultureInfo.InvariantCulture);
        int caseNo = 0;
        foreach (var @case in run.Elements("case"))
        {
            caseNo++;
            var a = Clean(@case.Element("a")?.Value);
            var b = Clean(@case.Element("b")?.Value);
            var desc = Clean(@case.Element("desc")?.Value) ?? $"Fall {caseNo}";
            foreach (var op in @case.Elements("test").Select(t => t.Element("op")).OfType<XElement>())
            {
                var (algorithm, parameters, usesB) = MapJts(op);
                bool swapped = op.Attribute("arg1")?.Value is "B" or "b";
                if (algorithm is null || a is null || (usesB && b is null) || swapped)
                {
                    skipped++;
                    continue;
                }

                if (gridSize is { } g && algorithm is "intersection" or "union" or "difference" or "symDifference" or "buffer")
                {
                    parameters["gridSize"] = g;
                }

                Write("jts", algorithm, $"{Path.GetFileNameWithoutExtension(file)}-{caseNo}", desc,
                    $"JTS {Path.GetFileName(file)}, case {caseNo}", a, usesB ? b : null, parameters);
            }
        }
    }
}

(string?, Dictionary<string, object>, bool) MapJts(XElement op)
{
    var name = op.Attribute("name")!.Value;
    var n = (name.EndsWith("NG", StringComparison.Ordinal) ? name[..^2] : name).ToLowerInvariant();
    var arg2 = op.Attribute("arg2")?.Value;
    var p = new Dictionary<string, object>();
    switch (n)
    {
        case "intersection": return ("intersection", p, true);
        case "union": return arg2 is null ? (null, p, false) : ("union", p, true);
        case "difference": return ("difference", p, true);
        case "symdifference": return ("symDifference", p, true);
        case "relate": return ("relate", p, true);
        case "contains": return ("contains", p, true);
        case "coveredby": return ("coveredBy", p, true);
        case "covers": return ("covers", p, true);
        case "crosses": return ("crosses", p, true);
        case "disjoint": return ("disjoint", p, true);
        case "equalstopo": return ("equalsTopo", p, true);
        case "intersects": return ("intersects", p, true);
        case "overlaps": return ("overlaps", p, true);
        case "touches": return ("touches", p, true);
        case "within": return ("within", p, true);
        case "getcentroid": return ("centroid", p, false);
        case "isvalid": return ("isValid", p, false);
        case "convexhull": return ("convexHull", p, false);
        case "distance": return ("distance", p, true);
        case "buffer" when Number(arg2) is { } d: p["distance"] = d; return ("buffer", p, false);
        case "simplifydp" when Number(arg2) is { } t: p["tolerance"] = t; return ("simplify", p, false);
        case "simplifytp" when Number(arg2) is { } t: p["tolerance"] = t; p["preserveTopology"] = true; return ("simplify", p, false);
        default: return (null, p, false);
    }
}

void ConvertPolygonClipping(string dir)
{
    var operations = new Dictionary<string, string>
    {
        ["union"] = "union", ["intersection"] = "intersection", ["difference"] = "difference", ["xor"] = "symDifference",
    };
    foreach (var caseDir in Directory.GetDirectories(dir).Order(StringComparer.Ordinal))
    {
        var argsFile = Path.Combine(caseDir, "args.geojson");
        if (!File.Exists(argsFile)) continue;
        var features = JsonNode.Parse(File.ReadAllText(argsFile))!["features"]!.AsArray();
        var caseName = Path.GetFileName(caseDir);
        if (features.Count != 2)
        {
            skipped++; // nicht genau zwei Operanden: kein binäres Overlay
            continue;
        }

        var a = ToWkt(features[0]!["geometry"]!);
        var b = ToWkt(features[1]!["geometry"]!);
        foreach (var (file, algorithm) in operations)
        {
            if (File.Exists(Path.Combine(caseDir, file + ".geojson")))
            {
                Write("polygon-clipping", algorithm, caseName, caseName.Replace('-', ' '),
                    $"polygon-clipping test/end-to-end/{caseName}", a, b, []);
            }
        }
    }
}

void Write(string source, string algorithm, string slug, string name, string origin, string a, string? b, Dictionary<string, object> parameters)
{
    foreach (var wkt in new[] { a, b }.OfType<string>())
    {
        try
        {
            Wkt.Read(wkt, ImpGeometryFactory.Instance); // nur Einzelgeometrien in unterstütztem WKT
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            skipped++;
            return;
        }
    }

    var key = $"{source}/{algorithm}/{slug}/{string.Join(",", parameters.Select(p => $"{p.Key}={p.Value}"))}";
    if (!seen.Add(key)) return;
    var suffix = parameters.TryGetValue("distance", out var d) ? $"-d{d}" : parameters.TryGetValue("tolerance", out var t) ? $"-t{t}" : "";
    var fileName = Path.Combine(output, source, algorithm, Slug(slug + suffix) + ".json");
    Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);

    var node = new JsonObject
    {
        ["name"] = name,
        ["tags"] = new JsonArray("external", source),
        ["source"] = origin,
        ["a"] = a,
    };
    if (b is not null) node["b"] = b;
    if (parameters.Count > 0) node["params"] = JsonSerializer.SerializeToNode(parameters);
    File.WriteAllText(fileName, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    written++;
}

static string? Clean(string? text) =>
    string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

static double? Number(string? text) =>
    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

static string Slug(string text) =>
    new string(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '-').ToArray()).Trim('-');

static string ToWkt(JsonNode geometry)
{
    var type = geometry["type"]!.GetValue<string>();
    var coords = geometry["coordinates"]!;
    return type switch
    {
        "Polygon" => "POLYGON " + Rings(coords),
        "MultiPolygon" => "MULTIPOLYGON (" + string.Join(", ", coords.AsArray().Select(p => Rings(p!))) + ")",
        _ => throw new NotSupportedException(type),
    };

    static string Rings(JsonNode polygon) =>
        "(" + string.Join(", ", polygon.AsArray().Select(r => "(" + string.Join(", ", r!.AsArray().Select(Pt)) + ")")) + ")";

    static string Pt(JsonNode? p) =>
        string.Join(' ', p!.AsArray().Take(2).Select(v => v!.GetValue<double>().ToString("R", CultureInfo.InvariantCulture)));
}
