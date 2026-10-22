namespace SpatialNET.Internal;

/// <summary>Basic computation on rings and paths.</summary>
internal static class Rings
{
    /// <summary>The coordinates of a point sequence.</summary>
    public static Coordinate[] Coordinates(IPointCollection2D points)
    {
        var result = new Coordinate[points.PointCount];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = points[i].ToCoordinate();
        }

        return result;
    }

    /// <summary>The coordinates of a ring, closed (closing point = start point), even if the host stores it open.</summary>
    public static Coordinate[] Closed(IPointCollection2D ring)
    {
        var c = Coordinates(ring);
        if (c.Length == 0 || SameXY(c[0], c[^1]))
        {
            return c;
        }

        return [.. c, c[0]];
    }

    public static bool SameXY(Coordinate a, Coordinate b) => a.X == b.X && a.Y == b.Y;

    /// <summary>Signed area of a closed ring; positive counter-clockwise.</summary>
    /// <remarks>Computed relative to the first point so that large coordinates (UTM) do not cancel out.</remarks>
    public static double SignedArea(ReadOnlySpan<Coordinate> closed)
    {
        if (closed.Length < 4)
        {
            return 0;
        }

        double x0 = closed[0].X, y0 = closed[0].Y, sum = 0;
        for (int i = 1; i < closed.Length - 1; i++)
        {
            sum += (closed[i].X - x0) * (closed[i + 1].Y - y0) - (closed[i + 1].X - x0) * (closed[i].Y - y0);
        }

        return sum / 2;
    }

    /// <summary>
    /// Whether a ring has no area: fewer than 3 distinct points or all points exactly collinear.
    /// (Not via the signed area – for self-crossing rings it can be 0.)
    /// </summary>
    public static bool IsDegenerate(ReadOnlySpan<Coordinate> closed)
    {
        if (closed.Length < 4) return true;
        var a = closed[0];
        int i = 1;
        while (i < closed.Length && SameXY(closed[i], a)) i++;
        if (i == closed.Length) return true;
        var b = closed[i];
        for (int k = i + 1; k < closed.Length; k++)
        {
            if (Noding.Orientation.Orient2D(a, b, closed[k]) != 0) return false;
        }

        return true;
    }

    /// <summary>Position of a point relative to a closed ring (crossing number, boundary explicit).</summary>
    public static Location Locate(Coordinate p, ReadOnlySpan<Coordinate> closed)
    {
        bool inside = false;
        for (int i = 1; i < closed.Length; i++)
        {
            var a = closed[i - 1];
            var b = closed[i];
            if (Segments.IsOnSegment(p, a, b))
            {
                return Location.Boundary;
            }

            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (p.X < x)
                {
                    inside = !inside;
                }
            }
        }

        return inside ? Location.Inside : Location.Outside;
    }

    /// <summary>
    /// Position of a point relative to the area of a polygon by the even-odd rule over all rings (ADR 0003).
    /// </summary>
    public static Location LocateInPolygon(Coordinate p, IReadOnlyList<Coordinate[]> closedRings)
    {
        int count = 0;
        foreach (var ring in closedRings)
        {
            switch (Locate(p, ring))
            {
                case Location.Boundary:
                    return Location.Boundary;
                case Location.Inside:
                    count++;
                    break;
            }
        }

        return count % 2 == 1 ? Location.Inside : Location.Outside;
    }

    public static double Length(ReadOnlySpan<Coordinate> path)
    {
        double sum = 0;
        for (int i = 1; i < path.Length; i++)
        {
            sum += Distance(path[i - 1], path[i]);
        }

        return sum;
    }

    public static double Distance(Coordinate a, Coordinate b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
