using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Triangulation;

/// <summary>The segments that a constrained triangulation must keep (ADR 0038): collecting them, finding crossings, and noding them.</summary>
internal static class ConstraintSet
{
    /// <summary>
    /// The segments of a geometry: the consecutive pairs of every path of a polyline and the edges of every ring of a polygon (closed).
    /// Points contribute none; segments without length or with a vertex that is not finite are left out.
    /// </summary>
    public static List<(Coordinate A, Coordinate B)> Segments(IGeometry2D geometry)
    {
        var result = new List<(Coordinate, Coordinate)>();
        void Add(Coordinate[] path)
        {
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                if (double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(b.X) && double.IsFinite(b.Y) && (a.X != b.X || a.Y != b.Y))
                {
                    result.Add((a, b));
                }
            }
        }

        switch (geometry)
        {
            case IPolygon2D polygon:
                for (int r = 0; r < polygon.RingCount; r++)
                {
                    Add(Rings.Closed(polygon[r]));
                }

                break;
            case IPolyline2D line:
                for (int p = 0; p < line.PathCount; p++)
                {
                    Add(Rings.Coordinates(line[p]));
                }

                break;
        }

        return result;
    }

    /// <summary>Whether two segments cross in a point that is an end of neither (touching and overlapping do not count).</summary>
    public static bool HasProperCrossing(IReadOnlyList<(Coordinate A, Coordinate B)> segments)
    {
        if (segments.Count < 2)
        {
            return false;
        }

        var index = new SpatialIndex<int>(segments.Select((s, i) => (new Envelope2D(
            Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)));
        var candidates = new List<int>();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            candidates.Clear();
            index.Query(new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), candidates);
            foreach (int j in candidates)
            {
                if (j <= i)
                {
                    continue;
                }

                var t = segments[j];
                if (Orientation.Orient2D(t.A, t.B, s.A) * Orientation.Orient2D(t.A, t.B, s.B) < 0
                    && Orientation.Orient2D(s.A, s.B, t.A) * Orientation.Orient2D(s.A, s.B, t.B) < 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The segments noded on a grid by the overlay core (ADR 0005): crossings become vertices, duplicates collapse.</summary>
    public static List<(Coordinate A, Coordinate B)> Noded(IReadOnlyList<(Coordinate A, Coordinate B)> segments, IGeometryFactory factory, double? gridSize)
    {
        var paths = segments.Select(s => (ReadOnlyMemory<Coordinate>)new[] { s.A, s.B }).ToArray();
        var noded = factory.CreatePolyline(paths, null).Union(factory.CreatePolyline([], null), factory, gridSize);
        return Segments(noded);
    }
}
