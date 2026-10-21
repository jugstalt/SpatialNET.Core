using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>Measures: area, length, envelope, centroid and distance (ADR 0012).</summary>
/// <remarks>
/// Measures assume valid input (ADR 0003); for invalid input the result is
/// deterministic but not guaranteed. Empty or invalid geometries never throw.
/// </remarks>
public static partial class Measures
{
    /// <summary>The area by the even-odd rule: shells minus holes, islands in holes positive again.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    public static double Area(this IPolygon2D polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        double area = 0;
        foreach (var part in PolygonStructure.Build(polygon))
        {
            area += Rings.SignedArea(part.Shell);
            foreach (var hole in part.Holes)
            {
                area += Rings.SignedArea(hole); // Löcher sind im Uhrzeigersinn, also negativ
            }
        }

        return area;
    }

    /// <summary>The area; 0 for points, multipoints and polylines.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static double Area(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry is IPolygon2D polygon ? polygon.Area() : 0;
    }

    /// <summary>The length of all paths.</summary>
    /// <param name="polyline">The polyline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> is <c>null</c>.</exception>
    public static double Length(this IPolyline2D polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);

        double length = 0;
        for (int i = 0; i < polyline.PathCount; i++)
        {
            length += Rings.Length(Rings.Coordinates(polyline[i]));
        }

        return length;
    }

    /// <summary>The perimeter: the length of all rings, each closed.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    public static double Length(this IPolygon2D polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        double length = 0;
        for (int i = 0; i < polygon.RingCount; i++)
        {
            length += Rings.Length(Rings.Closed(polygon[i]));
        }

        return length;
    }

    /// <summary>The length or perimeter; 0 for points and multipoints.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static double Length(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IPolyline2D line => line.Length(),
            IPolygon2D polygon => polygon.Length(),
            _ => 0,
        };
    }

    /// <summary>The envelope; <see cref="Envelope2D.Empty"/> for an empty geometry.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static Envelope2D Envelope(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in GeometryAccess.AllPoints(geometry))
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return double.IsPositiveInfinity(minX) ? Envelope2D.Empty : new Envelope2D(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// The centroid as a value, without Z; <c>null</c> for an empty geometry.
    /// Areas are area-weighted, lines length-weighted, points averaged; if a geometry has
    /// no area or length, the computation falls back to the next lower dimension (as in JTS).
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static Coordinate? Centroid(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));

        if (geometry is IPolygon2D polygon && AreaCentroid(polygon) is { } areaCentroid)
        {
            return areaCentroid;
        }

        if (geometry is IPolyline2D or IPolygon2D && LineCentroid(geometry) is { } lineCentroid)
        {
            return lineCentroid;
        }

        return PointCentroid(geometry);
    }

    /// <summary>
    /// The smallest distance between two geometries; 0 if they intersect or one lies in the area of the
    /// other. <see cref="double.NaN"/> if either is empty (deliberate deviation from NTS, ADR 0012).
    /// </summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static double Distance(this IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));

        if (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b))
        {
            return double.NaN;
        }

        if (HasPointInArea(a, b) || HasPointInArea(b, a))
        {
            return 0;
        }

        var segmentsB = GeometryAccess.Segments(b).ToArray();
        double min = double.PositiveInfinity;
        foreach (var (p, q) in GeometryAccess.Segments(a))
        {
            foreach (var (r, s) in segmentsB)
            {
                min = Math.Min(min, Segments.Distance(p, q, r, s));
                if (min == 0)
                {
                    return 0;
                }
            }
        }

        return min;
    }

    private static bool HasPointInArea(IGeometry2D area, IGeometry2D other)
    {
        if (area is not IPolygon2D polygon)
        {
            return false;
        }

        var rings = new List<Coordinate[]>();
        for (int i = 0; i < polygon.RingCount; i++)
        {
            rings.Add(Rings.Closed(polygon[i]));
        }

        return GeometryAccess.AllPoints(other).Any(p => Rings.LocateInPolygon(p.ToCoordinate(), rings) != Location.Outside);
    }

    private static Coordinate? AreaCentroid(IPolygon2D polygon)
    {
        double area = 0, cx = 0, cy = 0;
        foreach (var part in PolygonStructure.Build(polygon))
        {
            foreach (var ring in part.Holes.Prepend(part.Shell))
            {
                var (a, x, y) = RingCentroid(ring);
                area += a;
                cx += a * x;
                cy += a * y;
            }
        }

        return area == 0 ? null : new Coordinate(cx / area, cy / area);
    }

    /// <summary>Signed area and centroid of a ring (triangle fan relative to the first point).</summary>
    private static (double Area, double X, double Y) RingCentroid(Coordinate[] closed)
    {
        double x0 = closed[0].X, y0 = closed[0].Y, area2 = 0, sx = 0, sy = 0;
        for (int i = 1; i < closed.Length - 1; i++)
        {
            double xi = closed[i].X - x0, yi = closed[i].Y - y0;
            double xj = closed[i + 1].X - x0, yj = closed[i + 1].Y - y0;
            double cross = xi * yj - xj * yi;
            area2 += cross;
            sx += cross * (xi + xj);
            sy += cross * (yi + yj);
        }

        return (area2 / 2, x0 + sx / (3 * area2), y0 + sy / (3 * area2));
    }

    private static Coordinate? LineCentroid(IGeometry2D geometry)
    {
        double length = 0, cx = 0, cy = 0;
        foreach (var (a, b) in GeometryAccess.Segments(geometry))
        {
            double l = Rings.Distance(a, b);
            length += l;
            cx += l * (a.X + b.X) / 2;
            cy += l * (a.Y + b.Y) / 2;
        }

        return length == 0 ? null : new Coordinate(cx / length, cy / length);
    }

    private static Coordinate? PointCentroid(IGeometry2D geometry)
    {
        double cx = 0, cy = 0;
        int n = 0;
        foreach (var p in GeometryAccess.AllPoints(geometry))
        {
            cx += p.X;
            cy += p.Y;
            n++;
        }

        return n == 0 ? null : new Coordinate(cx / n, cy / n);
    }
}
