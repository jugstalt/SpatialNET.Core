using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// Where a point lies relative to a geometry (ADR 0022). Exact: the decisions use the input coordinates without a grid,
/// like the predicates.
/// </summary>
public static class Locations
{
    /// <summary>
    /// The position of <paramref name="point"/> relative to <paramref name="geometry"/>: for a polygon by the even-odd rule over all
    /// rings (a hole is <see cref="Location.Outside"/>, its ring <see cref="Location.Boundary"/>), for a polyline
    /// <see cref="Location.Boundary"/> at an end point by the mod-2 rule and <see cref="Location.Inside"/> elsewhere on the line, for points
    /// <see cref="Location.Inside"/> if the point is one of them. An empty geometry has no inside: <see cref="Location.Outside"/>.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="point">The point; its coordinates must be finite.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static Location Locate(this IGeometry2D geometry, Coordinate point)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        EnsureFinite(point);
        return LocateCore(geometry, point);
    }

    /// <inheritdoc cref="Locate(IGeometry2D, Coordinate)"/>
    public static Location Locate(this IGeometry2D geometry, IPoint2D point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return geometry.Locate(point.ToCoordinate());
    }

    internal static void EnsureFinite(Coordinate point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "The coordinates of the point must be finite.");
        }
    }

    internal static Location LocateCore(IGeometry2D geometry, Coordinate p)
    {
        switch (geometry)
        {
            case IPolygon2D polygon:
                var rings = Enumerable.Range(0, polygon.RingCount).Select(i => Rings.Closed(polygon[i]))
                    .Where(r => r.Length > 0 && !Rings.IsDegenerate(r)).ToList();
                return Rings.LocateInPolygon(p, PolygonStructure.CancelDuplicates(rings));

            case IPolyline2D line:
                if (Constructions.LineBoundary(line).Any(b => Rings.SameXY(b, p)))
                {
                    return Location.Boundary;
                }

                for (int i = 0; i < line.PathCount; i++)
                {
                    var path = Rings.Coordinates(line[i]);
                    if (path.Length == 1 ? Rings.SameXY(path[0], p) : Enumerable.Range(1, path.Length - 1).Any(k => Segments.IsOnSegment(p, path[k - 1], path[k])))
                    {
                        return Location.Inside;
                    }
                }

                return Location.Outside;

            default:
                return GeometryAccess.AllPoints(geometry).Any(q => q.X == p.X && q.Y == p.Y) ? Location.Inside : Location.Outside;
        }
    }
}
