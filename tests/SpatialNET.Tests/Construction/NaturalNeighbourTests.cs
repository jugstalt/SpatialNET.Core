using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>Natural neighbour interpolation on a triangulation (ADR 0041), against the definition by areas and by its properties.</summary>
public class NaturalNeighbourTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<double> Value = Gen.Double[0, 100].Select(d => Math.Round(d, 3));

    private static readonly Gen<Coordinate> Pt = Gen.Select(Value, Value).Select((x, y) => new Coordinate(x, y));

    private static Triangulation Of(Coordinate[] points) => Triangulation.FromPoints(F.CreateMultiPoint(points, null));

    /// <summary>The definition: the area that the cell of every site loses when the point is added, weighted by the height of the site.</summary>
    private static double ByAreas(Coordinate[] sites, Coordinate q)
    {
        const double lo = -1e4, hi = 1e4;
        var all = sites.Append(q).ToArray();
        double sum = 0, weighted = 0;
        foreach (var s in sites)
        {
            double stolen = VoronoiTests.ReferenceArea(sites, s, lo, lo, hi, hi) - VoronoiTests.ReferenceArea(all, s, lo, lo, hi, hi);
            sum += stolen;
            weighted += stolen * s.Z;
        }

        return weighted / sum;
    }

    /// <summary>Within 1 of a hull edge: the new cell of the point is then so large that the rectangle of the reference cuts it.</summary>
    private static bool OnTheBorder(Triangulation t, Coordinate q)
    {
        for (int i = 0; i < t.TriangleCount; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                if (t.Neighbour(i, k) >= 0)
                {
                    continue;
                }

                var (a, b, c) = t.Triangle(i);
                int[] ids = [a, b, c];
                var p = t.Vertices[ids[k]];
                var r = t.Vertices[ids[(k + 1) % 3]];
                if (Math.Abs((r.X - p.X) * (q.Y - p.Y) - (r.Y - p.Y) * (q.X - p.X)) < 1 * Math.Sqrt((r.X - p.X) * (r.X - p.X) + (r.Y - p.Y) * (r.Y - p.Y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void The_centre_of_a_square_is_the_mean_of_the_corners_and_a_vertex_gives_its_own_height()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 40), (0 10 10))"));

        Assert.Equal(15, t.InterpolateNaturalNeighbour(new Coordinate(5, 5)), 1e-9);
        Assert.Equal(40, t.InterpolateNaturalNeighbour(new Coordinate(10, 10)));
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void Outside_the_hull_and_without_Z_the_result_is_NaN()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 40), (0 10 10))"));
        var flat = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10), (5 4))"));

        Assert.True(double.IsNaN(t.InterpolateNaturalNeighbour(new Coordinate(11, 5))));
        Assert.True(double.IsNaN(flat.InterpolateNaturalNeighbour(new Coordinate(3, 3))));
        Assert.True(double.IsNaN(Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 1), (1 1 1))")).InterpolateNaturalNeighbour(new Coordinate(0.5, 0.5))));
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void A_triangulation_with_a_break_line_gives_the_linear_height()
    {
        var points = G("MULTIPOINT Z ((0 0 0), (10 0 0), (10 10 10), (0 10 10), (5 5 3))");
        var t = Triangulation.FromPointsAndLines(points, G("LINESTRING Z (0 0 0, 10 10 10)"));

        var q = new Coordinate(7, 3);

        Assert.Equal(t.InterpolateZ(q), t.InterpolateNaturalNeighbour(q));
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void A_plane_is_reproduced_exactly()
    {
        Gen.Select(Pt.Array[5, 40], Gen.Double[-3, 3], Gen.Double[-3, 3], Pt.Array[1, 8]).Sample((points, a, b, queries) =>
        {
            var withZ = points.Select(p => p with { Z = a * p.X + b * p.Y + 7 }).ToArray();
            var t = Of(withZ);
            if (t.TriangleCount == 0)
            {
                return;
            }

            foreach (var q in queries)
            {
                double z = t.InterpolateNaturalNeighbour(q);
                if (!double.IsNaN(z))
                {
                    Assert.True(Math.Abs(z - (a * q.X + b * q.Y + 7)) <= 1e-6, $"{z} against {a * q.X + b * q.Y + 7} at {q}");
                }
            }
        }, iter: 300);
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void The_height_is_the_one_of_the_definition_by_areas()
    {
        Gen.Select(Pt.Array[4, 30], Gen.Double[-50, 50].Array[30], Pt).Sample((points, heights, q) =>
        {
            var withZ = points.Select((p, i) => p with { Z = heights[i] }).ToArray();
            var distinct = withZ.GroupBy(p => (p.X, p.Y)).Select(g => g.First()).ToArray();
            var t = Of(distinct);
            if (t.TriangleCount == 0 || t.Locate(q) < 0 || distinct.Any(p => p.X == q.X && p.Y == q.Y) || OnTheBorder(t, q))
            {
                return;   // on the border of the hull the result is the linear one, by definition; near it the reference is cut off
            }

            double expected = ByAreas(distinct, q);
            double actual = t.InterpolateNaturalNeighbour(q);

            Assert.True(Math.Abs(expected - actual) <= 1e-6 * (1 + Math.Abs(expected)), $"{actual} against {expected} at {q}");
        }, iter: 300);
    }

    [Fact]
    [Covers("InterpolateNaturalNeighbour")]
    public void The_height_stays_within_the_range_of_the_heights()
    {
        Gen.Select(Pt.Array[4, 40], Gen.Double[-50, 50].Array[40], Pt).Sample((points, heights, q) =>
        {
            var withZ = points.Select((p, i) => p with { Z = heights[i] }).ToArray();
            var t = Of(withZ);
            double z = t.TriangleCount == 0 ? double.NaN : t.InterpolateNaturalNeighbour(q);
            if (!double.IsNaN(z))
            {
                double lo = t.Vertices.Min(v => v.Z), hi = t.Vertices.Max(v => v.Z);
                Assert.InRange(z, lo - 1e-9, hi + 1e-9);
            }
        }, iter: 300);
    }
}
