using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// <c>Project</c> and <c>ProjectToM</c> with a window of lengths (ADR 0031). NTS has only <c>IndexOfAfter</c>, which
/// falls back to the whole line, so the cases are hand-written and checked against a brute-force scan.
/// </summary>
[Covers("Project")]
[Covers("ProjectToM")]
public class ProjectWindowTests
{
    private const double Tolerance = 1e-9;

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    // A road that drives east along y = 0, turns around and comes back along y = 1 (length 10 + 1 + 10).
    private static MPolyline Hairpin() =>
        Line(C(0, 0, m: 0), C(10, 0, m: 10), C(10, 1, m: 11), C(0, 1, m: 21));

    [Fact]
    public void Tells_the_two_passes_of_a_hairpin_apart()
    {
        var road = Hairpin();
        var click = C(4, 0.4); // nearer to the outward pass

        Assert.Equal(4, road.Project(click), Tolerance);
        Assert.Equal(4, road.Project(click, 0, 10), Tolerance);
        Assert.Equal(17, road.Project(click, 11, 21), Tolerance); // the return pass: (4 1) is at length 11 + 6
    }

    [Fact]
    public void ProjectToM_follows_the_window()
    {
        var road = Hairpin();
        var click = C(4, 0.4);

        Assert.Equal(4, road.ProjectToM(click, 0, 10), Tolerance);
        Assert.Equal(17, road.ProjectToM(click, 11, 21), Tolerance);
    }

    [Fact]
    public void A_window_over_the_whole_line_is_the_same_as_no_window()
    {
        var road = Hairpin();

        foreach (var p in new[] { C(4, 0.4), C(-3, -3), C(12, 0.5), C(5, 1.2) })
        {
            Assert.Equal(road.Project(p), road.Project(p, 0, 21), 0);
            Assert.Equal(road.Project(p), road.Project(p, -100, 100), 0);
        }
    }

    [Fact]
    public void The_result_stays_within_the_window_even_if_the_point_is_nearer_outside()
    {
        var road = Line(C(0, 0), C(10, 0));

        Assert.Equal(6, road.Project(C(2, 0), 6, 8), Tolerance); // nearest point of the window is its start
        Assert.Equal(8, road.Project(C(9, 0), 6, 8), Tolerance); // ... or its end
        Assert.Equal(7, road.Project(C(7, 3), 6, 8), Tolerance); // inside, as without a window
    }

    [Fact]
    public void A_window_inside_a_segment_cuts_the_segment()
    {
        var road = Line(C(0, 0), C(10, 0), C(10, 10));

        Assert.Equal(12, road.Project(C(14, 12), 5, 12), Tolerance); // end of the window, on the second segment
        Assert.Equal(5, road.Project(C(2, 1), 5, 12), Tolerance);
    }

    [Fact]
    public void An_empty_window_gives_its_position_and_swapped_bounds_are_the_same_window()
    {
        var road = Line(C(0, 0), C(10, 0));

        Assert.Equal(3, road.Project(C(8, 2), 3, 3), Tolerance);
        Assert.Equal(road.Project(C(8, 2), 2, 6), road.Project(C(8, 2), 6, 2), 0);
    }

    [Fact]
    public void Matches_a_brute_force_scan_over_a_dense_line()
    {
        var road = Hairpin();
        var rng = new Random(7);
        for (int n = 0; n < 200; n++)
        {
            var p = C(rng.NextDouble() * 14 - 2, rng.NextDouble() * 4 - 1.5);
            double a = rng.NextDouble() * 21, b = rng.NextDouble() * 21;
            double from = Math.Min(a, b), to = Math.Max(a, b);

            double best = double.PositiveInfinity, bestPos = double.NaN;
            for (double d = from; d <= to + 1e-12; d += 0.001)
            {
                var q = road.Interpolate(Math.Min(d, to))!.Value;
                double dist = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                if (dist < best - 1e-12)
                {
                    best = dist;
                    bestPos = Math.Min(d, to);
                }
            }

            double found = road.Project(p, from, to);
            Assert.InRange(found, from - 1e-9, to + 1e-9);
            var f = road.Interpolate(found)!.Value;
            double foundDist = Math.Sqrt((f.X - p.X) * (f.X - p.X) + (f.Y - p.Y) * (f.Y - p.Y));
            Assert.True(foundDist <= best + 1e-9, $"{p} [{from}, {to}]: {foundDist} vs {best} at {bestPos}");
        }
    }

    [Fact]
    public void Works_across_paths_and_on_single_point_paths()
    {
        var road = new MPolyline([[C(0, 0), C(10, 0)], [C(50, 5)], [C(20, 0), C(30, 0)]]);

        // lengths: path 1 is 0..10, the single point is at 10, path 3 is 10..20
        Assert.Equal(14, road.Project(C(24, 3), 10, 20), Tolerance);
        Assert.Equal(10, road.Project(C(50, 5), 10, 10), Tolerance);
        Assert.Equal(5, road.Project(C(5, 2), 0, 9), Tolerance);
    }

    [Fact]
    public void Empty_lines_and_bad_input()
    {
        Assert.True(double.IsNaN(new MPolyline([]).Project(C(1, 1), 0, 5)));
        Assert.True(double.IsNaN(new MPolyline([]).ProjectToM(C(1, 1), 0, 5)));

        var road = Line(C(0, 0), C(10, 0));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Project(C(0, 0), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.Project(C(double.NaN, 0), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.Project(C(0, 0), double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.Project(C(0, 0), 0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.ProjectToM(C(0, 0), 0, double.NaN));
    }

    [Fact]
    public void The_IPoint2D_overloads_convert()
    {
        var road = Hairpin();
        var click = new MPoint(C(4, 0.4));

        Assert.Equal(17, road.Project(click, 11, 21), Tolerance);
        Assert.Equal(17, road.ProjectToM(click, 11, 21), Tolerance);
    }
}
