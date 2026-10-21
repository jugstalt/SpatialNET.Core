namespace SpatialNET.Internal;

/// <summary>Uniform access to the four geometry kinds.</summary>
internal static class GeometryAccess
{
    public static Coordinate ToCoordinate(this IPoint2D p) =>
        new(p.X, p.Y, p is IPoint3D p3 ? p3.Z : double.NaN, p is IPointM pm ? pm.M : double.NaN);

    /// <summary>The coordinate without the dimensions that the factory does not support (<c>SupportsZ</c>, <c>SupportsM</c>).</summary>
    public static Coordinate Restrict(Coordinate c, IGeometryFactory f) =>
        new(c.X, c.Y, f.SupportsZ ? c.Z : double.NaN, f.SupportsM ? c.M : double.NaN);

    public static Coordinate[] Restrict(Coordinate[] coordinates, IGeometryFactory f) =>
        f.SupportsZ && f.SupportsM ? coordinates : coordinates.Select(c => Restrict(c, f)).ToArray();

    public static PolygonPart Restrict(PolygonPart part, IGeometryFactory f) =>
        f.SupportsZ && f.SupportsM
            ? part
            : new(Restrict(part.Shell.ToArray(), f),
                part.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)Restrict(h.ToArray(), f)).ToArray());

    /// <summary>A read-only point for a coordinate that exposes Z and M only if the coordinate carries them.</summary>
    public static IPoint2D PointOf(Coordinate c) => (c.HasZ, c.HasM) switch
    {
        (false, false) => new CoordinatePoint(c),
        (true, false) => new CoordinatePointZ(c),
        (false, true) => new CoordinatePointM(c),
        _ => new CoordinatePointZM(c),
    };

    private class CoordinatePoint(Coordinate coordinate) : IPoint2D
    {
        protected Coordinate Value { get; } = coordinate;
        public double X => Value.X;
        public double Y => Value.Y;
    }

    private sealed class CoordinatePointZ(Coordinate c) : CoordinatePoint(c), IPoint3D
    {
        public double Z => Value.Z;
    }

    private sealed class CoordinatePointM(Coordinate c) : CoordinatePoint(c), IPointM
    {
        public double M => Value.M;
    }

    private sealed class CoordinatePointZM(Coordinate c) : CoordinatePoint(c), IPoint3D, IPointM
    {
        public double Z => Value.Z;
        public double M => Value.M;
    }

    /// <summary>Throws for <c>null</c> or an unknown geometry kind (ADR 0012).</summary>
    public static void EnsureKnown(IGeometry2D geometry, string paramName)
    {
        ArgumentNullException.ThrowIfNull(geometry, paramName);
        if (geometry is not (IPoint2D or IMultiPoint2D or IPolyline2D or IPolygon2D))
        {
            throw new NotSupportedException(
                $"{geometry.GetType().FullName} is none of the geometry kinds IPoint2D, IMultiPoint2D, IPolyline2D or IPolygon2D.");
        }
    }

    /// <summary>All points of a geometry (for rings as stored, without closing point added).</summary>
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

    /// <summary>Whether the geometry has no points.</summary>
    public static bool IsEmpty(IGeometry2D geometry) => geometry switch
    {
        IPoint2D => false,
        _ => !AllPoints(geometry).Any(),
    };

    /// <summary>
    /// The line segments of a geometry: paths as stored, rings closed,
    /// points as segments of length 0.
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
