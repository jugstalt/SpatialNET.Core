using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Network;
using SpatialNET.Network.Building;

namespace SpatialNET.Playground.Services;

/// <summary>One point or line of the network input: a GeoJSON geometry (WGS84, or local planar) and its type id.</summary>
public sealed record NetworkElement(JsonElement? Geometry, int Type);

/// <summary>
/// A request to <c>POST /api/network/build</c> and <c>POST /api/network/path</c> (ADR 0050). The path request carries the
/// same input again and rebuilds the network, so the server keeps no state between two requests.
/// </summary>
/// <param name="Points">The real nodes.</param>
/// <param name="Lines">The lines, one path each.</param>
/// <param name="Tolerance">In the unit of the computation CRS (metres for UTM).</param>
/// <param name="Kbs"><c>auto</c> (auto-UTM, ADR 0010) or an EPSG code.</param>
/// <param name="Local">Local coordinates: no projection.</param>
/// <param name="From">Path only: the id of the start node.</param>
/// <param name="To">Path only: the id of the end node.</param>
/// <param name="Blocked">Path and supply: ids of nodes that are blocked for this search (<see cref="NetworkState{TNodeId, TEdgeId}.Block"/>).</param>
/// <param name="Sources">Supply only: the ids of the nodes that are sources (ADR 0051); the sinks are the nodes of the customer type.</param>
public sealed record NetworkRequest(
    List<NetworkElement>? Points,
    List<NetworkElement>? Lines,
    double Tolerance = 0.05,
    bool SplitAtNodes = true,
    bool SplitAtLineEnds = true,
    bool NodeAtCrossings = false,
    string? Kbs = "auto",
    bool Local = false,
    string? From = null,
    string? To = null,
    List<string>? Blocked = null,
    List<string>? Sources = null,
    List<NetworkRuleRow>? Rules = null);

/// <summary>
/// One row of the rule table of the network view (ADR 0052): the plain values of a <see cref="NetworkRuleDefinition"/>,
/// with the enums as text. Patterns use the names of <see cref="NetworkTypes"/>, for example <c>Kasten</c> or <c>Kabel | Freileitung</c>.
/// </summary>
public sealed record NetworkRuleRow(
    string Id,
    string? Severity,
    string Subject,
    string SubjectTypes,
    string? EndFilter,
    string? EndTypes,
    string CountTypes,
    int Min,
    int? Max);

/// <summary>The fixed node and edge types of the network view (a small table of a utility network).</summary>
public static class NetworkTypes
{
    /// <summary>The node type of the sinks of a supply trace (ADR 0051).</summary>
    public const int Customer = 16;

    /// <summary>The node types a point can get.</summary>
    public static IReadOnlyList<(int Id, string Name)> Nodes { get; } =
        [(25, "Trafo"), (15, "Kasten"), (17, "Schalter"), (16, "Kunde")];

    /// <summary>The edge types a line can get.</summary>
    public static IReadOnlyList<(int Id, string Name)> Edges { get; } =
        [(11, "Kabel"), (12, "Freileitung"), (13, "Kastenverbinder")];

    /// <summary>
    /// The types as a registry with one facet, so that rules can use patterns by name (<c>Kasten | Trafo</c>, <c>*</c>,
    /// <c>untyped</c>). The facet values only number the names; the type ids are those of the lists above.
    /// </summary>
    public static TypeRegistry Registry { get; } = CreateRegistry();

    private static TypeRegistry CreateRegistry()
    {
        var names = new Dictionary<int, string>();
        var types = new List<NetworkType>();
        foreach (var (id, name) in Nodes)
        {
            names[names.Count + 1] = name;
            types.Add(new NetworkType(id, ElementKind.Node, [names.Count], name));
        }

        foreach (var (id, name) in Edges)
        {
            names[names.Count + 1] = name;
            types.Add(new NetworkType(id, ElementKind.Edge, [names.Count], name));
        }

        return new TypeRegistry([new Facet("Type", names)], types);
    }

    /// <summary>The name of a node type, or the number for a type that is not in the table.</summary>
    public static string NodeName(int id) => id == 0 ? "Helper node" : Nodes.FirstOrDefault(t => t.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name of an edge type, or the number for a type that is not in the table.</summary>
    public static string EdgeName(int id) => Edges.FirstOrDefault(t => t.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds a network from drawn points and lines with <see cref="NetworkBuilder{TNodeId, TEdgeId, TSource}"/> into an
/// <see cref="InMemoryGraph{TNodeId, TEdgeId, TSource}"/> and searches it (ADR 0050). The projection is the one of
/// <see cref="RunService"/> (ADR 0010): project, compute, project only the vertices back.
/// </summary>
public sealed class NetworkService
{
    private static readonly IGeometryFactory Factory = ImpGeometryFactory.Instance;

    /// <summary>The upper limit of elements per request; the playground is for small examples.</summary>
    private const int MaxElements = 20000;

    /// <summary>The fixed types a point or a line can get, for the UI.</summary>
    public static object Types() => new
    {
        nodes = NetworkTypes.Nodes.Select(t => new { id = t.Id, name = t.Name }),
        edges = NetworkTypes.Edges.Select(t => new { id = t.Id, name = t.Name }),
    };

    /// <summary>Builds the network and describes it: nodes, edges with their geometry, diagnostics or conflicts.</summary>
    /// <exception cref="ArgumentException">The request is invalid (no geometry, wrong geometry type, unknown type, tolerance, CRS).</exception>
    public JsonObject Build(NetworkRequest request)
    {
        var net = Prepare(request, out var failure);
        if (failure is not null)
        {
            return failure;
        }

        var response = new JsonObject
        {
            ["ok"] = true,
            ["kbs"] = net!.Kbs,
            ["unit"] = net.Projection?.Unit,
            ["warnings"] = net.Warnings,
            ["nodeCount"] = net.Result!.NodeCount,
            ["edgeCount"] = net.Result.EdgeCount,
            ["nodes"] = new JsonArray(net.Graph.Nodes.Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.Id,
                ["type"] = n.TypeId,
                ["typeName"] = NetworkTypes.NodeName(n.TypeId),
                ["helper"] = n.TypeId == 0,
                ["geometry"] = net.Geo(Factory.CreatePoint(n.Location, null)),
            }).ToArray()),
            ["edges"] = new JsonArray(net.Graph.Edges.Select(e => (JsonNode)net.Edge(e)).ToArray()),
            // The playground has no shard regions, so the note "outside every shard region" would appear on every build.
            ["diagnostics"] = new JsonArray(net.Result.Diagnostics.Where(d => d.Kind != BuildDiagnosticKind.OutsideShardRegions).Select(d => (JsonNode)new JsonObject
            {
                ["kind"] = d.Kind.ToString(),
                ["message"] = d.Message,
                ["location"] = net.Geo(Factory.CreatePoint(d.Location, null)),
            }).ToArray()),
            ["conflicts"] = new JsonArray(),
        };
        return response;
    }

    /// <summary>The shortest path between two nodes by length, with its geometry from <see cref="NetworkPathGeometry"/>.</summary>
    /// <exception cref="ArgumentException">The request is invalid, or a node id is not in the network.</exception>
    public JsonObject Path(NetworkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
        {
            throw new ArgumentException("A path needs \"from\" and \"to\".");
        }

        var net = Prepare(request, out var failure);
        if (failure is not null)
        {
            return failure;
        }

        var state = new NetworkState<string, string>();
        foreach (string id in request.Blocked ?? [])
        {
            state = state.Block(id);
        }

        var path = net!.Graph.ShortestPath(request.From, request.To, new TraversalOptions<string, string> { State = state });
        var response = new JsonObject
        {
            ["ok"] = true,
            ["found"] = path is not null,
            ["from"] = request.From,
            ["to"] = request.To,
            ["unit"] = net.Projection?.Unit,
        };
        if (path is null)
        {
            return response;
        }

        var line = path.ToPolyline(e => net.Shape(e), Factory);
        response["cost"] = path.Cost;
        response["edges"] = new JsonArray(path.Steps.Select(s => (JsonNode)s.Edge).ToArray());
        response["geometry"] = line.PathCount == 0 ? null : net.Geo(line);
        return response;
    }

    /// <summary>
    /// The supply areas of the marked sources and the supply of every customer (ADR 0051): <see cref="NetworkAnalysis.TraceEdges{TNodeId, TEdgeId}"/>
    /// gives the edges of each area, <see cref="NetworkAnalysis.Supply{TNodeId, TEdgeId}"/> the sources of each customer.
    /// </summary>
    /// <exception cref="ArgumentException">The request is invalid, no source is marked, or a source id is not in the network.</exception>
    public JsonObject Supply(NetworkRequest request)
    {
        var sources = (request.Sources ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (sources.Count == 0)
        {
            throw new ArgumentException("Mark at least one node as a source.");
        }

        var net = Prepare(request, out var failure);
        if (failure is not null)
        {
            return failure;
        }

        var state = new NetworkState<string, string>();
        foreach (string id in request.Blocked ?? [])
        {
            state = state.Block(id);
        }

        var options = new TraversalOptions<string, string> { State = state };
        var areas = sources.ToDictionary(s => s, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var traced in net!.Graph.TraceEdges(sources, options))
        {
            areas[traced.Source].Add(traced.Edge.Id);
        }

        var nodes = net.Graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var sinks = net.Graph.Nodes.Where(n => n.TypeId == NetworkTypes.Customer).Select(n => n.Id).ToList();
        var supplies = net.Graph.Supply(sources, sinks, options).ToList();
        return new JsonObject
        {
            ["ok"] = true,
            ["unit"] = net.Projection?.Unit,
            ["sources"] = new JsonArray(sources.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s,
                ["blocked"] = state.IsBlocked(nodes[s]),
                ["edges"] = new JsonArray(areas[s].Select(e => (JsonNode)e).ToArray()),
            }).ToArray()),
            ["sinks"] = new JsonArray(supplies.Select(x => (JsonNode)new JsonObject
            {
                ["id"] = x.Sink,
                ["geometry"] = net.Geo(Factory.CreatePoint(nodes[x.Sink].Location, null)),
                ["sources"] = new JsonArray(x.Sources.Select(s => (JsonNode)s).ToArray()),
            }).ToArray()),
        };
    }

    /// <summary>
    /// The violations of the rules of the table over every node and edge of the network (ADR 0052), each with the place of
    /// its node for marking it on the map. The rows are resolved with <see cref="NetworkRule.FromDefinition"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The request is invalid, there is no rule, or a row cannot be a rule (the message names the rule id).</exception>
    public JsonObject Validate(NetworkRequest request)
    {
        var rows = request.Rules ?? [];
        if (rows.Count == 0)
        {
            throw new ArgumentException("Add at least one rule.");
        }

        var rules = rows.Select(ToRule).ToList();
        var net = Prepare(request, out var failure);
        if (failure is not null)
        {
            return failure;
        }

        var nodes = net!.Graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var violations = net.Graph.Validate(net.Graph.Nodes.Select(n => n.Id), rules).ToList();
        return new JsonObject
        {
            ["ok"] = true,
            ["unit"] = net.Projection?.Unit,
            ["rules"] = new JsonArray(rules.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["text"] = r.ToString() }).ToArray()),
            ["violations"] = new JsonArray(violations.Select(v => (JsonNode)new JsonObject
            {
                ["rule"] = v.RuleId,
                ["severity"] = v.Severity.ToString(),
                ["kind"] = v.Kind.ToString(),
                ["node"] = v.Node,
                ["edge"] = v.Kind == ElementKind.Edge ? v.Edge : null,
                ["typeName"] = v.Kind == ElementKind.Edge ? NetworkTypes.EdgeName(v.TypeId) : NetworkTypes.NodeName(v.TypeId),
                ["count"] = v.Count,
                ["min"] = v.Min,
                ["max"] = v.Max,
                ["geometry"] = net.Geo(Factory.CreatePoint(nodes[v.Node].Location, null)),
            }).ToArray()),
        };
    }

    private static NetworkRule ToRule(NetworkRuleRow row)
    {
        T Parse<T>(string? text, T fallback, string what)
            where T : struct, Enum =>
            string.IsNullOrWhiteSpace(text) ? fallback
                : Enum.TryParse<T>(text.Trim(), ignoreCase: true, out var value) && Enum.IsDefined(value) ? value
                : throw new ArgumentException($"Rule '{row.Id}': \"{text}\" is not a {what}.");

        return NetworkRule.FromDefinition(
            new NetworkRuleDefinition(
                row.Id,
                Parse(row.Severity, RuleSeverity.Error, "severity (Error or Warning)"),
                Parse(row.Subject, RuleSubject.Node, "subject (Node or Edge)"),
                row.SubjectTypes,
                Parse(row.EndFilter, RuleEndFilter.All, "end filter (All, NodeIs or NodeIsNot)"),
                row.EndTypes,
                row.CountTypes,
                row.Min,
                row.Max),
            NetworkTypes.Registry);
    }

    /// <summary>Reads and projects the input, builds the graph; a <see cref="NetworkBuildException"/> becomes a <paramref name="failure"/> response.</summary>
    private static BuiltNetwork? Prepare(NetworkRequest request, out JsonObject? failure)
    {
        failure = null;
        var points = request.Points ?? [];
        var lines = request.Lines ?? [];
        if (points.Count + lines.Count > MaxElements)
        {
            throw new ArgumentException($"At most {MaxElements} points and lines per request.");
        }

        if (points.Count + lines.Count == 0)
        {
            throw new ArgumentException("Draw or load points and lines first.");
        }

        if (!double.IsFinite(request.Tolerance) || request.Tolerance <= 0)
        {
            throw new ArgumentException("The tolerance must be a number greater than 0.");
        }

        var rawPoints = points.Select((p, i) => Read<IPoint2D>(p, $"Point {i + 1}", "Point")).ToList();
        var rawLines = lines.Select((l, i) => Read<IPolyline2D>(l, $"Line {i + 1}", "LineString")).ToList();
        for (int i = 0; i < points.Count; i++)
        {
            if (NetworkTypes.Nodes.All(t => t.Id != points[i].Type))
            {
                throw new ArgumentException($"Point {i + 1}: unknown node type {points[i].Type}.");
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (NetworkTypes.Edges.All(t => t.Id != lines[i].Type))
            {
                throw new ArgumentException($"Line {i + 1}: unknown edge type {lines[i].Type}.");
            }
        }

        var warnings = new JsonArray();
        Projection? projection = null;
        string kbs = "local";
        if (!request.Local)
        {
            var requested = string.IsNullOrWhiteSpace(request.Kbs) ? "auto" : request.Kbs.Trim();
            bool auto = requested.Equals("auto", StringComparison.OrdinalIgnoreCase);
            var center = RunService.Center(rawPoints.Cast<IGeometry2D>().Concat(rawLines));
            string epsg = auto ? Projection.AutoUtm(center.X, center.Y) : requested.ToUpperInvariant();
            try
            {
                projection = new Projection(epsg);
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Unknown or unsupported CRS \"{epsg}\" ({e.Message}).", e);
            }

            kbs = auto ? $"{epsg} (auto)" : epsg;
            if (projection.IsGeographic)
            {
                warnings.Add("Geographic CRS: the tolerance and the lengths are in degrees, not in metres.");
            }
        }

        var projectedPoints = rawPoints.Select(p => (IPoint2D)Project(projection, p)).ToList();
        var projectedLines = rawLines.Select(l => (IPolyline2D)Project(projection, l)).ToList();

        var builder = new NetworkBuilder<string, string, string>(
            new NetworkBuildOptions
            {
                Tolerance = request.Tolerance,
                SplitAtNodes = request.SplitAtNodes,
                SplitAtLineEnds = request.SplitAtLineEnds,
                NodeAtCrossings = request.NodeAtCrossings,
            },
            new PlaygroundIds());
        var sources = new Dictionary<string, IPolyline2D>(StringComparer.Ordinal);
        for (int i = 0; i < projectedPoints.Count; i++)
        {
            string id = $"P{i + 1}";
            builder.AddNode(id, id, projectedPoints[i], points[i].Type);
        }

        for (int i = 0; i < projectedLines.Count; i++)
        {
            string id = $"L{i + 1}";
            sources[id] = projectedLines[i];
            builder.AddLine(id, projectedLines[i], lines[i].Type);
        }

        var graph = new InMemoryGraph<string, string, string>();
        BuildResult result;
        try
        {
            result = builder.Build(graph);
        }
        catch (NetworkBuildException e)
        {
            var geo = new BuiltNetwork(projection, kbs, warnings, graph, sources, null);
            failure = new JsonObject
            {
                ["ok"] = false,
                ["error"] = e.Message,
                ["kbs"] = kbs,
                ["unit"] = projection?.Unit,
                ["warnings"] = warnings,
                ["conflicts"] = new JsonArray(e.Conflicts.Select(c => (JsonNode)new JsonObject
                {
                    ["kind"] = c.Kind.ToString(),
                    ["sources"] = new JsonArray(c.Sources.Select(s => (JsonNode?)Convert.ToString(s, CultureInfo.InvariantCulture)).ToArray()),
                    ["location"] = geo.Geo(Factory.CreatePoint(c.Location, null)),
                }).ToArray()),
            };
            return null;
        }

        return new BuiltNetwork(projection, kbs, warnings, graph, sources, result);
    }

    /// <summary>Reads a GeoJSON geometry of the expected type; a message with the number of the element otherwise.</summary>
    private static T Read<T>(NetworkElement element, string name, string expected)
        where T : class, IGeometry2D
    {
        if (element.Geometry is not { ValueKind: JsonValueKind.Object } json)
        {
            throw new ArgumentException($"{name}: the geometry is missing.");
        }

        try
        {
            return GeoJson.Read(json.GetRawText(), Factory) as T
                ?? throw new ArgumentException($"{name}: a {expected} is expected.");
        }
        catch (Exception e) when (e is FormatException or NotSupportedException or JsonException)
        {
            throw new ArgumentException($"{name}: {e.Message}", e);
        }
    }

    private static IGeometry2D Project(Projection? projection, IGeometry2D g) => projection is null ? g : projection.ToProjected(g);

    /// <summary>The ids of nodes and edges; a helper node is named after its place, so the same input builds the same ids.</summary>
    private sealed class PlaygroundIds : INetworkIdFactory<string, string, string>
    {
        public string NewHelperNode(Coordinate location) =>
            string.Create(CultureInfo.InvariantCulture, $"H({location.X:0.###},{location.Y:0.###})");

        public string NewEdge(string source, int part, int partCount) => $"{source}#{part}";
    }

    /// <summary>A built network with what is needed to describe it in map coordinates.</summary>
    private sealed class BuiltNetwork(
        Projection? projection,
        string kbs,
        JsonArray warnings,
        InMemoryGraph<string, string, string> graph,
        Dictionary<string, IPolyline2D> sources,
        BuildResult? result)
    {
        public Projection? Projection { get; } = projection;

        public string Kbs { get; } = kbs;

        public JsonArray Warnings { get; } = warnings;

        public InMemoryGraph<string, string, string> Graph { get; } = graph;

        public BuildResult? Result { get; } = result;

        public JsonNode? Geo(IGeometry2D g) => JsonNode.Parse((Projection is null ? g : Projection.ToWgs84(g)).ToGeoJson());

        /// <summary>The line and range an edge was cut from.</summary>
        public EdgeShape Shape(string edge)
        {
            if (!Graph.TryGetSource(edge, out string source, out double from, out double to))
            {
                throw new InvalidOperationException($"The edge {edge} has no source line.");
            }

            return new EdgeShape(sources[source], from, to);
        }

        public JsonObject Edge(GraphEdge<string, string> e)
        {
            var shape = Shape(e.Id);
            var piece = shape.Line.Substring(shape.FromMeasure, shape.ToMeasure, Factory);
            Graph.TryGetSource(e.Id, out string source, out _, out _);
            return new JsonObject
            {
                ["id"] = e.Id,
                ["from"] = e.From,
                ["to"] = e.To,
                ["type"] = e.TypeId,
                ["typeName"] = NetworkTypes.EdgeName(e.TypeId),
                ["length"] = e.Length,
                ["source"] = source,
                ["fromMeasure"] = shape.FromMeasure,
                ["toMeasure"] = shape.ToMeasure,
                ["geometry"] = piece.PathCount == 0 ? null : Geo(piece),
            };
        }
    }
}
