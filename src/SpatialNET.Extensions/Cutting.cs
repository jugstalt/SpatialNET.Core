using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>
/// Cut according to ADR 0007: a flat list of the pieces without left/right. Cut pieces are one element each;
/// all untouched parts or paths together form one element and keep their original coordinates
/// (without snap rounding). If nothing is cut, the list contains exactly one element. The cutting line is
/// not extended; Z and M at intersection points are interpolated on the original segment (ADR 0005, ADR 0015).
/// </summary>
public static class Cutting
{
    /// <summary>
    /// Splits the polygon along the cutting line: only complete crossings cut; loose ends, runs
    /// along the boundary and touches at vertices do not. Holes are distributed over the pieces accordingly.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="cutter">The cutting line; all paths cut.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public static IReadOnlyList<IPolygon2D> Cut(this IPolygon2D polygon, IPolyline2D cutter, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(cutter);
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, polygon, cutter);

        var result = new List<IPolygon2D>();
        var untouched = new List<PolygonPart>();
        foreach (var part in PolygonStructure.Build(polygon))
        {
            var partPolygon = Imp(part);
            var engine = new OverlayEngine(partPolygon, cutter, grid);
            var graph = engine.Graph;
            var selected = new bool[graph.HalfEdgeCount];
            for (int h = 0; h < graph.HalfEdgeCount; h++)
            {
                // Inneres links, und entweder Rand der Teilfläche oder Schnittlinie im Inneren
                selected[h] = engine.InsideLeft(h, 0) && (!engine.InsideRight(h, 0) || graph.Edges[h >> 1].Line[1] > 0);
            }

            var pieces = engine.BuildAreas(selected);
            if (pieces.Count <= 1)
            {
                untouched.Add(part.ToPolygonPart());
                continue;
            }

            foreach (var piece in pieces)
            {
                result.Add(f.CreatePolygon([GeometryAccess.Restrict(piece, f)], polygon));
            }
        }

        if (untouched.Count > 0 || result.Count == 0)
        {
            result.Add(f.CreatePolygon(untouched.ToArray(), polygon));
        }

        return result;
    }

    /// <summary>
    /// Splits the polyline at every contact with the cutting line (crossing, touch, start and end of an
    /// overlap). Each cut piece is a polyline with exactly one path.
    /// </summary>
    /// <param name="polyline">The polyline.</param>
    /// <param name="cutter">The cutting line; all paths cut.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public static IReadOnlyList<IPolyline2D> Cut(this IPolyline2D polyline, IPolyline2D cutter, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        ArgumentNullException.ThrowIfNull(cutter);
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, polyline, cutter);

        var strings = new List<SegmentString>();
        for (int i = 0; i < polyline.PathCount; i++)
        {
            strings.Add(new SegmentString(Rings.Coordinates(polyline[i]), 0, strings.Count));
        }

        int pathCount = strings.Count;
        for (int i = 0; i < cutter.PathCount; i++)
        {
            var path = Rings.Coordinates(cutter[i]);
            if (path.Length > 0) strings.Add(new SegmentString(path, 1, strings.Count));
        }

        var noded = SnapRoundingNoder.Node(strings, grid);
        var cutNodes = new HashSet<GridPoint>();
        foreach (var e in noded.Where(e => e.Source.Operand == 1))
        {
            cutNodes.Add(e.A);
            cutNodes.Add(e.B);
        }

        var result = new List<IPolyline2D>();
        var untouched = new List<ReadOnlyMemory<Coordinate>>();
        for (int p = 0; p < pathCount; p++)
        {
            var edges = noded.Where(e => e.Source.Index == p).ToList(); // in Pfadreihenfolge
            var pieces = new List<List<Coordinate>>();
            var current = new List<Coordinate>();
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (current.Count == 0) current.Add(grid.ToCoordinate(e.A, e.ZA, e.MA));
                current.Add(grid.ToCoordinate(e.B, e.ZB, e.MB));
                bool last = i == edges.Count - 1;
                if (!last && cutNodes.Contains(e.B))
                {
                    pieces.Add(current);
                    current = [];
                }
            }

            if (current.Count > 0) pieces.Add(current);

            if (pieces.Count <= 1)
            {
                untouched.Add(strings[p].Points);
                continue;
            }

            foreach (var piece in pieces)
            {
                var coordinates = GeometryAccess.Restrict(piece.ToArray(), f);
                result.Add(f.CreatePolyline([coordinates], polyline));
            }
        }

        if (untouched.Count > 0 || result.Count == 0)
        {
            result.Add(f.CreatePolyline(untouched.ToArray(), polyline));
        }

        return result;
    }

    private static IPolygon2D Imp(Part part) => new PartPolygon(part);

    private sealed class PartPolygon(Part part) : IPolygon2D
    {
        private readonly IRing2D[] _rings = part.Holes.Prepend(part.Shell).Select(r => (IRing2D)new PartRing(r)).ToArray();
        public int RingCount => _rings.Length;
        public IRing2D this[int index] => _rings[index];
    }

    private sealed class PartRing(Coordinate[] coordinates) : IRing2D
    {
        private readonly IPoint2D[] _points = coordinates.Select(GeometryAccess.PointOf).ToArray();
        public int PointCount => _points.Length;
        public IPoint2D this[int index] => _points[index];
    }

}
