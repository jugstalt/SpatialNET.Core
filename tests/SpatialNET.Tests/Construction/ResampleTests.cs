using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>A smoother surface: the heights of a grid by natural neighbour interpolation added to the vertices (ADR 0043).</summary>
public class ResampleTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[1, 99], Gen.Double[1, 99]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    /// <summary>The square (0 0)-(100 100) with scattered points and the heights of the function.</summary>
    private static Triangulation Square(IEnumerable<Coordinate> scattered, Func<double, double, double> height)
    {
        var corners = new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var all = scattered.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = height(p.X, p.Y) }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(all, null));
    }

    private static double Area(Triangulation t) => t.ToPolygons(F).Sum(p => p.Area());

    [Fact]
    [Covers("Resample")]
    public void A_plane_stays_a_plane_and_the_border_and_the_area_stay()
    {
        Gen.Select(Pt.Array[3, 30], Gen.Double[-2, 2].Select(d => Math.Round(d, 3)), Gen.Double[-2, 2].Select(d => Math.Round(d, 3))).Sample((points, a, b) =>
        {
            var t = Square(points, (x, y) => a * x + b * y + 7);

            var fine = t.Resample(10);
            var kept = t.Resample(10, keepVertices: true);

            Assert.Equal(Area(t), Area(fine), 1e-6 * Area(t));
            Assert.Equal(Area(t), Area(kept), 1e-6 * Area(t));
            var vertices = fine.Vertices.Select(v => (v.X, v.Y)).ToHashSet();
            Assert.All(new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) }, v => Assert.Contains((v.X, v.Y), vertices));   // the border stays
            var all = kept.Vertices.Select(v => (v.X, v.Y)).ToHashSet();
            Assert.All(t.Vertices, v => Assert.Contains((v.X, v.Y), all));                                                                     // so do the inner ones, if asked
            Assert.All(fine.Vertices.Concat(kept.Vertices), v => Assert.True(Math.Abs(v.Z - (a * v.X + b * v.Y + 7)) <= 1e-6, $"{v}"));
        }, iter: 100);
    }

    [Fact]
    [Covers("Resample")]
    public void The_grid_points_are_multiples_of_the_cell_size_and_the_result_does_not_depend_on_the_order_of_the_threads()
    {
        var t = Square([new(33.3, 44.4), new(70.2, 12.9), new(15.5, 81.7)], (x, y) => x * 0.1 + y * 0.05);

        var first = t.Resample(7);
        var second = t.Resample(7);

        Assert.Equal(first.Vertices.Count, second.Vertices.Count);
        Assert.Equal(first.TriangleCount, second.TriangleCount);
        var original = t.Vertices.Select(v => (v.X, v.Y)).ToHashSet();
        Assert.All(first.Vertices.Where(v => !original.Contains((v.X, v.Y))), v =>
        {
            Assert.Equal(0, v.X % 7, 1e-9);
            Assert.Equal(0, v.Y % 7, 1e-9);
        });
    }

    [Fact]
    [Covers("Resample")]
    public void The_contour_line_of_a_smooth_function_turns_less_on_the_finer_surface_and_without_the_measurements()
    {
        // z = x² + y² over scattered points: the contour at 900 is the circle of radius 30
        var random = new Random(21);
        var points = Enumerable.Range(0, 120).Select(_ => new Coordinate(Math.Round(random.NextDouble() * 100 - 50, 3), Math.Round(random.NextDouble() * 100 - 50, 3)));
        var corners = new Coordinate[] { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) };
        var all = points.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = p.X * p.X + p.Y * p.Y }).ToArray();
        var t = Triangulation.FromPoints(F.CreateMultiPoint(all, null));

        // the largest change of direction at a vertex of the contour line, in degrees, and the mean of them
        (double Mean, double Worst) Turns(Triangulation surface)
        {
            var contour = surface.Contour(900, F);
            double sum = 0, worst = 0;
            int count = 0;
            for (int p = 0; p < contour.PathCount; p++)
            {
                var path = Internal.Rings.Coordinates(contour[p]);
                for (int i = 1; i + 1 < path.Length; i++)
                {
                    double a1 = Math.Atan2(path[i].Y - path[i - 1].Y, path[i].X - path[i - 1].X), a2 = Math.Atan2(path[i + 1].Y - path[i].Y, path[i + 1].X - path[i].X);
                    double turn = Math.Abs(Math.IEEERemainder(a2 - a1, 2 * Math.PI)) * 180 / Math.PI;
                    sum += turn;
                    worst = Math.Max(worst, turn);
                    count++;
                }
            }

            return (sum / count, worst);
        }

        var linear = Turns(t);
        var smooth = Turns(t.Resample(4));
        var withVertices = Turns(t.Resample(4, keepVertices: true));

        // the measurements are points of the interpolated surface, and stay as kinks of the line; without them it is smoother
        Assert.True(smooth.Mean < linear.Mean, $"{smooth.Mean} against {linear.Mean}");
        Assert.True(smooth.Mean < withVertices.Mean, $"{smooth.Mean} against {withVertices.Mean}");
    }

    [Fact]
    [Covers("Resample")]
    public void Contour_lines_of_different_heights_of_the_finer_surface_do_not_touch()
    {
        Gen.Select(Pt.Array[5, 40], Gen.Double[-30, 30].Select(d => Math.Round(d, 2)).Array[44]).Sample((points, heights) =>
        {
            int i = 0;
            var t = Square(points, (_, _) => heights[i++ % heights.Length]);

            var fine = t.Resample(12);
            var lines = new[] { -10.0, 0.5, 11.5, 20.5 }.Select(level => fine.Contour(level, F)).ToArray();

            for (int a = 0; a < lines.Length; a++)
            {
                for (int b = a + 1; b < lines.Length; b++)
                {
                    Assert.False(lines[a].Intersects(lines[b]), $"the lines of the heights {a} and {b} touch");
                }
            }
        }, iter: 50);
    }

    [Fact]
    [Covers("Resample")]
    public void A_polygon_keeps_its_shape_and_its_border()
    {
        var polygon = (IPolygon2D)G("POLYGON Z ((0 0 0, 40 0 10, 40 10 20, 10 10 30, 10 40 40, 0 40 50, 0 0 0))");
        var t = Triangulation.FromPolygon(polygon);

        var fine = t.Resample(3);

        Assert.True(fine.TriangleCount > t.TriangleCount);
        Assert.Equal(polygon.Area(), Area(fine), 1e-6);
        for (int i = 0; i < fine.TriangleCount; i++)
        {
            var (a, b, c) = fine.Triangle(i);
            var (u, v, w) = (fine.Vertices[a], fine.Vertices[b], fine.Vertices[c]);
            Assert.Equal(Location.Inside, polygon.Locate(new Coordinate((u.X + v.X + w.X) / 3, (u.Y + v.Y + w.Y) / 3)));
            for (int k = 0; k < 3; k++)
            {
                Assert.True(fine.Neighbour(i, k) >= 0 || fine.IsConstrained(i, k), "a border edge of a polygon is a constraint");
            }
        }
    }

    [Fact]
    [Covers("Resample")]
    public void A_break_line_stays_a_chain_of_constrained_edges_on_the_line()
    {
        var points = G("MULTIPOINT Z ((0 0 0), (100 0 0), (100 100 0), (0 100 0), (20 60 5), (70 30 8))");
        var t = Triangulation.FromPointsAndLines(points, G("LINESTRING Z (10 10 0, 90 40 0)"));

        var fine = t.Resample(9);

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
                Assert.True(Math.Abs((v.X - 10) * 30 - (v.Y - 10) * 80) < 1e-6 && Math.Abs((u.X - 10) * 30 - (u.Y - 10) * 80) < 1e-6);
                constrained += Math.Sqrt((v.X - u.X) * (v.X - u.X) + (v.Y - u.Y) * (v.Y - u.Y)) / 2;
            }
        }

        Assert.Equal(Math.Sqrt(80 * 80 + 30 * 30), constrained, 1e-6);
        Assert.True(fine.Vertices.Count > t.Vertices.Count);
        Assert.Equal(10000, Area(fine), 1e-5);
    }

    [Fact]
    [Covers("Resample")]
    public void Vertices_without_Z_give_points_without_Z_and_a_cell_larger_than_the_surface_changes_nothing()
    {
        var flat = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (40 0), (40 40), (0 40), (20 15))"));
        var small = Triangulation.FromPoints(G("MULTIPOINT Z ((10 10 1), (20 10 2), (10 20 3))"));

        var fine = flat.Resample(10);

        Assert.True(fine.Vertices.Count > flat.Vertices.Count);
        Assert.All(fine.Vertices, v => Assert.False(v.HasZ));
        Assert.Same(small, small.Resample(1000));
        var empty = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (1 1))"));
        Assert.Same(empty, empty.Resample(1));
    }

    [Fact]
    [Covers("Resample")]
    public void A_cell_size_that_is_not_finite_not_positive_or_far_too_small_is_rejected()
    {
        var t = Square([new(50, 50)], (x, y) => x + y);

        Assert.Throws<ArgumentOutOfRangeException>(() => t.Resample(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Resample(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Resample(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Resample(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Resample(1e-3));
    }
}
