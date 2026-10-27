using SpatialNET.Internal.Noding;

namespace SpatialNET;

// A smoother surface (ADR 0043): the heights of a regular grid by natural neighbour interpolation, added to the vertices.
public sealed partial class Triangulation
{
    private const long MaxGridPoints = 5_000_000;

    /// <summary>
    /// A smoother surface (ADR 0043): the heights of a regular grid of the given cell size are computed with
    /// <see cref="InterpolateNaturalNeighbour"/>, and the grid is triangulated again together with the border and the constraints (the vertices on the
    /// border and on break lines keep their heights, and the area that is covered is the same). The surface over the new triangles has no creases
    /// at the old edges and no peaks at the measurements, and <see cref="Contour"/> and <see cref="ContourBands"/> of it are smooth, where those of the
    /// original follow its triangles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contour lines of any triangulation cannot cross each other, since over a triangle they are parallel straight lines, and this one is no
    /// exception, so <b>smoothing the surface</b> is the way to smooth contour lines that must not touch. Smoothing the lines afterwards
    /// (<see cref="Smoothing.Smooth(IPolyline2D, int, SmoothingMethod, IGeometryFactory?)"/>) rounds the last corners, but cannot promise that.
    /// </para>
    /// <para>
    /// The inner vertices (those that are not on the border or on a constraint) are <b>not</b> kept by default: the interpolated surface is smooth
    /// everywhere but at the measurements themselves, where it has a point (an interpolation passes through its data), and that point would stay as a
    /// kink in the contour lines. So the result is a smoothed surface that no longer passes exactly through the inner measurements, like a raster that
    /// was interpolated from them (it passes through them to within the interpolation over one cell); <paramref name="keepVertices"/> keeps them, for a
    /// surface that must. Smooth is not the same as closer to the true terrain: natural neighbour interpolation of a curved surface is biased towards
    /// the neighbours that surround a point, about as much as the linear interpolation is.
    /// </para>
    /// <para>
    /// A cell size of about the mean distance between the vertices is a start, half of it gives a visibly smoother map. Grid points that lie on the
    /// border, on a constraint, or on a vertex are left out. Where the triangulation has constraints (break lines, a polygon) the heights between
    /// them are the linear ones, since natural neighbour interpolation does not cross a crease (<see cref="InterpolateNaturalNeighbour"/>); the
    /// surface then is finer, but not smoother. Vertices without Z give points without Z.
    /// </para>
    /// </remarks>
    /// <param name="cellSize">The distance between the grid points; finite and greater than 0, and not so small that there would be more than five million of them.</param>
    /// <param name="keepVertices"><c>true</c> to keep the inner vertices as well (the surface then passes through every measurement, with the point there).</param>
    /// <returns>The finer triangulation; the same instance if there are no triangles or no grid point lies inside.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellSize"/> is not finite or not greater than 0, or so small that the grid would be unreasonably large.</exception>
    public Triangulation Resample(double cellSize, bool keepVertices = false)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "The cell size must be finite and greater than 0.");
        }

        if (TriangleCount == 0)
        {
            return this;
        }

        double minX = _vertices.Min(v => v.X), maxX = _vertices.Max(v => v.X), minY = _vertices.Min(v => v.Y), maxY = _vertices.Max(v => v.Y);
        double startX = Math.Ceiling(minX / cellSize) * cellSize, startY = Math.Ceiling(minY / cellSize) * cellSize;
        double columnsEstimate = Math.Floor((maxX - startX) / cellSize) + 1, rowsEstimate = Math.Floor((maxY - startY) / cellSize) + 1;
        if (columnsEstimate * rowsEstimate > MaxGridPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, $"The cell size is so small that the grid would have more than {MaxGridPoints} points.");
        }

        int columns = (int)Math.Max(columnsEstimate, 0), rows = (int)Math.Max(rowsEstimate, 0);
        var found = new List<Coordinate>[rows];
        double near = 1e-6 * cellSize;
        Parallel.For(0, rows, row =>
        {
            var list = new List<Coordinate>();
            double y = startY + row * cellSize;
            for (int column = 0; column < columns; column++)
            {
                var p = new Coordinate(startX + column * cellSize, y);
                int t = Locate(p);
                if (t < 0 || IsOnVertexOrBorder(t, p, near))
                {
                    continue;
                }

                double z = InterpolateNaturalNeighbour(p);
                list.Add(double.IsNaN(z) ? p : new Coordinate(p.X, p.Y, z));
            }

            found[row] = list;
        });

        int gridPoints = found.Sum(list => list.Count);
        if (gridPoints == 0)
        {
            return this;
        }

        // the vertices to keep, with their new ids: all of them, or those on the border and on the constraints
        var segments = BorderAndConstraintSegments();
        var onSegment = new bool[_vertices.Length];
        foreach (var (u, v) in segments.Keys)
        {
            onSegment[u] = true;
            onSegment[v] = true;
        }

        var keep = new int[_vertices.Length];
        Array.Fill(keep, -1);
        var points = new List<Coordinate>(gridPoints + _vertices.Length);
        for (int id = 0; id < _vertices.Length; id++)
        {
            if (!keepVertices && !onSegment[id])
            {
                continue;
            }

            keep[id] = points.Count;
            points.Add(_vertices[id]);
        }

        var remapped = new Dictionary<(int, int), Segment>(segments.Count);
        foreach (var (key, segment) in segments)
        {
            int u = keep[key.Item1], v = keep[key.Item2];
            remapped[u < v ? (u, v) : (v, u)] = new Segment(segment.Constrained, segment.From >= 0 ? keep[segment.From] : -1, segment.To >= 0 ? keep[segment.To] : -1);
        }

        foreach (var list in found)
        {
            points.AddRange(list);
        }

        return Rebuild(points, remapped) ?? this;
    }

    /// <summary>Whether a point of a triangle is (nearly) one of its vertices, or on an edge that is a border or a constraint.</summary>
    private bool IsOnVertexOrBorder(int t, Coordinate p, double near)
    {
        for (int k = 0; k < 3; k++)
        {
            var u = _vertices[_triangles[3 * t + k]];
            if (Math.Abs(u.X - p.X) <= near && Math.Abs(u.Y - p.Y) <= near)
            {
                return true;
            }

            var v = _vertices[_triangles[3 * t + (k + 1) % 3]];
            if ((_neighbours[3 * t + k] < 0 || _constrained[3 * t + k]) && Orientation.Orient2D(u, v, p) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
