using System.Collections.ObjectModel;
using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

/// <summary>
/// A triangulated surface (ADR 0037, ADR 0038): vertices, triangles that know their neighbours, and what a terrain model needs from
/// it: the triangle at a point, the height at a point, contour lines. It is made from points (the Delaunay triangulation), from points and
/// lines that must be kept (a constrained Delaunay triangulation, for break lines), or from a polygon (the triangles inside it).
/// </summary>
/// <remarks>
/// <para>
/// The triangulation is immutable and thread-safe: any number of threads may ask it at the same time. Triangle <c>t</c> has the vertex ids
/// <see cref="Triangle"/> counter-clockwise; its side <c>k</c> (0, 1 or 2) is the edge from its vertex <c>k</c> to its vertex <c>k + 1</c>
/// (modulo 3), and <see cref="Neighbour"/> is the triangle on the other side of it, or −1 at the border.
/// </para>
/// <para>
/// The vertices carry the Z of the points they come from (M is not kept); <see cref="InterpolateZ"/> and <see cref="Contour"/> need Z.
/// Equal triangulations are not guaranteed for points on a common circle: there are several equally Delaunay ones, and which one comes out
/// is deterministic but not specified.
/// </para>
/// </remarks>
public sealed partial class Triangulation
{
    private readonly Coordinate[] _vertices;
    private readonly int[] _triangles;      // three vertex ids per triangle, counter-clockwise
    private readonly int[] _neighbours;     // three per triangle: across the edge of side k, or -1
    private readonly bool[] _constrained;   // three per triangle: whether the edge of side k is a constraint
    private readonly Lazy<SpatialIndex<int>> _index;

    private Triangulation(Coordinate[] vertices, int[] triangles, int[] neighbours, bool[] constrained)
    {
        _vertices = vertices;
        _triangles = triangles;
        _neighbours = neighbours;
        _constrained = constrained;
        Vertices = new ReadOnlyCollection<Coordinate>(vertices);
        _index = new Lazy<SpatialIndex<int>>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static readonly Triangulation None = new([], [], [], []);

    /// <summary>The vertices; the ids used by <see cref="Triangle"/> are indices into this list.</summary>
    public IReadOnlyList<Coordinate> Vertices { get; }

    /// <summary>The number of triangles; 0 if there were fewer than three different vertices or all of them are collinear.</summary>
    public int TriangleCount => _triangles.Length / 3;

    // ---------- creating ----------

    /// <summary>
    /// The Delaunay triangulation of the vertices of a geometry (as <see cref="Shapes.DelaunayTriangles"/>, ADR 0029): triangles such that
    /// no vertex lies inside the circumcircle of a triangle. They tile the convex hull of the vertices. Every vertex of every kind of
    /// geometry counts, with X and Y; vertices with the same X and Y count once (the first one with a Z gives it), and vertices that are
    /// not finite are ignored.
    /// </summary>
    /// <param name="points">The geometry whose vertices are triangulated.</param>
    /// <returns>The triangulation; without triangles if there are fewer than three different vertices or all of them are collinear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="points"/> is not a known geometry kind.</exception>
    public static Triangulation FromPoints(IGeometry2D points)
    {
        GeometryAccess.EnsureKnown(points, nameof(points));
        var mesh = DelaunayMesh.Create(Shapes.DistinctPoints(points));
        return mesh is null ? None : From(mesh, null);
    }

    /// <summary>
    /// The constrained Delaunay triangulation (ADR 0038) of points and lines: a triangulation that contains every segment of the lines as a
    /// chain of edges (break lines, boundaries) and is Delaunay everywhere else. The vertices of the lines are vertices of the
    /// triangulation. The triangles tile the convex hull of all vertices.
    /// </summary>
    /// <remarks>
    /// Constraints may touch, overlap and share end points, and a constraint that passes exactly through a vertex is split there. Where two
    /// constraints <b>cross</b> in a point that is an end of neither, they are first noded like in an overlay (ADR 0005): the crossing
    /// becomes a vertex, and all constraints are rounded to the grid, so their vertices can move by up to half of it. Without crossings the
    /// constraints are used exactly as they are.
    /// </remarks>
    /// <param name="points">The geometry whose vertices are points of the triangulation; may be empty.</param>
    /// <param name="lines">Polylines whose segments must be kept, or polygons whose rings must be; other kinds contribute their vertices only.</param>
    /// <param name="factory">The factory used to node crossing constraints; <c>null</c> = <see cref="GeometryFactory.Default"/>, needed only if they cross.</param>
    /// <param name="gridSize">The grid for noding crossing constraints; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The triangulation; without triangles if there are fewer than three different vertices or all of them are collinear.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">A geometry is not a known geometry kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">The constraints cross and there is no factory.</exception>
    public static Triangulation FromPointsAndLines(IGeometry2D points, IGeometry2D lines, IGeometryFactory? factory = null, double? gridSize = null)
    {
        GeometryAccess.EnsureKnown(points, nameof(points));
        GeometryAccess.EnsureKnown(lines, nameof(lines));
        var vertices = new VertexSet(Shapes.DistinctPoints(points));
        foreach (var p in Shapes.DistinctPoints(lines))
        {
            vertices.Add(p);
        }

        var segments = Prepare(ConstraintSet.Segments(lines), factory, gridSize);
        return Constrained(vertices, segments, polygon: false);
    }

    /// <summary>
    /// The triangles inside a polygon (ADR 0038): its constrained Delaunay triangulation without anything outside the rings. The vertices
    /// are the vertices of the polygon, and every edge of a ring is an edge of a triangle; holes are left out. A polygon without holes
    /// and <c>n</c> vertices has <c>n − 2</c> triangles, every hole adds two.
    /// </summary>
    /// <remarks>
    /// The inside is decided by the even-odd rule on the rings, so an invalid polygon is read the way <c>MakeValid</c> reads it. Rings that
    /// cross are noded first, as for <see cref="FromPointsAndLines"/>. A polygon with fewer than three different vertices or only collinear
    /// ones has no triangles.
    /// </remarks>
    /// <param name="polygon">The polygon.</param>
    /// <param name="factory">The factory used to node crossing rings; <c>null</c> = <see cref="GeometryFactory.Default"/>, needed only if they cross.</param>
    /// <param name="gridSize">The grid for noding crossing rings; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <returns>The triangulation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gridSize"/> is not finite or not greater than 0.</exception>
    /// <exception cref="InvalidOperationException">The rings cross and there is no factory.</exception>
    public static Triangulation FromPolygon(IPolygon2D polygon, IGeometryFactory? factory = null, double? gridSize = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        GeometryAccess.EnsureKnown(polygon, nameof(polygon));
        CheckGridSize(gridSize);
        if (PolygonMesh.TryBuild(polygon) is { } fast)
        {
            // a valid polygon (it has no crossing and no touching, which TryBuild has checked): ear clipping and flips with the rings
            // fixed, without the Delaunay triangulation of all vertices
            return From(fast, null, borderConstrained: true);
        }

        var segments = Prepare(ConstraintSet.Segments(polygon), factory, gridSize);
        return FromPolygonGeneral(segments);
    }

    /// <summary>The general path of <see cref="FromPolygon"/>: all vertices are triangulated, then the rings are forced in. Internal for the tests.</summary>
    internal static Triangulation FromPolygonGeneral(IPolygon2D polygon, IGeometryFactory? factory = null, double? gridSize = null)
        => FromPolygonGeneral(Prepare(ConstraintSet.Segments(polygon), factory, gridSize));

    private static Triangulation FromPolygonGeneral(List<(Coordinate A, Coordinate B)> segments)
    {
        var vertices = new VertexSet([]);
        foreach (var (a, b) in segments)
        {
            vertices.Add(a);
            vertices.Add(b);
        }

        return Constrained(vertices, segments, polygon: true);
    }

    private static void CheckGridSize(double? gridSize)
    {
        if (gridSize is { } size && (!double.IsFinite(size) || size <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), gridSize, "The grid size must be finite and greater than 0.");
        }
    }

    private static List<(Coordinate A, Coordinate B)> Prepare(List<(Coordinate A, Coordinate B)> segments, IGeometryFactory? factory, double? gridSize)
    {
        CheckGridSize(gridSize);
        return ConstraintSet.HasProperCrossing(segments) ? ConstraintSet.Noded(segments, GeometryFactory.Resolve(factory), gridSize) : segments;
    }

    private static Triangulation Constrained(VertexSet vertices, List<(Coordinate A, Coordinate B)> segments, bool polygon)
    {
        // the noded segments may have vertices that the points do not have
        foreach (var (a, b) in segments)
        {
            vertices.Add(a);
            vertices.Add(b);
        }

        var points = vertices.ToArray();
        var mesh = DelaunayMesh.Create(points);
        if (mesh is null)
        {
            return None;
        }

        mesh.InsertConstraints(segments.Select(s => (vertices.IdOf(s.A), vertices.IdOf(s.B))));
        return From(mesh, polygon ? InsideTriangles(mesh) : null);
    }

    /// <summary>The triangles inside the rings by the even-odd rule: from the outside of the hull, crossing a constraint toggles.</summary>
    private static bool[] InsideTriangles(DelaunayMesh mesh)
    {
        int count = mesh.TriangleCount;
        var inside = new bool[count];
        var known = new bool[count];
        var queue = new Queue<int>();
        var tri = mesh.Triangles;
        for (int e = 0; e < count * 3; e++)
        {
            if (mesh.HalfEdges[e] == -1)
            {
                int t = e / 3;
                if (!known[t])
                {
                    // outside the hull is outside; a hull edge that is a constraint is a border of the polygon
                    inside[t] = mesh.IsConstrained(tri[e], tri[DelaunayMesh.Next(e)]);
                    known[t] = true;
                    queue.Enqueue(t);
                }
            }
        }

        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            for (int k = 0; k < 3; k++)
            {
                int e = 3 * t + k;
                int h = mesh.HalfEdges[e];
                if (h == -1 || known[h / 3])
                {
                    continue;
                }

                int n = h / 3;
                inside[n] = mesh.IsConstrained(tri[e], tri[DelaunayMesh.Next(e)]) ? !inside[t] : inside[t];
                known[n] = true;
                queue.Enqueue(n);
            }
        }

        return inside;
    }

    /// <summary>The triangulation of the kept triangles of a mesh (all of them if <paramref name="keep"/> is <c>null</c>); <paramref name="borderConstrained"/>: the border of the mesh consists of constraints.</summary>
    private static Triangulation From(DelaunayMesh mesh, bool[]? keep, bool borderConstrained = false)
    {
        int count = mesh.TriangleCount;
        var map = new int[count];
        int kept = 0;
        for (int t = 0; t < count; t++)
        {
            map[t] = keep is null || keep[t] ? kept++ : -1;
        }

        var triangles = new int[kept * 3];
        var neighbours = new int[kept * 3];
        var constrained = new bool[kept * 3];
        for (int t = 0; t < count; t++)
        {
            if (map[t] < 0)
            {
                continue;
            }

            for (int k = 0; k < 3; k++)
            {
                int e = 3 * t + k;
                int h = mesh.HalfEdges[e];
                int slot = 3 * map[t] + k;
                triangles[slot] = mesh.Triangles[e];
                neighbours[slot] = h == -1 ? -1 : map[h / 3];
                constrained[slot] = (borderConstrained && h == -1) || mesh.IsConstrained(mesh.Triangles[e], mesh.Triangles[DelaunayMesh.Next(e)]);
            }
        }

        return new Triangulation(mesh.Points, triangles, neighbours, constrained);
    }

    // ---------- the triangles ----------

    /// <summary>The vertex ids of a triangle, counter-clockwise.</summary>
    /// <param name="triangle">The triangle, from 0 to <see cref="TriangleCount"/> − 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public (int A, int B, int C) Triangle(int triangle)
    {
        CheckTriangle(triangle);
        return (_triangles[3 * triangle], _triangles[3 * triangle + 1], _triangles[3 * triangle + 2]);
    }

    /// <summary>The triangle on the other side of the edge from vertex <paramref name="side"/> to vertex <paramref name="side"/> + 1 of a triangle.</summary>
    /// <param name="triangle">The triangle.</param>
    /// <param name="side">0, 1 or 2.</param>
    /// <returns>The neighbour, or −1 where the triangle is at the border (the hull, or the edge of a polygon).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> or <paramref name="side"/> is out of range.</exception>
    public int Neighbour(int triangle, int side)
    {
        CheckTriangle(triangle);
        CheckSide(side);
        return _neighbours[3 * triangle + side];
    }

    /// <summary>Whether the edge of a side is a constraint (a break line, a ring of the polygon).</summary>
    /// <param name="triangle">The triangle.</param>
    /// <param name="side">0, 1 or 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> or <paramref name="side"/> is out of range.</exception>
    public bool IsConstrained(int triangle, int side)
    {
        CheckTriangle(triangle);
        CheckSide(side);
        return _constrained[3 * triangle + side];
    }

    private void CheckTriangle(int triangle)
    {
        if ((uint)triangle >= (uint)TriangleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(triangle), triangle, "The triangle does not exist.");
        }
    }

    private static void CheckSide(int side)
    {
        if ((uint)side > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "The side must be 0, 1 or 2.");
        }
    }

    // ---------- locating and interpolating ----------

    /// <summary>
    /// The triangle that contains a point, on its border or inside. On a border shared by triangles it is the one with the smallest index.
    /// The decision is exact (the orientation test of the overlays, ADR 0005).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The triangle, or −1 if the point lies outside every triangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public int Locate(Coordinate point)
    {
        EnsurePoint(point);
        if (TriangleCount == 0)
        {
            return -1;
        }

        int best = -1;
        foreach (int t in _index.Value.Query(new Envelope2D(point.X, point.Y, point.X, point.Y)))
        {
            if ((best == -1 || t < best) && Contains(t, point))
            {
                best = t;
            }
        }

        return best;
    }

    private bool Contains(int t, Coordinate p)
    {
        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        return Orientation.Orient2D(a, b, p) >= 0 && Orientation.Orient2D(b, c, p) >= 0 && Orientation.Orient2D(c, a, p) >= 0;
    }

    private SpatialIndex<int> BuildIndex() => new(Enumerable.Range(0, TriangleCount).Select(t =>
    {
        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        return (new Envelope2D(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)),
            Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y))), t);
    }));

    /// <summary>
    /// The height at a point: Z interpolated linearly in the triangle that contains it (the plane through its three vertices). At a vertex it is
    /// the Z of the vertex, and on a surface that is a plane it is exact up to rounding.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The height; <see cref="double.NaN"/> if the point lies outside every triangle or a vertex of its triangle has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public double InterpolateZ(Coordinate point)
    {
        int t = Locate(point);
        if (t < 0)
        {
            return double.NaN;
        }

        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        if (!a.HasZ || !b.HasZ || !c.HasZ)
        {
            return double.NaN;
        }

        double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        double wa = ((b.Y - c.Y) * (point.X - c.X) + (c.X - b.X) * (point.Y - c.Y)) / det;
        double wb = ((c.Y - a.Y) * (point.X - c.X) + (a.X - c.X) * (point.Y - c.Y)) / det;
        double wc = 1 - wa - wb;
        return wa * a.Z + wb * b.Z + wc * c.Z;
    }

    private static void EnsurePoint(Coordinate point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "The coordinates of the point must be finite.");
        }
    }

    // ---------- as geometries ----------

    /// <summary>The triangles as polygons, counter-clockwise, in the order of their ids, with the Z of their vertices.</summary>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IReadOnlyList<IPolygon2D> ToPolygons(IGeometryFactory? factory = null)
    {
        var f = GeometryFactory.Resolve(factory);
        var result = new List<IPolygon2D>(TriangleCount);
        for (int t = 0; t < TriangleCount; t++)
        {
            var a = _vertices[_triangles[3 * t]];
            var b = _vertices[_triangles[3 * t + 1]];
            var c = _vertices[_triangles[3 * t + 2]];
            result.Add(f.CreatePolygon([new PolygonPart(GeometryAccess.Restrict([a, b, c, a], f))], null));
        }

        return result;
    }

    /// <summary>The edges of the triangles, every edge once, as one path each, with the Z of their vertices.</summary>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolyline2D Edges(IGeometryFactory? factory = null)
    {
        var f = GeometryFactory.Resolve(factory);
        var paths = new List<ReadOnlyMemory<Coordinate>>();
        for (int t = 0; t < TriangleCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = _neighbours[3 * t + k];
                if (n == -1 || n > t)
                {
                    paths.Add(GeometryAccess.Restrict([_vertices[_triangles[3 * t + k]], _vertices[_triangles[3 * t + (k + 1) % 3]]], f));
                }
            }
        }

        return f.CreatePolyline(paths.ToArray(), null);
    }

    /// <summary>The edges that are constraints (break lines, rings of the polygon), every edge once, as one path each.</summary>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolyline2D ConstrainedEdges(IGeometryFactory? factory = null)
    {
        var f = GeometryFactory.Resolve(factory);
        var paths = new List<ReadOnlyMemory<Coordinate>>();
        for (int t = 0; t < TriangleCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = _neighbours[3 * t + k];
                if (_constrained[3 * t + k] && (n == -1 || n > t))
                {
                    paths.Add(GeometryAccess.Restrict([_vertices[_triangles[3 * t + k]], _vertices[_triangles[3 * t + (k + 1) % 3]]], f));
                }
            }
        }

        return f.CreatePolyline(paths.ToArray(), null);
    }

    /// <summary>
    /// The contour line at a height (ADR 0037): where the surface, linear in every triangle, has the height <paramref name="z"/>. A contour
    /// is a polyline whose vertices have Z = <paramref name="z"/>; it is closed (first = last) where it comes back to its start, and it
    /// ends at the border of the triangulation. Pieces that meet in exactly one other piece are joined into one path.
    /// </summary>
    /// <remarks>
    /// A vertex whose height is exactly <paramref name="z"/> counts as lying above the contour, so that the contour passes through it
    /// and never splits there. Triangles with a vertex without Z are skipped, and a flat area at exactly <paramref name="z"/> has no
    /// contour. Where the contour of a neighbouring pair of triangles meets, it does so bit for bit.
    /// </remarks>
    /// <param name="z">The height; finite.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The contour; an empty polyline if the surface does not reach the height.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="z"/> is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolyline2D Contour(double z, IGeometryFactory? factory = null)
    {
        if (!double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(z), z, "The height must be finite.");
        }

        var f = GeometryFactory.Resolve(factory);
        var pieces = new List<Coordinate[]>();
        var crossing = new Coordinate[3];
        for (int t = 0; t < TriangleCount; t++)
        {
            var ids = new[] { _triangles[3 * t], _triangles[3 * t + 1], _triangles[3 * t + 2] };
            if (ids.Any(i => !_vertices[i].HasZ))
            {
                continue;
            }

            int found = 0;
            for (int k = 0; k < 3; k++)
            {
                int i = ids[k], j = ids[(k + 1) % 3];
                bool above = _vertices[i].Z >= z, other = _vertices[j].Z >= z;
                if (above != other)
                {
                    crossing[found++] = Crossing(i, j, z);
                }
            }

            if (found == 2 && (crossing[0].X != crossing[1].X || crossing[0].Y != crossing[1].Y))
            {
                pieces.Add([crossing[0], crossing[1]]);
            }
        }

        var merged = LineMerger.Merge(pieces, directed: false);
        return f.CreatePolyline(merged.Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p, f)).ToArray(), null);
    }

    /// <summary>Where the edge between two vertices has the height z; computed from the end points in a fixed order, so both triangles get the same point.</summary>
    private Coordinate Crossing(int i, int j, double z)
    {
        if (i > j)
        {
            (i, j) = (j, i);
        }

        var a = _vertices[i];
        var b = _vertices[j];
        double t = (z - a.Z) / (b.Z - a.Z);
        return new Coordinate(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), z);
    }

    // ---------- the vertices while building ----------

    /// <summary>Distinct vertices by X and Y in the order of their first occurrence; a Z that is missing is taken from a later one.</summary>
    private sealed class VertexSet
    {
        private readonly List<Coordinate> _list = [];
        private readonly Dictionary<(double, double), int> _ids = [];

        public VertexSet(IEnumerable<Coordinate> initial)
        {
            foreach (var c in initial)
            {
                Add(c);
            }
        }

        public void Add(Coordinate c)
        {
            if (!double.IsFinite(c.X) || !double.IsFinite(c.Y))
            {
                return;
            }

            if (_ids.TryGetValue((c.X, c.Y), out int id))
            {
                if (!_list[id].HasZ && c.HasZ)
                {
                    _list[id] = _list[id] with { Z = c.Z };
                }

                return;
            }

            _ids[(c.X, c.Y)] = _list.Count;
            _list.Add(c with { M = double.NaN });
        }

        public int IdOf(Coordinate c) => _ids[(c.X, c.Y)];

        public Coordinate[] ToArray() => _list.ToArray();
    }
}
