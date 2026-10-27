using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>Height bands, the areas between two heights of a surface (ADR 0042).</summary>
public class ContourBandsTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[1, 99], Gen.Double[1, 99]).Select((x, y) => new Coordinate(Math.Round(x, 3), Math.Round(y, 3)));

    /// <summary>A surface over the square (0 0)-(100 100) with scattered points and the heights of the function.</summary>
    private static Triangulation Square(Coordinate[] scattered, Func<double, double, double> height)
    {
        var corners = new Coordinate[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var all = scattered.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = height(p.X, p.Y) }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(all, null));
    }

    /// <summary>The area of the square where lo ≤ a·x + b·y + c &lt; hi, by clipping the square with the two half-planes: independent of the library.</summary>
    private static double PlaneBand(double a, double b, double c, double lo, double hi)
    {
        var polygon = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 100), (0, 100) };
        foreach (var (level, above) in new[] { (lo, true), (hi, false) })
        {
            if (double.IsInfinity(level))
            {
                continue;
            }

            double H((double X, double Y) p) => a * p.X + b * p.Y + c - level;
            var next = new List<(double X, double Y)>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var cur = polygon[i];
                var prev = polygon[(i + polygon.Count - 1) % polygon.Count];
                bool curIn = above ? H(cur) >= 0 : H(cur) <= 0, prevIn = above ? H(prev) >= 0 : H(prev) <= 0;
                if (curIn != prevIn)
                {
                    double s = H(prev) / (H(prev) - H(cur));
                    next.Add((prev.X + s * (cur.X - prev.X), prev.Y + s * (cur.Y - prev.Y)));
                }

                if (curIn)
                {
                    next.Add(cur);
                }
            }

            polygon = next;
        }

        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            area += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(area) / 2;
    }

    [Fact]
    [Covers("ContourBands")]
    public void A_ramp_is_cut_into_strips()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 10), (0 10 0))"));

        var bands = t.ContourBands([3, 7], F);

        Assert.Equal(3, bands.Count);
        Assert.Equal(30, bands[0].Area(), 1e-9);    // x from 0 to 3
        Assert.Equal(40, bands[1].Area(), 1e-9);    // from 3 to 7
        Assert.Equal(30, bands[2].Area(), 1e-9);
        Assert.All(bands, b => Assert.True(b.IsValid()));
        Assert.Equal(3, bands[0].Envelope().MaxX, 1e-9);
        Assert.Equal(7, bands[1].Envelope().MaxX, 1e-9);
    }

    [Fact]
    [Covers("ContourBands")]
    public void A_triangle_that_is_flat_at_a_height_belongs_to_the_band_above_and_without_levels_there_is_one_band()
    {
        var flat = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 5), (10 0 5), (10 10 5), (0 10 5))"));

        var bands = flat.ContourBands([5], F);

        Assert.Equal(0, bands[0].Area());
        Assert.Equal(100, bands[1].Area(), 1e-9);
        Assert.Equal(100, flat.ContourBands([], F).Single().Area(), 1e-9);
        Assert.Equal(100, flat.ContourBands([6], F)[0].Area(), 1e-9);
        Assert.Equal(0, flat.ContourBands([4], F)[0].Area());
    }

    [Fact]
    [Covers("ContourBands")]
    public void A_hill_gives_rings_with_a_hole_and_the_vertices_on_the_lines_have_the_height()
    {
        // a pyramid: the band between two heights is a ring around the top
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (10 10 0), (0 10 0), (5 5 10))"));

        var bands = t.ContourBands([2, 6], F);

        Assert.Equal(2, bands[1].RingCount);                                   // the shell and the hole around the top
        Assert.All(bands, b => Assert.True(b.IsValid()));
        Assert.Equal(100, bands.Sum(b => b.Area()), 1e-9);
        foreach (var b in bands.Skip(1).Take(1))
        {
            var ring = Internal.Rings.Closed(b[0]);
            Assert.All(ring, c => Assert.True(Math.Abs(c.Z - 2) < 1e-9 || Math.Abs(c.Z - 6) < 1e-9 || c.Z == 0 || c.Z == 10));
        }
    }

    [Fact]
    [Covers("ContourBands")]
    public void The_bands_of_a_plane_have_the_area_of_the_clipped_square()
    {
        Gen.Select(Pt.Array[3, 30], Gen.Double[-2, 2].Select(d => Math.Round(d, 3)), Gen.Double[-2, 2].Select(d => Math.Round(d, 3)), Gen.Double[-50, 50].Select(d => Math.Round(d, 2)).Array[1, 5]).Sample((points, a, b, raw) =>
        {
            var t = Square(points, (x, y) => a * x + b * y + 7);
            var levels = raw.Distinct().OrderBy(v => v).ToArray();
            if (a == 0 && b == 0 && levels.Contains(7))
            {
                return;   // a flat surface exactly at a height belongs to the band above, where the reference would count it below as well
            }

            var bands = t.ContourBands(levels, F);

            Assert.Equal(levels.Length + 1, bands.Count);
            for (int k = 0; k < bands.Count; k++)
            {
                double lo = k == 0 ? double.NegativeInfinity : levels[k - 1], hi = k == levels.Length ? double.PositiveInfinity : levels[k];
                double expected = PlaneBand(a, b, 7, lo, hi);
                Assert.True(Math.Abs(expected - bands[k].Area()) <= 1e-6 * (1 + expected), $"band {k}: {bands[k].Area()} against {expected}");
                Assert.True(bands[k].IsValid(), $"band {k} is not valid");
            }
        }, iter: 200);
    }

    [Fact]
    [Covers("ContourBands")]
    public void The_bands_of_any_terrain_tile_the_surface_and_hold_every_point_in_its_band()
    {
        Gen.Select(Pt.Array[3, 40], Gen.Double[-30, 30].Select(d => Math.Round(d, 2)).Array[44], Gen.Double[-30, 30].Select(d => Math.Round(d, 2)).Array[1, 5], Pt.Array[1, 6]).Sample((points, heights, raw, queries) =>
        {
            int i = 0;
            var t = Square(points, (_, _) => heights[i++ % heights.Length]);
            var levels = raw.Distinct().OrderBy(v => v).ToArray();

            var bands = t.ContourBands(levels, F);

            double total = t.ToPolygons(F).Sum(p => p.Area());
            Assert.Equal(total, bands.Sum(b => b.Area()), 1e-6 * (1 + total));
            Assert.All(bands, b => Assert.True(b.IsValid(), $"{string.Join("; ", b.Validate().Issues.Select(x => x.ToString()))} {b.ToWkt()} levels {string.Join(",", levels.Select(l => l.ToString("R")))}"));
            foreach (var q in queries)
            {
                double z = t.InterpolateZ(q);
                if (double.IsNaN(z) || levels.Any(l => Math.Abs(l - z) < 1e-6))
                {
                    continue;
                }

                int band = levels.Count(l => l <= z);
                Assert.NotEqual(Location.Outside, bands[band].Locate(q));
            }
        }, iter: 150);
    }

    [Fact]
    [Covers("ContourBands")]
    public void Heights_that_are_not_finite_or_not_ascending_and_null_are_rejected_and_triangles_without_Z_are_left_out()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (0 10 0))"));

        Assert.Throws<ArgumentNullException>(() => t.ContourBands(null!, F));
        Assert.Throws<ArgumentException>(() => t.ContourBands([5, 5], F));
        Assert.Throws<ArgumentException>(() => t.ContourBands([6, 5], F));
        Assert.Throws<ArgumentException>(() => t.ContourBands([double.NaN], F));
        Assert.Equal(0, Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10))")).ContourBands([1], F).Sum(b => b.Area()));
    }
}
