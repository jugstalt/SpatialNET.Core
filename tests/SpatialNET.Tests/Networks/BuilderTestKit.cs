using System.Globalization;
using SpatialNET.Imp;
using SpatialNET.Network;
using SpatialNET.Network.Building;

namespace SpatialNET.Tests.Networks;

/// <summary>Hilfen für die Tests des Builders (ADR 0050): Linien, ein aufzeichnender Writer, stabile IDs, Zufallseingaben.</summary>
internal static class BuilderTestKit
{
    public static IPolyline2D Line(params (double X, double Y)[] points) =>
        ImpGeometryFactory.Instance.CreatePolyline([points.Select(p => new Coordinate(p.X, p.Y)).ToArray()], null);

    public static IPolyline2D MultiLine(params (double X, double Y)[][] paths) =>
        ImpGeometryFactory.Instance.CreatePolyline(
            paths.Select(path => (ReadOnlyMemory<Coordinate>)path.Select(p => new Coordinate(p.X, p.Y)).ToArray()).ToArray(), null);

    public static IPolygon2D Square(double minX, double minY, double maxX, double maxY) =>
        ImpGeometryFactory.Instance.CreatePolygon(
            [new PolygonPart(new Coordinate[] { new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY), new(minX, minY) })], null);

    public static NetworkBuilder<string, string, string> Builder(double tolerance = 0.01, Func<NetworkBuildOptions, NetworkBuildOptions>? configure = null)
    {
        var options = new NetworkBuildOptions { Tolerance = tolerance };
        return new NetworkBuilder<string, string, string>(configure?.Invoke(options) ?? options, new StableIds());
    }

    /// <summary>Ids that follow from their arguments: helper nodes from the rounded location, edges from source and part.</summary>
    internal sealed class StableIds : INetworkIdFactory<string, string, string>
    {
        public string NewHelperNode(Coordinate location) =>
            string.Create(CultureInfo.InvariantCulture, $"H({Math.Round(location.X, 6)},{Math.Round(location.Y, 6)})");

        public string NewEdge(string source, int part, int partCount) => $"{source}#{part}/{partCount}";
    }

    internal sealed class RecordingWriter : INetworkWriter<string, string, string>
    {
        public List<BuiltNode<string, string>> Nodes { get; } = [];

        public List<BuiltEdge<string, string, string>> Edges { get; } = [];

        public bool EdgeBeforeAllNodes { get; private set; }

        public void WriteNode(in BuiltNode<string, string> node)
        {
            EdgeBeforeAllNodes |= Edges.Count > 0;
            Nodes.Add(node);
        }

        public void WriteEdge(in BuiltEdge<string, string, string> edge) => Edges.Add(edge);

        public BuiltEdge<string, string, string> Edge(string id) => Edges.Single(e => e.Edge.Id == id);

        public BuiltNode<string, string> Node(string id) => Nodes.Single(n => n.Node.Id == id);
    }

    /// <summary>A random input on a coarse grid, so that ends often meet, lie on lines and lines cross.</summary>
    public sealed record RandomInput(
        List<(string Id, Coordinate At, int Type)> Nodes,
        List<(string Source, (double X, double Y)[] Points, int Type, double? Surveyed, double Absolute)> Lines,
        double Tolerance)
    {
        public static RandomInput Create(int seed)
        {
            var random = new Random(seed);
            double tolerance = 0.3;
            var used = new HashSet<(int, int)>();
            var nodes = new List<(string, Coordinate, int)>();
            for (int i = 0, n = random.Next(0, 6); i < n; i++)
            {
                var cell = (random.Next(0, 8), random.Next(0, 8));
                if (used.Add(cell))
                {
                    nodes.Add(($"N{i}", new Coordinate(cell.Item1 * 2, cell.Item2 * 2), 1 + random.Next(2)));
                }
            }

            var lines = new List<(string, (double, double)[], int, double?, double)>();
            for (int i = 0, n = random.Next(1, 8); i < n; i++)
            {
                var points = new (double, double)[random.Next(2, 5)];
                for (int k = 0; k < points.Length; k++)
                {
                    // mostly on the grid, sometimes a little beside it (within the tolerance)
                    double jitter = random.Next(4) == 0 ? 0.1 : 0;
                    points[k] = (random.Next(0, 8) * 2 + jitter, random.Next(0, 8) * 2 - jitter);
                }

                lines.Add(($"L{i}", points, 10 + random.Next(2), random.Next(3) == 0 ? random.Next(1, 100) : null, random.Next(0, 10)));
            }

            return new RandomInput(nodes, lines, tolerance);
        }

        public (RecordingWriter Writer, BuildResult Result) Build(bool shuffle = false, int seed = 0, bool crossings = false)
        {
            var absolute = new WeightDefinition(7, "fixed", WeightKind.Absolute);
            var builder = Builder(Tolerance, o => o with { NodeAtCrossings = crossings, Weights = [absolute] });
            var nodes = Nodes.ToList();
            var lines = Lines.ToList();
            if (shuffle)
            {
                var random = new Random(seed);
                nodes = nodes.OrderBy(_ => random.Next()).ToList();
                lines = lines.OrderBy(_ => random.Next()).ToList();
            }

            foreach (var (id, at, type) in nodes)
            {
                builder.AddNode(id, id, at, type);
            }

            foreach (var (source, points, type, surveyed, value) in lines)
            {
                builder.AddLine(source, Line(points), type, new LineAttributes(Length: surveyed, Weights: [new EdgeWeight(7, value)]));
            }

            var writer = new RecordingWriter();
            return (writer, builder.Build(writer));
        }
    }
}
