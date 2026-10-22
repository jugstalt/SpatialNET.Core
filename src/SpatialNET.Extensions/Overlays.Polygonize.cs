using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

public static partial class Overlays
{
    // ---------- Polygonize ----------

    /// <summary>
    /// The areas enclosed by a polyline (ADR 0023): the lines are noded on the grid like in <c>Union</c>, and every bounded
    /// face of the arrangement becomes a polygon of its own (with the components of lines inside it as holes). Dangles and edges
    /// that enclose nothing are dropped; the unbounded outer face is never returned. Adjacent faces are separate polygons that
    /// share their edge; a face that touches itself at a vertex gives one polygon per lobe.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The faces in a deterministic order; an empty list if the lines enclose nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IReadOnlyList<IPolygon2D> Polygonize(this IPolyline2D line, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = GeometryFactory.Resolve(factory);
        var grid = GridModel.Resolve(gridSize, line);
        if (GeometryAccess.IsEmpty(line))
        {
            return [];
        }

        // All half-edges selected: following the faces gives one ring per face (and per component); the assembly of the overlays
        // makes shells from the counter-clockwise rings, assigns the clockwise ones as holes, and drops the rings of area 0
        // (the two sides of a dangle or a bridge).
        var engine = new OverlayEngine(line, null, grid, FillRule.EvenOdd);
        var all = new bool[engine.Graph.HalfEdgeCount];
        Array.Fill(all, true);
        return engine.BuildAreas(all).Select(part => f.CreatePolygon([GeometryAccess.Restrict(part, f)], line)).ToList();
    }
}
