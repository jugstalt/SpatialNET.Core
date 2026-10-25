using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// <c>LocateBetween</c> and <c>LocateAlong</c> (ADR 0032). NTS has no M-based counterpart, so the cases are
/// hand-written and checked against a brute-force scan.
/// </summary>
[Covers("LocateBetween")]
[Covers("LocateAlong")]
public class LocateTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private const double Tolerance = 1e-9;

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    private static Coordinate[] Vertices(IPolyline2D line, int path) =>
        Enumerable.Range(0, line[path].PointCount).Select(i => ToCoordinate(line[path][i])).ToArray();

    private static Coordinate ToCoordinate(IPoint2D p) =>
        new(p.X, p.Y, p is IPoint3D p3 ? p3.Z : double.NaN, p is IPointM pm ? pm.M : double.NaN);

    private static (double X, double Y, double M)[] Xym(Coordinate[] v) => v.Select(c => (c.X, c.Y, c.M)).ToArray();

    // ---------- LocateBetween ----------

    [Fact]
    public void LocateBetween_cuts_the_stretch_inside_the_interval()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100));

        var piece = line.LocateBetween(20, 70, ZM);

        Assert.Equal(1, piece.PathCount);
        Assert.Equal([(2d, 0d, 20d), (7d, 0d, 70d)], Xym(Vertices(piece, 0)));
    }

    [Fact]
    public void LocateBetween_keeps_original_vertices_and_joins_consecutive_segments()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(10, 10, m: 200));

        var piece = line.LocateBetween(50, 150, ZM);

        Assert.Equal(1, piece.PathCount);
        Assert.Equal([(5d, 0d, 50d), (10d, 0d, 100d), (10d, 5d, 150d)], Xym(Vertices(piece, 0)));
    }

    [Fact]
    public void LocateBetween_finds_every_stretch_when_M_is_not_monotonic()
    {
        // M rises 0..100, falls to 0, rises to 100 again: the interval 40..60 is met three times.
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(20, 0, m: 0), C(30, 0, m: 100));

        var piece = line.LocateBetween(40, 60, ZM);

        Assert.Equal(3, piece.PathCount);
        Assert.Equal([(4d, 0d, 40d), (6d, 0d, 60d)], Xym(Vertices(piece, 0)));
        Assert.Equal([(14d, 0d, 60d), (16d, 0d, 40d)], Xym(Vertices(piece, 1)));  // keeps the direction of the line: M falls here
        Assert.Equal([(24d, 0d, 40d), (26d, 0d, 60d)], Xym(Vertices(piece, 2)));
    }

    [Fact]
    public void LocateBetween_swaps_the_bounds_and_includes_the_boundary_values()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100));

        Assert.Equal(Xym(Vertices(line.LocateBetween(20, 70, ZM), 0)), Xym(Vertices(line.LocateBetween(70, 20, ZM), 0)));
        var whole = line.LocateBetween(0, 100, ZM);
        Assert.Equal([(0d, 0d, 0d), (10d, 0d, 100d)], Xym(Vertices(whole, 0)));
    }

    [Fact]
    public void LocateBetween_starts_a_new_path_where_M_is_missing_or_the_path_ends()
    {
        var line = new MPolyline([
            [C(0, 0, m: 0), C(10, 0, m: 10), C(20, 0), C(30, 0, m: 30), C(40, 0, m: 40)],
            [C(50, 0, m: 50), C(60, 0, m: 60)],
        ]);

        var piece = line.LocateBetween(0, 100, ZM);

        Assert.Equal(3, piece.PathCount);   // before the gap, after the gap, the second path
        Assert.Equal(2, Vertices(piece, 0).Length);
        Assert.Equal([(30d, 0d, 30d), (40d, 0d, 40d)], Xym(Vertices(piece, 1)));
    }

    [Fact]
    public void LocateBetween_handles_constant_M_and_drops_a_touch_at_a_point()
    {
        var flat = Line(C(0, 0, m: 5), C(10, 0, m: 5));
        Assert.Equal(1, flat.LocateBetween(5, 9, ZM).PathCount);          // the whole segment
        Assert.Equal(0, flat.LocateBetween(6, 9, ZM).PathCount);

        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10), C(20, 0, m: 0));
        Assert.Equal(0, line.LocateBetween(10, 10, ZM).PathCount);        // M equals 10 at one point only: no length
        Assert.Equal(0, line.LocateBetween(20, 30, ZM).PathCount);
    }

    [Fact]
    public void LocateBetween_interpolates_Z_at_the_cut_ends()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100));

        var v = Vertices(line.LocateBetween(20, 70, ZM), 0);

        Assert.Equal((2d, 7d), (v[0].Z, v[1].Z));
    }

    [Fact]
    public void LocateBetween_matches_a_brute_force_scan_of_the_length_inside_the_interval()
    {
        var rng = new Random(11);
        for (int n = 0; n < 100; n++)
        {
            var path = new List<Coordinate> { C(0, 0, m: rng.NextDouble() * 100) };
            for (int i = 0; i < 6; i++)
            {
                path.Add(C(path[^1].X + 1 + rng.NextDouble() * 5, rng.NextDouble() * 5, m: rng.NextDouble() * 100));
            }

            var line = Line(path.ToArray());
            double a = rng.NextDouble() * 100, b = rng.NextDouble() * 100;
            double lo = Math.Min(a, b), hi = Math.Max(a, b);

            double expected = 0, step = 0.0005, total = line.Length();
            for (double d = 0; d < total; d += step)
            {
                double mid = line.Interpolate(Math.Min(d + step / 2, total))!.Value.M;
                if (mid >= lo && mid <= hi)
                {
                    expected += Math.Min(step, total - d);
                }
            }

            double found = line.LocateBetween(lo, hi, ZM).Length();
            Assert.Equal(expected, found, 0.05);
        }
    }

    // ---------- LocateAlong ----------

    [Fact]
    public void LocateAlong_finds_every_hit_in_the_order_along_the_line()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(20, 0, m: 0), C(30, 0, m: 100));

        var hits = line.LocateAlong(50, ZM);

        Assert.Equal(3, hits.PointCount);
        Assert.Equal([5d, 15d, 25d], Enumerable.Range(0, 3).Select(i => hits[i].X));
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(50, ((IPointM)hits[i]).M));
    }

    [Fact]
    public void LocateAlong_counts_a_hit_at_a_shared_vertex_once()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(20, 0, m: 200));

        var hits = line.LocateAlong(100, ZM);

        Assert.Equal(1, hits.PointCount);
        Assert.Equal(10, hits[0].X);
    }

    [Fact]
    public void LocateAlong_answers_a_constant_segment_with_its_start_and_ignores_stretches_without_M()
    {
        var flat = Line(C(0, 0, m: 5), C(10, 0, m: 5));
        Assert.Equal([0d], Enumerable.Range(0, 1).Select(i => flat.LocateAlong(5, ZM)[i].X));

        var gap = Line(C(0, 0, m: 0), C(10, 0), C(20, 0, m: 20));
        Assert.Equal(0, gap.LocateAlong(10, ZM).PointCount);
        Assert.Equal(0, flat.LocateAlong(6, ZM).PointCount);
    }

    [Fact]
    public void LocateAlong_agrees_with_InterpolateByM_on_the_first_hit()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(20, 0, m: 0), C(30, 0, m: 100));

        var first = line.InterpolateByM(30)!.Value;
        var all = line.LocateAlong(30, ZM);

        Assert.Equal(first.X, all[0].X, Tolerance);
    }

    // ---------- Errors ----------

    [Fact]
    public void Bad_input_and_empty_lines()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10));

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).LocateBetween(0, 1, ZM));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).LocateAlong(0, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.LocateBetween(double.NaN, 1, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.LocateBetween(0, double.PositiveInfinity, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.LocateAlong(double.NaN, ZM));

        Assert.Equal(0, new MPolyline([]).LocateBetween(0, 1, ZM).PathCount);
        Assert.Equal(0, new MPolyline([]).LocateAlong(0, ZM).PointCount);
    }
}
