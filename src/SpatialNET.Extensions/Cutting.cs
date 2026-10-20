using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>
/// Cut nach ADR 0007: flache Liste der Teile ohne links/rechts. Zerschnittene Stücke sind je ein Element;
/// alle unberührten Teilflächen bzw. Pfade bilden zusammen ein Element und behalten ihre Originalkoordinaten
/// (ohne Snap-Rounding). Wird nichts geschnitten, enthält die Liste genau ein Element. Die Schnittlinie wird
/// nicht verlängert; Z an Schnittpunkten wird auf dem Originalsegment interpoliert (ADR 0005).
/// </summary>
public static class Cutting
{
    /// <summary>
    /// Zerlegt das Polygon an der Schnittlinie: Nur vollständige Durchquerungen schneiden; lose Enden, Verläufe
    /// entlang des Randes und Berührungen in Vertices nicht. Löcher werden passend auf die Teile verteilt.
    /// </summary>
    /// <param name="polygon">Das Polygon.</param>
    /// <param name="cutter">Die Schnittlinie; alle Pfade schneiden.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException">Ein Argument ist <c>null</c>.</exception>
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
                result.Add(f.CreatePolygon([f.SupportsZ ? piece : StripZ(piece)], polygon));
            }
        }

        if (untouched.Count > 0 || result.Count == 0)
        {
            result.Add(f.CreatePolygon(untouched.ToArray(), polygon));
        }

        return result;
    }

    /// <summary>
    /// Zerlegt die Polylinie an jedem Kontakt mit der Schnittlinie (Kreuzung, Berührung, Anfang und Ende einer
    /// Überlappung). Jedes zerschnittene Stück ist eine Polylinie mit genau einem Pfad.
    /// </summary>
    /// <param name="polyline">Die Polylinie.</param>
    /// <param name="cutter">Die Schnittlinie; alle Pfade schneiden.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException">Ein Argument ist <c>null</c>.</exception>
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
                if (current.Count == 0) current.Add(grid.ToCoordinate(e.A, e.ZA));
                current.Add(grid.ToCoordinate(e.B, e.ZB));
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
                var coordinates = f.SupportsZ ? piece.ToArray() : piece.Select(c => new Coordinate(c.X, c.Y)).ToArray();
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
        private readonly IPoint2D[] _points = coordinates.Select(c => c.HasZ ? new PartPoint3(c) : (IPoint2D)new PartPoint(c)).ToArray();
        public int PointCount => _points.Length;
        public IPoint2D this[int index] => _points[index];
    }

    private sealed class PartPoint(Coordinate c) : IPoint2D
    {
        public double X => c.X;
        public double Y => c.Y;
    }

    private sealed class PartPoint3(Coordinate c) : IPoint3D
    {
        public double X => c.X;
        public double Y => c.Y;
        public double Z => c.Z;
    }

    private static PolygonPart StripZ(PolygonPart part) =>
        new(part.Shell.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray(),
            part.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)h.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray()).ToArray());
}
