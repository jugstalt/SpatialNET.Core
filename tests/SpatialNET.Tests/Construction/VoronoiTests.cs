using CsCheck;
using NetTopologySuite.Triangulate;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace SpatialNET.Tests.Construction;

/// <summary>VoronoiDiagram, VoronoiEdges and DelaunayEdges (ADR 0036): cases, properties and the NTS diagram as oracle.</summary>
public class VoronoiTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-50, 50], Gen.Int[-50, 50], Gen.Double[0.05, 0.95], Gen.Double[0.05, 0.95]).Select((x, y, a, b) => new Coordinate(x + a, y + b));

    private static IGeometry2D Points(IEnumerable<Coordinate> pts) => F.CreateMultiPoint(pts.ToArray(), null);

    // ---------- cases ----------

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Two_sites_are_split_by_their_bisector()
    {
        var cells = G("MULTIPOINT ((0 0), (10 0))").VoronoiDiagram(factory: F);

        Assert.Equal(2, cells.Count);
        // the clip area is (-5 -5) to (15 5), 20 by 10: the bisector x = 5 splits it into 100 and 100
        Assert.Equal(100, cells[0].Cell.Area(), 9);
        Assert.Equal(100, cells[1].Cell.Area(), 9);
        Assert.Equal(new Coordinate(0, 0), cells[0].Site);
        Assert.Equal(Location.Inside, cells[0].Cell.Locate(new Coordinate(0, 0)));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void The_corners_of_a_square_give_four_equal_cells()
    {
        var cells = G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10))").VoronoiDiagram(factory: F);

        Assert.Equal(4, cells.Count);
        Assert.All(cells, c => Assert.Equal(100, c.Cell.Area(), 9));   // the clip area is 20 by 20
        Assert.All(cells, c => Assert.True(c.Cell.IsValid()));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void A_single_site_gets_the_whole_clip_area_and_collinear_sites_get_strips()
    {
        var one = G("POINT (3 4)").VoronoiDiagram(factory: F);
        Assert.Single(one);
        Assert.Equal(4, one[0].Cell.Area(), 9);      // no extent: widened by 1 on every side, 2 by 2

        var line = G("MULTIPOINT ((0 0), (10 0), (20 0))").VoronoiDiagram(factory: F);
        Assert.Equal(3, line.Count);
        Assert.Equal(40 * 20, line.Sum(c => c.Cell.Area()), 9);   // clip area (-10 -10) to (30 10)
        Assert.Equal(15 * 20, line[1].Cell.Area() + 5 * 20, 9);   // the middle strip runs from x = 5 to x = 15
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Duplicates_count_once_non_finite_vertices_are_ignored_and_empty_input_gives_nothing()
    {
        Assert.Equal(3, G("MULTIPOINT ((0 0), (10 0), (0 0), (5 8))").VoronoiDiagram(factory: F).Count);
        Assert.Empty(G("POINT EMPTY").VoronoiDiagram(factory: F));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void The_result_is_in_the_order_of_the_first_occurrence_of_the_sites()
    {
        var cells = G("MULTIPOINT ((5 8), (0 0), (10 0))").VoronoiDiagram(factory: F);

        Assert.Equal([new Coordinate(5, 8), new Coordinate(0, 0), new Coordinate(10, 0)], cells.Select(c => c.Site));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void The_site_keeps_its_Z()
    {
        var cells = G("MULTIPOINT Z ((0 0 5), (10 0 7), (5 8 9))").VoronoiDiagram(factory: F);

        Assert.Equal([5d, 7d, 9d], cells.Select(c => c.Site.Z));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void A_clip_polygon_cuts_the_cells_and_the_areas_add_up_to_it()
    {
        var sites = G("MULTIPOINT ((2 2), (8 2), (5 8), (14 6), (2 12), (-30 40))");
        var clip = (IPolygon2D)G("POLYGON ((0 0, 12 0, 12 6, 6 6, 6 14, 0 14, 0 0))");   // an L

        var cells = sites.VoronoiDiagram(clip, F);

        Assert.Equal(6, cells.Count);
        Assert.Equal(clip.Area(), cells.Sum(c => c.Cell.Area()), 6);
        Assert.All(cells, c => Assert.True(c.Cell.IsValid()));
        Assert.Equal(0, cells[5].Cell.RingCount);                          // far away from the polygon, and its cell does not reach it
        Assert.Equal(Location.Inside, cells[0].Cell.Locate(new Coordinate(2, 2)));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Bad_arguments_throw()
    {
        var sites = G("MULTIPOINT ((0 0), (10 0), (5 8))");

        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).VoronoiDiagram(factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => sites.VoronoiDiagram(null, F, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sites.VoronoiDiagram(null, F, double.NaN));
        Assert.Empty(sites.VoronoiDiagram(F.CreatePolygon([], null), F));     // a clip area without area: nothing to cut to
    }

    // ---------- properties ----------

    [Fact]
    [Covers("VoronoiDiagram")]
    public void The_cells_are_valid_tile_the_clip_area_and_are_nearest_neighbour_regions()
    {
        Gen.Select(Pt.Array[1, 60], Gen.Double[0.001, 0.999], Gen.Double[0.001, 0.999]).Sample((sites, u, v) =>
        {
            var geometry = Points(sites);
            var cells = geometry.VoronoiDiagram(factory: F);
            var distinct = sites.Distinct().ToArray();

            Assert.Equal(distinct.Length, cells.Count);

            double minX = distinct.Min(p => p.X), maxX = distinct.Max(p => p.X), minY = distinct.Min(p => p.Y), maxY = distinct.Max(p => p.Y);
            double margin = Math.Max(maxX - minX, maxY - minY) / 2;
            if (!(margin > 0))
            {
                margin = 1;    // a single site has no extent
            }

            double area = (maxX - minX + 2 * margin) * (maxY - minY + 2 * margin);
            Assert.Equal(area, cells.Sum(c => c.Cell.Area()), 1e-6 * area);
            Assert.All(cells, c => Assert.True(c.Cell.IsValid(), c.Cell.ToWkt()));

            // a point of the clip area lies in the cell of its nearest site (the nearest site is unique almost everywhere)
            var p = new Coordinate(minX - margin + u * (maxX - minX + 2 * margin), minY - margin + v * (maxY - minY + 2 * margin));
            double best = distinct.Min(s => Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y)));
            var owners = cells.Where(c => c.Cell.Locate(p) != Location.Outside).ToArray();
            Assert.NotEmpty(owners);
            Assert.All(owners, c => Assert.True(Math.Sqrt((c.Site.X - p.X) * (c.Site.X - p.X) + (c.Site.Y - p.Y) * (c.Site.Y - p.Y)) <= best + 1e-6));
        }, iter: 300);
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void A_regular_grid_of_cocircular_sites_gives_valid_cells_that_tile_the_area()
    {
        // every four neighbouring sites lie on a circle: the circumcentres of two triangles coincide
        var sites = Enumerable.Range(0, 12).SelectMany(x => Enumerable.Range(0, 9).Select(y => new Coordinate(x * 10, y * 10))).ToArray();

        var cells = Points(sites).VoronoiDiagram(factory: F);

        Assert.Equal(sites.Length, cells.Count);
        Assert.Equal(220 * 190, cells.Sum(c => c.Cell.Area()), 6);     // (0 0)-(110 80) widened by 55 on every side: 220 by 190
        Assert.All(cells, c => Assert.True(c.Cell.IsValid(), c.Cell.ToWkt()));
        Assert.Equal(100, cells.Single(c => c.Site.X == 50 && c.Site.Y == 40).Cell.Area(), 9);   // an inner cell is a 10 by 10 square
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Large_coordinates_work()
    {
        var rng = new Random(36);
        var sites = Enumerable.Range(0, 200).Select(_ => new Coordinate(500000 + rng.NextDouble() * 4000, 5300000 + rng.NextDouble() * 4000)).ToArray();

        var cells = Points(sites).VoronoiDiagram(factory: F);

        double minX = sites.Min(p => p.X), maxX = sites.Max(p => p.X), minY = sites.Min(p => p.Y), maxY = sites.Max(p => p.Y);
        double margin = Math.Max(maxX - minX, maxY - minY) / 2;
        double area = (maxX - minX + 2 * margin) * (maxY - minY + 2 * margin);
        Assert.Equal(area, cells.Sum(c => c.Cell.Area()), 1e-6 * area);
        Assert.All(cells, c => Assert.True(c.Cell.IsValid()));
    }

    /// <summary>The cell of one site by cutting the rectangle with the bisector to every other site: independent of the library.</summary>
    internal static double ReferenceArea(Coordinate[] sites, Coordinate site, double minX, double minY, double maxX, double maxY)
    {
        var polygon = new List<(double X, double Y)> { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) };
        foreach (var other in sites.Where(o => o != site))
        {
            double mx = (site.X + other.X) / 2, my = (site.Y + other.Y) / 2, nx = other.X - site.X, ny = other.Y - site.Y;
            double Side((double X, double Y) p) => (p.X - mx) * nx + (p.Y - my) * ny;   // not above 0: nearer to the site
            var next = new List<(double X, double Y)>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var cur = polygon[i];
                var prev = polygon[(i + polygon.Count - 1) % polygon.Count];
                double fc = Side(cur), fp = Side(prev);
                if ((fc <= 0) != (fp <= 0))
                {
                    double t = fp / (fp - fc);
                    next.Add((prev.X + t * (cur.X - prev.X), prev.Y + t * (cur.Y - prev.Y)));
                }

                if (fc <= 0)
                {
                    next.Add(cur);
                }
            }

            polygon = next;
            if (polygon.Count < 3)
            {
                return 0;
            }
        }

        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            sum += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(sum) / 2;
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Cells_at_the_border_with_a_circumcentre_far_away_are_exact()
    {
        // almost collinear sites at the border give a circumcentre about 1e17 away; the closure of the cell lies at 1e18, where a crossing
        // with the rectangle computed from the far end is off by 16 (found by a stress test)
        var sites = new Coordinate[]
        {
            new(-43.79, 42), new(30, 48), new(-16, 35.031), new(25, 7), new(30, 9.3), new(13, 48), new(17.646, -39), new(34, -39.1), new(-0, 37),
            new(18.8, -25), new(-20, -4), new(30.2, 6.222), new(-35.1, -7.389), new(-43, 5.328), new(36, -13), new(0, 0), new(-44.4, 5.125),
            new(-47, -0), new(-0, 35), new(-19, 48), new(36.2, -23), new(-1, -45), new(-13, 2.5), new(34.8, 47),
        };
        double minX = sites.Min(p => p.X), maxX = sites.Max(p => p.X), minY = sites.Min(p => p.Y), maxY = sites.Max(p => p.Y);
        double margin = Math.Max(maxX - minX, maxY - minY) / 2;

        var cells = Points(sites).VoronoiDiagram(factory: F);

        Assert.All(cells, c => Assert.Equal(ReferenceArea(sites, c.Site, minX - margin, minY - margin, maxX + margin, maxY + margin), c.Cell.Area(), 1e-6));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void Two_sites_that_differ_by_5e_20_are_split_along_their_bisector()
    {
        // the third site is far away: seen from it, the two close sites are the same point, and the circumcentre must not be computed from there
        var sites = new Coordinate[] { new(29.262840339965408, -46), new(-8.57E-78, -4.877E-20), new(8.177E-87, 6.054E-29) };

        var cells = Points(sites).VoronoiDiagram(factory: F);

        var lower = cells[1].Cell.Envelope();
        var upper = cells[2].Cell.Envelope();
        Assert.Equal(0, lower.MaxY, 1e-9);                              // the bisector is y = -2.4e-20
        Assert.Equal(0, upper.MinY, 1e-9);
        Assert.Equal(75.26284033996541 * 92, cells.Sum(c => c.Cell.Area()), 1e-6);
        Assert.All(cells, c => Assert.True(c.Cell.IsValid()));
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void A_site_lies_in_its_own_cell()
    {
        Pt.Array[2, 60].Sample(sites =>
        {
            var cells = Points(sites).VoronoiDiagram(factory: F);

            Assert.All(cells, c => Assert.NotEqual(Location.Outside, c.Cell.Locate(c.Site)));
        }, iter: 200);
    }

    [Fact]
    [Covers("VoronoiDiagram")]
    public void The_cells_have_the_areas_of_the_NTS_diagram()
    {
        // points in general position; the clip area is the same envelope on both sides
        Pt.Array[4, 50].Sample(sites =>
        {
            var distinct = sites.Distinct().ToArray();
            double minX = distinct.Min(p => p.X), maxX = distinct.Max(p => p.X), minY = distinct.Min(p => p.Y), maxY = distinct.Max(p => p.Y);
            double margin = Math.Max(maxX - minX, maxY - minY) / 2;

            var builder = new VoronoiDiagramBuilder();
            builder.SetSites(distinct.Select(s => new NtsCoordinate(s.X, s.Y)).ToList());
            builder.ClipEnvelope = new NetTopologySuite.Geometries.Envelope(minX - margin, maxX + margin, minY - margin, maxY + margin);
            var diagram = builder.GetDiagram(NetTopologySuite.Geometries.GeometryFactory.Default);

            var ours = Points(sites).VoronoiDiagram(factory: F);
            foreach (var cell in diagram.Geometries)
            {
                var site = (NtsCoordinate)cell.UserData;
                var mine = ours.Single(c => c.Site.X == site.X && c.Site.Y == site.Y).Cell;
                Assert.Equal(cell.Area, mine.Area(), 1e-6 * (1 + cell.Area));
            }
        }, iter: 200);
    }

    // ---------- VoronoiEdges ----------

    [Fact]
    [Covers("VoronoiEdges")]
    public void The_edges_of_two_sites_are_their_bisector_and_there_are_none_for_one()
    {
        var edges = G("MULTIPOINT ((0 0), (10 0))").VoronoiEdges(factory: F);

        Assert.Equal(1, edges.PathCount);
        Assert.Equal(10, edges.Length(), 9);          // x = 5 from y = -5 to y = 5
        Assert.Equal(0, G("POINT (1 1)").VoronoiEdges(factory: F).PathCount);
        Assert.Equal(0, G("POINT EMPTY").VoronoiEdges(factory: F).PathCount);
    }

    [Fact]
    [Covers("VoronoiEdges")]
    public void Every_edge_lies_between_two_equally_near_sites()
    {
        Pt.Array[3, 40].Sample(sites =>
        {
            var distinct = sites.Distinct().ToArray();
            var edges = Points(sites).VoronoiEdges(factory: F);

            for (int i = 0; i < edges.PathCount; i++)
            {
                var a = edges[i][0];
                var b = edges[i][1];
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                var d = distinct.Select(s => Math.Sqrt((s.X - mx) * (s.X - mx) + (s.Y - my) * (s.Y - my))).Order().Take(2).ToArray();
                Assert.True(d.Length < 2 || Math.Abs(d[0] - d[1]) < 1e-6, $"{d[0]} vs {d[1]}");
            }
        }, iter: 200);
    }

    [Fact]
    [Covers("VoronoiEdges")]
    public void The_edges_are_cut_to_a_clip_polygon()
    {
        var sites = G("MULTIPOINT ((2 2), (8 2), (5 8), (14 6), (2 12))");
        var clip = (IPolygon2D)G("POLYGON ((0 0, 12 0, 12 6, 6 6, 6 14, 0 14, 0 0))");

        var edges = sites.VoronoiEdges(clip, F);

        Assert.True(edges.PathCount > 0);
        Assert.True(edges.Within(clip) || edges.CoveredBy(clip));
    }

    // ---------- DelaunayEdges ----------

    [Fact]
    [Covers("DelaunayEdges")]
    public void The_edges_are_those_of_the_triangles()
    {
        Pt.Array[3, 60].Sample(sites =>
        {
            var geometry = Points(sites);
            var triangles = geometry.DelaunayTriangles(F);
            if (triangles.Count == 0)
            {
                Assert.Equal(0, geometry.DelaunayEdges(F).PathCount);
                return;
            }

            var expected = new HashSet<(double, double, double, double)>();
            foreach (var t in triangles)
            {
                for (int i = 0; i < 3; i++)
                {
                    var a = t[0][i];
                    var b = t[0][i + 1];
                    expected.Add(a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a.X, a.Y, b.X, b.Y) : (b.X, b.Y, a.X, a.Y));
                }
            }

            var edges = geometry.DelaunayEdges(F);
            var actual = Enumerable.Range(0, edges.PathCount).Select(i => edges[i]).Select(p =>
            {
                var a = p[0];
                var b = p[1];
                return a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a.X, a.Y, b.X, b.Y) : (b.X, b.Y, a.X, a.Y);
            }).ToList();

            Assert.Equal(actual.Count, actual.Distinct().Count());     // every edge once
            Assert.Equal(expected.Order(), actual.Order());
        }, iter: 200);
    }

    [Fact]
    [Covers("DelaunayEdges")]
    public void A_triangle_has_three_edges_and_collinear_points_none()
    {
        Assert.Equal(3, G("POLYGON ((0 0, 4 0, 0 3, 0 0))").DelaunayEdges(F).PathCount);
        Assert.Equal(0, G("MULTIPOINT ((0 0), (1 1), (2 2))").DelaunayEdges(F).PathCount);
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).DelaunayEdges(F));
    }
}
