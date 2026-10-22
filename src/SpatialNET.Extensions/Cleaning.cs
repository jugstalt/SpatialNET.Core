using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// Clean-up of geometries (ADR 0027): <c>RemoveHoles</c>, <c>RemoveSmallParts</c>, <c>SnapToGrid</c> and <c>ReducePrecision</c>.
/// Results are created through the factory and restricted to what it supports (<c>SupportsZ</c>, <c>SupportsM</c>).
/// </summary>
public static class Cleaning
{
    // ---------- RemoveHoles ----------

    /// <summary>
    /// Removes the holes of a polygon whose area is <b>smaller than</b> <paramref name="minArea"/>; the others stay. Without
    /// a limit (the default <see cref="double.PositiveInfinity"/>) every hole is removed. Shells and parts stay as they are;
    /// the rings are normalized (ADR 0003).
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="minArea">The smallest area a hole must have to stay; not negative, not <c>NaN</c>. 0 keeps every hole.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minArea"/> is negative or <c>NaN</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D RemoveHoles(this IPolygon2D polygon, double minArea = double.PositiveInfinity, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        EnsureLimit(minArea, nameof(minArea));
        var f = GeometryFactory.Resolve(factory);
        var parts = PolygonStructure.Build(polygon).Select(part => new PolygonPart(
            GeometryAccess.Restrict(part.Shell, f),
            part.Holes.Where(h => Math.Abs(Rings.SignedArea(h)) >= minArea)
                .Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(h, f)).ToArray())).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    // ---------- RemoveSmallParts ----------

    /// <summary>
    /// Removes the parts of a polygon whose area (shell minus holes) is <b>smaller than</b> <paramref name="minArea"/>.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="minArea">The smallest area a part must have to stay; not negative, not <c>NaN</c>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minArea"/> is negative or <c>NaN</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D RemoveSmallParts(this IPolygon2D polygon, double minArea, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        EnsureLimit(minArea, nameof(minArea));
        var f = GeometryFactory.Resolve(factory);
        var parts = PolygonStructure.Build(polygon)
            .Where(part => Math.Abs(Rings.SignedArea(part.Shell)) - part.Holes.Sum(h => Math.Abs(Rings.SignedArea(h))) >= minArea)
            .Select(part => GeometryAccess.Restrict(part.ToPolygonPart(), f)).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    /// <summary>Removes the paths of a polyline that are <b>shorter than</b> <paramref name="minLength"/>.</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="minLength">The smallest length a path must have to stay; not negative, not <c>NaN</c>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minLength"/> is negative or <c>NaN</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D RemoveSmallParts(this IPolyline2D line, double minLength, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureLimit(minLength, nameof(minLength));
        var f = GeometryFactory.Resolve(factory);
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0 && Rings.Length(p) >= minLength)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Removes the small parts of any geometry: polygon parts by area, polyline paths by length (see the overloads); a point
    /// or multipoint has no size and is copied unchanged.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="minSize">The smallest area (polygon) or length (polyline) that a part must have to stay; not negative, not <c>NaN</c>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minSize"/> is negative or <c>NaN</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D RemoveSmallParts(this IGeometry2D geometry, double minSize, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        EnsureLimit(minSize, nameof(minSize));
        var f = GeometryFactory.Resolve(factory);
        return geometry switch
        {
            IPolygon2D polygon => polygon.RemoveSmallParts(minSize, f),
            IPolyline2D line => line.RemoveSmallParts(minSize, f),
            IMultiPoint2D multiPoint => f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint), f), geometry),
            _ => f.CreatePoint(GeometryAccess.Restrict(((IPoint2D)geometry).ToCoordinate(), f), geometry),
        };
    }

    // ---------- SnapToGrid ----------

    /// <summary>
    /// Rounds X and Y of every vertex to the nearest multiple of <paramref name="gridSize"/> (halves away from zero) and
    /// nothing else (ADR 0027). Vertices that fall together are merged (consecutive repeats of a path or ring), paths with fewer
    /// than two and rings with fewer than three distinct vertices are dropped, and a part whose shell collapses goes with its
    /// holes. The result <b>may be invalid</b>: rounding can make edges cross or touch. Use
    /// <see cref="ReducePrecision(IGeometry2D, double, IGeometryFactory?)"/> for a valid result. Z and M of a merged vertex are
    /// those of the first one. The points of a multipoint all stay, also if they now coincide.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="gridSize">The distance of the grid lines; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a rounded coordinate leaves the range of <see cref="double"/>.</exception>
    public static IGeometry2D SnapToGrid(this IGeometry2D geometry, double gridSize, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        EnsureGrid(gridSize);
        var f = GeometryFactory.Resolve(factory);
        Coordinate Snap(Coordinate c)
        {
            double x = Math.Round(c.X / gridSize, MidpointRounding.AwayFromZero) * gridSize + 0.0;
            double y = Math.Round(c.Y / gridSize, MidpointRounding.AwayFromZero) * gridSize + 0.0;
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new InvalidOperationException("A rounded coordinate is not finite; the grid size is too small for the coordinates.");
            }

            return c with { X = x, Y = y };
        }

        switch (geometry)
        {
            case IPolygon2D polygon:
                var parts = new List<PolygonPart>();
                foreach (var part in PolygonStructure.Build(polygon))
                {
                    var shell = SnapRing(part.Shell, Snap);
                    if (shell is null)
                    {
                        continue;
                    }

                    var holes = part.Holes.Select(h => SnapRing(h, Snap)).Where(h => h is not null)
                        .Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(PolygonStructure.Oriented(h!, counterClockwise: false), f)).ToArray();
                    parts.Add(new PolygonPart(GeometryAccess.Restrict(PolygonStructure.Oriented(shell, counterClockwise: true), f), holes));
                }

                return f.CreatePolygon(parts.ToArray(), geometry);
            case IPolyline2D line:
                var paths = Enumerable.Range(0, line.PathCount)
                    .Select(i => WithoutRepeats(Rings.Coordinates(line[i]).Select(Snap)))
                    .Where(p => p.Length >= 2)
                    .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f)).ToArray();
                return f.CreatePolyline(paths, geometry);
            case IMultiPoint2D multiPoint:
                return f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint).Select(Snap).ToArray(), f), geometry);
            default:
                return f.CreatePoint(GeometryAccess.Restrict(Snap(((IPoint2D)geometry).ToCoordinate()), f), geometry);
        }
    }

    private static Coordinate[]? SnapRing(Coordinate[] closed, Func<Coordinate, Coordinate> snap)
    {
        var ring = WithoutRepeats(closed.Select(snap));
        return ring.Length >= 4 && Rings.SameXY(ring[0], ring[^1]) ? ring : null;
    }

    private static Coordinate[] WithoutRepeats(IEnumerable<Coordinate> path)
    {
        var result = new List<Coordinate>();
        foreach (var c in path)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], c))
            {
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    // ---------- ReducePrecision ----------

    /// <summary>
    /// Reduces the precision of a geometry to a grid and keeps it <b>valid</b> (ADR 0027): the geometry is snap-rounded by the
    /// overlay core like in <c>Union</c> (ADR 0005). A polygon is made valid under even-odd (as <c>MakeValid</c>, ADR 0020), so
    /// parts that collapse vanish; a polyline is noded and its duplicates collapse (as <c>Union</c>, ADR 0018); a multipoint
    /// loses its duplicates; a point is rounded. Every vertex of the result lies on the grid. Z and M follow the rules of the
    /// overlays (ADR 0015).
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="gridSize">The distance of the grid lines; finite and greater than 0.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D ReducePrecision(this IGeometry2D geometry, double gridSize, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        EnsureGrid(gridSize);
        var f = GeometryFactory.Resolve(factory);
        return geometry switch
        {
            IPolygon2D polygon => polygon.MakeValid(FillRule.EvenOdd, f, gridSize),
            IPolyline2D line => line.Union(f.CreatePolyline([], null), f, gridSize),
            IMultiPoint2D multiPoint => multiPoint.Union(f.CreateMultiPoint([], null), f, gridSize),
            _ => geometry.SnapToGrid(gridSize, f),
        };
    }

    // ---------- Helpers ----------

    private static void EnsureLimit(double value, string name)
    {
        if (double.IsNaN(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "The limit must not be negative or NaN.");
        }
    }

    private static void EnsureGrid(double gridSize)
    {
        if (!double.IsFinite(gridSize) || gridSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), gridSize, "The grid size must be finite and greater than 0.");
        }
    }
}
