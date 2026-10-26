using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>Quality meshes (ADR 0041): the smallest angle, and refining a triangulation by Delaunay refinement.</summary>
public class RefineTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[1, 99], Gen.Double[1, 99]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    /// <summary>The square (0 0)-(100 100) with scattered points, on the plane z = ax + by + 7; the hull is the square, whose angles are 90 degrees.</summary>
    private static Triangulation Square(Coordinate[] scattered, double a = 0.5, double b = -0.25)
    {
        var corners = new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var all = scattered.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = a * p.X + b * p.Y + 7 }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(all, null));
    }

    private static double Area(Triangulation t) => t.ToPolygons(F).Sum(p => p.Area());

    // ---------- MinAngle ----------

    [Fact]
    [Covers("MinAngle")]
    public void The_smallest_angle_of_known_triangles()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (5 8.660254037844386))"));
        var right = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10))"));
        var sliver = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (5 0.01))"));

        Assert.Equal(60, t.MinAngle(0), 1e-9);
        Assert.Equal(45, right.MinAngle(), 1e-9);
        Assert.True(sliver.MinAngle() < 0.2);
        Assert.True(double.IsNaN(Triangulation.FromPoints(G("MULTIPOINT ((0 0), (1 1))")).MinAngle()));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.MinAngle(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.MinAngle(-1));
    }

    // ---------- Refine ----------

    [Fact]
    [Covers("Refine")]
    public void Refining_a_square_with_a_sharp_triangle_removes_every_angle_below_the_limit()
    {
        var t = Square([new(50, 1), new(10, 3), new(77, 98)]);

        var fine = t.Refine(25);

        Assert.True(t.MinAngle() < 25);
        Assert.True(fine.MinAngle() >= 25 - 1e-9, $"{fine.MinAngle()}");
        Assert.Equal(10000, Area(fine), 1e-6);
    }

    [Fact]
    [Covers("Refine")]
    public void Refined_random_surfaces_keep_their_vertices_and_their_surface_and_have_no_small_angles()
    {
        Pt.Array[3, 40].Sample(points =>
        {
            var t = Square(points);

            var fine = t.Refine(20);

            Assert.True(fine.MinAngle() >= 20 - 1e-7, $"{fine.MinAngle()}");
            Assert.Equal(10000, Area(fine), 1e-5);
            var vertices = fine.Vertices.Select(v => (v.X, v.Y)).ToHashSet();
            Assert.All(t.Vertices, v => Assert.Contains((v.X, v.Y), vertices));
            Assert.All(fine.Vertices, v => Assert.True(Math.Abs(v.Z - (0.5 * v.X - 0.25 * v.Y + 7)) <= 1e-6, $"{v}"));
        }, iter: 100);
    }

    [Fact]
    [Covers("Refine")]
    public void The_largest_area_is_respected()
    {
        var t = Square([new(50, 50)]);

        var fine = t.Refine(minAngle: 0, maxArea: 400);

        for (int i = 0; i < fine.TriangleCount; i++)
        {
            var (a, b, c) = fine.Triangle(i);
            var (p, q, r) = (fine.Vertices[a], fine.Vertices[b], fine.Vertices[c]);
            Assert.True(Math.Abs((q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y)) / 2 <= 400 + 1e-9);
        }

        Assert.Equal(10000, Area(fine), 1e-6);
        Assert.True(fine.TriangleCount >= 25);
    }

    [Fact]
    [Covers("Refine")]
    public void A_break_line_stays_a_chain_of_constrained_edges_on_the_line()
    {
        var points = G("MULTIPOINT Z ((0 0 0), (100 0 0), (100 100 0), (0 100 0), (20 60 5))");
        var t = Triangulation.FromPointsAndLines(points, G("LINESTRING Z (10 10 0, 90 40 0)"));

        var fine = t.Refine(22);

        double constrained = 0;
        for (int i = 0; i < fine.TriangleCount; i++)
        {
            var (a, b, c) = fine.Triangle(i);
            int[] ids = [a, b, c];
            for (int k = 0; k < 3; k++)
            {
                if (!fine.IsConstrained(i, k))
                {
                    continue;
                }

                var u = fine.Vertices[ids[k]];
                var v = fine.Vertices[ids[(k + 1) % 3]];
                Assert.True(Math.Abs((v.X - 10) * 30 - (v.Y - 10) * 80) < 1e-6 && Math.Abs((u.X - 10) * 30 - (u.Y - 10) * 80) < 1e-6, $"{u} {v}");
                constrained += Math.Sqrt((v.X - u.X) * (v.X - u.X) + (v.Y - u.Y) * (v.Y - u.Y)) / 2;   // every edge is seen from two triangles
            }
        }

        Assert.Equal(Math.Sqrt(80 * 80 + 30 * 30), constrained, 1e-6);
        Assert.True(fine.MinAngle() >= 22 - 1e-7, $"{fine.MinAngle()}");
        Assert.Equal(10000, Area(fine), 1e-5);
    }

    [Fact]
    [Covers("Refine")]
    public void A_polygon_keeps_its_shape_and_its_border()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 40 0, 40 10, 10 10, 10 40, 0 40, 0 0))");
        var t = Triangulation.FromPolygon(polygon);

        var fine = t.Refine(20, maxArea: 20);

        Assert.Equal(polygon.Area(), Area(fine), 1e-6);
        Assert.True(fine.MinAngle() >= 20 - 1e-7);
        for (int i = 0; i < fine.TriangleCount; i++)
        {
            var (p, q, r) = fine.Triangle(i);
            var (u, v, w) = (fine.Vertices[p], fine.Vertices[q], fine.Vertices[r]);
            Assert.Equal(Location.Inside, polygon.Locate(new Coordinate((u.X + v.X + w.X) / 3, (u.Y + v.Y + w.Y) / 3)));
        }

        for (int i = 0; i < fine.TriangleCount; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                Assert.True(fine.Neighbour(i, k) >= 0 || fine.IsConstrained(i, k), "a border edge of a polygon is a constraint");
            }
        }
    }

    [Fact]
    [Covers("Refine")]
    public void A_triangulation_that_needs_nothing_is_returned_as_it_is()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (5 8.660254037844386))"));

        Assert.Same(t, t.Refine());

        var empty = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (1 1))"));
        Assert.Same(empty, empty.Refine());
    }

    [Fact]
    [Covers("Refine")]
    public void The_number_of_added_points_is_limited_and_bad_arguments_are_rejected()
    {
        var t = Square([new(50, 1), new(10, 3)]);

        var limited = t.Refine(30, maxSteinerPoints: 3);

        Assert.True(limited.Vertices.Count <= t.Vertices.Count + 3 + 3);   // the limit is checked per round, which may overshoot a little
        Assert.Same(t, t.Refine(30, maxSteinerPoints: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(35));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(20, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(20, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Refine(20, null, -1));
    }
}
