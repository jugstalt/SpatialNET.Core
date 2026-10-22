using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// Small constructions with geometric results (ADR 0019): <c>Boundary</c>, <c>Densify</c> and <c>Reverse</c>.
/// Results are created through the factory and restricted to what it supports (<c>SupportsZ</c>, <c>SupportsM</c>).
/// </summary>
public static class Constructions
{
    /// <summary>The most vertices <c>Densify</c> creates before it refuses the parameter.</summary>
    private const long MaxVertices = 50_000_000;

    // ---------- Boundary ----------

    /// <summary>The boundary of a polygon: its rings, normalized (shells counter-clockwise, holes clockwise), one path per ring.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Boundary(this IPolygon2D polygon, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var f = GeometryFactory.Resolve(factory);
        var paths = PolygonStructure.Build(polygon)
            .SelectMany(part => part.Holes.Prepend(part.Shell))
            .Select(ring => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(ring, f))
            .ToArray();
        return f.CreatePolyline(paths, polygon);
    }

    /// <summary>
    /// The boundary of a polyline: its end points by the mod-2 rule (an end point that occurs an odd number of
    /// times; closed paths have no boundary, ADR 0013).
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D Boundary(this IPolyline2D line, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = GeometryFactory.Resolve(factory);
        var boundary = LineBoundary(line);
        return f.CreateMultiPoint(GeometryAccess.Restrict(boundary, f), line);
    }

    /// <summary>The mod-2 boundary of a polyline: the end points that occur an odd number of times, in order of first occurrence.</summary>
    internal static Coordinate[] LineBoundary(IPolyline2D line)
    {
        var order = new List<(double X, double Y)>();
        var ends = new Dictionary<(double X, double Y), (Coordinate Point, int Count)>();
        for (int i = 0; i < line.PathCount; i++)
        {
            var path = Rings.Coordinates(line[i]);
            if (path.Length < 2 || Rings.SameXY(path[0], path[^1]))
            {
                continue; // closed or without length: no boundary
            }

            foreach (var end in new[] { path[0], path[^1] })
            {
                var key = (end.X, end.Y);
                if (ends.TryGetValue(key, out var known))
                {
                    ends[key] = (known.Point, known.Count + 1);
                }
                else
                {
                    ends[key] = (end, 1);
                    order.Add(key);
                }
            }
        }

        return order.Where(k => ends[k].Count % 2 == 1).Select(k => ends[k].Point).ToArray();
    }

    /// <summary>
    /// The boundary of any geometry: the rings of a polygon, the mod-2 end points of a polyline, and an empty
    /// multipoint for a point or multipoint.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D Boundary(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IPolygon2D polygon => polygon.Boundary(factory),
            IPolyline2D line => line.Boundary(factory),
            _ => GeometryFactory.Resolve(factory).CreateMultiPoint([], geometry),
        };
    }

    // ---------- Densify ----------

    /// <summary>
    /// Splits every segment longer than <paramref name="maxSegmentLength"/> into equal pieces no longer than it; the shape
    /// and all original vertices stay. Z and M of new vertices are interpolated.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="maxSegmentLength">The longest allowed segment; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSegmentLength"/> is not finite or not greater than 0, or so small that the result would be unreasonably large.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Densify(this IPolyline2D line, double maxSegmentLength, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureLength(maxSegmentLength);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(DensifyPath(p, maxSegmentLength, ref budget), f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Densifies the rings of a polygon after their normalization (shells counter-clockwise, holes clockwise), so the
    /// polygon stays valid. See <see cref="Densify(IPolyline2D, double, IGeometryFactory?)"/>.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="maxSegmentLength">The longest allowed segment; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSegmentLength"/> is not finite or not greater than 0, or so small that the result would be unreasonably large.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D Densify(this IPolygon2D polygon, double maxSegmentLength, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        EnsureLength(maxSegmentLength);
        var f = GeometryFactory.Resolve(factory);
        long budget = MaxVertices;
        var parts = PolygonStructure.Build(polygon).Select(part => new PolygonPart(
            GeometryAccess.Restrict(DensifyPath(part.Shell, maxSegmentLength, ref budget), f),
            part.Holes.Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(DensifyPath(h, maxSegmentLength, ref budget), f)).ToArray())).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    /// <summary>Densifies a polyline or polygon; a point or multipoint is copied unchanged.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="maxSegmentLength">The longest allowed segment; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSegmentLength"/> is not finite or not greater than 0, or so small that the result would be unreasonably large.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D Densify(this IGeometry2D geometry, double maxSegmentLength, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        EnsureLength(maxSegmentLength);
        var f = GeometryFactory.Resolve(factory);
        return geometry switch
        {
            IPolygon2D polygon => polygon.Densify(maxSegmentLength, f),
            IPolyline2D line => line.Densify(maxSegmentLength, f),
            IMultiPoint2D multiPoint => f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint), f), geometry),
            _ => f.CreatePoint(GeometryAccess.Restrict(((IPoint2D)geometry).ToCoordinate(), f), geometry),
        };
    }

    private static void EnsureLength(double maxSegmentLength)
    {
        if (!double.IsFinite(maxSegmentLength) || maxSegmentLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSegmentLength), maxSegmentLength, "The maximum segment length must be finite and greater than 0.");
        }
    }

    private static Coordinate[] DensifyPath(Coordinate[] path, double max, ref long budget)
    {
        var result = new List<Coordinate>(path.Length) { path[0] };
        for (int i = 1; i < path.Length; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            double length = Rings.Distance(a, b);
            if (length > max)
            {
                double pieces = Math.Ceiling(length / max);
                budget -= (long)Math.Min(pieces, MaxVertices + 1);
                if (budget < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(max), max, "The maximum segment length is so small that the result would be unreasonably large.");
                }

                int n = (int)pieces;
                for (int k = 1; k < n; k++)
                {
                    result.Add(Segments.Lerp(a, b, k / (double)n));
                }
            }

            result.Add(b);
        }

        return result.ToArray();
    }

    // ---------- Reverse ----------

    /// <summary>Reverses every path and the order of the paths: the same line, traversed backwards.</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Reverse(this IPolyline2D line, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = GeometryFactory.Resolve(factory);
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Reverse()
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p.Reverse().ToArray(), f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }
}
