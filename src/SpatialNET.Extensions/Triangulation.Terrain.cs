using SpatialNET.Internal;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

// Terrain analysis on a triangulation (ADR 0039): what the triangles say about slope, area and volume, and a line laid over the surface.
public sealed partial class Triangulation
{
    // ---------- slope, aspect, area ----------

    /// <summary>
    /// The gradient of a triangle: how much the height changes per unit in X and in Y, in the plane through its three vertices (ADR 0039).
    /// X, Y and Z are taken to be in the same unit.
    /// </summary>
    /// <param name="triangle">The triangle.</param>
    /// <returns>The gradient; <see cref="double.NaN"/> for both if a vertex has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public (double Dx, double Dy) Gradient(int triangle)
    {
        CheckTriangle(triangle);
        return GradientOf(triangle);
    }

    private (double Dx, double Dy) GradientOf(int t)
    {
        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        if (!a.HasZ || !b.HasZ || !c.HasZ)
        {
            return (double.NaN, double.NaN);
        }

        double det = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        return (((b.Z - a.Z) * (c.Y - a.Y) - (c.Z - a.Z) * (b.Y - a.Y)) / det,
                ((c.Z - a.Z) * (b.X - a.X) - (b.Z - a.Z) * (c.X - a.X)) / det);
    }

    /// <summary>
    /// The slope of a triangle in degrees: the angle of its plane to the horizontal, from 0 (flat) to 90 (vertical). X, Y and Z are taken to be
    /// in the same unit (ADR 0039).
    /// </summary>
    /// <param name="triangle">The triangle.</param>
    /// <returns>The slope in degrees; <see cref="double.NaN"/> if a vertex has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public double Slope(int triangle)
    {
        var (dx, dy) = Gradient(triangle);
        return double.IsNaN(dx) ? double.NaN : Math.Atan(Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI;
    }

    /// <summary>
    /// The aspect of a triangle: the compass direction that its plane faces, which is the direction of steepest descent, in degrees clockwise
    /// from north (0 north, 90 east, 180 south, 270 west), in <c>[0, 360)</c> (ADR 0039).
    /// </summary>
    /// <param name="triangle">The triangle.</param>
    /// <returns>The aspect in degrees; <see cref="double.NaN"/> for a flat triangle (it faces nowhere) or if a vertex has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public double Aspect(int triangle)
    {
        var (dx, dy) = Gradient(triangle);
        if (double.IsNaN(dx) || (dx == 0 && dy == 0))
        {
            return double.NaN;
        }

        double bearing = Math.Atan2(-dx, -dy) * 180 / Math.PI;   // downhill is (-dx, -dy): east first, north second
        double aspect = bearing < 0 ? bearing + 360 : bearing;
        return aspect >= 360 ? 0 : aspect;                       // a bearing of -1e-15 gives 360 after the addition
    }

    /// <summary>The area of a triangle on the surface, in the plane through its three vertices: larger than its area in the map when it slopes (ADR 0039).</summary>
    /// <param name="triangle">The triangle.</param>
    /// <returns>The area; <see cref="double.NaN"/> if a vertex has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="triangle"/> is out of range.</exception>
    public double SurfaceArea(int triangle)
    {
        CheckTriangle(triangle);
        return SurfaceAreaOf(triangle);
    }

    private double SurfaceAreaOf(int t)
    {
        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        if (!a.HasZ || !b.HasZ || !c.HasZ)
        {
            return double.NaN;
        }

        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
        return Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2;
    }

    /// <summary>
    /// The area of the whole surface: the sum of <see cref="SurfaceArea(int)"/> over all triangles. Triangles with a vertex without Z are not counted
    /// (ADR 0039).
    /// </summary>
    /// <returns>The area; 0 for a triangulation without triangles.</returns>
    public double SurfaceArea()
    {
        double sum = 0;
        for (int t = 0; t < TriangleCount; t++)
        {
            double area = SurfaceAreaOf(t);
            if (!double.IsNaN(area))
            {
                sum += area;
            }
        }

        return sum;
    }

    // ---------- volume ----------

    /// <summary>
    /// The volume between the surface and the horizontal plane at a height (ADR 0039): <c>Above</c> is what lies above the plane (to cut away),
    /// <c>Below</c> what is missing up to it (to fill), both not negative. Their difference is the net volume. The surface is linear in every triangle,
    /// so the result is exact; a triangle that the plane cuts is split along the cut. Triangles with a vertex without Z are not counted. X, Y and Z are
    /// taken to be in the same unit, and the volume is in its cube.
    /// </summary>
    /// <param name="referenceZ">The height of the plane.</param>
    /// <returns>The volume above and below the plane.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="referenceZ"/> is <c>NaN</c> or infinite.</exception>
    public (double Above, double Below) Volume(double referenceZ)
    {
        if (!double.IsFinite(referenceZ))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceZ), referenceZ, "The height must be finite.");
        }

        double above = 0, below = 0;
        for (int t = 0; t < TriangleCount; t++)
        {
            var a = _vertices[_triangles[3 * t]];
            var b = _vertices[_triangles[3 * t + 1]];
            var c = _vertices[_triangles[3 * t + 2]];
            if (!a.HasZ || !b.HasZ || !c.HasZ)
            {
                continue;
            }

            double area = ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            double h0 = a.Z - referenceZ, h1 = b.Z - referenceZ, h2 = c.Z - referenceZ;
            above += Positive(h0, h1, h2, area);
            below += Positive(-h0, -h1, -h2, area);
        }

        return (above, below);
    }

    /// <summary>The integral of max(h, 0) over a triangle of the given area whose height h is linear with the values <paramref name="h0"/>, <paramref name="h1"/>, <paramref name="h2"/> at its corners.</summary>
    private static double Positive(double h0, double h1, double h2, double area)
    {
        int positive = (h0 > 0 ? 1 : 0) + (h1 > 0 ? 1 : 0) + (h2 > 0 ? 1 : 0);
        switch (positive)
        {
            case 0:
                return 0;
            case 3:
                return area * (h0 + h1 + h2) / 3;
            case 1:
                // a small triangle at the corner above the plane, with the heights (h, 0, 0)
                var (hp, hq, hr) = h0 > 0 ? (h0, h1, h2) : h1 > 0 ? (h1, h0, h2) : (h2, h0, h1);
                return area * (hp / (hp - hq)) * (hp / (hp - hr)) * hp / 3;
            default:
                // two corners above (a, b) and one on or below the plane (c): the part above is a quadrilateral, cut into two triangles; a sum
                // of products of non-negative numbers, so that no cancellation can make it negative
                var (ha, hb, hc) = h0 <= 0 ? (h1, h2, h0) : h1 <= 0 ? (h0, h2, h1) : (h0, h1, h2);
                double sa = ha / (ha - hc), sb = hb / (hb - hc);   // where the plane cuts the edges from c to a and to b, from a and from b
                return area * (sb * (ha + hb) + (1 - sb) * sa * ha) / 3;
        }
    }

    // ---------- draping a line ----------

    /// <summary>
    /// Lays a polyline over the surface (ADR 0039): the same line in the map, with the height of the surface as Z and the distance along the
    /// input line as M. Wherever the line crosses an edge of the triangulation, or passes through a vertex, a vertex is inserted, so that the
    /// result follows the surface exactly: between two vertices the height is linear. Z needs a factory with <see cref="IGeometryFactory.SupportsZ"/>,
    /// M one with <see cref="IGeometryFactory.SupportsM"/>; what it does not support is dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M is the planar length along the input line with its paths laid end to end, as in <see cref="LinearReferencing"/>, so
    /// <c>Interpolate</c>, <c>Project</c> and the other methods of linear referencing apply to the result, and the height profile of a road
    /// is its Z against its M. The 3D length is <c>Length()</c> of the planar line plus the climbing, which a caller computes from the vertices.
    /// </para>
    /// <para>
    /// Parts of the line outside the triangulation, and parts over a triangle with a vertex without Z, are left out; the result then has several
    /// paths, one for every part that is inside. A line along an edge or through vertices is handled exactly; the decision whether a stretch is
    /// inside allows a tolerance of 1e-10 of the size of the surface. The cost is proportional to the triangles along the line (it is looked up
    /// in pieces of a few triangle sizes), so a long line over a large surface is cheap.
    /// </para>
    /// </remarks>
    /// <param name="line">The polyline.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The line over the surface; empty if no part of it is over the surface.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolyline2D Drape(IPolyline2D line, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = GeometryFactory.Resolve(factory);
        var paths = new List<ReadOnlyMemory<Coordinate>>();
        if (TriangleCount == 0)
        {
            return f.CreatePolyline([], line);
        }

        double tolerance = Tolerance();
        double along = 0;
        for (int p = 0; p < line.PathCount; p++)
        {
            var path = Rings.Coordinates(line[p]);
            List<Coordinate>? current = null;

            void Close()
            {
                if (current is { Count: >= 2 })
                {
                    paths.Add(GeometryAccess.Restrict(current.ToArray(), f));
                }

                current = null;
            }

            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (length == 0)
                {
                    continue;
                }

                var cuts = CutParameters(a, b);
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    double t0 = cuts[k].T, t1 = cuts[k + 1].T;
                    var mid = new Coordinate(a.X + (t0 + t1) / 2 * (b.X - a.X), a.Y + (t0 + t1) / 2 * (b.Y - a.Y));
                    int triangle = LocateWithin(mid, tolerance);
                    var (dx, dy) = triangle < 0 ? (double.NaN, double.NaN) : GradientOf(triangle);
                    if (triangle < 0 || double.IsNaN(dx))
                    {
                        Close();
                        continue;
                    }

                    // the height at a cut is that of the edge or vertex, computed in one dimension; only at the ends of the segment, which lie
                    // inside the triangle, the plane of the triangle is asked, from its vertex nearest to the point (a thin triangle makes the
                    // plane inexact far from its vertices)
                    Coordinate At(double t, double cutZ)
                    {
                        double x = t == 0 ? a.X : t == 1 ? b.X : a.X + t * (b.X - a.X);
                        double y = t == 0 ? a.Y : t == 1 ? b.Y : a.Y + t * (b.Y - a.Y);
                        var origin = NearestVertex(triangle, x, y);
                        double z = double.IsNaN(cutZ) ? origin.Z + dx * (x - origin.X) + dy * (y - origin.Y) : cutZ;
                        return new Coordinate(x, y, z, along + t * length);
                    }

                    current ??= [At(t0, cuts[k].Z)];
                    current.Add(At(t1, cuts[k + 1].Z));
                }

                along += length;
            }

            Close();
        }

        return f.CreatePolyline(paths.ToArray(), line);
    }

    private double _typicalSize;   // 0 = not known yet; racing threads compute the same value

    /// <summary>The side of a square of the area that one triangle has on average (the extent of the vertices divided by the number of triangles).</summary>
    private double TypicalSize()
    {
        if (_typicalSize == 0)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var v in _vertices)
            {
                minX = Math.Min(minX, v.X);
                maxX = Math.Max(maxX, v.X);
                minY = Math.Min(minY, v.Y);
                maxY = Math.Max(maxY, v.Y);
            }

            double width = maxX - minX, height = maxY - minY;
            double area = Math.Max(width, 1e-300) * Math.Max(height, 1e-300);
            _typicalSize = Math.Max(Math.Sqrt(area / Math.Max(TriangleCount, 1)), 1e-300);
        }

        return _typicalSize;
    }

    /// <summary>
    /// The triangles that the segment a–b can touch, each once. The segment is cut into pieces of a few triangle sizes and the index is asked for the
    /// envelope of every piece, so that a long diagonal line looks at the triangles along it and not at all that lie in its envelope (which grow with the
    /// square of its length). A triangle that the segment crosses meets at least one piece, whose envelope is widened by a hair against rounding.
    /// </summary>
    private IEnumerable<int> AlongSegment(Coordinate a, Coordinate b)
    {
        double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int pieces = (int)Math.Clamp(Math.Ceiling(length / (6 * TypicalSize())), 1, 100_000);
        if (pieces == 1)
        {
            return _index.Value.Query(new Envelope2D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        }

        double margin = 1e-9 * Math.Max(length, 1e-300);
        var found = new HashSet<int>();
        var result = new List<int>();
        double px = a.X, py = a.Y;
        for (int i = 1; i <= pieces; i++)
        {
            double qx = i == pieces ? b.X : a.X + (b.X - a.X) * i / pieces, qy = i == pieces ? b.Y : a.Y + (b.Y - a.Y) * i / pieces;
            var box = new Envelope2D(Math.Min(px, qx) - margin, Math.Min(py, qy) - margin, Math.Max(px, qx) + margin, Math.Max(py, qy) + margin);
            foreach (int t in _index.Value.Query(box))
            {
                if (found.Add(t))
                {
                    result.Add(t);
                }
            }

            (px, py) = (qx, qy);
        }

        return result;
    }

    /// <summary>
    /// The parameters along the segment a–b, from 0 to 1, where it crosses an edge of the triangulation or passes through a vertex, sorted and
    /// without near duplicates, with the height of the surface there (<see cref="double.NaN"/> at 0 and 1, which lie inside a triangle).
    /// </summary>
    private List<(double T, double Z)> CutParameters(Coordinate a, Coordinate b)
    {
        var ts = new List<(double T, double Z)> { (0, double.NaN), (1, double.NaN) };
        var seenEdges = new HashSet<(int, int)>();
        var seenVertices = new HashSet<int>();
        double dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (int t in AlongSegment(a, b))
        {
            for (int k = 0; k < 3; k++)
            {
                int iu = _triangles[3 * t + k], iv = _triangles[3 * t + (k + 1) % 3];
                var u = _vertices[iu];
                var v = _vertices[iv];

                if (seenVertices.Add(iu) && Orientation.Orient2D(a, b, u) == 0)
                {
                    double s = Math.Abs(dx) >= Math.Abs(dy) ? (u.X - a.X) / dx : (u.Y - a.Y) / dy;
                    if (s > 0 && s < 1)
                    {
                        ts.Add((s, u.Z));   // NaN without Z
                    }
                }

                if (!seenEdges.Add(iu < iv ? (iu, iv) : (iv, iu)))
                {
                    continue;
                }

                if (Orientation.Orient2D(a, b, u) * Orientation.Orient2D(a, b, v) < 0
                    && Orientation.Orient2D(u, v, a) * Orientation.Orient2D(u, v, b) < 0)
                {
                    double ex = v.X - u.X, ey = v.Y - u.Y, cross = dx * ey - dy * ex;
                    double s = ((u.X - a.X) * ey - (u.Y - a.Y) * ex) / cross;
                    if (s > 0 && s < 1)
                    {
                        // where on the edge, from u to v: the crossing projected onto the edge, which stays exact for a segment that runs almost
                        // along the edge (the cross products of two almost parallel directions are lost in rounding)
                        double px = a.X + s * dx - u.X, py = a.Y + s * dy - u.Y;
                        double along = (px * ex + py * ey) / (ex * ex + ey * ey);
                        ts.Add((s, u.Z + Math.Clamp(along, 0, 1) * (v.Z - u.Z)));
                    }
                }
            }
        }

        ts.Sort((x, y) => x.T.CompareTo(y.T));
        var result = new List<(double T, double Z)> { ts[0] };
        for (int i = 1; i < ts.Count; i++)
        {
            if (ts[i].T - result[^1].T > 1e-12)
            {
                result.Add(ts[i]);
            }
            else if (double.IsNaN(result[^1].Z) && result[^1].T != 0)
            {
                result[^1] = ts[i];   // the same place found twice: keep the one with a height
            }
        }

        if (result[^1].T != 1)
        {
            result[^1] = (1, double.NaN);   // the last one closer than the tolerance to 1 is 1
        }

        return result;
    }

    private Coordinate NearestVertex(int t, double x, double y)
    {
        var best = _vertices[_triangles[3 * t]];
        double bestDistance = double.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            var v = _vertices[_triangles[3 * t + k]];
            double d = (v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = v;
            }
        }

        return best;
    }

    /// <summary>1e-10 of the size of the surface.</summary>
    private double Tolerance()
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var v in _vertices)
        {
            minX = Math.Min(minX, v.X);
            maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y);
            maxY = Math.Max(maxY, v.Y);
        }

        return 1e-10 * Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-300);
    }

    /// <summary>The triangle that contains the point, or whose border is within <paramref name="tolerance"/> of it; −1 if there is none.</summary>
    private int LocateWithin(Coordinate p, double tolerance)
    {
        int exact = Locate(p);
        if (exact >= 0)
        {
            return exact;
        }

        int best = -1;
        foreach (int t in _index.Value.Query(new Envelope2D(p.X - tolerance, p.Y - tolerance, p.X + tolerance, p.Y + tolerance)))
        {
            if ((best == -1 || t < best) && WithinOf(t, p, tolerance))
            {
                best = t;
            }
        }

        return best;
    }

    private bool WithinOf(int t, Coordinate p, double tolerance)
    {
        for (int k = 0; k < 3; k++)
        {
            var u = _vertices[_triangles[3 * t + k]];
            var v = _vertices[_triangles[3 * t + (k + 1) % 3]];
            double ex = v.X - u.X, ey = v.Y - u.Y, length = Math.Sqrt(ex * ex + ey * ey);
            if ((ex * (p.Y - u.Y) - ey * (p.X - u.X)) / length < -tolerance)   // the signed distance to the edge, positive on the inside
            {
                return false;
            }
        }

        return true;
    }
}
