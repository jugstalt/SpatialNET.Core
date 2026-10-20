namespace SpatialNET.Internal;

/// <summary>Einheitlicher Zugriff auf die vier Geometrie-Arten.</summary>
internal static class GeometryAccess
{
    public static Coordinate ToCoordinate(this IPoint2D p) =>
        p is IPoint3D p3 ? new Coordinate(p.X, p.Y, p3.Z) : new Coordinate(p.X, p.Y);

    /// <summary>Wirft bei <c>null</c> bzw. einer unbekannten Geometrie-Art (ADR 0012).</summary>
    public static void EnsureKnown(IGeometry2D geometry, string paramName)
    {
        ArgumentNullException.ThrowIfNull(geometry, paramName);
        if (geometry is not (IPoint2D or IMultiPoint2D or IPolyline2D or IPolygon2D))
        {
            throw new NotSupportedException(
                $"{geometry.GetType().FullName} ist keine der Geometrie-Arten IPoint2D, IMultiPoint2D, IPolyline2D oder IPolygon2D.");
        }
    }

    /// <summary>Alle Punkte einer Geometrie (bei Ringen wie gespeichert, ohne Schluss-Ergänzung).</summary>
    public static IEnumerable<IPoint2D> AllPoints(IGeometry2D geometry)
    {
        switch (geometry)
        {
            case IPoint2D p:
                yield return p;
                break;
            case IMultiPoint2D mp:
                for (int i = 0; i < mp.PointCount; i++) yield return mp[i];
                break;
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                    for (int k = 0, n = line[i].PointCount; k < n; k++) yield return line[i][k];
                break;
            case IPolygon2D polygon:
                for (int i = 0; i < polygon.RingCount; i++)
                    for (int k = 0, n = polygon[i].PointCount; k < n; k++) yield return polygon[i][k];
                break;
        }
    }

    /// <summary>Ob die Geometrie keine Punkte hat.</summary>
    public static bool IsEmpty(IGeometry2D geometry) => geometry switch
    {
        IPoint2D => false,
        _ => !AllPoints(geometry).Any(),
    };

    /// <summary>
    /// Die Liniensegmente einer Geometrie: Pfade wie gespeichert, Ringe geschlossen,
    /// Punkte als Segmente der Länge 0.
    /// </summary>
    public static IEnumerable<(Coordinate A, Coordinate B)> Segments(IGeometry2D geometry)
    {
        switch (geometry)
        {
            case IPoint2D p:
                yield return (p.ToCoordinate(), p.ToCoordinate());
                break;
            case IMultiPoint2D mp:
                for (int i = 0; i < mp.PointCount; i++) yield return (mp[i].ToCoordinate(), mp[i].ToCoordinate());
                break;
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                    foreach (var s in PathSegments(Rings.Coordinates(line[i]))) yield return s;
                break;
            case IPolygon2D polygon:
                for (int i = 0; i < polygon.RingCount; i++)
                    foreach (var s in PathSegments(Rings.Closed(polygon[i]))) yield return s;
                break;
        }
    }

    private static IEnumerable<(Coordinate, Coordinate)> PathSegments(Coordinate[] path)
    {
        if (path.Length == 1)
        {
            yield return (path[0], path[0]);
        }

        for (int i = 1; i < path.Length; i++)
        {
            yield return (path[i - 1], path[i]);
        }
    }
}
