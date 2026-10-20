using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>Vereinfachung nach Douglas-Peucker (ADR 0012).</summary>
public static class Simplification
{
    /// <summary>
    /// Vereinfacht jeden Pfad nach Douglas-Peucker: Ein Vertex bleibt, wenn er weiter als
    /// <paramref name="tolerance"/> von der Sehne entfernt ist. Anfangs- und Endpunkt bleiben immer erhalten;
    /// die verbleibenden Vertices sind unverändert (inklusive Z).
    /// </summary>
    /// <param name="polyline">Die Polylinie.</param>
    /// <param name="tolerance">Die Toleranz in Karteneinheiten, ≥ 0.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> ist negativ oder nicht endlich.</exception>
    /// <exception cref="InvalidOperationException">Keine Factory verfügbar.</exception>
    public static IPolyline2D Simplify(this IPolyline2D polyline, double tolerance, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        EnsureTolerance(tolerance);
        var f = GeometryFactory.Resolve(factory);

        var paths = new List<ReadOnlyMemory<Coordinate>>(polyline.PathCount);
        for (int i = 0; i < polyline.PathCount; i++)
        {
            var path = Rings.Coordinates(polyline[i]);
            if (path.Length > 0)
            {
                paths.Add(DouglasPeucker(path, tolerance));
            }
        }

        return f.CreatePolyline(paths.ToArray(), polyline);
    }

    internal static void EnsureTolerance(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Die Toleranz muss endlich und ≥ 0 sein.");
        }
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
