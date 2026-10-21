using SpatialNET.Internal;

namespace SpatialNET;

public static partial class Measures
{
    /// <summary>
    /// The two points, one on each geometry, whose distance is <see cref="Distance(IGeometry2D, IGeometry2D)"/> (ADR 0019).
    /// If the geometries intersect, or one lies in the area of the other, both points are the same. Among several
    /// equally near pairs the first in input order is returned. Z and M are interpolated on their segment (ADR 0015).
    /// </summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <returns>The pair, or <c>null</c> if either geometry is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static (Coordinate OnA, Coordinate OnB)? NearestPoints(this IGeometry2D a, IGeometry2D b)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));

        if (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b))
        {
            return null;
        }

        if (FirstPointInArea(a, b) is { } inA)
        {
            return (inA, inA);
        }

        if (FirstPointInArea(b, a) is { } inB)
        {
            return (inB, inB);
        }

        var segmentsB = GeometryAccess.Segments(b).ToArray();
        var best = (Distance: double.PositiveInfinity, OnA: default(Coordinate), OnB: default(Coordinate));
        foreach (var (p, q) in GeometryAccess.Segments(a))
        {
            foreach (var (r, s) in segmentsB)
            {
                var candidate = Internal.Segments.Nearest(p, q, r, s);
                if (candidate.Distance < best.Distance)
                {
                    best = (candidate.Distance, candidate.OnAb, candidate.OnCd);
                    if (best.Distance == 0)
                    {
                        return (best.OnA, best.OnB);
                    }
                }
            }
        }

        return (best.OnA, best.OnB);
    }

    /// <summary>
    /// Whether the distance between two geometries is at most <paramref name="distance"/> (0 = they touch or
    /// overlap). Faster than comparing <see cref="Distance(IGeometry2D, IGeometry2D)"/> because it stops at the first
    /// pair of segments within the distance (ADR 0019).
    /// </summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="distance">The distance; finite and not negative.</param>
    /// <returns><c>false</c> if either geometry is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is negative, <c>NaN</c> or infinite.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    public static bool IsWithinDistance(this IGeometry2D a, IGeometry2D b, double distance)
    {
        GeometryAccess.EnsureKnown(a, nameof(a));
        GeometryAccess.EnsureKnown(b, nameof(b));
        if (!double.IsFinite(distance) || distance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be finite and not negative.");
        }

        if (GeometryAccess.IsEmpty(a) || GeometryAccess.IsEmpty(b))
        {
            return false;
        }

        var ea = a.Envelope();
        var eb = b.Envelope();
        double gapX = Math.Max(0, Math.Max(ea.MinX - eb.MaxX, eb.MinX - ea.MaxX));
        double gapY = Math.Max(0, Math.Max(ea.MinY - eb.MaxY, eb.MinY - ea.MaxY));
        if (gapX * gapX + gapY * gapY > distance * distance)
        {
            return false;
        }

        if (HasPointInArea(a, b) || HasPointInArea(b, a))
        {
            return true;
        }

        var segmentsB = GeometryAccess.Segments(b).ToArray();
        foreach (var (p, q) in GeometryAccess.Segments(a))
        {
            foreach (var (r, s) in segmentsB)
            {
                if (Internal.Segments.Distance(p, q, r, s) <= distance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A point that lies on the geometry, without Z and M, <c>null</c> for an empty geometry (ADR 0019). For a polygon
    /// it is an interior point (the widest stretch of a horizontal line through the envelope), for a polyline the
    /// interior vertex nearest to the centroid (or the middle of the line), for points the point nearest to the centroid.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static Coordinate? PointOnSurface(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));

        if (GeometryAccess.IsEmpty(geometry))
        {
            return null;
        }

        return geometry switch
        {
            IPolygon2D polygon => AreaPoint(polygon),
            IPolyline2D line => LinePoint(line),
            _ => NearestToCentroid(GeometryAccess.AllPoints(geometry).Select(p => p.ToCoordinate()).ToList(), geometry),
        };
    }

    private static Coordinate? FirstPointInArea(IGeometry2D area, IGeometry2D other)
    {
        if (area is not IPolygon2D polygon)
        {
            return null;
        }

        var rings = new List<Coordinate[]>();
        for (int i = 0; i < polygon.RingCount; i++)
        {
            rings.Add(Rings.Closed(polygon[i]));
        }

        foreach (var p in GeometryAccess.AllPoints(other))
        {
            var c = p.ToCoordinate();
            if (Rings.LocateInPolygon(c, rings) != Location.Outside)
            {
                return c;
            }
        }

        return null;
    }

    private static Coordinate AreaPoint(IPolygon2D polygon)
    {
        var rings = Enumerable.Range(0, polygon.RingCount).Select(i => Rings.Closed(polygon[i]))
            .Where(r => r.Length > 0 && !Rings.IsDegenerate(r)).ToList();
        var vertices = rings.SelectMany(r => r).ToList();
        if (vertices.Count == 0)
        {
            var first = GeometryAccess.AllPoints(polygon).First();
            return new Coordinate(first.X, first.Y);
        }

        double minY = vertices.Min(v => v.Y), maxY = vertices.Max(v => v.Y), centre = minY / 2 + maxY / 2;
        double lo = minY, hi = maxY;
        foreach (var v in vertices)
        {
            if (v.Y <= centre && v.Y > lo) lo = v.Y;
            else if (v.Y > centre && v.Y < hi) hi = v.Y;
        }

        // A horizontal line between two neighbouring vertex heights meets no vertex.
        double scanY = lo / 2 + hi / 2;
        var crossings = new List<double>();
        foreach (var ring in rings)
        {
            for (int i = 1; i < ring.Length; i++)
            {
                var p = ring[i - 1];
                var q = ring[i];
                if ((p.Y > scanY) != (q.Y > scanY))
                {
                    crossings.Add(p.X + (scanY - p.Y) * (q.X - p.X) / (q.Y - p.Y));
                }
            }
        }

        crossings.Sort();
        double bestWidth = -1, bestX = double.NaN;
        for (int i = 0; i + 1 < crossings.Count; i += 2)
        {
            double width = crossings[i + 1] - crossings[i];
            if (width > bestWidth)
            {
                bestWidth = width;
                bestX = crossings[i] / 2 + crossings[i + 1] / 2;
            }
        }

        // A polygon without area has no interior: take a vertex of its rings.
        return double.IsNaN(bestX) ? new Coordinate(vertices[0].X, vertices[0].Y) : new Coordinate(bestX, scanY);
    }

    private static Coordinate LinePoint(IPolyline2D line)
    {
        var interior = new List<Coordinate>();
        for (int i = 0; i < line.PathCount; i++)
        {
            var path = Rings.Coordinates(line[i]);
            for (int k = 1; k < path.Length - 1; k++)
            {
                interior.Add(path[k]);
            }
        }

        if (interior.Count > 0)
        {
            return NearestToCentroid(interior, line)!.Value;
        }

        double length = line.Length();
        if (length > 0 && line.Interpolate(length / 2) is { } middle)
        {
            return new Coordinate(middle.X, middle.Y);
        }

        var first = GeometryAccess.AllPoints(line).First();
        return new Coordinate(first.X, first.Y);
    }

    private static Coordinate? NearestToCentroid(List<Coordinate> candidates, IGeometry2D geometry)
    {
        var centre = geometry.Centroid() ?? candidates[0];
        var best = candidates[0];
        double bestDistance = double.PositiveInfinity;
        foreach (var c in candidates)
        {
            double d = Rings.Distance(c, centre);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = c;
            }
        }

        return new Coordinate(best.X, best.Y);
    }
}
