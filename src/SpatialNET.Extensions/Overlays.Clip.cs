using SpatialNET.Internal;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>Conversions of an <see cref="Envelope2D"/> to a geometry (ADR 0022).</summary>
public static class Envelopes
{
    /// <summary>
    /// The envelope as a rectangular polygon (counter-clockwise, closed). An empty envelope, or one without area (zero width
    /// or height), gives an empty polygon.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid (not finite, or Min greater than Max, other than <see cref="Envelope2D.Empty"/>).</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D ToPolygon(this Envelope2D envelope, IGeometryFactory? factory = null)
    {
        var f = GeometryFactory.Resolve(factory);
        EnsureValid(envelope, nameof(envelope));
        if (envelope.IsEmpty || envelope.Width <= 0 || envelope.Height <= 0)
        {
            return f.CreatePolygon([], null);
        }

        return f.CreatePolygon(
        [
            new PolygonPart(new Coordinate[]
            {
                new(envelope.MinX, envelope.MinY), new(envelope.MaxX, envelope.MinY),
                new(envelope.MaxX, envelope.MaxY), new(envelope.MinX, envelope.MaxY), new(envelope.MinX, envelope.MinY),
            }),
        ], null);
    }

    internal static void EnsureValid(Envelope2D envelope, string paramName)
    {
        if (envelope.IsEmpty && double.IsNaN(envelope.MinY) && double.IsNaN(envelope.MaxX) && double.IsNaN(envelope.MaxY))
        {
            return;
        }

        if (!double.IsFinite(envelope.MinX) || !double.IsFinite(envelope.MinY) || !double.IsFinite(envelope.MaxX) || !double.IsFinite(envelope.MaxY)
            || envelope.MinX > envelope.MaxX || envelope.MinY > envelope.MaxY)
        {
            throw new ArgumentOutOfRangeException(paramName, envelope,
                "The envelope must be finite and Min must not exceed Max (use Envelope2D.Empty for nothing).");
        }
    }
}

public static partial class Overlays
{
    // ---------- Clip ----------

    /// <summary>
    /// Clips a polygon to a rectangle (ADR 0022): the intersection with the closed rectangle. A polygon that lies completely in the
    /// rectangle is returned as a normalized copy without rounding to the grid, one that does not meet it gives an empty polygon;
    /// only where the border of the rectangle cuts, the overlay runs and the result lies on the grid. An envelope without area gives an empty polygon.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="envelope">The clip rectangle.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid, or <paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolygon2D Clip(this IPolygon2D polygon, Envelope2D envelope, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var f = GeometryFactory.Resolve(factory);
        Envelopes.EnsureValid(envelope, nameof(envelope));
        EnsureGridSize(gridSize);

        var own = polygon.Envelope();
        if (own.IsEmpty || !own.Intersects(envelope) || envelope.Width <= 0 || envelope.Height <= 0)
        {
            return f.CreatePolygon([], polygon);
        }

        if (envelope.Contains(own))
        {
            var parts = PolygonStructure.Build(polygon).Select(part => GeometryAccess.Restrict(part.ToPolygonPart(), f)).ToArray();
            return f.CreatePolygon(parts, polygon);
        }

        return (IPolygon2D)OverlayRunner.Run(OverlayOp.Intersection, polygon, envelope.ToPolygon(f), FillRule.EvenOdd, f, gridSize);
    }

    /// <summary>
    /// Clips a polyline to a rectangle (ADR 0022): the parts inside or on the border of the closed rectangle. A polyline that lies
    /// completely in the rectangle is returned as a copy without rounding, one that does not meet it gives an empty polyline;
    /// only where the border cuts, the overlay runs and the result lies on the grid. An envelope without area gives an empty polyline.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="envelope">The clip rectangle.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid, or <paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Clip(this IPolyline2D line, Envelope2D envelope, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = GeometryFactory.Resolve(factory);
        Envelopes.EnsureValid(envelope, nameof(envelope));
        EnsureGridSize(gridSize);

        var own = line.Envelope();
        if (own.IsEmpty || !own.Intersects(envelope) || envelope.Width <= 0 || envelope.Height <= 0)
        {
            return f.CreatePolyline([], line);
        }

        if (envelope.Contains(own))
        {
            var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(p => p.Length > 0)
                .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f)).ToArray();
            return f.CreatePolyline(paths, line);
        }

        return (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, line, envelope.ToPolygon(f), FillRule.EvenOdd, f, gridSize);
    }

    /// <summary>Clips a multipoint to a rectangle (ADR 0022): the points inside or on the border; nothing is rounded.</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="envelope">The clip rectangle.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D Clip(this IMultiPoint2D multiPoint, Envelope2D envelope, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(multiPoint);
        var f = GeometryFactory.Resolve(factory);
        Envelopes.EnsureValid(envelope, nameof(envelope));
        var inside = Rings.Coordinates(multiPoint).Where(envelope.Contains).ToArray();
        return f.CreateMultiPoint(GeometryAccess.Restrict(inside, f), multiPoint);
    }

    /// <summary>
    /// Clips any geometry to a rectangle (ADR 0022). A point inside or on the border is copied, a point outside gives an empty multipoint.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="envelope">The clip rectangle.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size for polygons and polylines; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid, or <paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D Clip(this IGeometry2D geometry, Envelope2D envelope, IGeometryFactory? factory = null, double? gridSize = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return geometry switch
        {
            IPolygon2D polygon => polygon.Clip(envelope, factory, gridSize),
            IPolyline2D line => line.Clip(envelope, factory, gridSize),
            IMultiPoint2D multiPoint => multiPoint.Clip(envelope, factory),
            _ => ClipPoint((IPoint2D)geometry, envelope, GeometryFactory.Resolve(factory)),
        };
    }

    /// <summary>The grid size is checked also on the fast paths, where no overlay would complain.</summary>
    private static void EnsureGridSize(double? gridSize)
    {
        if (gridSize is { } size)
        {
            _ = new Internal.Noding.GridModel(size);
        }
    }

    private static IGeometry2D ClipPoint(IPoint2D point, Envelope2D envelope, IGeometryFactory f)
    {
        Envelopes.EnsureValid(envelope, nameof(envelope));
        var c = point.ToCoordinate();
        return envelope.Contains(c) ? f.CreatePoint(GeometryAccess.Restrict(c, f), point) : f.CreateMultiPoint([], point);
    }
}
