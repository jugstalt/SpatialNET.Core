using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>
/// Overlay operations (ADR 0005/0012): snap rounding to the grid size (<c>gridSize</c> →
/// <see cref="Precision.DefaultGridSize"/> → automatic), winding numbers with a selectable <see cref="FillRule"/>,
/// strict semantics (results are dimension-homogeneous, touch and collapse remnants are dropped). Results are
/// valid, rings closed, shells counter-clockwise; Z at new vertices is interpolated on the
/// original segment, at crossings the Z of the first operand applies.
/// </summary>
/// <remarks>
/// Typed overloads return the exact result type; the general <see cref="IGeometry2D"/> overload
/// branches at run time. If the first operand is a point or multipoint, the result is a multipoint.
/// Union and SymDifference exist only for equal dimensions.
/// </remarks>
public static class Overlays
{
    // ---------- Intersection ----------

    /// <summary>Intersection area of two polygons.</summary>
    /// <param name="a">The first polygon.</param>
    /// <param name="b">The second polygon.</param>
    /// <param name="fillRule">The fill rule for both operands; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolygon2D Intersection(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>The parts of the line in the interior and on the boundary of the polygon.</summary>
    /// <param name="a">The polyline.</param>
    /// <param name="b">The polygon.</param>
    /// <param name="fillRule">The fill rule of the polygon; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Intersection(this IPolyline2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>The parts of the line in the interior and on the boundary of the polygon.</summary>
    /// <param name="a">The polygon.</param>
    /// <param name="b">The polyline.</param>
    /// <param name="fillRule">The fill rule of the polygon; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Intersection(this IPolygon2D a, IPolyline2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Shared line parts (overlaps); pure crossing points are dropped under strict semantics.</summary>
    /// <param name="a">The first polyline.</param>
    /// <param name="b">The second polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Intersection(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>The points that lie in <paramref name="b"/> (interior or boundary).</summary>
    /// <param name="a">The multipoint.</param>
    /// <param name="b">The other geometry.</param>
    /// <param name="fillRule">The fill rule, if <paramref name="b"/> is a polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D Intersection(this IMultiPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>The point if it lies in <paramref name="b"/> (interior or boundary), otherwise an empty multipoint.</summary>
    /// <param name="a">The point.</param>
    /// <param name="b">The other geometry.</param>
    /// <param name="fillRule">The fill rule, if <paramref name="b"/> is a polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D Intersection(this IPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Intersection of arbitrary geometries; result type by strict semantics (smallest dimension).</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="fillRule">The fill rule for polygons; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IGeometry2D Intersection(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    // ---------- Union ----------

    /// <summary>Union of two polygons; with <see cref="FillRule.NonZero"/>, e.g. overlapping shells are merged.</summary>
    /// <param name="a">The first polygon.</param>
    /// <param name="b">The second polygon.</param>
    /// <param name="fillRule">The fill rule for both operands; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolygon2D Union(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Union, a, b, fillRule, factory, gridSize);

    /// <summary>Union of two polylines, noded and joined into maximal paths.</summary>
    /// <param name="a">The first polyline.</param>
    /// <param name="b">The second polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Union(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Union, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Union of two multipoints.</summary>
    /// <param name="a">The first multipoint.</param>
    /// <param name="b">The second multipoint.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D Union(this IMultiPoint2D a, IMultiPoint2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Union, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Union of geometries of equal dimension.</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="fillRule">The fill rule for polygons; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="NotSupportedException">The dimensions differ.</exception>
    public static IGeometry2D Union(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Union, a, b, fillRule, factory, gridSize);

    // ---------- Difference ----------

    /// <summary>The first polygon without the area of the second.</summary>
    /// <param name="a">The polygon.</param>
    /// <param name="b">The polygon to subtract.</param>
    /// <param name="fillRule">The fill rule for both operands; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolygon2D Difference(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>The polygon (normalized); lines have no area that could be subtracted.</summary>
    /// <param name="a">The polygon.</param>
    /// <param name="b">The polyline.</param>
    /// <param name="fillRule">The fill rule of the polygon; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolygon2D Difference(this IPolygon2D a, IPolyline2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>The parts of the line outside the polygon (boundary parts are dropped).</summary>
    /// <param name="a">The polyline.</param>
    /// <param name="b">The polygon.</param>
    /// <param name="fillRule">The fill rule of the polygon; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Difference(this IPolyline2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>The parts of the first line that do not lie on the second.</summary>
    /// <param name="a">The polyline.</param>
    /// <param name="b">The polyline to subtract.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D Difference(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Difference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>The points that do not lie in <paramref name="b"/> (interior or boundary).</summary>
    /// <param name="a">The multipoint.</param>
    /// <param name="b">The other geometry.</param>
    /// <param name="fillRule">The fill rule, if <paramref name="b"/> is a polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D Difference(this IMultiPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>The point if it does not lie in <paramref name="b"/>, otherwise an empty multipoint.</summary>
    /// <param name="a">The point.</param>
    /// <param name="b">The other geometry.</param>
    /// <param name="fillRule">The fill rule, if <paramref name="b"/> is a polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D Difference(this IPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Difference of arbitrary geometries; the result has the type of <paramref name="a"/>.</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The geometry to subtract.</param>
    /// <param name="fillRule">The fill rule for polygons; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IGeometry2D Difference(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    // ---------- SymDifference ----------

    /// <summary>The areas that belong to exactly one of the polygons.</summary>
    /// <param name="a">The first polygon.</param>
    /// <param name="b">The second polygon.</param>
    /// <param name="fillRule">The fill rule for both operands; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolygon2D SymDifference(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, fillRule, factory, gridSize);

    /// <summary>The line parts that belong to exactly one of the polylines.</summary>
    /// <param name="a">The first polyline.</param>
    /// <param name="b">The second polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IPolyline2D SymDifference(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>The points that belong to exactly one of the multipoints.</summary>
    /// <param name="a">The first multipoint.</param>
    /// <param name="b">The second multipoint.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    public static IMultiPoint2D SymDifference(this IMultiPoint2D a, IMultiPoint2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Symmetric difference of geometries of equal dimension.</summary>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="fillRule">The fill rule for polygons; default EvenOdd.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="NotSupportedException">The dimensions differ.</exception>
    public static IGeometry2D SymDifference(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.SymDifference, a, b, fillRule, factory, gridSize);
}
