using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Topology;

namespace SpatialNET.Internal.Overlay;

internal enum OverlayOp { Intersection, Union, Difference, SymDifference }

/// <summary>
/// Overlay core (ADR 0005): snap rounding of all edges, planar graph over grid points, winding number per
/// operand and face, fill rule → inside/outside, selection by operation, construction of parts, paths or
/// points. Strict semantics: the result has exactly the dimension requested by the caller.
/// </summary>
internal sealed class OverlayEngine
{
    private readonly record struct Source(GridPoint A, GridPoint B, double ZA, double ZB, int Operand, int String);

    private readonly GridModel _grid;
    private readonly FillRule[] _fill;
    private readonly int[] _dimension = new int[2];
    private readonly List<Coordinate>[] _points = [[], []];
    private readonly List<Source> _sources = [];
    private readonly HashSet<GridPoint> _lineEndpoints = [];

    public PlanarGraph<GridPoint> Graph { get; }

    public OverlayEngine(IGeometry2D? a, IGeometry2D? b, GridModel grid, FillRule fillRule = FillRule.EvenOdd)
        : this([a, b], grid, [fillRule, fillRule], extraLines: null)
    {
    }

    /// <summary>Overlay of two areas with envelope prefilter (<see cref="RingClipper"/>).</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="fillRule">Fill rule.</param>
    /// <param name="clip">Rectangle per operand to which its rings are reduced (null = none).</param>
    /// <param name="resultBox">Region that safely contains the result; only edges inside it are selected.</param>
    public OverlayEngine(IGeometry2D a, IGeometry2D? b, GridModel grid, FillRule fillRule, Envelope2D?[] clip, Envelope2D resultBox)
        : this([a, b], grid, [fillRule, fillRule], extraLines: null, clip)
    {
        _resultBox = (grid.ToUnits(resultBox.MinX), grid.ToUnits(resultBox.MinY), grid.ToUnits(resultBox.MaxX), grid.ToUnits(resultBox.MaxY));
    }

    private readonly (double MinX, double MinY, double MaxX, double MaxY)? _resultBox;

    /// <param name="operands">Up to two operands.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="fill">Fill rule per operand.</param>
    /// <param name="extraLines">Additional rings/paths per operand with their own orientation (e.g. raw offsets of the buffer).</param>
    /// <param name="clip">Optional: rectangle per operand for the envelope prefilter of the rings.</param>
    public OverlayEngine(IGeometry2D?[] operands, GridModel grid, FillRule[] fill, List<(Coordinate[] Path, int Operand, bool Area)>? extraLines,
        Envelope2D?[]? clip = null)
    {
        _grid = grid;
        _fill = fill;
        var strings = new List<SegmentString>();
        var areaString = new List<bool>();

        for (int k = 0; k < operands.Length; k++)
        {
            switch (operands[k])
            {
                case null:
                    break;
                case IPoint2D p:
                    _points[k].Add(p.ToCoordinate());
                    _dimension[k] = 0;
                    break;
                case IMultiPoint2D mp:
                    _points[k].AddRange(Rings.Coordinates(mp));
                    _dimension[k] = 0;
                    break;
                case IPolyline2D line:
                    _dimension[k] = 1;
                    for (int i = 0; i < line.PathCount; i++)
                    {
                        var path = Rings.Coordinates(line[i]);
                        if (path.Length > 0) Add(path, k, area: false);
                    }

                    break;
                case IPolygon2D polygon:
                    _dimension[k] = 2;
                    // flächenlose und sich paarweise auslöschende Ringe tragen nichts bei (ADR 0003)
                    var rings = Enumerable.Range(0, polygon.RingCount)
                        .Select(i => Rings.Closed(polygon[i]))
                        .Where(r => r.Length > 0 && !Rings.IsDegenerate(r))
                        .ToList();
                    var region = clip?[k];
                    foreach (var ring in PolygonStructure.CancelDuplicates(rings))
                    {
                        Add(region is { } r ? RingClipper.Clip(ring, r) : ring, k, area: true);
                    }

                    break;
            }
        }

        foreach (var (path, operand, area) in extraLines ?? [])
        {
            _dimension[operand] = Math.Max(_dimension[operand], area ? 2 : 1);
            Add(path, operand, area);
        }

        foreach (var str in strings)
        {
            if (!areaString[str.Index] && str.Points.Length > 0)
            {
                _lineEndpoints.Add(grid.Round(str.Points[0]));
                _lineEndpoints.Add(grid.Round(str.Points[^1]));
            }
        }

        var noded = SnapRoundingNoder.Node(strings, grid);
        var input = new List<GraphInput<GridPoint>>(noded.Count);
        foreach (var e in noded)
        {
            bool area = areaString[e.Source.Index];
            _sources.Add(new Source(e.A, e.B, e.ZA, e.ZB, e.Source.Operand, e.Source.Index));
            input.Add(new GraphInput<GridPoint>(e.A, e.B, e.Source.Operand, area ? 1 : 0, area ? 0 : 1, _sources.Count - 1));
        }

        Graph = new PlanarGraph<GridPoint>(input);

        void Add(Coordinate[] path, int operand, bool area)
        {
            strings.Add(new SegmentString(path, operand, strings.Count));
            areaString.Add(area);
        }
    }

    public GridModel Grid => _grid;

    // ---------- Lage ----------

    public bool Inside(int operand, int winding) => _fill[operand] switch
    {
        FillRule.EvenOdd => (winding & 1) == 1,
        FillRule.NonZero => winding != 0,
        FillRule.Positive => winding > 0,
        FillRule.Negative => winding < 0,
        _ => false,
    };

    public bool InsideLeft(int h, int operand) => _dimension[operand] == 2 && Inside(operand, Graph.LeftWinding(h, operand));

    public bool InsideRight(int h, int operand) => _dimension[operand] == 2 && Inside(operand, Graph.RightWinding(h, operand));

    public static bool Apply(OverlayOp op, bool a, bool b) => op switch
    {
        OverlayOp.Intersection => a && b,
        OverlayOp.Union => a || b,
        OverlayOp.Difference => a && !b,
        _ => a != b,
    };

    /// <summary>Position of an edge (via half-edge 2e) relative to the area of the operand: 0 outside, 1 boundary, 2 inside.</summary>
    public int EdgeAreaLocation(int e, int operand)
    {
        bool left = InsideLeft(2 * e, operand), right = InsideRight(2 * e, operand);
        return left != right ? 1 : left ? 2 : 0;
    }

    // ---------- Flächen ----------

    /// <summary>Parts of the face selection <paramref name="inResult"/> (for the face to the left of a half-edge).</summary>
    public List<PolygonPart> Areas(Func<int, bool> inResult)
    {
        var selected = new bool[Graph.HalfEdgeCount];
        for (int h = 0; h < Graph.HalfEdgeCount; h++)
        {
            selected[h] = inResult(h) && !inResult(h ^ 1) && InResultBox(h >> 1);
        }

        return BuildAreas(selected);
    }

    /// <summary>Whether the edge lies in the result region (always, without envelope prefilter); prefilter artifacts lie outside.</summary>
    private bool InResultBox(int e)
    {
        if (_resultBox is not { } box) return true;
        var a = Graph.Nodes[Graph.Edges[e].A];
        var b = Graph.Nodes[Graph.Edges[e].B];
        return Math.Min(a.X, b.X) >= box.MinX && Math.Max(a.X, b.X) <= box.MaxX
            && Math.Min(a.Y, b.Y) >= box.MinY && Math.Max(a.Y, b.Y) <= box.MaxY;
    }

    /// <summary>Parts from selected half-edges (result on the left): follow faces, split at multiple nodes, assign holes.</summary>
    public List<PolygonPart> BuildAreas(bool[] selected)
    {
        var used = new bool[Graph.HalfEdgeCount];
        var rings = new List<List<int>>(); // Folgen von Halbkanten
        for (int start = 0; start < Graph.HalfEdgeCount; start++)
        {
            if (!selected[start] || used[start]) continue;
            var cycle = new List<int>();
            int h = start;
            do
            {
                used[h] = true;
                cycle.Add(h);
                h = NextSelected(h, selected);
            }
            while (h != start && h >= 0 && !used[h]);

            foreach (var simple in SplitAtRepeatedNodes(cycle))
            {
                rings.Add(simple);
            }
        }

        var shells = new List<(GridPoint[] Ring, Int128 Area2)>();
        var holes = new List<GridPoint[]>();
        foreach (var r in rings)
        {
            var points = r.Select(h => Graph.Nodes[Graph.Origin(h)]).Append(Graph.Nodes[Graph.Origin(r[0])]).ToArray();
            var area2 = SignedArea2(points);
            if (area2 > 0) shells.Add((points, area2));
            else if (area2 < 0) holes.Add(points);
        }

        // Löcher dem kleinsten umgebenden Außenring zuordnen
        var assigned = shells.Select(_ => new List<GridPoint[]>()).ToList();
        foreach (var hole in holes)
        {
            int best = -1;
            for (int s = 0; s < shells.Count; s++)
            {
                if (ContainsRing(shells[s].Ring, hole) && (best < 0 || shells[s].Area2 < shells[best].Area2)) best = s;
            }

            if (best >= 0) assigned[best].Add(hole);
        }

        var parts = new List<PolygonPart>(shells.Count);
        for (int s = 0; s < shells.Count; s++)
        {
            parts.Add(new PolygonPart(ToCoordinates(shells[s].Ring),
                assigned[s].Select(h => (ReadOnlyMemory<Coordinate>)ToCoordinates(h)).ToArray()));
        }

        return parts;
    }

    private int NextSelected(int h, bool[] selected)
    {
        var outgoing = Graph.Outgoing(Graph.Head(h));
        int twin = h ^ 1;
        int index = 0;
        for (int i = 0; i < outgoing.Count; i++) if (outgoing[i] == twin) { index = i; break; }

        // im Uhrzeigersinn ab Twin(h) die erste ausgewählte Halbkante (Maschenverfolgung)
        for (int k = 1; k <= outgoing.Count; k++)
        {
            int candidate = outgoing[(index - k + outgoing.Count * 2) % outgoing.Count];
            if (selected[candidate]) return candidate;
        }

        return -1;
    }

    private IEnumerable<List<int>> SplitAtRepeatedNodes(List<int> cycle)
    {
        var stack = new List<int>();
        var position = new Dictionary<int, int>();
        foreach (int h in cycle)
        {
            int node = Graph.Origin(h);
            if (position.TryGetValue(node, out int at))
            {
                yield return stack.GetRange(at, stack.Count - at);
                for (int i = at; i < stack.Count; i++) position.Remove(Graph.Origin(stack[i]));
                stack.RemoveRange(at, stack.Count - at);
            }

            position[node] = stack.Count;
            stack.Add(h);
        }

        if (stack.Count > 0) yield return stack;
    }

    private static Int128 SignedArea2(GridPoint[] closed)
    {
        Int128 sum = 0;
        for (int i = 0; i < closed.Length - 1; i++)
        {
            sum += (Int128)closed[i].X * closed[i + 1].Y - (Int128)closed[i + 1].X * closed[i].Y;
        }

        return sum;
    }

    /// <summary>Whether the (non-crossing) ring <paramref name="inner"/> lies in <paramref name="outer"/>: a vertex that is not on the boundary.</summary>
    private static bool ContainsRing(GridPoint[] outer, GridPoint[] inner)
    {
        foreach (var p in inner)
        {
            int location = Locate(p, outer);
            if (location != 1) return location == 2;
        }

        return false;
    }

    /// <summary>Position of a grid point relative to a closed grid ring: 0 outside, 1 boundary, 2 inside – exact.</summary>
    internal static int Locate(GridPoint p, GridPoint[] closed)
    {
        bool inside = false;
        for (int i = 1; i < closed.Length; i++)
        {
            var a = closed[i - 1];
            var b = closed[i];
            int o = GridPoint.Orient(a, b, p);
            if (o == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y))
            {
                return 1;
            }

            var (lo, hi) = a.Y <= b.Y ? (a, b) : (b, a);
            if (lo.Y <= p.Y && hi.Y > p.Y && GridPoint.Orient(lo, hi, p) < 0) inside = !inside;
        }

        return inside ? 2 : 0;
    }

    // ---------- Linien ----------

    /// <summary>
    /// Selected edges into paths. Joining happens only at pure intermediate vertices (degree 2 in the whole graph,
    /// not a path end point of an input) – as in JTS OverlayNG; a new path starts at noding nodes.
    /// </summary>
    public List<ReadOnlyMemory<Coordinate>> Lines(Func<int, bool> selectEdge)
    {
        var selected = new bool[Graph.Edges.Count];
        var degree = new int[Graph.Nodes.Count];
        for (int e = 0; e < Graph.Edges.Count; e++)
        {
            selected[e] = selectEdge(e);
        }

        for (int n = 0; n < Graph.Nodes.Count; n++)
        {
            // durchlaufen nur, wo genau eine Eingabe-Punktfolge einen Zwischen-Vertex hat
            bool through = Graph.Outgoing(n).Count == 2 && !_lineEndpoints.Contains(Graph.Nodes[n])
                           && Graph.Outgoing(n).All(h => selected[h >> 1])
                           && Graph.Outgoing(n).SelectMany(h => Graph.Edges[h >> 1].Sources).Select(i => _sources[i].String).Distinct().Count() == 1;
            degree[n] = through ? 2 : -1; // -1: Pfad endet hier
        }

        var used = new bool[Graph.Edges.Count];
        var paths = new List<ReadOnlyMemory<Coordinate>>();

        void Walk(int node, int edge)
        {
            var nodes = new List<int> { node };
            while (true)
            {
                used[edge] = true;
                var ed = Graph.Edges[edge];
                node = ed.A == node ? ed.B : ed.A;
                nodes.Add(node);
                if (degree[node] != 2) break;
                int next = -1;
                foreach (int h in Graph.Outgoing(node))
                {
                    int e = h >> 1;
                    if (selected[e] && !used[e]) { next = e; break; }
                }

                if (next < 0) break;
                edge = next;
            }

            paths.Add(LineCoordinates(nodes));
        }

        for (int n = 0; n < Graph.Nodes.Count; n++)
        {
            if (degree[n] == 2) continue;
            foreach (int h in Graph.Outgoing(n))
            {
                int e = h >> 1;
                if (selected[e] && !used[e]) Walk(n, e);
            }
        }

        // geschlossene Ketten (nur Knoten vom Grad 2)
        for (int e = 0; e < Graph.Edges.Count; e++)
        {
            if (selected[e] && !used[e]) Walk(Graph.Edges[e].A, e);
        }

        return paths;
    }

    private Coordinate[] LineCoordinates(List<int> nodes)
    {
        // Richtung möglichst wie die Eingabe des ersten Operanden (erste Kante)
        var result = nodes.Select(n => ToCoordinate(n)).ToArray();
        return result;
    }

    // ---------- Punkte ----------

    /// <summary>Rounded, distinct points of the operand.</summary>
    public IEnumerable<(GridPoint Grid, Coordinate Original)> Points(int operand) =>
        _points[operand].Where(c => double.IsFinite(c.X) && double.IsFinite(c.Y))
            .Select(c => (_grid.Round(c), c))
            .DistinctBy(p => p.Item1);

    /// <summary>Whether a grid point lies in the closure (interior or boundary) of the operand.</summary>
    public bool Covers(int operand, GridPoint p)
    {
        switch (_dimension[operand])
        {
            case 2:
                if (Graph.TryGetNode(p, out int node))
                {
                    foreach (int h in Graph.Outgoing(node))
                        if (InsideLeft(h, operand) || InsideRight(h, operand)) return true;
                    return false;
                }

                if (OnEdge(p, e => EdgeAreaLocation(e, operand) == 1)) return true;
                return Inside(operand, Graph.RayWinding(p, operand));
            case 1:
                if (Graph.TryGetNode(p, out int n))
                    return Graph.Outgoing(n).Any(h => Graph.Edges[h >> 1].Line[operand] > 0);
                return OnEdge(p, e => Graph.Edges[e].Line[operand] > 0);
            case 0:
                return Points(operand).Any(q => q.Grid == p);
            default:
                return false;
        }
    }

    private bool OnEdge(GridPoint p, Func<int, bool> filter)
    {
        for (int e = 0; e < Graph.Edges.Count; e++)
        {
            if (!filter(e)) continue;
            var a = Graph.Nodes[Graph.Edges[e].A];
            var b = Graph.Nodes[Graph.Edges[e].B];
            if (GridPoint.Orient(a, b, p) == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
                && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y))
            {
                return true;
            }
        }

        return false;
    }

    public int Dimension(int operand) => _dimension[operand];

    // ---------- Koordinaten mit Z ----------

    private Coordinate[] ToCoordinates(GridPoint[] ring) => ring.Select(p => _grid.ToCoordinate(p, ZAt(p))).ToArray();

    private Coordinate ToCoordinate(int node) => _grid.ToCoordinate(Graph.Nodes[node], ZAt(Graph.Nodes[node]));

    private Dictionary<GridPoint, double>? _z;

    /// <summary>Z at a node: from the first operand, otherwise from the second (ADR 0005); NaN = none.</summary>
    public double ZAt(GridPoint p)
    {
        if (_z is null)
        {
            _z = [];
            foreach (var s in _sources.OrderBy(s => s.Operand))
            {
                if (!double.IsNaN(s.ZA)) _z.TryAdd(s.A, s.ZA);
                if (!double.IsNaN(s.ZB)) _z.TryAdd(s.B, s.ZB);
            }
        }

        return _z.TryGetValue(p, out double z) ? z : double.NaN;
    }
}
