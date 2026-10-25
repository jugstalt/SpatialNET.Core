using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// <c>LocateFeature</c> (ADR 0034): the ranges of M along a route where a line or an area lies. NTS has no counterpart, so the
/// cases are hand-written; M is exact to about the grid of the overlay, hence the small deltas.
/// </summary>
[Covers("LocateFeature")]
public class LocateFeatureTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private const double Delta = 1e-6;

    private static Coordinate C(double x, double y, double m = double.NaN) => new(x, y, double.NaN, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    private static MPolygon Box(double x0, double y0, double x1, double y1) =>
        new([[C(x0, y0), C(x1, y0), C(x1, y1), C(x0, y1), C(x0, y0)]]);

    // a straight road along y = 0 with M = 10 * x
    private static MPolyline Road() => Line(C(0, 0, 0), C(100, 0, 1000));

    private static void AssertRanges((double From, double To)[] expected, IReadOnlyList<(double From, double To)> actual, double delta = Delta)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].From, actual[i].From, delta);
            Assert.Equal(expected[i].To, actual[i].To, delta);
        }
    }

    [Fact]
    public void An_area_crossing_the_road_gives_the_range_it_covers()
    {
        var ranges = Road().LocateFeature(Box(30, -5, 60, 5), 0, ZM);

        AssertRanges([(300, 600)], ranges);
    }

    [Fact]
    public void An_area_that_shares_the_edge_with_the_road_counts_with_tolerance_0()
    {
        var ranges = Road().LocateFeature(Box(20, 0, 50, 10), 0, ZM);

        AssertRanges([(200, 500)], ranges);
    }

    [Fact]
    public void A_tolerance_reaches_an_area_next_to_the_road()
    {
        var parcel = Box(20, 3, 50, 10);

        Assert.Empty(Road().LocateFeature(parcel, 0, ZM));
        Assert.Empty(Road().LocateFeature(parcel, 2.5, ZM));
        // 4 m reach 1 m into the road; the rounded corners add about 2.65 m on each side
        AssertRanges([(173.5, 526.5)], Road().LocateFeature(parcel, 4, ZM), delta: 1.5);
    }

    [Fact]
    public void A_line_next_to_the_road_needs_a_tolerance()
    {
        var pipe = Line(C(10, 3), C(40, 3));

        AssertRanges([(60, 440)], Road().LocateFeature(pipe, 5, ZM), delta: 1.5);   // sqrt(5² - 3²) = 4 m beyond both ends
        Assert.Throws<ArgumentOutOfRangeException>(() => Road().LocateFeature(pipe, 0, ZM));
    }

    [Fact]
    public void A_route_that_passes_twice_gives_one_range_per_pass_if_they_are_apart_in_M()
    {
        // east along y = 0 (M 0..100), a bend, back west along y = 10 (M 110..210)
        var route = Line(C(0, 0, 0), C(100, 0, 100), C(100, 10, 110), C(0, 10, 210));

        var ranges = route.LocateFeature(Box(40, -1, 60, 11), 0, ZM);

        AssertRanges([(40, 60), (150, 170)], ranges);
    }

    [Fact]
    public void Pieces_that_touch_are_joined()
    {
        var route = Line(C(0, 0, 0), C(10, 0, 10), C(20, 0, 20));

        AssertRanges([(5, 15)], route.LocateFeature(Box(5, -1, 15, 1), 0, ZM));
    }

    [Fact]
    public void M_may_fall_along_the_route()
    {
        var route = Line(C(0, 0, 1000), C(100, 0, 0));

        AssertRanges([(400, 700)], route.LocateFeature(Box(30, -1, 60, 1), 0, ZM));
    }

    [Fact]
    public void Stretches_without_M_are_left_out()
    {
        var route = Line(C(0, 0, 0), C(10, 0, 10), C(20, 0), C(30, 0, 30), C(40, 0, 40));

        var ranges = route.LocateFeature(Box(-1, -1, 41, 1), 0, ZM);

        AssertRanges([(0, 10), (30, 40)], ranges);
    }

    [Fact]
    public void A_touch_at_a_point_and_a_feature_far_away_give_nothing()
    {
        Assert.Empty(Road().LocateFeature(new MPolygon([[C(50, 0), C(60, 5), C(40, 5), C(50, 0)]]), 0, ZM));
        Assert.Empty(Road().LocateFeature(Box(0, 50, 10, 60), 0, ZM));
        Assert.Empty(Road().LocateFeature(new MPolygon([]), 0, ZM));
        Assert.Empty(new MPolyline([]).LocateFeature(Box(0, 0, 1, 1), 0, ZM));
    }

    [Fact]
    public void Points_negative_tolerance_and_a_factory_without_M_are_errors()
    {
        var road = Road();

        Assert.Throws<ArgumentException>(() => road.LocateFeature(new MPoint(C(50, 0)), 1, ZM));
        Assert.Throws<ArgumentException>(() => road.LocateFeature(new MMultiPoint([C(50, 0)]), 1, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.LocateFeature(Box(0, 0, 1, 1), -1, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.LocateFeature(Box(0, 0, 1, 1), double.NaN, ZM));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).LocateFeature(Box(0, 0, 1, 1), 0, ZM));
        Assert.Throws<ArgumentNullException>(() => road.LocateFeature(null!, 0, ZM));
        Assert.Throws<InvalidOperationException>(() => road.LocateFeature(Box(0, 0, 1, 1), 0, new MFactory(true, false)));
    }

    [Fact]
    public void The_ranges_can_be_used_as_events()
    {
        var parcelRange = Road().LocateFeature(Box(30, -5, 60, 5), 0, ZM).Select(r => new LineEvent<string>(r.From, r.To, "parcel"));
        var speed = new[] { new LineEvent<int>(0, 450, 50), new LineEvent<int>(450, 1000, 70) };

        var both = LineEvents.Intersect(speed, parcelRange, (s, p) => s);

        Assert.Equal([(300d, 450d, 50), (450d, 600d, 70)], both.Select(e => (Math.Round(e.From, 4), Math.Round(e.To, 4), e.Value)));
    }
}
