using System.Globalization;
using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>Whole-surface questions on a triangulation (ADR 0041): the volume between two surfaces, zones of equal slope and the line of sight.</summary>
public class SurfaceTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[1, 99], Gen.Double[1, 99]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    /// <summary>A surface over the square (0 0)-(100 100) with random heights: the four corners and some points.</summary>
    private static Triangulation Terrain(Coordinate[] scattered, double[] heights, int offset = 0)
    {
        var corners = new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var all = scattered.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).ToArray();
        var withZ = all.Select((p, i) => p with { Z = heights[(i + offset) % heights.Length] }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
    }

    private static double Net((double Above, double Below) v) => v.Above - v.Below;

    private static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static (double, double) Round((double Above, double Below) v) => (Math.Round(v.Above, 9), Math.Round(v.Below, 9));

    // ---------- the volume between two surfaces ----------

    [Fact]
    [Covers("Volume")]
    public void Two_parallel_surfaces_enclose_the_slab_between_them()
    {
        var upper = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 3), (10 0 3), (10 10 3), (0 10 3))"));
        var lower = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (10 10 0), (0 10 0), (3 4 0), (7 6 0))"));

        Assert.Equal((300d, 0d), Round(upper.Volume(lower)));
        Assert.Equal((0d, 300d), Round(lower.Volume(upper)));
    }

    [Fact]
    [Covers("Volume")]
    public void A_ramp_against_a_flat_surface_is_cut_and_filled_in_equal_parts()
    {
        var ramp = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 10), (0 10 0))"));
        var flat = Triangulation.FromPoints(G("MULTIPOINT Z ((-5 -5 5), (15 -5 5), (15 15 5), (-5 15 5), (5 5 5))"));

        var (above, below) = ramp.Volume(flat);

        Assert.Equal(125d, above, 1e-9);   // x from 5 to 10, height x - 5 up to 5, over a width of 10; and the same below
        Assert.Equal(125d, below, 1e-9);
    }

    [Fact]
    [Covers("Volume")]
    public void Only_the_area_that_both_surfaces_cover_counts()
    {
        var a = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 4), (10 0 4), (10 10 4), (0 10 4))"));
        var b = Triangulation.FromPoints(G("MULTIPOINT Z ((5 5 0), (15 5 0), (15 15 0), (5 15 0))"));

        Assert.Equal((100d, 0d), Round(a.Volume(b)));                   // 5 by 5 in common, 4 high
        Assert.Equal((0d, 0d), Round(a.Volume(Triangulation.FromPoints(G("MULTIPOINT Z ((50 50 0), (60 50 0), (60 60 0))")))));
    }

    [Fact]
    [Covers("Volume")]
    public void The_volume_between_two_surfaces_rejects_null_and_ignores_empty_ones()
    {
        var a = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 4), (10 0 4), (10 10 4))"));

        Assert.Throws<ArgumentNullException>(() => a.Volume((Triangulation)null!));
        Assert.Equal((0d, 0d), a.Volume(Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 1))"))));
    }

    [Fact]
    [Covers("Volume")]
    public void Against_a_flat_surface_it_is_the_volume_above_and_below_that_height()
    {
        Gen.Select(Pt.Array[3, 40], Gen.Double[-50, 50].Array[44], Gen.Double[-50, 50]).Sample((points, heights, level) =>
        {
            var t = Terrain(points, heights);
            string z = R(level);
            var flat = Triangulation.FromPoints(G($"MULTIPOINT Z ((-10 -10 {z}), (110 -10 {z}), (110 110 {z}), (-10 110 {z}), (50 50 {z}))"));

            var expected = t.Volume(level);
            var actual = t.Volume(flat);

            Assert.Equal(expected.Above, actual.Above, 1e-6 * (1 + expected.Above));
            Assert.Equal(expected.Below, actual.Below, 1e-6 * (1 + expected.Below));
        }, iter: 200);
    }

    [Fact]
    [Covers("Volume")]
    public void Swapping_the_surfaces_swaps_above_and_below_and_the_net_volume_is_the_difference_of_the_nets()
    {
        Gen.Select(Pt.Array[3, 30], Pt.Array[3, 30], Gen.Double[-50, 50].Array[34]).Sample((p1, p2, heights) =>
        {
            var a = Terrain(p1, heights);
            var b = Terrain(p2, heights, offset: 7);

            var ab = a.Volume(b);
            var ba = b.Volume(a);

            Assert.Equal(ab.Above, ba.Below, 1e-6 * (1 + ab.Above));
            Assert.Equal(ab.Below, ba.Above, 1e-6 * (1 + ab.Below));
            Assert.Equal(Net(a.Volume(0.0)) - Net(b.Volume(0.0)), Net(ab), 1e-5 * (1 + Math.Abs(Net(ab))));
        }, iter: 200);
    }

    // ---------- zones of equal slope ----------

    [Fact]
    [Covers("SlopeZones")]
    public void Slope_zones_separate_the_flat_part_from_the_ramp()
    {
        var points = G("MULTIPOINT Z ((0 0 0), (5 0 0), (5 10 0), (0 10 0), (10 0 5), (10 10 5))");
        var t = Triangulation.FromPointsAndLines(points, G("LINESTRING Z (5 0 0, 5 10 0)"));

        var zones = t.SlopeZones([10, 50], F);

        Assert.Equal(3, zones.Count);
        Assert.Equal(50, zones[0].Area(), 1e-9);    // flat
        Assert.Equal(50, zones[1].Area(), 1e-9);    // the ramp, 45 degrees
        Assert.Equal(0, zones[2].Area(), 1e-9);
        Assert.All(zones, z => Assert.True(z.IsValid()));
    }

    [Fact]
    [Covers("SlopeZones")]
    public void Slope_zones_tile_the_surface_and_every_triangle_lies_in_its_class()
    {
        Gen.Select(Pt.Array[3, 30], Gen.Double[-20, 20].Array[34]).Sample((points, heights) =>
        {
            var t = Terrain(points, heights);
            double[] limits = [5, 20, 60];

            var zones = t.SlopeZones(limits, F);

            var triangles = t.ToPolygons(F);
            double hull = triangles.Sum(p => p.Area());
            Assert.Equal(hull, zones.Sum(z => z.Area()), 1e-6 * (1 + hull));
            for (int i = 0; i < t.TriangleCount; i++)
            {
                double slope = t.Slope(i);
                int cls = limits.Count(l => slope >= l);
                Assert.NotEqual(Location.Outside, zones[cls].Locate(Centroid(t, i)));
            }
        }, iter: 100);
    }

    [Fact]
    [Covers("SlopeZones")]
    public void Slope_zones_are_valid_and_equal_the_union_of_their_triangles()
    {
        Gen.Select(Pt.Array[3, 40], Gen.Double[-20, 20].Array[44]).Sample((points, heights) =>
        {
            var t = Terrain(points, heights);
            double[] limits = [4, 15, 40];

            var zones = t.SlopeZones(limits, F);

            var triangles = t.ToPolygons(F);
            for (int cls = 0; cls <= limits.Length; cls++)
            {
                var inClass = Enumerable.Range(0, t.TriangleCount).Where(i => limits.Count(l => t.Slope(i) >= l) == cls).Select(i => triangles[i]).ToList();
                Assert.True(zones[cls].IsValid(), $"zone {cls} is not valid: {zones[cls].Validate()} {zones[cls].ToWkt()}");
                if (inClass.Count == 0)
                {
                    Assert.Equal(0, zones[cls].Area());
                    continue;
                }

                var union = inClass.UnionAll(factory: F);
                Assert.Equal(union.Area(), zones[cls].Area(), 1e-6 * (1 + union.Area()));
                Assert.Equal(0, union.Difference(zones[cls], factory: F).Area() + zones[cls].Difference(union, factory: F).Area(), 1e-5 * (1 + union.Area()));
            }
        }, iter: 100);
    }

    [Fact]
    [Covers("SlopeZones")]
    public void Slope_zones_keep_holes_and_parts_that_touch_in_a_vertex()
    {
        // a 4 x 4 grid of squares with the heights of a chessboard-like relief: flat cells and steep cells, the flat ones touch at the corners
        var rows = new List<string>();
        for (int x = 0; x <= 4; x++)
        {
            for (int y = 0; y <= 4; y++)
            {
                double z = (x + y) % 2 == 0 ? 0 : 10;
                rows.Add($"({x} {y} {z})");
            }
        }

        var t = Triangulation.FromPoints(G("MULTIPOINT Z (" + string.Join(", ", rows) + ")"));

        var zones = t.SlopeZones([5], F);

        Assert.Equal(16, zones[0].Area() + zones[1].Area(), 1e-9);
        Assert.All(zones, z => Assert.True(z.IsValid()));

        // a flat island in a steep ring: the ring has a hole
        var island = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 5), (6 0 5), (6 6 5), (0 6 5), (2 2 0), (4 2 0), (4 4 0), (2 4 0))"));
        var rings = island.SlopeZones([1], F);

        Assert.Equal(36, rings[0].Area() + rings[1].Area(), 1e-9);
        Assert.Equal(4, rings[0].Area(), 1e-9);
        Assert.True(rings[1].IsValid());
        Assert.Equal(2, rings[1].RingCount);   // the shell and the hole
    }

    private static Coordinate Centroid(Triangulation t, int i)
    {
        var (a, b, c) = t.Triangle(i);
        var (p, q, r) = (t.Vertices[a], t.Vertices[b], t.Vertices[c]);
        return new Coordinate((p.X + q.X + r.X) / 3, (p.Y + q.Y + r.Y) / 3);
    }

    [Fact]
    [Covers("SlopeZones")]
    public void Slope_zones_reject_bad_limits()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (10 10 0))"));

        Assert.Throws<ArgumentNullException>(() => t.SlopeZones(null!, F));
        Assert.Throws<ArgumentException>(() => t.SlopeZones([20, 10], F));
        Assert.Throws<ArgumentException>(() => t.SlopeZones([10, 10], F));
        Assert.Throws<ArgumentException>(() => t.SlopeZones([double.NaN], F));
        Assert.Single(t.SlopeZones([], F));
    }

    // ---------- line of sight ----------

    private static Triangulation Hill() => Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (10 10 0), (0 10 0), (5 5 10))"));

    [Fact]
    [Covers("LineOfSight")]
    public void A_hill_blocks_the_line_of_sight_unless_the_eye_and_the_target_are_high_enough()
    {
        var t = Hill();

        Assert.False(t.LineOfSight(new Coordinate(0, 5), new Coordinate(10, 5)));
        Assert.True(t.LineOfSight(new Coordinate(0, 5), new Coordinate(10, 5), observerHeight: 20, targetHeight: 20));
        Assert.False(t.LineOfSight(new Coordinate(0, 1), new Coordinate(10, 1)));                // the slope is 2 high along y = 1
        Assert.True(t.LineOfSight(new Coordinate(0, 1), new Coordinate(10, 1), observerHeight: 3, targetHeight: 3));
    }

    [Fact]
    [Covers("LineOfSight")]
    public void Flat_ground_and_the_same_point_are_free_and_a_point_outside_is_not_visible()
    {
        var flat = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 3), (10 0 3), (10 10 3), (0 10 3))"));

        Assert.True(flat.LineOfSight(new Coordinate(1, 1), new Coordinate(9, 9)));
        Assert.True(flat.LineOfSight(new Coordinate(1, 1), new Coordinate(1, 1)));
        Assert.False(flat.LineOfSight(new Coordinate(1, 1), new Coordinate(20, 9)));
        Assert.False(Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (10 10))")).LineOfSight(new Coordinate(1, 1), new Coordinate(2, 2)));
    }

    [Fact]
    [Covers("LineOfSight")]
    public void The_line_of_sight_is_symmetric_for_equal_heights_and_rejects_bad_arguments()
    {
        var t = Hill();
        var a = new Coordinate(1, 2);
        var b = new Coordinate(9, 8);

        Assert.Equal(t.LineOfSight(a, b, 2, 2), t.LineOfSight(b, a, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.LineOfSight(new Coordinate(double.NaN, 0), b));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.LineOfSight(a, b, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.LineOfSight(a, b, 0, double.PositiveInfinity));
    }

    [Fact]
    [Covers("LineOfSight")]
    public void A_higher_eye_and_a_higher_target_never_see_less()
    {
        Gen.Select(Pt.Array[3, 30], Gen.Double[-20, 20].Array[34], Pt, Pt, Gen.Double[0, 30]).Sample((points, heights, from, to, extra) =>
        {
            var t = Terrain(points, heights);

            if (t.LineOfSight(from, to, 0, 0))
            {
                Assert.True(t.LineOfSight(from, to, extra, 0));
                Assert.True(t.LineOfSight(from, to, 0, extra));
            }

            if (t.LineOfSight(from, to, 1, 1))
            {
                Assert.True(t.LineOfSight(from, to, 1 + extra, 1 + extra));
            }
        }, iter: 300);
    }
}
