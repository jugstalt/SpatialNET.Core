using SpatialNET.Internal;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

public static partial class Overlays
{
    // ---------- MakeValid ----------

    /// <summary>
    /// Makes a polygon valid by even-odd normalization through the overlay core (ADR 0020): rings that cross, overlap or
    /// touch dissolve, holes, islands and parts arise from the overlay, rings without area are dropped. The area of
    /// the result is the area of the input under <paramref name="fillRule"/>. The result lies on the grid, also the
    /// parts that were valid before; check <c>IsValid()</c> first to leave valid input untouched. Non-finite
    /// coordinates are removed first.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="fillRule">The fill rule for the rings; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>A new, valid polygon (never the input instance).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D MakeValid(this IPolygon2D polygon, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var f = GeometryFactory.Resolve(factory);

        var rings = Enumerable.Range(0, polygon.RingCount).Select(i => Rings.Coordinates(polygon[i])).ToArray();
        var finite = rings.Select(r => r.Where(IsFinite).ToArray()).ToArray();
        IPolygon2D input = finite.Zip(rings).All(p => p.First.Length == p.Second.Length) ? polygon : new CoordinatePolygon(finite);

        return OverlayRunner.Areas(OverlayOp.Union, input, null, fillRule, f, gridSize);
    }

    /// <summary>
    /// Makes a polyline valid: removes non-finite points and consecutive duplicate points, and drops paths with fewer than
    /// two distinct points (ADR 0020). Crossing lines are valid. Coordinates are not rounded.
    /// </summary>
    /// <param name="polyline">The polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>A new, valid polyline (never the input instance).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D MakeValid(this IPolyline2D polyline, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        var f = GeometryFactory.Resolve(factory);

        var paths = new List<ReadOnlyMemory<Coordinate>>();
        for (int i = 0; i < polyline.PathCount; i++)
        {
            var cleaned = new List<Coordinate>();
            foreach (var c in Rings.Coordinates(polyline[i]).Where(IsFinite))
            {
                if (cleaned.Count == 0 || !Rings.SameXY(cleaned[^1], c))
                {
                    cleaned.Add(c);
                }
            }

            if (cleaned.Count >= 2)
            {
                paths.Add(GeometryAccess.Restrict(cleaned.ToArray(), f));
            }
        }

        return f.CreatePolyline(paths.ToArray(), polyline);
    }

    /// <summary>Makes a multipoint valid: removes non-finite points; duplicates stay (ADR 0020).</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>A new multipoint (never the input instance).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D MakeValid(this IMultiPoint2D multiPoint, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(multiPoint);
        var f = GeometryFactory.Resolve(factory);
        return f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint).Where(IsFinite).ToArray(), f), multiPoint);
    }

    /// <summary>
    /// Makes any geometry valid (ADR 0020). A point that is not finite becomes an empty multipoint, a finite one a copy.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="fillRule">The fill rule for polygonal input; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size for polygonal input; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>A new, valid geometry (never the input instance).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D MakeValid(this IGeometry2D geometry, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IPolygon2D polygon => polygon.MakeValid(fillRule, factory, gridSize),
            IPolyline2D line => line.MakeValid(factory),
            IMultiPoint2D multiPoint => multiPoint.MakeValid(factory),
            _ => MakeValidPoint((IPoint2D)geometry, GeometryFactory.Resolve(factory)),
        };
    }

    private static IGeometry2D MakeValidPoint(IPoint2D point, IGeometryFactory f)
    {
        var c = point.ToCoordinate();
        return IsFinite(c) ? f.CreatePoint(GeometryAccess.Restrict(c, f), point) : f.CreateMultiPoint([], point);
    }

    private static bool IsFinite(Coordinate c) => double.IsFinite(c.X) && double.IsFinite(c.Y);
}
