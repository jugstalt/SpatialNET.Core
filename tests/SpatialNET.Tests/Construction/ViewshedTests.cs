using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>What can be seen from a point: the visible part of the surface as a polygon (ADR 0044).</summary>
public class ViewshedTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[1, 99], Gen.Double[1, 99]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    private static Triangulation Square(IEnumerable<Coordinate> scattered, Func<double, double, double> height)
    {
        var corners = new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var all = scattered.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = height(p.X, p.Y) }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(all, null));
    }

    /// <summary>A ridge across the square at x = 50: height 10 on the crest, 0 on both sides, for an observer at (10 50).</summary>
    private static Triangulation Ridge() =>
        Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (100 0 0), (100 100 0), (0 100 0), (50 0 10), (50 25 10), (50 50 10), (50 75 10), (50 100 10))"));

    [Fact]
    [Covers("Viewshed")]
    public void On_flat_ground_everything_within_the_distance_is_seen_a_disc_cut_by_the_edge_of_the_surface()
    {
        var flat = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 5), (200 0 5), (200 200 5), (0 200 5))"));

        var area = flat.Viewshed(new Coordinate(100, 100), 50, observerHeight: 1.7, rays: 360, factory: F);

        Assert.Equal(Math.PI * 50 * 50, area.Area(), 0.01 * Math.PI * 50 * 50);
        Assert.True(area.IsValid());
        Assert.Equal(Location.Inside, area.Locate(new Coordinate(100, 100)));
        Assert.Equal(Location.Outside, area.Locate(new Coordinate(100, 160)));
        // near the border of the surface the disc is cut: the observer at a corner sees a quarter
        var quarter = flat.Viewshed(new Coordinate(0, 0), 50, 1.7, rays: 720, factory: F);
        Assert.Equal(Math.PI * 50 * 50 / 4, quarter.Area(), 0.02 * Math.PI * 50 * 50 / 4);
    }

    [Fact]
    [Covers("Viewshed")]
    public void A_ridge_hides_what_lies_behind_it()
    {
        var t = Ridge();

        var area = t.Viewshed(new Coordinate(10, 50), 200, observerHeight: 1.7, rays: 720, factory: F);

        Assert.Equal(Location.Inside, area.Locate(new Coordinate(30, 50)));      // on the slope in front
        Assert.Equal(Location.Inside, area.Locate(new Coordinate(49, 20)));
        Assert.Equal(Location.Outside, area.Locate(new Coordinate(70, 50)));     // behind the crest
        Assert.Equal(Location.Outside, area.Locate(new Coordinate(90, 20)));
        Assert.Equal(5000, area.Area(), 0.03 * 5000);                            // the half of the square in front of the ridge
    }

    [Fact]
    [Covers("Viewshed")]
    public void A_higher_eye_or_target_sees_more_and_a_longer_distance_too()
    {
        var t = Ridge();
        var observer = new Coordinate(10, 50);

        double low = t.Viewshed(observer, 200, 1.7, 0, 360, F).Area();
        double high = t.Viewshed(observer, 200, 30, 0, 360, F).Area();
        double tall = t.Viewshed(observer, 200, 1.7, 20, 360, F).Area();
        double near = t.Viewshed(observer, 30, 1.7, 0, 360, F).Area();

        Assert.True(high >= low - 1e-6, $"{high} against {low}");
        Assert.True(tall >= low - 1e-6, $"{tall} against {low}");
        Assert.True(near <= low + 1e-6, $"{near} against {low}");
        Assert.True(high > 1.5 * low, "an eye above the crest sees the other side");
    }

    [Fact]
    [Covers("Viewshed")]
    public void The_visible_area_agrees_with_the_line_of_sight_to_random_points()
    {
        Gen.Select(Pt.Array[5, 40], Gen.Double[-15, 15].Select(d => Math.Round(d, 1)).Array[44], Pt, Pt.Array[40, 40]).Sample((points, heights, observer, targets) =>
        {
            int i = 0;
            var t = Square(points, (_, _) => heights[i++ % heights.Length]);
            var area = t.Viewshed(observer, 150, 1.7, 0.5, rays: 720, factory: F);

            int disagree = 0, counted = 0;
            foreach (var q in targets)
            {
                double distance = Math.Sqrt((q.X - observer.X) * (q.X - observer.X) + (q.Y - observer.Y) * (q.Y - observer.Y));
                if (distance < 2)
                {
                    continue;
                }

                counted++;
                bool seen = t.LineOfSight(observer, q, 1.7, 0.5);
                bool inside = area.Locate(q) != Location.Outside;
                if (seen != inside)
                {
                    disagree++;
                }
            }

            // between two rays the edge is approximate, so a target near the edge of the visible area may be on the other side
            Assert.True(disagree <= Math.Max(2, counted / 10), $"{disagree} of {counted} disagree");
        }, iter: 40);
    }

    [Fact]
    [Covers("Viewshed")]
    public void An_observer_outside_the_surface_or_without_height_sees_nothing()
    {
        var t = Ridge();
        var flat = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (100 0), (100 100), (0 100))"));

        Assert.Equal(0, t.Viewshed(new Coordinate(500, 50), 100, factory: F).RingCount);
        Assert.Equal(0, flat.Viewshed(new Coordinate(50, 50), 100, factory: F).RingCount);
        Assert.Equal(0, Triangulation.FromPoints(G("MULTIPOINT ((0 0), (1 1))")).Viewshed(new Coordinate(0, 0), 10, factory: F).RingCount);
    }

    [Fact]
    [Covers("Viewshed")]
    public void The_result_does_not_depend_on_the_order_of_the_threads_and_bad_arguments_are_rejected()
    {
        var t = Ridge();
        var observer = new Coordinate(10, 50);

        Assert.Equal(t.Viewshed(observer, 100, 1.7, 0, 90, F).ToWkt(), t.Viewshed(observer, 100, 1.7, 0, 90, F).ToWkt());
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(new Coordinate(double.NaN, 0), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, 10, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, 10, 0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, 10, 0, 0, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Viewshed(observer, 10, 0, 0, 100_001));
    }
}
