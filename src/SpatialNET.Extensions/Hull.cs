using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>Konvexe Hülle (ADR 0012).</summary>
public static class Hull
{
    /// <summary>
    /// Die konvexe Hülle aller Punkte. Je nach Entartung ein Polygon (gegen den Uhrzeigersinn), eine Polylinie
    /// zwischen den beiden Extrempunkten (alle Punkte kollinear) oder ein Punkt; leer bei leerer Eingabe (wie JTS).
    /// </summary>
    /// <param name="geometry">Die Geometrie.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> ist <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> ist keine bekannte Geometrie-Art.</exception>
    /// <exception cref="InvalidOperationException">Keine Factory verfügbar.</exception>
    public static IGeometry2D ConvexHull(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = GeometryFactory.Resolve(factory);

        var points = GeometryAccess.AllPoints(geometry).Select(p => p.ToCoordinate())
            .OrderBy(c => c.X).ThenBy(c => c.Y)
            .ToList();
        points = Distinct(points);

        if (points.Count == 0)
        {
            return f.CreatePolygon([], geometry);
        }

        if (points.Count == 1)
        {
            return f.CreatePoint(points[0], geometry);
        }

        // Andrews Monotone Chain; kollineare Punkte werden verworfen.
        var hull = new Coordinate[2 * points.Count];
        int k = 0;
        for (int i = 0; i < points.Count; i++)
        {
            while (k >= 2 && Predicates.Orient(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }

        for (int i = points.Count - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Predicates.Orient(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }

        if (k <= 3)
        {
            // alle Punkte kollinear: Linie zwischen den Extrempunkten
            ReadOnlyMemory<Coordinate>[] path = [new[] { points[0], points[^1] }];
            return f.CreatePolyline(path, geometry);
        }

        return f.CreatePolygon([new PolygonPart(hull[..k])], geometry);
    }

    private static List<Coordinate> Distinct(List<Coordinate> sorted)
    {
        var result = new List<Coordinate>(sorted.Count);
        foreach (var c in sorted)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], c))
            {
                result.Add(c);
            }
        }

        return result;
    }
}
