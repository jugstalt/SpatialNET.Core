using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.Construction;

/// <summary>Rounding of lines: <c>Smooth</c> by corner cutting and by a Catmull–Rom spline (ADR 0043).</summary>
public class SmoothingTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static IPolyline2D Line(string wkt) => (IPolyline2D)G(wkt);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[0, 100], Gen.Double[0, 100]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    private static Coordinate[] Path(IPolyline2D line, int index = 0) => Internal.Rings.Coordinates(line[index]);

    private static IPolyline2D Open(Coordinate[] points) => F.CreatePolyline([points], null);

    /// <summary>Points that are all different and not all on one line, as a path.</summary>
    private static Coordinate[] Distinct(Coordinate[] points) => points.GroupBy(p => (p.X, p.Y)).Select(g => g.First()).ToArray();

    // ---------- Chaikin ----------

    [Fact]
    [Covers("Smooth")]
    public void A_corner_is_cut_at_a_quarter_of_both_segments_and_the_ends_stay()
    {
        var smooth = Line("LINESTRING (0 0, 8 0, 8 8)").Smooth(1, SmoothingMethod.Chaikin, F);

        var p = Path(smooth);
        Assert.Equal(4, p.Length);
        Assert.Equal((0d, 0d), (p[0].X, p[0].Y));
        Assert.Equal((6d, 0d), (p[1].X, p[1].Y));       // three quarters of the first segment
        Assert.Equal((8d, 2d), (p[2].X, p[2].Y));       // a quarter of the second
        Assert.Equal((8d, 8d), (p[3].X, p[3].Y));
    }

    [Fact]
    [Covers("Smooth")]
    public void A_closed_path_is_smoothed_all_the_way_round_and_stays_closed()
    {
        var smooth = Line("LINESTRING (0 0, 8 0, 8 8, 0 8, 0 0)").Smooth(1, SmoothingMethod.Chaikin, F);

        var p = Path(smooth);
        Assert.Equal(9, p.Length);                                   // eight corner points and the closing one
        Assert.Equal((p[0].X, p[0].Y), (p[^1].X, p[^1].Y));
        Assert.DoesNotContain(p, c => c.X == 0 && c.Y == 0);       // the first corner is cut as well
    }

    [Fact]
    [Covers("Smooth")]
    public void Two_points_and_no_iterations_change_nothing_and_a_line_of_one_point_is_kept()
    {
        Assert.Equal(2, Path(Line("LINESTRING (0 0, 5 5)").Smooth(3, SmoothingMethod.Chaikin, F)).Length);
        Assert.Equal(4, Path(Line("LINESTRING (0 0, 5 5, 9 1, 12 6)").Smooth(0, SmoothingMethod.CatmullRom, F)).Length);
        Assert.Single(Path(Line("LINESTRING (1 1, 1 1)").Smooth(2, SmoothingMethod.Chaikin, F)).Select(c => (c.X, c.Y)).Distinct());
    }

    [Fact]
    [Covers("Smooth")]
    public void Every_iteration_doubles_the_segments_of_an_open_path()
    {
        Pt.Array[3, 12].Sample(points =>
        {
            var d = Distinct(points);
            if (d.Length < 3)
            {
                return;
            }

            var line = Open(d);

            int chaikin = Path(line.Smooth(1, SmoothingMethod.Chaikin, F)).Length;
            int spline = Path(line.Smooth(3, SmoothingMethod.CatmullRom, F)).Length;

            Assert.Equal(2 * d.Length - 2, chaikin);
            Assert.Equal((d.Length - 1) * 8 + 1, spline);
        }, iter: 200);
    }

    [Fact]
    [Covers("Smooth")]
    public void The_curve_keeps_its_ends_stays_in_the_convex_hull_and_is_not_longer()
    {
        Pt.Array[3, 15].Sample(points =>
        {
            var d = Distinct(points);
            if (d.Length < 3)
            {
                return;
            }

            var line = Open(d);
            var smooth = line.Smooth(3, SmoothingMethod.Chaikin, F);
            var hull = line.ConvexHull(F);

            var p = Path(smooth);
            Assert.Equal((d[0].X, d[0].Y), (p[0].X, p[0].Y));
            Assert.Equal((d[^1].X, d[^1].Y), (p[^1].X, p[^1].Y));
            Assert.True(smooth.Length() <= line.Length() + 1e-9);
            if (hull is IPolygon2D polygon)
            {
                Assert.All(p, c => Assert.True(polygon.Locate(c) != Location.Outside || polygon.Distance(F.CreatePoint(c, null)) < 1e-9));
            }
        }, iter: 200);
    }

    [Fact]
    [Covers("Smooth")]
    public void The_curve_stays_within_half_of_the_longest_segment_of_the_line()
    {
        Pt.Array[3, 15].Sample(points =>
        {
            var d = Distinct(points);
            if (d.Length < 3)
            {
                return;
            }

            var line = Open(d);
            double longest = Enumerable.Range(1, d.Length - 1).Max(i => Math.Sqrt((d[i].X - d[i - 1].X) * (d[i].X - d[i - 1].X) + (d[i].Y - d[i - 1].Y) * (d[i].Y - d[i - 1].Y)));

            var smooth = line.Smooth(3, SmoothingMethod.Chaikin, F);

            Assert.True(line.HausdorffDistance(smooth) <= 0.5 * longest + 1e-9);
        }, iter: 200);
    }

    [Fact]
    [Covers("Smooth")]
    public void A_straight_line_stays_straight_and_the_height_of_a_contour_line_stays()
    {
        var straight = Line("LINESTRING (0 0, 3 3, 7 7, 10 10)").Smooth(3, SmoothingMethod.Chaikin, F);
        var contour = Line("LINESTRING Z (0 0 50, 6 2 50, 9 7 50, 3 12 50)").Smooth(2, SmoothingMethod.CatmullRom, ZM);

        Assert.All(Path(straight), c => Assert.Equal(c.X, c.Y, 1e-9));
        Assert.All(Path(contour), c => Assert.Equal(50, c.Z, 1e-9));
    }

    [Fact]
    [Covers("Smooth")]
    public void M_stays_monotonic_and_Z_stays_between_the_values_of_the_line()
    {
        var line = Line("LINESTRING ZM (0 0 10 0, 10 0 20 10, 10 10 5 20, 0 10 30 30)");

        foreach (var method in new[] { SmoothingMethod.Chaikin, SmoothingMethod.CatmullRom })
        {
            var p = Path(line.Smooth(3, method, ZM));

            for (int i = 1; i < p.Length; i++)
            {
                Assert.True(p[i].M >= p[i - 1].M - 1e-9, $"{method}: M falls at {i}");
            }

            Assert.All(p, c => Assert.InRange(c.Z, 5 - 1e-9, 30 + 1e-9));
            Assert.Equal(0, p[0].M);
            Assert.Equal(30, p[^1].M);
        }
    }

    // ---------- Catmull–Rom ----------

    [Fact]
    [Covers("Smooth")]
    public void The_spline_passes_through_every_vertex()
    {
        Pt.Array[3, 12].Sample(points =>
        {
            var d = Distinct(points);
            if (d.Length < 3)
            {
                return;
            }

            var p = Path(Open(d).Smooth(2, SmoothingMethod.CatmullRom, F));

            foreach (var v in d)
            {
                Assert.Contains(p, c => c.X == v.X && c.Y == v.Y);
            }
        }, iter: 200);
    }

    [Fact]
    [Covers("Smooth")]
    public void The_spline_of_a_closed_path_is_closed_and_round_where_the_input_is_a_square()
    {
        var spline = Line("LINESTRING (0 0, 10 0, 10 10, 0 10, 0 0)").Smooth(3, SmoothingMethod.CatmullRom, F);

        var p = Path(spline);
        Assert.Equal(4 * 8 + 1, p.Length);
        Assert.Equal((p[0].X, p[0].Y), (p[^1].X, p[^1].Y));
        // between two corners it bulges beyond the side a little at most, and no vertex lies far outside the square
        Assert.All(p, c => Assert.InRange(c.X, -2, 12));
        Assert.All(p, c => Assert.InRange(c.Y, -2, 12));
    }

    [Fact]
    [Covers("Smooth")]
    public void A_sampled_circle_is_followed_closely_by_the_spline()
    {
        var points = Enumerable.Range(0, 12).Select(i => new Coordinate(50 + 40 * Math.Cos(2 * Math.PI * i / 12), 50 + 40 * Math.Sin(2 * Math.PI * i / 12))).ToList();
        points.Add(points[0]);
        var ring = Open(points.ToArray());

        var spline = Path(ring.Smooth(3, SmoothingMethod.CatmullRom, F));
        var chaikin = Path(ring.Smooth(3, SmoothingMethod.Chaikin, F));

        Assert.All(spline, c => Assert.Equal(40, Math.Sqrt((c.X - 50) * (c.X - 50) + (c.Y - 50) * (c.Y - 50)), 0.1));
        Assert.All(chaikin, c => Assert.InRange(Math.Sqrt((c.X - 50) * (c.X - 50) + (c.Y - 50) * (c.Y - 50)), 37.5, 40));
    }

    // ---------- polygons, other geometries, errors ----------

    [Fact]
    [Covers("Smooth")]
    public void A_convex_polygon_loses_area_by_corner_cutting_and_keeps_its_orientation()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        var smooth = polygon.Smooth(2, SmoothingMethod.Chaikin, F);

        Assert.True(smooth.Area() < polygon.Area());
        Assert.True(smooth.Area() > 0.8 * polygon.Area());
        Assert.True(smooth.IsValid());
        Assert.Equal(1, smooth.RingCount);
    }

    [Fact]
    [Covers("Smooth")]
    public void The_holes_of_a_polygon_are_smoothed_too()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 40 0, 40 40, 0 40, 0 0), (10 10, 10 30, 30 30, 30 10, 10 10))");

        var smooth = polygon.Smooth(2, SmoothingMethod.CatmullRom, F);

        Assert.Equal(2, smooth.RingCount);
        Assert.True(smooth.IsValid());
        Assert.True(smooth.Area() > 0);
    }

    [Fact]
    [Covers("Smooth")]
    public void The_geometry_overload_dispatches_and_copies_points()
    {
        Assert.IsAssignableFrom<IPolygon2D>(G("POLYGON ((0 0, 10 0, 10 10, 0 0))").Smooth(1, SmoothingMethod.Chaikin, F));
        Assert.IsAssignableFrom<IPolyline2D>(G("LINESTRING (0 0, 5 5, 9 1)").Smooth(1, SmoothingMethod.Chaikin, F));
        Assert.Equal("POINT (3 4)", G("POINT (3 4)").Smooth(2, SmoothingMethod.Chaikin, F).ToWkt());
        Assert.Equal("MULTIPOINT ((1 2), (3 4))", G("MULTIPOINT ((1 2), (3 4))").Smooth(2, SmoothingMethod.CatmullRom, F).ToWkt());
    }

    [Fact]
    [Covers("Smooth")]
    public void Bad_arguments_are_rejected()
    {
        var line = Line("LINESTRING (0 0, 5 5, 9 1)");

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Smooth());
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Smooth());
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Smooth());
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Smooth(-1, SmoothingMethod.Chaikin, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Smooth(11, SmoothingMethod.Chaikin, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Smooth(2, (SmoothingMethod)7, F));
        Assert.Equal((1 << 10) + 2, Path(line.Smooth(10, SmoothingMethod.Chaikin, F)).Length);   // three points: 2^k + 2 after k iterations
    }
}
