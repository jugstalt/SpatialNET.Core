using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Overlay;

/// <summary>Führt eine Overlay-Operation aus und erzeugt das Ergebnis über die Factory (ADR 0004/0005/0012).</summary>
internal static class OverlayRunner
{
    public static int Dimension(IGeometry2D g) => g switch
    {
        IPoint2D or IMultiPoint2D => 0,
        IPolyline2D => 1,
        _ => 2,
    };

    public static IGeometry2D Run(OverlayOp op, IGeometry2D a, IGeometry2D b, FillRule fill, IGeometryFactory? factory, double? gridSize)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        int da = Dimension(a), db = Dimension(b);

        switch (op)
        {
            case OverlayOp.Intersection:
                if (da == 0 || db == 0) return Points(op, da == 0 ? a : b, da == 0 ? b : a, fill, factory, gridSize);
                return Math.Min(da, db) == 1 ? Lines(op, a, b, fill, factory, gridSize) : Areas(op, a, b, fill, factory, gridSize);

            case OverlayOp.Difference:
                return da switch
                {
                    0 => Points(op, a, b, fill, factory, gridSize),
                    1 => Lines(op, a, db == 0 ? null : b, fill, factory, gridSize),
                    // Linien nehmen am Noding teil (Snap-Rounding wie JTS), tragen aber keine Fläche bei; Punkte nicht.
                    _ => Areas(op, a, db == 0 ? null : b, fill, factory, gridSize),
                };

            default:
                if (da != db && (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b)))
                {
                    // Mit einer leeren Geometrie ist das Ergebnis die andere (normalisiert), unabhängig von der Dimension.
                    var nonEmpty = GeometryAccess.IsEmpty(a) ? b : a;
                    return Dimension(nonEmpty) switch
                    {
                        0 => Points(OverlayOp.Union, nonEmpty, nonEmpty, fill, factory, gridSize),
                        1 => Lines(OverlayOp.Union, nonEmpty, null, fill, factory, gridSize),
                        _ => Areas(OverlayOp.Union, nonEmpty, null, fill, factory, gridSize),
                    };
                }

                if (da != db)
                {
                    throw new NotSupportedException(
                        $"{op} gemischter Dimensionen ({da}/{db}) ist nicht abbildbar: Ergebnisse sind dimensionshomogen (ADR 0004/0012).");
                }

                return da switch
                {
                    0 => Points(op, a, b, fill, factory, gridSize),
                    1 => Lines(op, a, b, fill, factory, gridSize),
                    _ => Areas(op, a, b, fill, factory, gridSize),
                };
        }
    }

    public static IPolygon2D Areas(OverlayOp op, IGeometry2D a, IGeometry2D? b, FillRule fill, IGeometryFactory? factory, double? gridSize)
    {
        var f = GeometryFactory.Resolve(factory);
        var engine = new OverlayEngine(a, b, GridModel.Resolve(gridSize, a, b), fill);
        var parts = engine.Areas(h => OverlayEngine.Apply(op, engine.InsideLeft(h, 0), engine.InsideLeft(h, 1)));
        return f.CreatePolygon(f.SupportsZ ? parts.ToArray() : parts.Select(StripZ).ToArray(), a);
    }

    public static IPolyline2D Lines(OverlayOp op, IGeometry2D a, IGeometry2D? b, FillRule fill, IGeometryFactory? factory, double? gridSize)
    {
        var f = GeometryFactory.Resolve(factory);
        var engine = new OverlayEngine(a, b, GridModel.Resolve(gridSize, a, b), fill);
        var graph = engine.Graph;
        int da = engine.Dimension(0), db = b is null ? -1 : engine.Dimension(1);

        bool Select(int e)
        {
            var edge = graph.Edges[e];
            bool inA = da == 1 ? edge.Line[0] > 0 : da == 2 && engine.EdgeAreaLocation(e, 0) > 0;
            bool inB = db == 1 ? edge.Line[1] > 0 : db == 2 && engine.EdgeAreaLocation(e, 1) > 0;
            bool lineA = da == 1 && edge.Line[0] > 0, lineB = db == 1 && edge.Line[1] > 0;
            return op switch
            {
                // Linie ∩ Fläche: Linienteile im Inneren und auf dem Rand (Abschluss der Fläche)
                OverlayOp.Intersection => (lineA || lineB) && inA && inB,
                // Linie \ Fläche bzw. Linie \ Linie: Linienteile außerhalb
                OverlayOp.Difference => lineA && !inB,
                OverlayOp.Union => lineA || lineB,
                _ => lineA != lineB,
            };
        }

        var paths = engine.Lines(Select);
        return f.CreatePolyline(f.SupportsZ ? paths.ToArray() : paths.Select(p => (ReadOnlyMemory<Coordinate>)StripZ(p.ToArray())).ToArray(), a);
    }

    public static IMultiPoint2D Points(OverlayOp op, IGeometry2D a, IGeometry2D b, FillRule fill, IGeometryFactory? factory, double? gridSize)
    {
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, a, b);
        var engine = new OverlayEngine(a, b, grid, fill);
        var result = new List<Coordinate>();

        var pointsA = engine.Points(0).ToList();
        switch (op)
        {
            case OverlayOp.Intersection:
                result.AddRange(pointsA.Where(p => engine.Covers(1, p.Grid)).Select(p => grid.ToCoordinate(p.Grid, p.Original.Z)));
                break;
            case OverlayOp.Difference:
                result.AddRange(pointsA.Where(p => !engine.Covers(1, p.Grid)).Select(p => grid.ToCoordinate(p.Grid, p.Original.Z)));
                break;
            default:
                var pointsB = engine.Points(1).ToList();
                var setA = pointsA.Select(p => p.Grid).ToHashSet();
                var setB = pointsB.Select(p => p.Grid).ToHashSet();
                var all = pointsA.Concat(pointsB).DistinctBy(p => p.Grid);
                result.AddRange(all.Where(p => op == OverlayOp.Union || setA.Contains(p.Grid) != setB.Contains(p.Grid))
                    .Select(p => grid.ToCoordinate(p.Grid, p.Original.Z)));
                break;
        }

        var coordinates = result.ToArray();
        return f.CreateMultiPoint(f.SupportsZ ? coordinates : StripZ(coordinates), a);
    }

    private static Coordinate[] StripZ(Coordinate[] coordinates) => coordinates.Select(c => new Coordinate(c.X, c.Y)).ToArray();

    private static PolygonPart StripZ(PolygonPart part) =>
        new(StripZ(part.Shell.ToArray()), part.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)StripZ(h.ToArray())).ToArray());
}
