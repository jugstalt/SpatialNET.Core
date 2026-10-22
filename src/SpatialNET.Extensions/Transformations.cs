using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// Coordinate transformations with geometric results (ADR 0025): <c>Translate</c>, <c>Scale</c>, <c>Rotate</c> and the general
/// <c>Transform</c>. Every vertex is mapped, X and Y by the transformation, Z and M are kept (unless <c>Transform</c> changes
/// them). Results are created through the factory and restricted to what it supports (<c>SupportsZ</c>, <c>SupportsM</c>).
/// </summary>
public static class Transformations
{
    // ---------- Transform ----------

    /// <summary>
    /// Maps every vertex of a point through <paramref name="transform"/>. Z and M of the result are those the function returns.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="transform">The mapping; X and Y of its result must be finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> or <paramref name="transform"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or the mapping returned a coordinate with a <c>NaN</c> or infinite X or Y.</exception>
    public static IPoint2D Transform(this IPoint2D point, Func<Coordinate, Coordinate> transform, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(transform);
        var f = GeometryFactory.Resolve(factory);
        return f.CreatePoint(GeometryAccess.Restrict(Map(point.ToCoordinate(), transform), f), point);
    }

    /// <summary>Maps every vertex of a multipoint through <paramref name="transform"/> (see <see cref="Transform(IPoint2D, Func{Coordinate, Coordinate}, IGeometryFactory?)"/>).</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="transform">The mapping; X and Y of its result must be finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> or <paramref name="transform"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or the mapping returned a coordinate with a <c>NaN</c> or infinite X or Y.</exception>
    public static IMultiPoint2D Transform(this IMultiPoint2D multiPoint, Func<Coordinate, Coordinate> transform, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(multiPoint);
        ArgumentNullException.ThrowIfNull(transform);
        var f = GeometryFactory.Resolve(factory);
        return f.CreateMultiPoint(GeometryAccess.Restrict(Rings.Coordinates(multiPoint).Select(c => Map(c, transform)).ToArray(), f), multiPoint);
    }

    /// <summary>Maps every vertex of a polyline through <paramref name="transform"/> (see <see cref="Transform(IPoint2D, Func{Coordinate, Coordinate}, IGeometryFactory?)"/>).</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="transform">The mapping; X and Y of its result must be finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> or <paramref name="transform"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or the mapping returned a coordinate with a <c>NaN</c> or infinite X or Y.</exception>
    public static IPolyline2D Transform(this IPolyline2D line, Func<Coordinate, Coordinate> transform, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(transform);
        var f = GeometryFactory.Resolve(factory);
        var paths = Enumerable.Range(0, line.PathCount)
            .Select(i => Rings.Coordinates(line[i]))
            .Where(p => p.Length > 0)
            .Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p.Select(c => Map(c, transform)).ToArray(), f))
            .ToArray();
        return f.CreatePolyline(paths, line);
    }

    /// <summary>
    /// Maps every vertex of a polygon through <paramref name="transform"/>. The rings are normalized first and the orientation
    /// is restored afterwards (shells counter-clockwise, holes clockwise, ADR 0003), so a mirroring mapping does not flip the
    /// polygon inside out. A mapping that is not one-to-one and continuous can make the result invalid.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="transform">The mapping; X and Y of its result must be finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> or <paramref name="transform"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or the mapping returned a coordinate with a <c>NaN</c> or infinite X or Y.</exception>
    public static IPolygon2D Transform(this IPolygon2D polygon, Func<Coordinate, Coordinate> transform, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(transform);
        var f = GeometryFactory.Resolve(factory);
        var parts = PolygonStructure.Build(polygon).Select(part => new PolygonPart(
            GeometryAccess.Restrict(Oriented(part.Shell.Select(c => Map(c, transform)).ToArray(), counterClockwise: true), f),
            part.Holes.Select(h => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(Oriented(h.Select(c => Map(c, transform)).ToArray(), counterClockwise: false), f)).ToArray())).ToArray();
        return f.CreatePolygon(parts, polygon);
    }

    /// <summary>Maps every vertex of any geometry through <paramref name="transform"/> (see the overloads for the kinds).</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="transform">The mapping; X and Y of its result must be finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> or <paramref name="transform"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or the mapping returned a coordinate with a <c>NaN</c> or infinite X or Y.</exception>
    public static IGeometry2D Transform(this IGeometry2D geometry, Func<Coordinate, Coordinate> transform, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        ArgumentNullException.ThrowIfNull(transform);
        return geometry switch
        {
            IPolygon2D polygon => polygon.Transform(transform, factory),
            IPolyline2D line => line.Transform(transform, factory),
            IMultiPoint2D multiPoint => multiPoint.Transform(transform, factory),
            _ => ((IPoint2D)geometry).Transform(transform, factory),
        };
    }

    // ---------- Translate ----------

    /// <summary>Moves a point by (<paramref name="dx"/>, <paramref name="dy"/>); Z and M stay.</summary>
    /// <param name="point">The point.</param>
    /// <param name="dx">The shift in X; finite.</param>
    /// <param name="dy">The shift in Y; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPoint2D Translate(this IPoint2D point, double dx, double dy, IGeometryFactory? factory = null) =>
        point.Transform(TranslateBy(dx, dy), factory);

    /// <summary>Moves a multipoint by (<paramref name="dx"/>, <paramref name="dy"/>); Z and M stay.</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="dx">The shift in X; finite.</param>
    /// <param name="dy">The shift in Y; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IMultiPoint2D Translate(this IMultiPoint2D multiPoint, double dx, double dy, IGeometryFactory? factory = null) =>
        multiPoint.Transform(TranslateBy(dx, dy), factory);

    /// <summary>Moves a polyline by (<paramref name="dx"/>, <paramref name="dy"/>); Z and M stay.</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="dx">The shift in X; finite.</param>
    /// <param name="dy">The shift in Y; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolyline2D Translate(this IPolyline2D line, double dx, double dy, IGeometryFactory? factory = null) =>
        line.Transform(TranslateBy(dx, dy), factory);

    /// <summary>Moves a polygon by (<paramref name="dx"/>, <paramref name="dy"/>); Z and M stay.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="dx">The shift in X; finite.</param>
    /// <param name="dy">The shift in Y; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolygon2D Translate(this IPolygon2D polygon, double dx, double dy, IGeometryFactory? factory = null) =>
        polygon.Transform(TranslateBy(dx, dy), factory);

    /// <summary>Moves any geometry by (<paramref name="dx"/>, <paramref name="dy"/>); Z and M stay.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="dx">The shift in X; finite.</param>
    /// <param name="dy">The shift in Y; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is <c>NaN</c> or infinite.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IGeometry2D Translate(this IGeometry2D geometry, double dx, double dy, IGeometryFactory? factory = null) =>
        geometry.Transform(TranslateBy(dx, dy), factory);

    private static Func<Coordinate, Coordinate> TranslateBy(double dx, double dy)
    {
        EnsureFinite(dx, nameof(dx));
        EnsureFinite(dy, nameof(dy));
        return c => c with { X = c.X + dx, Y = c.Y + dy };
    }

    // ---------- Scale ----------

    /// <summary>
    /// Scales a point by <paramref name="sx"/> and <paramref name="sy"/> about <paramref name="origin"/>; Z and M stay. A negative
    /// factor mirrors, a factor of 0 collapses the geometry onto a line or a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="sx">The factor in X; finite.</param>
    /// <param name="sy">The factor in Y; finite.</param>
    /// <param name="origin">The fixed point of the scaling; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A factor or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPoint2D Scale(this IPoint2D point, double sx, double sy, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        point.Transform(ScaleBy(sx, sy, origin), factory);

    /// <summary>Scales a multipoint (see <see cref="Scale(IPoint2D, double, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="sx">The factor in X; finite.</param>
    /// <param name="sy">The factor in Y; finite.</param>
    /// <param name="origin">The fixed point of the scaling; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A factor or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IMultiPoint2D Scale(this IMultiPoint2D multiPoint, double sx, double sy, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        multiPoint.Transform(ScaleBy(sx, sy, origin), factory);

    /// <summary>Scales a polyline (see <see cref="Scale(IPoint2D, double, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="sx">The factor in X; finite.</param>
    /// <param name="sy">The factor in Y; finite.</param>
    /// <param name="origin">The fixed point of the scaling; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A factor or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolyline2D Scale(this IPolyline2D line, double sx, double sy, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        line.Transform(ScaleBy(sx, sy, origin), factory);

    /// <summary>Scales a polygon; the orientation of the rings is restored after a mirroring scale (see <see cref="Scale(IPoint2D, double, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="sx">The factor in X; finite.</param>
    /// <param name="sy">The factor in Y; finite.</param>
    /// <param name="origin">The fixed point of the scaling; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A factor or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolygon2D Scale(this IPolygon2D polygon, double sx, double sy, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        polygon.Transform(ScaleBy(sx, sy, origin), factory);

    /// <summary>Scales any geometry (see <see cref="Scale(IPoint2D, double, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="sx">The factor in X; finite.</param>
    /// <param name="sy">The factor in Y; finite.</param>
    /// <param name="origin">The fixed point of the scaling; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A factor or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IGeometry2D Scale(this IGeometry2D geometry, double sx, double sy, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        geometry.Transform(ScaleBy(sx, sy, origin), factory);

    private static Func<Coordinate, Coordinate> ScaleBy(double sx, double sy, Coordinate? origin)
    {
        EnsureFinite(sx, nameof(sx));
        EnsureFinite(sy, nameof(sy));
        var o = EnsureOrigin(origin);
        return c => c with { X = o.X + (c.X - o.X) * sx, Y = o.Y + (c.Y - o.Y) * sy };
    }

    // ---------- Rotate ----------

    /// <summary>
    /// Rotates a point by <paramref name="angle"/> (radians, counter-clockwise) about <paramref name="origin"/>; Z and M stay.
    /// Multiples of a quarter turn are exact (no rounding error from sine and cosine).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="angle">The angle in radians, counter-clockwise; finite.</param>
    /// <param name="origin">The centre of the rotation; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPoint2D Rotate(this IPoint2D point, double angle, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        point.Transform(RotateBy(angle, origin), factory);

    /// <summary>Rotates a multipoint (see <see cref="Rotate(IPoint2D, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="multiPoint">The multipoint.</param>
    /// <param name="angle">The angle in radians, counter-clockwise; finite.</param>
    /// <param name="origin">The centre of the rotation; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiPoint"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IMultiPoint2D Rotate(this IMultiPoint2D multiPoint, double angle, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        multiPoint.Transform(RotateBy(angle, origin), factory);

    /// <summary>Rotates a polyline (see <see cref="Rotate(IPoint2D, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="angle">The angle in radians, counter-clockwise; finite.</param>
    /// <param name="origin">The centre of the rotation; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolyline2D Rotate(this IPolyline2D line, double angle, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        line.Transform(RotateBy(angle, origin), factory);

    /// <summary>Rotates a polygon (see <see cref="Rotate(IPoint2D, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="angle">The angle in radians, counter-clockwise; finite.</param>
    /// <param name="origin">The centre of the rotation; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IPolygon2D Rotate(this IPolygon2D polygon, double angle, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        polygon.Transform(RotateBy(angle, origin), factory);

    /// <summary>Rotates any geometry (see <see cref="Rotate(IPoint2D, double, Coordinate?, IGeometryFactory?)"/>).</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="angle">The angle in radians, counter-clockwise; finite.</param>
    /// <param name="origin">The centre of the rotation; <c>null</c> = (0, 0).</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The angle or the origin is <c>NaN</c> or infinite.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or a coordinate leaves the range of <see cref="double"/>.</exception>
    public static IGeometry2D Rotate(this IGeometry2D geometry, double angle, Coordinate? origin = null, IGeometryFactory? factory = null) =>
        geometry.Transform(RotateBy(angle, origin), factory);

    private static Func<Coordinate, Coordinate> RotateBy(double angle, Coordinate? origin)
    {
        EnsureFinite(angle, nameof(angle));
        var o = EnsureOrigin(origin);
        var (sin, cos) = SinCos(angle);
        return c =>
        {
            double dx = c.X - o.X, dy = c.Y - o.Y;
            return c with { X = o.X + dx * cos - dy * sin, Y = o.Y + dx * sin + dy * cos };
        };
    }

    /// <summary>Sine and cosine; exact for multiples of a quarter turn.</summary>
    private static (double Sin, double Cos) SinCos(double angle)
    {
        double quarters = angle / (Math.PI / 2);
        if (quarters == Math.Floor(quarters))
        {
            return (quarters - 4 * Math.Floor(quarters / 4)) switch
            {
                0 => (0, 1),
                1 => (1, 0),
                2 => (0, -1),
                _ => (-1, 0),
            };
        }

        return Math.SinCos(angle);
    }

    // ---------- Helpers ----------

    private static Coordinate Map(Coordinate c, Func<Coordinate, Coordinate> transform)
    {
        var result = transform(c);
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y))
        {
            throw new InvalidOperationException("The transformation produced a coordinate whose X or Y is NaN or infinite.");
        }

        return result;
    }

    private static Coordinate[] Oriented(Coordinate[] closed, bool counterClockwise) =>
        (Rings.SignedArea(closed) > 0) == counterClockwise || Rings.SignedArea(closed) == 0 ? closed : closed.Reverse().ToArray();

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }
    }

    private static Coordinate EnsureOrigin(Coordinate? origin)
    {
        var o = origin ?? new Coordinate(0, 0);
        if (!double.IsFinite(o.X) || !double.IsFinite(o.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), o, "The origin must be finite.");
        }

        return o;
    }
}
