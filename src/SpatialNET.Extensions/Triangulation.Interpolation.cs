using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET;

// Smooth interpolation on a triangulation (ADR 0041): natural neighbour (Sibson) as the alternative to the linear InterpolateZ.
public sealed partial class Triangulation
{
    private int _constraintState;   // 0 = not known yet, 1 = no constraint, 2 = has constraints; racing threads compute the same value

    private bool HasConstraints
    {
        get
        {
            if (_constraintState == 0)
            {
                _constraintState = Array.IndexOf(_constrained, true) >= 0 ? 2 : 1;
            }

            return _constraintState == 2;
        }
    }

    /// <summary>
    /// The height at a point by natural neighbour interpolation (Sibson, ADR 0041): the heights of the vertices that the point would be
    /// connected to if it were inserted, weighted by the area of their Voronoi cells that the new point takes over. The result is continuous,
    /// smooth between the vertices (except at the vertices themselves), reproduces a plane exactly, and never leaves the range of the heights
    /// around it, so it has none of the creases of the linear <see cref="InterpolateZ"/> and no overshoots of a polynomial.
    /// </summary>
    /// <remarks>
    /// It needs the Delaunay property, so for a triangulation with constraints (break lines, a polygon) it gives the linear
    /// <see cref="InterpolateZ"/> instead, which is the right answer when the surface must have a crease. The same holds for a point on the
    /// border of the hull and for the rare case that the areas cannot be computed (a sliver triangle with a circumcentre beyond double precision).
    /// The cost is proportional to the number of vertices around the point (about six), so it is as fast as the linear one to within a small factor.
    /// </remarks>
    /// <param name="point">The point.</param>
    /// <returns>The height; <see cref="double.NaN"/> if the point lies outside every triangle or a vertex that takes part has no Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public double InterpolateNaturalNeighbour(Coordinate point)
    {
        int t0 = Locate(point);
        if (t0 < 0)
        {
            return double.NaN;
        }

        for (int k = 0; k < 3; k++)
        {
            var v = _vertices[_triangles[3 * t0 + k]];
            if (v.X == point.X && v.Y == point.Y)
            {
                return v.HasZ ? v.Z : double.NaN;
            }
        }

        if (HasConstraints)
        {
            return InterpolateZ(point);
        }

        return NaturalNeighbour(t0, point) ?? InterpolateZ(point);
    }

    private double? NaturalNeighbour(int t0, Coordinate q)
    {
        // the triangles whose circumcircle contains the point (they are connected: the Delaunay property)
        var cavity = new HashSet<int> { t0 };
        var stack = new Stack<int>();
        stack.Push(t0);
        while (stack.Count > 0)
        {
            int t = stack.Pop();
            for (int k = 0; k < 3; k++)
            {
                int n = _neighbours[3 * t + k];
                if (n >= 0 && !cavity.Contains(n)
                    && InCircle.Sign(_vertices[_triangles[3 * n]], _vertices[_triangles[3 * n + 1]], _vertices[_triangles[3 * n + 2]], q) > 0)
                {
                    cavity.Add(n);
                    stack.Push(n);
                }
            }
        }

        // the boundary of the cavity, counter-clockwise around the point: edge a → b of the triangle t on side k
        var edges = new Dictionary<int, (int B, int T, int K)>();
        foreach (int t in cavity)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = _neighbours[3 * t + k];
                if (n >= 0 && cavity.Contains(n))
                {
                    continue;
                }

                int a = _triangles[3 * t + k], b = _triangles[3 * t + (k + 1) % 3];
                if (n < 0 && Orientation.Orient2D(_vertices[a], _vertices[b], q) == 0)
                {
                    return null;   // on the border of the hull: the cell of the point would be unbounded
                }

                if (!edges.TryAdd(a, (b, t, k)))
                {
                    return null;
                }
            }
        }

        if (edges.Count < 3)
        {
            return null;
        }

        var ring = new List<(int A, int B, int T, int K)>(edges.Count);
        int start = edges.Keys.First(), current = start;
        do
        {
            if (!edges.TryGetValue(current, out var e))
            {
                return null;
            }

            ring.Add((current, e.B, e.T, e.K));
            current = e.B;
        }
        while (current != start && ring.Count <= edges.Count);

        if (ring.Count != edges.Count || current != start)
        {
            return null;
        }

        int m = ring.Count;
        var fresh = new (double X, double Y)[m];   // the circumcentre of the point with edge i: the corners of its new cell
        for (int i = 0; i < m; i++)
        {
            fresh[i] = VoronoiBuilder.Centre(q, _vertices[ring[i].A], _vertices[ring[i].B]);
        }

        var old = new Dictionary<int, (double X, double Y)>();
        (double X, double Y) Old(int t)
        {
            if (!old.TryGetValue(t, out var c))
            {
                c = VoronoiBuilder.Centre(_vertices[_triangles[3 * t]], _vertices[_triangles[3 * t + 1]], _vertices[_triangles[3 * t + 2]]);
                old[t] = c;
            }

            return c;
        }

        double sum = 0, weighted = 0;
        var polygon = new List<(double X, double Y)>();
        for (int i = 0; i < m; i++)
        {
            var (a, _, tIn, kIn) = ring[i];
            var va = _vertices[a];
            if (!va.HasZ)
            {
                return double.NaN;
            }

            // what the new cell takes from the old cell of a: the corners of the new cell on the bisector, and the old cell's corners between
            polygon.Clear();
            polygon.Add(fresh[(i + m - 1) % m]);
            polygon.Add(fresh[i]);
            int t = tIn, k = kIn;
            for (int guard = 0; guard <= cavity.Count; guard++)
            {
                polygon.Add(Old(t));
                int side = (k + 2) % 3;
                int n = _neighbours[3 * t + side];
                if (n < 0 || !cavity.Contains(n))
                {
                    break;
                }

                t = n;
                k = _triangles[3 * t] == a ? 0 : _triangles[3 * t + 1] == a ? 1 : 2;
            }

            double area = 0;
            for (int j = 0; j < polygon.Count; j++)
            {
                var p = polygon[j];
                var r = polygon[(j + 1) % polygon.Count];
                area += (p.X - q.X) * (r.Y - q.Y) - (r.X - q.X) * (p.Y - q.Y);
            }

            area = Math.Abs(area) / 2;
            if (!double.IsFinite(area))
            {
                return null;
            }

            sum += area;
            weighted += area * va.Z;
        }

        return sum > 0 && double.IsFinite(weighted) ? weighted / sum : null;
    }
}
