using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// <c>CalibrateM</c> (ADR 0030): M from anchor points onto a line. NTS has no counterpart, so the reference is the
/// formula that hosts used before: M interpolated linearly in the station between the previous and the next anchor.
/// </summary>
[Covers("CalibrateM")]
public class CalibrateMTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private const double Tolerance = 1e-9;

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    private static Coordinate[] Vertices(IPolyline2D line, int path = 0) =>
        Enumerable.Range(0, line[path].PointCount).Select(i => ToCoordinate(line[path][i])).ToArray();

    private static Coordinate ToCoordinate(IPoint2D p) =>
        new(p.X, p.Y, p is IPoint3D p3 ? p3.Z : double.NaN, p is IPointM pm ? pm.M : double.NaN);

    [Fact]
    public void Gives_the_result_of_the_station_formula_between_two_anchors()
    {
        // A road of length 30 with a bend; anchors at stations 5 (M 100) and 25 (M 200).
        var road = Line(C(0, 0), C(10, 0), C(10, 20));
        var anchors = new[] { C(5, 0, m: 100), C(10, 15, m: 200) };

        var calibrated = road.CalibrateM(anchors, 0.001, factory: ZM);

        foreach (var click in new[] { C(5, 0), C(8, 1), C(10, 4), C(10, 10), C(10, 15) })
        {
            double station = road.Project(click);
            double expected = 100 + (200 - 100d) / (25 - 5) * (station - 5);
            Assert.Equal(expected, calibrated.ProjectToM(click), Tolerance);
        }
    }

    [Fact]
    public void Inserts_a_vertex_with_M_at_every_anchor_and_keeps_the_shape()
    {
        var road = Line(C(0, 0), C(10, 0));

        var calibrated = road.CalibrateM([C(2, 0, m: 20), C(7, 0, m: 70)], 0, factory: ZM);

        var v = Vertices(calibrated);
        Assert.Equal([(0d, 0d), (2d, 0d), (7d, 0d), (10d, 0d)], v.Select(c => (c.X, c.Y)));
        Assert.Equal(20, v[1].M);
        Assert.Equal(70, v[2].M);
        Assert.False(v[0].HasM);   // before the first anchor: no M
        Assert.False(v[3].HasM);   // after the last anchor: no M
    }

    [Fact]
    public void Does_not_insert_a_vertex_where_an_anchor_lies_on_a_vertex()
    {
        var road = Line(C(0, 0), C(10, 0), C(10, 10));

        var calibrated = road.CalibrateM([C(0, 0, m: 0), C(10, 0, m: 10), C(10, 10, m: 20)], 0, factory: ZM);

        Assert.Equal([0d, 10d, 20d], Vertices(calibrated).Select(v => v.M));
    }

    [Fact]
    public void Snaps_anchors_next_to_the_line_and_places_the_vertex_on_the_line()
    {
        var road = Line(C(0, 0), C(10, 0));

        var calibrated = road.CalibrateM([C(3, 0.5, m: 3), C(8, -0.4, m: 8)], 0.5, factory: ZM);

        var v = Vertices(calibrated);
        Assert.Equal((3d, 0d, 3d), (v[1].X, v[1].Y, v[1].M));
        Assert.Equal((8d, 0d, 8d), (v[2].X, v[2].Y, v[2].M));
    }

    [Fact]
    public void Throws_for_an_anchor_beyond_the_tolerance()
    {
        var road = Line(C(0, 0), C(10, 0));

        var ex = Assert.Throws<ArgumentException>(() => road.CalibrateM([C(3, 0, m: 3), C(8, 2, m: 8)], 1, factory: ZM));

        Assert.Contains("Anchor 1", ex.Message);
    }

    [Fact]
    public void Accepts_anchors_in_any_order_and_non_monotonic_M()
    {
        var road = Line(C(0, 0), C(30, 0));

        var calibrated = road.CalibrateM([C(20, 0, m: 5), C(10, 0, m: 50), C(0, 0, m: 0)], 0, factory: ZM);

        Assert.Equal(25, calibrated.ProjectToM(C(5, 0)), Tolerance);
        Assert.Equal(27.5, calibrated.ProjectToM(C(15, 0)), Tolerance);
    }

    [Fact]
    public void Merges_anchors_at_the_same_position_with_equal_M_and_throws_for_different_M()
    {
        var road = Line(C(0, 0), C(10, 0));

        var calibrated = road.CalibrateM([C(5, 0, m: 7), C(5, 0.1, m: 7), C(0, 0, m: 0)], 1, factory: ZM);
        Assert.Equal(3, Vertices(calibrated).Length);

        Assert.Throws<ArgumentException>(() => road.CalibrateM([C(5, 0, m: 7), C(5, 0, m: 8)], 0, factory: ZM));
    }

    [Fact]
    public void Extrapolation_None_leaves_the_ends_without_M()
    {
        var road = Line(C(0, 0), C(10, 0), C(20, 0));

        var calibrated = road.CalibrateM([C(5, 0, m: 50), C(15, 0, m: 150)], 0, MExtrapolation.None, ZM);

        Assert.True(double.IsNaN(calibrated.ProjectToM(C(2, 0))));
        Assert.True(double.IsNaN(calibrated.ProjectToM(C(18, 0))));
        Assert.Equal(100, calibrated.ProjectToM(C(10, 0)), Tolerance);
    }

    [Fact]
    public void Extrapolation_Clamp_uses_the_M_of_the_nearest_anchor()
    {
        var road = Line(C(0, 0), C(10, 0), C(20, 0));

        var calibrated = road.CalibrateM([C(5, 0, m: 50), C(15, 0, m: 150)], 0, MExtrapolation.Clamp, ZM);

        Assert.Equal(50, calibrated.ProjectToM(C(2, 0)), Tolerance);
        Assert.Equal(150, calibrated.ProjectToM(C(20, 0)), Tolerance);
    }

    [Fact]
    public void Extrapolation_Linear_continues_the_slope_of_the_end_intervals()
    {
        var road = Line(C(0, 0), C(10, 0), C(20, 0));

        var calibrated = road.CalibrateM([C(5, 0, m: 50), C(15, 0, m: 150)], 0, MExtrapolation.Linear, ZM);

        Assert.Equal(20, calibrated.ProjectToM(C(2, 0)), Tolerance);
        Assert.Equal(200, calibrated.ProjectToM(C(20, 0)), Tolerance);
    }

    [Fact]
    public void Linear_extrapolation_needs_two_positions()
    {
        var road = Line(C(0, 0), C(10, 0));

        Assert.Throws<ArgumentException>(() => road.CalibrateM([C(5, 0, m: 50)], 0, MExtrapolation.Linear, ZM));
        Assert.Throws<ArgumentException>(() => road.CalibrateM([C(5, 0, m: 50), C(5, 0, m: 50)], 0, MExtrapolation.Linear, ZM));
    }

    [Fact]
    public void A_single_anchor_gives_M_at_its_own_position_only()
    {
        var road = Line(C(0, 0), C(10, 0));

        var none = road.CalibrateM([C(5, 0, m: 50)], 0, factory: ZM);
        var clamp = road.CalibrateM([C(5, 0, m: 50)], 0, MExtrapolation.Clamp, ZM);

        Assert.Equal(50, none.ProjectToM(C(5, 0)), Tolerance);
        Assert.True(double.IsNaN(none.ProjectToM(C(1, 0))));
        Assert.Equal(50, clamp.ProjectToM(C(1, 0)), Tolerance);
        Assert.Equal(50, clamp.ProjectToM(C(10, 0)), Tolerance);
    }

    [Fact]
    public void Keeps_Z_and_interpolates_it_at_inserted_vertices_and_ignores_the_M_of_the_line()
    {
        var road = Line(C(0, 0, 0, 999), C(10, 0, 10, 999));

        var calibrated = road.CalibrateM([C(5, 0, m: 1), C(10, 0, m: 2)], 0, factory: ZM);

        var v = Vertices(calibrated);
        Assert.Equal((5d, 5d, 1d), (v[1].X, v[1].Z, v[1].M));
        Assert.Equal(0, v[0].Z);
        Assert.False(v[0].HasM); // the old M is gone
    }

    [Fact]
    public void Lays_multiple_paths_end_to_end()
    {
        var road = new MPolyline([[C(0, 0), C(10, 0)], [C(10, 0), C(10, 10)]]);

        var calibrated = road.CalibrateM([C(0, 0, m: 0), C(10, 5, m: 15)], 0, factory: ZM);

        Assert.Equal(2, calibrated.PathCount);
        Assert.Equal(10, calibrated.ProjectToM(C(10, 0)), Tolerance);
        Assert.Equal(15, calibrated.ProjectToM(C(10, 5)), Tolerance);
        Assert.Equal(3, Vertices(calibrated, 1).Length); // the anchor in the second path is a new vertex
    }

    [Fact]
    public void Reads_anchors_from_points_that_carry_M()
    {
        var road = Line(C(0, 0), C(10, 0));
        IPoint2D[] anchors = [new MPoint(C(2, 0, m: 20)), new MPoint(C(8, 0, m: 80))];

        var calibrated = road.CalibrateM(anchors, 0, factory: ZM);

        Assert.Equal(50, calibrated.ProjectToM(C(5, 0)), Tolerance);
    }

    [Fact]
    public void Throws_for_bad_input_and_a_factory_without_M()
    {
        var road = Line(C(0, 0), C(10, 0));
        var ok = new[] { C(5, 0, m: 1) };

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).CalibrateM(ok, 0, factory: ZM));
        Assert.Throws<ArgumentNullException>(() => road.CalibrateM((IEnumerable<Coordinate>)null!, 0, factory: ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.CalibrateM(ok, -1, factory: ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.CalibrateM(ok, double.NaN, factory: ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => road.CalibrateM([C(double.NaN, 0, m: 1)], 1, factory: ZM));
        Assert.Throws<ArgumentException>(() => road.CalibrateM([C(5, 0)], 1, factory: ZM));          // no M
        Assert.Throws<ArgumentException>(() => road.CalibrateM(Array.Empty<Coordinate>(), 1, factory: ZM));
        Assert.Throws<InvalidOperationException>(() => road.CalibrateM(ok, 0, factory: new MFactory(true, false)));
    }

    [Fact]
    public void An_empty_line_gives_an_empty_line()
    {
        var empty = new MPolyline([]);

        Assert.Equal(0, empty.CalibrateM([C(5, 0, m: 1)], 0, factory: ZM).PathCount);
    }
}
