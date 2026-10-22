using CsCheck;
using SpatialNET.Imp;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;

namespace SpatialNET.Tests.Construction;

/// <summary>The internal Delaunay mesh (ADR 0029): the invariants of a triangulation and the empty-circle property, also for degenerate point sets.</summary>
public class DelaunayMeshTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static Coordinate[] Distinct(IEnumerable<Coordinate> points) => points.GroupBy(c => (c.X, c.Y)).Select(g => g.First()).ToArray();

    /// <summary>Checks everything that makes a mesh a Delaunay triangulation of its points; returns the number of triangles.</summary>
    internal static void Verify(DelaunayMesh mesh, string label)
    {
        var points = mesh.Points;
        int n = points.Length, triangles = mesh.TriangleCount;
        var hull = mesh.Hull();

        Assert.True(triangles == 2 * n - 2 - hull.Length, $"{label}: {triangles} triangles for {n} points and {hull.Length} hull vertices");

        double area = 0;
        for (int t = 0; t < triangles; t++)
        {
            var (a, b, c) = (points[mesh.Triangles[3 * t]], points[mesh.Triangles[3 * t + 1]], points[mesh.Triangles[3 * t + 2]]);
            Assert.True(Orientation.Orient2D(a, b, c) > 0, $"{label}: triangle {t} is not counter-clockwise");
            area += Math.Abs(((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2);

            // the empty-circle property against every point
            for (int k = 0; k < n; k++)
            {
                Assert.True(InCircle.Sign(a, b, c, points[k]) <= 0, $"{label}: point {k} lies in the circumcircle of triangle {t}");
            }
        }

        for (int e = 0; e < triangles * 3; e++)
        {
            int h = mesh.HalfEdges[e];
            if (h == -1) continue;
            Assert.Equal(e, mesh.HalfEdges[h]);
            Assert.Equal(mesh.Triangles[e], mesh.Triangles[DelaunayMesh.Next(h)]);
            Assert.Equal(mesh.Triangles[DelaunayMesh.Next(e)], mesh.Triangles[h]);
        }

        // the triangles tile the convex hull
        var hullArea = Math.Abs(Internal.Rings.SignedArea(hull.Append(hull[0]).Select(i => points[i]).ToArray()));
        Assert.True(Math.Abs(area - hullArea) <= 1e-9 * (1 + hullArea), $"{label}: area {area:R} vs hull {hullArea:R}");
        Assert.Equal(hull.Length, Enumerable.Range(0, triangles * 3).Count(e => mesh.HalfEdges[e] == -1));
    }

    private static readonly Gen<Coordinate> Jitter = Gen.Select(Gen.Int[-60, 60], Gen.Int[-60, 60], Gen.Double[0, 1], Gen.Double[0, 1]).Select((x, y, a, b) => new Coordinate(x + a, y + b));

    private static readonly Gen<Coordinate> Grid = Gen.Select(Gen.Int[0, 6], Gen.Int[0, 6]).Select((x, y) => new Coordinate(x, y));

    private static readonly Gen<Coordinate> Lines = Gen.Select(Gen.Int[0, 3], Gen.Int[0, 20]).Select((k, i) => k switch
    {
        0 => new Coordinate(i, 0),
        1 => new Coordinate(i, i),
        2 => new Coordinate(0, i),
        _ => new Coordinate(i, 3 * i + 1),
    });

    private static readonly Gen<Coordinate> Circle = Gen.Select(Gen.Int[0, 11], Gen.Int[0, 2]).Select((i, r) => new Coordinate(
        (10 + 10 * r) * Math.Cos(2 * Math.PI * i / 12), (10 + 10 * r) * Math.Sin(2 * Math.PI * i / 12)));

    private static IEnumerable<(string Name, Gen<Coordinate> Gen)> Families => [("jittered", Jitter), ("grid", Grid), ("lines", Lines), ("circles", Circle)];

    [Fact]
    public void Triangulations_of_random_point_sets_are_Delaunay_triangulations()
    {
        foreach (var (name, gen) in Families)
        {
            gen.Array[3, 40].Sample(raw =>
            {
                var points = Distinct(raw);
                foreach (bool radial in new[] { true, false })
                {
                    var mesh = DelaunayMesh.Create(points, radial);
                    if (mesh is null)
                    {
                        Assert.True(points.Length < 3 || points.Skip(2).All(c => Orientation.Orient2D(points[0], points[1], c) == 0)
                            || AllCollinear(points), $"{name}: no mesh for non-collinear points");
                        continue;
                    }

                    Verify(mesh, $"{name} radial={radial}: " + string.Join(" ", points.Select(c => $"({c.X:R},{c.Y:R})")));
                }
            }, iter: 600);
        }
    }

    private static bool AllCollinear(Coordinate[] points)
    {
        for (int i = 2; i < points.Length; i++)
        {
            if (Orientation.Orient2D(points[0], points[1], points[i]) != 0) return false;
        }

        return true;
    }

    [Fact]
    public void Degenerate_inputs_give_no_mesh_or_the_single_triangle()
    {
        Assert.Null(DelaunayMesh.Create([]));
        Assert.Null(DelaunayMesh.Create([new Coordinate(0, 0), new Coordinate(1, 1)]));
        Assert.Null(DelaunayMesh.Create([new Coordinate(0, 0), new Coordinate(1, 1), new Coordinate(2, 2), new Coordinate(5, 5)]));

        var triangle = DelaunayMesh.Create([new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(0, 3)])!;
        Assert.Equal(1, triangle.TriangleCount);
        Verify(triangle, "triangle");
    }

    [Fact]
    public void Extreme_coordinates_are_triangulated_exactly()
    {
        // huge and tiny magnitudes and almost cocircular points: the incircle test must be exact
        var points = new[]
        {
            new Coordinate(1e150, 1e150), new Coordinate(-1e150, 1e150), new Coordinate(-1e150, -1e150), new Coordinate(1e150, -1e150),
            new Coordinate(0, 0), new Coordinate(1e-150, 1e-150), new Coordinate(3e149, -2e149),
        };
        Verify(DelaunayMesh.Create(points)!, "extreme");

        double e = 1e-12;
        var nearCircle = Enumerable.Range(0, 24).Select(i => new Coordinate(
            1000 * Math.Cos(2 * Math.PI * i / 24) + e * (i % 3), 1000 * Math.Sin(2 * Math.PI * i / 24) - e * (i % 2))).ToArray();
        Verify(DelaunayMesh.Create(nearCircle)!, "almost cocircular");
    }

    [Fact]
    public void The_exact_incircle_test_agrees_with_a_brute_force_rational_computation()
    {
        Gen.Select(Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[-20, 20])
            .Sample((ax, ay, bx, by, cx, cy, dx, dy) =>
            {
                var (a, b, c, d) = (new Coordinate(ax, ay), new Coordinate(bx, by), new Coordinate(cx, cy), new Coordinate(dx, dy));
                if (Orientation.Orient2D(a, b, c) <= 0) return;

                // integers: the determinant in long arithmetic is exact
                long A = (ax - dx) * (ax - dx) + (ay - dy) * (ay - dy), B = (bx - dx) * (bx - dx) + (by - dy) * (by - dy), C = (cx - dx) * (cx - dx) + (cy - dy) * (cy - dy);
                long det = A * ((bx - dx) * (cy - dy) - (cx - dx) * (by - dy))
                    + B * ((cx - dx) * (ay - dy) - (ax - dx) * (cy - dy))
                    + C * ((ax - dx) * (by - dy) - (bx - dx) * (ay - dy));

                Assert.Equal(Math.Sign(det), InCircle.Sign(a, b, c, d));
            }, iter: 20_000);
    }

    [Fact]
    public void Large_inputs_run_in_reasonable_time_and_are_valid()
    {
        var random = new Random(7);
        var points = Distinct(Enumerable.Range(0, 20_000).Select(_ => new Coordinate(random.NextDouble() * 1000, random.NextDouble() * 1000)));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var mesh = DelaunayMesh.Create(points)!;

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"{watch.Elapsed}");
        Assert.Equal(2 * points.Length - 2 - mesh.Hull().Length, mesh.TriangleCount);
        // the empty-circle property on a sample of the triangles
        for (int t = 0; t < mesh.TriangleCount; t += 97)
        {
            var (a, b, c) = (points[mesh.Triangles[3 * t]], points[mesh.Triangles[3 * t + 1]], points[mesh.Triangles[3 * t + 2]]);
            foreach (var p in points.Where((_, k) => k % 7 == 0)) Assert.True(InCircle.Sign(a, b, c, p) <= 0);
        }
    }
}
