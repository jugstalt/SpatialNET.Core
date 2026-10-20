namespace SpatialNET.Internal;

/// <summary>Segment-Geometrie; Entscheidungen über das exakte orient2d, Abstände in double.</summary>
internal static class Segments
{
    /// <summary>Exaktes Vorzeichen: +1 c links von a→b, −1 rechts, 0 kollinear.</summary>
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
