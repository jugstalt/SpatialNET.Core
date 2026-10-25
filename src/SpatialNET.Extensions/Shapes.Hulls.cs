using SpatialNET.Internal;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

public static partial class Shapes
{
    // ---------- ConvexHull ----------

    /// <summary>
    /// The convex hull of all points. Depending on degeneracy a polygon (counter-clockwise), a polyline
    /// between the two extreme points (all points collinear) or a point; empty for empty input (as in JTS).
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D ConvexHull(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = GeometryFactory.Resolve(factory);

        var points = GeometryAccess.AllPoints(geometry).Select(p => GeometryAccess.Restrict(p.ToCoordinate() with { M = double.NaN }, f))
            .OrderBy(c => c.X).ThenBy(c => c.Y)
            .ToList();
        points = Distinct(points);

        if (points.Count == 0)
        {
            return f.CreatePolygon([], geometry);
        }

        if (points.Count == 1)
        {
            return f.CreatePoint(points[0], geometry);
        }

        // Andrews Monotone Chain; kollineare Punkte werden verworfen.
        var hull = new Coordinate[2 * points.Count];
        int k = 0;
        for (int i = 0; i < points.Count; i++)
        {
            while (k >= 2 && Predicates.Orient(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }

        for (int i = points.Count - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Predicates.Orient(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }

        if (k <= 3)
        {
            // alle Punkte kollinear: Linie zwischen den Extrempunkten
            ReadOnlyMemory<Coordinate>[] path = [new[] { points[0], points[^1] }];
            return f.CreatePolyline(path, geometry);
        }

        return f.CreatePolygon([new PolygonPart(hull[..k])], geometry);
    }

    private static List<Coordinate> Distinct(List<Coordinate> sorted)
    {
        var result = new List<Coordinate>(sorted.Count);
        foreach (var c in sorted)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], c))
            {
                result.Add(c);
            }
        }

        return result;
    }

    // ---------- DelaunayTriangles ----------

    /// <summary>
    /// The Delaunay triangulation of the vertices of a geometry (ADR 0029): triangles such that no vertex lies inside the circumcircle of
    /// a triangle. They tile the convex hull of the vertices. As for <see cref="ConvexHull"/> every vertex of every kind of
    /// geometry counts, with X and Y only; vertices with the same X and Y count once, and vertices that are not finite are ignored.
    /// </summary>
    /// <remarks>
    /// The result is a flat list of triangles, each a polygon with its vertices counter-clockwise, in no particular order (as for
    /// <see cref="Cutting.Cut(IPolygon2D, IPolyline2D, IGeometryFactory?, double?)"/>). Vertices on a common circle (the corners of a
    /// rectangle, a regular grid) allow several triangulations, all equally Delaunay; which one comes out is deterministic but not
    /// specified. Z of a vertex is kept, M is not.
    /// </remarks>
    /// <param name="geometry">The geometry.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The triangles; empty if there are fewer than three different vertices or all of them are collinear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IReadOnlyList<IPolygon2D> DelaunayTriangles(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = GeometryFactory.Resolve(factory);
        var mesh = DelaunayMesh.Create(DistinctPoints(geometry));
        if (mesh is null)
        {
            return [];
        }

        var triangles = new List<IPolygon2D>(mesh.TriangleCount);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var ring = new[]
            {
                mesh.Points[mesh.Triangles[3 * t]], mesh.Points[mesh.Triangles[3 * t + 1]], mesh.Points[mesh.Triangles[3 * t + 2]], mesh.Points[mesh.Triangles[3 * t]],
            };
            triangles.Add(f.CreatePolygon([new PolygonPart(GeometryAccess.Restrict(ring, f))], geometry));
        }

        return triangles;
    }

    internal static Coordinate[] DistinctPoints(IGeometry2D geometry)
    {
        var seen = new HashSet<(double, double)>();
        var points = new List<Coordinate>();
        foreach (var p in GeometryAccess.AllPoints(geometry))
        {
            if (double.IsFinite(p.X) && double.IsFinite(p.Y) && seen.Add((p.X, p.Y)))
            {
                points.Add(p.ToCoordinate() with { M = double.NaN });
            }
        }

        return points.ToArray();
    }

    // ---------- ConcaveHull ----------

    /// <summary>
    /// A concave hull of the vertices of a geometry (ADR 0029): a polygon that contains every vertex and follows their arrangement more
    /// closely than the convex hull. It starts from the Delaunay triangulation and takes triangles away from the border, longest border edge
    /// first, as long as an edge is longer than <paramref name="maxEdgeLength"/> and the polygon stays connected and keeps every vertex.
    /// With a limit of at least the longest edge the result is the convex hull; with a smaller one it can be much tighter, and it is
    /// never smaller than what the points allow. The limit decides what counts as "too far": a larger one fills gaps, a smaller one
    /// opens bays.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="holesAllowed"/> the result is one polygon without holes. With it, interior triangles of which all vertices are
    /// interior and which have an edge longer than the limit open holes that grow the same way; a hole never touches the border or another
    /// hole. As for <see cref="ConvexHull"/> a geometry with fewer than three different or only collinear vertices gives a point, the
    /// line between the extreme points, or an empty polygon. Z of a vertex is kept, M is not.
    /// </remarks>
    /// <param name="geometry">The geometry.</param>
    /// <param name="maxEdgeLength">The longest border edge that stays, in the unit of the coordinates; not negative, not <c>NaN</c>. <see cref="double.PositiveInfinity"/> gives the convex hull.</param>
    /// <param name="holesAllowed"><c>true</c> if the polygon may have holes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxEdgeLength"/> is negative or <c>NaN</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D ConcaveHull(this IGeometry2D geometry, double maxEdgeLength, bool holesAllowed = false, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        if (double.IsNaN(maxEdgeLength) || maxEdgeLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEdgeLength), maxEdgeLength, "The edge length must not be negative or NaN.");
        }

        var f = GeometryFactory.Resolve(factory);
        var mesh = DelaunayMesh.Create(DistinctPoints(geometry));
        return mesh is null ? geometry.ConvexHull(f) : HullOf(geometry, mesh, maxEdgeLength, holesAllowed, f);
    }

    /// <summary>
    /// A concave hull with the limit given as a ratio (ADR 0029): 1 gives the convex hull, 0 the tightest hull the points allow, and
    /// in between the limit is the shortest edge of the Delaunay triangulation plus <paramref name="edgeLengthRatio"/> times the
    /// difference between its longest and its shortest edge, which makes the ratio independent of the scale. See
    /// <see cref="ConcaveHull(IGeometry2D, double, bool, IGeometryFactory?)"/>.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="edgeLengthRatio">From 0 to 1.</param>
    /// <param name="holesAllowed"><c>true</c> if the polygon may have holes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeLengthRatio"/> is not from 0 to 1.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D ConcaveHullByRatio(this IGeometry2D geometry, double edgeLengthRatio, bool holesAllowed = false, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        if (!(edgeLengthRatio >= 0 && edgeLengthRatio <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(edgeLengthRatio), edgeLengthRatio, "The ratio must be from 0 to 1.");
        }

        var f = GeometryFactory.Resolve(factory);
        var mesh = DelaunayMesh.Create(DistinctPoints(geometry));
        if (mesh is null)
        {
            return geometry.ConvexHull(f);
        }

        var (shortest, longest) = ConcaveHullBuilder.EdgeLengths(mesh);
        double maxEdgeLength = edgeLengthRatio == 1 ? double.PositiveInfinity : shortest + edgeLengthRatio * (longest - shortest);
        return HullOf(geometry, mesh, maxEdgeLength, holesAllowed, f);
    }

    private static IGeometry2D HullOf(IGeometry2D geometry, DelaunayMesh mesh, double maxEdgeLength, bool holesAllowed, IGeometryFactory f)
    {
        var rings = ConcaveHullBuilder.Build(mesh, maxEdgeLength, holesAllowed);
        Coordinate[] Closed(int[] ring) => GeometryAccess.Restrict([.. ring.Select(i => mesh.Points[i]), mesh.Points[ring[0]]], f);

        var shell = Closed(rings[0]);
        var holes = rings.Skip(1).Select(r => (ReadOnlyMemory<Coordinate>)Closed(r)).ToArray();
        return f.CreatePolygon([new PolygonPart(shell, holes)], geometry);
    }
}
