namespace SpatialNET;

public static partial class Shapes
{
    /// <summary>
    /// The triangles inside a polygon (ADR 0038): a constrained Delaunay triangulation of its vertices without what lies outside its rings,
    /// as a flat list of polygons like <see cref="DelaunayTriangles"/>. Every edge of a ring is an edge of a triangle, and holes are left out. See
    /// <see cref="Triangulation.FromPolygon"/> for the rules and for the triangulation itself, with neighbours.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid for noding crossing rings; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The triangles, counter-clockwise; empty for a polygon without area.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IReadOnlyList<IPolygon2D> Triangulate(this IPolygon2D polygon, IGeometryFactory? factory = null, double? gridSize = null) =>
        Triangulation.FromPolygon(polygon, factory, gridSize).ToPolygons(factory);
}
