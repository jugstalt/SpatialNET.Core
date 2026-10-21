namespace SpatialNET.Internal;

/// <summary>Segment geometry; decisions via the exact orient2d, distances in double.</summary>
internal static class Segments
{
    /// <summary>Exact sign: +1 c left of a→b, −1 right, 0 collinear.</summary>
    public static int Orient(Coordinate a, Coordinate b, Coordinate c) => Noding.Orientation.Orient2D(a, b, c);

    public static bool IsOnSegment(Coordinate p, Coordinate a, Coordinate b) =>
        Orient(a, b, p) == 0 &&
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);

    public static bool Intersects(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        int o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
        if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
        {
            return true;
        }

        return IsOnSegment(c, a, b) || IsOnSegment(d, a, b) || IsOnSegment(a, c, d) || IsOnSegment(b, c, d);
    }

    public static double PointDistance(Coordinate p, Coordinate a, Coordinate b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 == 0)
        {
            return Rings.Distance(p, a);
        }

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Rings.Distance(p, new Coordinate(a.X + t * dx, a.Y + t * dy));
    }

    /// <summary>
    /// The point at parameter <paramref name="t"/> on a segment. The end points keep their own values; inside,
    /// Z and M are interpolated only if both ends carry them (ADR 0015).
    /// </summary>
    public static Coordinate Lerp(Coordinate a, Coordinate b, double t)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        return new Coordinate(
            a.X + t * (b.X - a.X),
            a.Y + t * (b.Y - a.Y),
            a.HasZ && b.HasZ ? a.Z + t * (b.Z - a.Z) : double.NaN,
            a.HasM && b.HasM ? a.M + t * (b.M - a.M) : double.NaN);
    }

    /// <summary>
    /// The nearest points of two segments (a-b and c-d) and their distance. Crossing segments give the crossing on
    /// both sides; among equally near candidates the first wins (ADR 0019).
    /// </summary>
    public static (double Distance, Coordinate OnAb, Coordinate OnCd) Nearest(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        int o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
        if (o1 * o2 < 0 && o3 * o4 < 0)
        {
            double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
            double denominator = rx * sy - ry * sx;
            double t = Math.Clamp(((c.X - a.X) * sy - (c.Y - a.Y) * sx) / denominator, 0, 1);
            double u = Math.Clamp(((c.X - a.X) * ry - (c.Y - a.Y) * rx) / denominator, 0, 1);
            return (0, Lerp(a, b, t), Lerp(c, d, u));
        }

        var best = (Distance: double.PositiveInfinity, OnAb: a, OnCd: c);
        var (da, onCdA) = ToSegment(a, c, d);
        if (da < best.Distance) best = (da, a, onCdA);
        var (db, onCdB) = ToSegment(b, c, d);
        if (db < best.Distance) best = (db, b, onCdB);
        var (dc, onAbC) = ToSegment(c, a, b);
        if (dc < best.Distance) best = (dc, onAbC, c);
        var (dd, onAbD) = ToSegment(d, a, b);
        if (dd < best.Distance) best = (dd, onAbD, d);
        return best;
    }

    /// <summary>The distance of a point to a segment and the nearest point on the segment (the point itself if it lies on it).</summary>
    private static (double Distance, Coordinate OnSegment) ToSegment(Coordinate p, Coordinate a, Coordinate b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        var q = Lerp(a, b, t);
        if (IsOnSegment(p, a, b) || (len2 == 0 && p.X == a.X && p.Y == a.Y))
        {
            return (0, q with { X = p.X, Y = p.Y });
        }

        return (Rings.Distance(p, q), q);
    }

    public static double Distance(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        if (Intersects(a, b, c, d))
        {
            return 0;
        }

        return Math.Min(
            Math.Min(PointDistance(a, c, d), PointDistance(b, c, d)),
            Math.Min(PointDistance(c, a, b), PointDistance(d, a, b)));
    }
}
