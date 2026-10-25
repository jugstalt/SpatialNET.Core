using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

/// <summary>The cell of one site of a Voronoi diagram (ADR 0036).</summary>
/// <param name="Site">The site, with its Z if it has one.</param>
/// <param name="Cell">The part of the clip area that lies nearer to <paramref name="Site"/> than to every other site; empty if the cell does not reach the clip area (a site far outside it) or collapses at the size of the grid (sites closer together than that).</param>
public readonly record struct VoronoiCell(Coordinate Site, IPolygon2D Cell);

public static partial class Shapes
{
    // ---------- VoronoiDiagram ----------

    /// <summary>
    /// The Voronoi diagram of the vertices of a geometry (ADR 0036): for every site the area that lies nearer to it than to every other
    /// site (the Thiessen polygon). The cells are cut to a clip area, since the cells at the border of the sites are unbounded. As for
    /// <see cref="DelaunayTriangles"/> every vertex of every kind of geometry is a site, with X and Y only (Z of a site is kept in the
    /// result); vertices with the same X and Y count once, and vertices that are not finite are ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result has one entry per distinct site, in the order in which the sites first occur. The cells have no gaps and do not overlap:
    /// their areas add up to the area of the clip area. The corners of the cells are rounded to a grid (see <paramref name="gridSize"/>) and
    /// the cells are valid; where a cell would collapse at the size of the grid, it is empty.
    /// </para>
    /// <para>
    /// Without <paramref name="clip"/> the clip area is the envelope of the sites, widened on every side by half of its larger extent
    /// (by 1 if it has none). With a polygon the cells are cut with it, which is what a service area within a boundary needs: a site outside the
    /// polygon can still have a cell inside it, and a cell that does not reach the polygon is empty. Use <c>Envelope2D.ToPolygon</c> for a rectangle.
    /// </para>
    /// </remarks>
    /// <param name="sites">The geometry whose vertices are the sites.</param>
    /// <param name="clip">The area to which the cells are cut; <c>null</c> = the widened envelope of the sites.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid to which the cells are rounded; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The cells; empty if there is no site or the clip area has no area.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sites"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IReadOnlyList<VoronoiCell> VoronoiDiagram(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
    {
        var f = GeometryFactory.Resolve(factory);
        if (!PrepareVoronoi(sites, clip, gridSize, out var points, out var rect, out var grid))
        {
            return [];
        }

        var rings = VoronoiBuilder.Cells(points, DelaunayMesh.Create(points), rect);
        var result = new List<VoronoiCell>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            result.Add(new VoronoiCell(points[i], clip is null ? RoundedCell(rings[i], grid, f) : ClippedCell(rings[i], clip, grid, f)));
        }

        return result;
    }

    private static IPolygon2D RoundedCell(Coordinate[] ring, GridModel grid, IGeometryFactory f)
    {
        if (ring.Length == 0)
        {
            return f.CreatePolygon([], null);
        }

        if (VoronoiBuilder.Round(ring, grid) is { } rounded)
        {
            return f.CreatePolygon([new PolygonPart(GeometryAccess.Restrict([.. rounded, rounded[0]], f))], null);
        }

        // folded or collapsed at the size of the grid: let the overlay core make it valid
        return ClosedPolygon(ring, f).MakeValid(FillRule.EvenOdd, f, grid.Size);
    }

    private static IPolygon2D ClippedCell(Coordinate[] ring, IPolygon2D clip, GridModel grid, IGeometryFactory f) =>
        ring.Length == 0 ? f.CreatePolygon([], null) : ClosedPolygon(ring, f).Intersection(clip, FillRule.EvenOdd, f, grid.Size);

    private static IPolygon2D ClosedPolygon(Coordinate[] ring, IGeometryFactory f) =>
        f.CreatePolygon([new PolygonPart(GeometryAccess.Restrict([.. ring, ring[0]], f))], null);

    // ---------- VoronoiEdges ----------

    /// <summary>
    /// The edges of the Voronoi diagram (ADR 0036): the lines between neighbouring cells, one path per edge, without the edges on the
    /// border of the clip area. The same sites, clip area and grid as <see cref="VoronoiDiagram"/>, which has the cells themselves.
    /// </summary>
    /// <param name="sites">The geometry whose vertices are the sites.</param>
    /// <param name="clip">The area to which the edges are cut; <c>null</c> = the widened envelope of the sites.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid to which the edges are rounded; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The edges; empty if there are fewer than two sites or the clip area has no area.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sites"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D VoronoiEdges(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
    {
        var f = GeometryFactory.Resolve(factory);
        if (!PrepareVoronoi(sites, clip, gridSize, out var points, out var rect, out var grid) || points.Length < 2)
        {
            return f.CreatePolyline([], null);
        }

        var rings = VoronoiBuilder.Cells(points, DelaunayMesh.Create(points), rect);
        var seen = new HashSet<(double, double, double, double)>();
        var paths = new List<ReadOnlyMemory<Coordinate>>();
        foreach (var (a, b) in VoronoiBuilder.Edges(rings, rect))
        {
            var p = new Coordinate(grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(a.X))), grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(a.Y))));
            var q = new Coordinate(grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(b.X))), grid.FromUnits(GridModel.RoundUnits(grid.ToUnits(b.Y))));
            if ((p.X != q.X || p.Y != q.Y) && seen.Add((p.X, p.Y, q.X, q.Y)))
            {
                paths.Add(new[] { p, q });
            }
        }

        var edges = f.CreatePolyline(paths.ToArray(), null);
        return clip is null ? edges : edges.Intersection(clip, FillRule.EvenOdd, f, grid.Size);
    }

    /// <summary>The distinct sites, the rectangle for the cells and the grid; <c>false</c> if there is nothing to compute.</summary>
    private static bool PrepareVoronoi(IGeometry2D sites, IPolygon2D? clip, double? gridSize, out Coordinate[] points, out Envelope2D rect, out GridModel grid)
    {
        GeometryAccess.EnsureKnown(sites, nameof(sites));
        points = DistinctPoints(sites);
        rect = Envelope2D.Empty;
        grid = new GridModel(1);
        if (gridSize is { } size && (!double.IsFinite(size) || size <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), gridSize, "The grid size must be finite and greater than 0.");
        }

        if (points.Length == 0)
        {
            return false;
        }

        if (clip is null)
        {
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            double margin = Math.Max(maxX - minX, maxY - minY) / 2;
            if (!(margin > 0))
            {
                margin = 1;
            }

            rect = new Envelope2D(minX - margin, minY - margin, maxX + margin, maxY + margin);
        }
        else
        {
            GeometryAccess.EnsureKnown(clip, nameof(clip));
            rect = clip.Envelope();
            if (rect.IsEmpty || !(rect.Width > 0 && rect.Height > 0))
            {
                return false;
            }
        }

        double maxAbs = new[] { rect.MinX, rect.MaxX, rect.MinY, rect.MaxY }.Max(Math.Abs);
        grid = new GridModel(gridSize ?? SpatialNET.Precision.DefaultGridSize ?? GridModel.AutoSize(maxAbs));
        return true;
    }

    // ---------- DelaunayEdges ----------

    /// <summary>
    /// The edges of the Delaunay triangulation of the vertices of a geometry (ADR 0036): every edge once, as one path, with Z of the
    /// vertices kept. The same vertices and triangulation as <see cref="DelaunayTriangles"/>, drawn as lines or used as the graph of the
    /// neighbourhood of the points.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The edges; empty if there are fewer than three different vertices or all of them are collinear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D DelaunayEdges(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = GeometryFactory.Resolve(factory);
        var mesh = DelaunayMesh.Create(DistinctPoints(geometry));
        if (mesh is null)
        {
            return f.CreatePolyline([], null);
        }

        var paths = new List<ReadOnlyMemory<Coordinate>>();
        for (int e = 0; e < mesh.TriangleCount * 3; e++)
        {
            int h = mesh.HalfEdges[e];
            if (h == -1 || e < h)
            {
                var a = mesh.Points[mesh.Triangles[e]];
                var b = mesh.Points[mesh.Triangles[DelaunayMesh.Next(e)]];
                paths.Add(GeometryAccess.Restrict(new[] { a, b }, f));
            }
        }

        return f.CreatePolyline(paths.ToArray(), geometry);
    }
}
