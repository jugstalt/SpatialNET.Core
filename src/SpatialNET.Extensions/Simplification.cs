using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>Vereinfachung nach Douglas-Peucker (ADR 0012).</summary>
public static class Simplification
{
    /// <summary>
    /// Vereinfacht jeden Pfad nach Douglas-Peucker: Ein Vertex bleibt, wenn er weiter als
    /// <paramref name="tolerance"/> von der Sehne entfernt ist. Anfangs- und Endpunkt bleiben erhalten; die
    /// verbleibenden Vertices sind unverändert (inklusive Z). Mit <paramref name="preserveTopology"/> entstehen
    /// keine neuen Kreuzungen zwischen oder innerhalb der Pfade (wie JTS TopologyPreservingSimplifier).
    /// </summary>
    /// <param name="polyline">Die Polylinie.</param>
    /// <param name="tolerance">Die Toleranz in Karteneinheiten, ≥ 0.</param>
    /// <param name="preserveTopology">Topologieerhaltend vereinfachen.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> ist negativ oder nicht endlich.</exception>
    /// <exception cref="InvalidOperationException">Keine Factory verfügbar.</exception>
    public static IPolyline2D Simplify(this IPolyline2D polyline, double tolerance, bool preserveTopology = false, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        EnsureTolerance(tolerance);
        var f = GeometryFactory.Resolve(factory);

        var paths = Enumerable.Range(0, polyline.PathCount).Select(i => Rings.Coordinates(polyline[i])).Where(p => p.Length > 0).ToList();
        var simplified = preserveTopology
            ? new TopologyPreservingSimplifier(tolerance).Simplify(paths.Select(p => (p, false)).ToList())
            : paths.Select(p => DouglasPeucker(p, tolerance)).ToList();

        return f.CreatePolyline(simplified.Select(p => (ReadOnlyMemory<Coordinate>)Z(p, f)).ToArray(), polyline);
    }

    /// <summary>
    /// Vereinfacht die Ringe des Polygons nach Douglas-Peucker (auch der Ring-Startpunkt darf entfallen); Ringe mit
    /// weniger als 4 Punkten fallen weg. Das Ergebnis wird über das Overlay normalisiert und ist immer gültig
    /// (ADR 0003/0012). Mit <paramref name="preserveTopology"/> gehen keine Ringe verloren und es entstehen keine
    /// Kreuzungen (wie JTS TopologyPreservingSimplifier).
    /// </summary>
    /// <param name="polygon">Das Polygon.</param>
    /// <param name="tolerance">Die Toleranz in Karteneinheiten, ≥ 0.</param>
    /// <param name="preserveTopology">Topologieerhaltend vereinfachen.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite der Normalisierung; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> ist negativ oder nicht endlich.</exception>
    public static IPolygon2D Simplify(this IPolygon2D polygon, double tolerance, bool preserveTopology = false, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        EnsureTolerance(tolerance);
        var f = GeometryFactory.Resolve(factory);

        var parts = PolygonStructure.Build(polygon);
        var rings = parts.SelectMany(p => p.Holes.Prepend(p.Shell)).ToList();
        List<Coordinate[]> simplified;
        if (preserveTopology)
        {
            simplified = new TopologyPreservingSimplifier(tolerance).Simplify(rings.Select(r => (r, true)).ToList());
        }
        else
        {
            simplified = [];
            int index = 0;
            foreach (var part in parts)
            {
                var shell = DouglasPeuckerRing(rings[index++], tolerance);
                var holes = part.Holes.Select(_ => DouglasPeuckerRing(rings[index++], tolerance)).ToList();
                if (shell.Length < 4) continue; // Außenring kollabiert: Teilfläche entfällt (wie JTS)
                simplified.Add(shell);
                simplified.AddRange(holes.Where(h => h.Length >= 4));
            }
        }

        var raw = new RingPolygon(simplified.Where(r => r.Length >= 4).ToArray());
        var grid = Internal.Noding.GridModel.Resolve(gridSize, polygon);
        var engine = new Internal.Overlay.OverlayEngine(raw, null, grid);
        var result = engine.Areas(h => engine.InsideLeft(h, 0));
        return f.CreatePolygon(result.Select(p => f.SupportsZ ? p : FlatPart(p)).ToArray(), polygon);
    }

    internal static void EnsureTolerance(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Die Toleranz muss endlich und ≥ 0 sein.");
        }
    }

    /// <summary>Douglas-Peucker für einen geschlossenen Ring; der Startpunkt darf entfallen (wie JTS ≥ 1.18).</summary>
    internal static Coordinate[] DouglasPeuckerRing(Coordinate[] closed, double tolerance)
    {
        var result = DouglasPeucker(closed, tolerance).ToList();
        if (result.Count >= 4 && Segments.PointDistance(result[0], result[1], result[^2]) <= tolerance)
        {
            result.RemoveAt(0);
            result.RemoveAt(result.Count - 1);
            result.Add(result[0]);
        }

        return result.ToArray();
    }

    private static Coordinate[] Z(Coordinate[] path, IGeometryFactory f) =>
        f.SupportsZ ? path : path.Select(c => new Coordinate(c.X, c.Y)).ToArray();

    private static PolygonPart FlatPart(PolygonPart p) =>
        new(p.Shell.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray(),
            p.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)h.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray()).ToArray());

    private sealed class RingPolygon(Coordinate[][] rings) : IPolygon2D
    {
        private readonly IRing2D[] _rings = rings.Select(r => (IRing2D)new RingPoints(r)).ToArray();
        public int RingCount => _rings.Length;
        public IRing2D this[int index] => _rings[index];
    }

    private sealed class RingPoints(Coordinate[] points) : IRing2D
    {
        public int PointCount => points.Length;
        public IPoint2D this[int index] => points[index].HasZ ? new P3(points[index]) : new P2(points[index]);
    }

    private sealed class P2(Coordinate c) : IPoint2D
    {
        public double X => c.X;
        public double Y => c.Y;
    }

    private sealed class P3(Coordinate c) : IPoint3D
    {
        public double X => c.X;
        public double Y => c.Y;
        public double Z => c.Z;
    }

    /// <summary>Douglas-Peucker über eine Koordinatenfolge, iterativ (kein Stack-Überlauf bei langen Pfaden).</summary>
    internal static Coordinate[] DouglasPeucker(Coordinate[] path, double tolerance)
    {
        if (path.Length <= 2)
        {
            return path;
        }

        var keep = new bool[path.Length];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int From, int To)>();
        stack.Push((0, path.Length - 1));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            double maxDistance = -1;
            int index = -1;
            for (int i = from + 1; i < to; i++)
            {
                double d = Segments.PointDistance(path[i], path[from], path[to]);
                if (d > maxDistance)
                {
                    maxDistance = d;
                    index = i;
                }
            }

            if (index >= 0 && maxDistance > tolerance)
            {
                keep[index] = true;
                stack.Push((from, index));
                stack.Push((index, to));
            }
        }

        return path.Where((_, i) => keep[i]).ToArray();
    }
}
