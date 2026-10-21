using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>Simplification by Douglas-Peucker (ADR 0012).</summary>
public static class Simplification
{
    /// <summary>
    /// Simplifies each path by Douglas-Peucker: a vertex is kept if it is farther than
    /// <paramref name="tolerance"/> from the chord. Start and end point are kept; the
    /// remaining vertices are unchanged (including Z and M). With <paramref name="preserveTopology"/> no new
    /// crossings arise between or within the paths (as in JTS TopologyPreservingSimplifier).
    /// </summary>
    /// <param name="polyline">The polyline.</param>
    /// <param name="tolerance">The tolerance in map units, ≥ 0.</param>
    /// <param name="preserveTopology">Simplify while preserving topology.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative or not finite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Simplify(this IPolyline2D polyline, double tolerance, bool preserveTopology = false, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        EnsureTolerance(tolerance);
        var f = GeometryFactory.Resolve(factory);

        var paths = Enumerable.Range(0, polyline.PathCount).Select(i => Rings.Coordinates(polyline[i])).Where(p => p.Length > 0).ToList();
        var simplified = preserveTopology
            ? new TopologyPreservingSimplifier(tolerance).Simplify(paths.Select(p => (p, false)).ToList())
            : paths.Select(p => DouglasPeucker(p, tolerance)).ToList();

        return f.CreatePolyline(simplified.Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f)).ToArray(), polyline);
    }

    /// <summary>
    /// Simplifies the rings of the polygon by Douglas-Peucker (the ring start point may also be dropped); rings with
    /// fewer than 4 points are dropped. The result is normalized by the overlay and is always valid
    /// (ADR 0003/0012). With <paramref name="preserveTopology"/> no rings are lost and no
    /// crossings arise (as in JTS TopologyPreservingSimplifier).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="tolerance">The tolerance in map units, ≥ 0.</param>
    /// <param name="preserveTopology">Simplify while preserving topology.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size of the normalization; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative or not finite.</exception>
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
        return f.CreatePolygon(result.Select(p => GeometryAccess.Restrict(p, f)).ToArray(), polygon);
    }

    internal static void EnsureTolerance(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and ≥ 0.");
        }
    }

    /// <summary>Douglas-Peucker for a closed ring; the start point may be dropped (as in JTS ≥ 1.18).</summary>
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

    private sealed class RingPolygon(Coordinate[][] rings) : IPolygon2D
    {
        private readonly IRing2D[] _rings = rings.Select(r => (IRing2D)new RingPoints(r)).ToArray();
        public int RingCount => _rings.Length;
        public IRing2D this[int index] => _rings[index];
    }

    private sealed class RingPoints(Coordinate[] points) : IRing2D
    {
        public int PointCount => points.Length;
        public IPoint2D this[int index] => GeometryAccess.PointOf(points[index]);
    }

    /// <summary>Douglas-Peucker over a coordinate sequence, iterative (no stack overflow for long paths).</summary>
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
