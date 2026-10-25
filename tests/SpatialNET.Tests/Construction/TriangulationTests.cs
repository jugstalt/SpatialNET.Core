using CsCheck;
using SpatialNET.Imp;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Triangulation;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>
/// <c>Triangulation</c> (ADR 0037, ADR 0038): the structure of the triangulation, locating and interpolating, contour lines, the constrained
/// Delaunay triangulation and the triangulation of a polygon.
/// </summary>
public class TriangulationTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-50, 50], Gen.Int[-50, 50], Gen.Double[0.05, 0.95], Gen.Double[0.05, 0.95]).Select((x, y, a, b) => new Coordinate(x + a, y + b));

    private static IGeometry2D Points(IEnumerable<Coordinate> pts) => F.CreateMultiPoint(pts.ToArray(), null);

    private static Coordinate[] Corners(Triangulation t, int tri)
    {
        var (a, b, c) = t.Triangle(tri);
        return [t.Vertices[a], t.Vertices[b], t.Vertices[c]];
    }

    private static double Area(Triangulation t) => Enumerable.Range(0, t.TriangleCount).Sum(i =>
    {
        var c = Corners(t, i);
        return ((c[1].X - c[0].X) * (c[2].Y - c[0].Y) - (c[2].X - c[0].X) * (c[1].Y - c[0].Y)) / 2;
    });

    /// <summary>Everything that makes a triangulation a triangulation: counter-clockwise triangles, neighbours that agree, no repeated vertex.</summary>
    private static void AssertStructure(Triangulation t)
    {
        for (int i = 0; i < t.TriangleCount; i++)
        {
            var c = Corners(t, i);
            Assert.True(Orientation.Orient2D(c[0], c[1], c[2]) > 0, "triangle " + i + " is not counter-clockwise");
            var (a, b, d) = t.Triangle(i);
            Assert.Equal(3, new[] { a, b, d }.Distinct().Count());

            for (int k = 0; k < 3; k++)
            {
                int n = t.Neighbour(i, k);
                if (n == -1)
                {
                    continue;
                }

                // the neighbour has the same edge in the other direction, and says the same about being a constraint
                var ids = new[] { a, b, d };
                int u = ids[k], v = ids[(k + 1) % 3];
                var (na, nb, nc) = t.Triangle(n);
                var nids = new[] { na, nb, nc };
                int back = Enumerable.Range(0, 3).Single(j => nids[j] == v && nids[(j + 1) % 3] == u);
                Assert.Equal(i, t.Neighbour(n, back));
                Assert.Equal(t.IsConstrained(i, k), t.IsConstrained(n, back));
            }
        }
    }

    // ---------- the structure ----------

    [Fact]
    [Covers("FromPoints")]
    public void A_square_has_two_triangles_that_are_neighbours()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (4 0), (4 4), (0 4))"));

        Assert.Equal(2, t.TriangleCount);
        Assert.Equal(4, t.Vertices.Count);
        AssertStructure(t);
        Assert.Equal(16, Area(t), 9);
        Assert.Equal(1, Enumerable.Range(0, 3).Count(k => t.Neighbour(0, k) == 1));
    }

    [Fact]
    [Covers("FromPoints")]
    public void Too_few_or_collinear_points_give_no_triangles()
    {
        Assert.Equal(0, Triangulation.FromPoints(G("MULTIPOINT ((0 0), (1 1))")).TriangleCount);
        Assert.Equal(0, Triangulation.FromPoints(G("LINESTRING (0 0, 1 1, 2 2, 5 5)")).TriangleCount);
        Assert.Equal(0, Triangulation.FromPoints(G("POINT EMPTY")).TriangleCount);
        Assert.Equal(-1, Triangulation.FromPoints(G("POINT EMPTY")).Locate(new Coordinate(0, 0)));
    }

    [Fact]
    [Covers("FromPoints")]
    public void The_triangulation_is_consistent_tiles_the_hull_and_matches_DelaunayTriangles()
    {
        Pt.Array[3, 80].Sample(points =>
        {
            var geometry = Points(points);
            var t = Triangulation.FromPoints(geometry);

            AssertStructure(t);
            Assert.Equal(geometry.DelaunayTriangles(F).Count, t.TriangleCount);
            Assert.Equal(geometry.ConvexHull(F).Area(), Area(t), 1e-6 * (1 + Area(t)));
            Assert.Equal(geometry.DelaunayEdges(F).PathCount, t.Edges(F).PathCount);
            Assert.Equal(0, t.ConstrainedEdges(F).PathCount);
        }, iter: 200);
    }

    [Fact]
    [Covers("FromPoints")]
    public void Z_of_the_points_is_kept_and_M_is_not()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ZM ((0 0 5 1), (10 0 7 2), (5 8 9 3))"));

        Assert.Equal([5d, 7d, 9d], t.Vertices.Select(v => v.Z));
        Assert.All(t.Vertices, v => Assert.False(v.HasM));
    }

    [Fact]
    [Covers("FromPoints")]
    public void Index_arguments_are_checked()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (4 0), (4 4), (0 4))"));

        Assert.Throws<ArgumentOutOfRangeException>(() => t.Triangle(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Triangle(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Neighbour(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.IsConstrained(0, -1));
        Assert.Throws<ArgumentNullException>(() => Triangulation.FromPoints(null!));
    }

    // ---------- locating and interpolating ----------

    [Fact]
    [Covers("Locate")]
    public void Locate_finds_the_triangle_that_contains_the_point_or_nothing()
    {
        Gen.Select(Pt.Array[4, 60], Gen.Double[-70, 70], Gen.Double[-70, 70]).Sample((points, x, y) =>
        {
            var t = Triangulation.FromPoints(Points(points));
            var p = new Coordinate(x, y);

            int found = t.Locate(p);
            bool Contains(int i)
            {
                var c = Corners(t, i);
                return Orientation.Orient2D(c[0], c[1], p) >= 0 && Orientation.Orient2D(c[1], c[2], p) >= 0 && Orientation.Orient2D(c[2], c[0], p) >= 0;
            }

            int expected = Enumerable.Range(0, t.TriangleCount).FirstOrDefault(Contains, -1);
            Assert.Equal(expected, found);
        }, iter: 300);
    }

    [Fact]
    [Covers("Locate")]
    public void Locate_on_a_vertex_and_on_an_edge_and_with_threads()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (4 0), (4 4), (0 4))"));

        Assert.True(t.Locate(new Coordinate(0, 0)) >= 0);
        Assert.True(t.Locate(new Coordinate(2, 2)) >= 0);       // on the diagonal, which both triangles share: the smaller index
        Assert.Equal(-1, t.Locate(new Coordinate(5, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Locate(new Coordinate(double.NaN, 0)));

        var many = Enumerable.Range(0, 2000).Select(i => new Coordinate(i % 40 * 0.1, i / 40 * 0.08)).ToArray();
        var expected = many.Select(t.Locate).ToArray();
        var actual = new int[many.Length];
        Parallel.For(0, many.Length, i => actual[i] = t.Locate(many[i]));
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Covers("InterpolateZ")]
    public void The_height_on_a_plane_is_exact_and_at_a_vertex_it_is_the_Z_of_the_vertex()
    {
        Gen.Select(Pt.Array[4, 60], Gen.Double[0, 1], Gen.Double[0, 1]).Sample((points, u, v) =>
        {
            double Plane(Coordinate c) => 3 * c.X - 2 * c.Y + 7;
            var withZ = points.Select(c => c with { Z = Plane(c) }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            if (t.TriangleCount == 0)
            {
                return;
            }

            var tri = Corners(t, (int)(u * (t.TriangleCount - 1)));
            var q = new Coordinate(tri[0].X * (1 - v) / 2 + tri[1].X * (1 + v) / 2 * 0.5 + tri[2].X * 0.25, tri[0].Y * (1 - v) / 2 + tri[1].Y * (1 + v) / 2 * 0.5 + tri[2].Y * 0.25);

            double z = t.InterpolateZ(q);
            if (!double.IsNaN(z))
            {
                Assert.Equal(Plane(q), z, 1e-6);
            }

            Assert.Equal(tri[0].Z, t.InterpolateZ(tri[0]));
        }, iter: 300);
    }

    [Fact]
    [Covers("InterpolateZ")]
    public void The_height_is_NaN_outside_and_without_Z()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (0 10 20))"));

        Assert.Equal(5, t.InterpolateZ(new Coordinate(5, 0)), 9);
        Assert.Equal(10, t.InterpolateZ(new Coordinate(0, 5)), 9);
        Assert.Equal(7.5, t.InterpolateZ(new Coordinate(2.5, 2.5)) + 0 * 1, 9);   // 0 + 10 * 0.25 + 20 * 0.25
        Assert.True(double.IsNaN(t.InterpolateZ(new Coordinate(20, 20))));
        Assert.True(double.IsNaN(Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10))")).InterpolateZ(new Coordinate(2, 2))));
    }

    // ---------- contours ----------

    [Fact]
    [Covers("Contour")]
    public void A_contour_of_a_plane_is_a_straight_line_and_every_vertex_has_the_height()
    {
        var points = Enumerable.Range(0, 11).SelectMany(x => Enumerable.Range(0, 11).Select(y => new Coordinate(x, y + x * 0.01, 2 * x))).ToArray();
        var t = Triangulation.FromPoints(F.CreateMultiPoint(points, null));

        var contour = t.Contour(7, F);

        Assert.Equal(1, contour.PathCount);
        for (int i = 0; i < contour[0].PointCount; i++)
        {
            Assert.Equal(3.5, contour[0][i].X, 1e-9);                    // 2x = 7
            Assert.Equal(7, ((IPoint3D)contour[0][i]).Z, 1e-12);
        }

        Assert.Equal(0, t.Contour(100, F).PathCount);
        Assert.Equal(0, t.Contour(-5, F).PathCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Contour(double.NaN, F));
    }

    [Fact]
    [Covers("Contour")]
    public void A_contour_around_a_cone_is_closed_and_its_points_have_the_height()
    {
        var rng = new Random(37);
        var points = Enumerable.Range(0, 600).Select(_ =>
        {
            double x = rng.NextDouble() * 20 - 10, y = rng.NextDouble() * 20 - 10;
            return new Coordinate(x, y, x * x + y * y);
        }).ToArray();
        var t = Triangulation.FromPoints(F.CreateMultiPoint(points, null));

        var contour = t.Contour(25, F);

        Assert.True(contour.PathCount >= 1);
        var ring = contour[0];
        Assert.Equal(ring[0].X, ring[ring.PointCount - 1].X);        // closed
        Assert.Equal(ring[0].Y, ring[ring.PointCount - 1].Y);
        for (int i = 0; i < contour.PathCount; i++)
        {
            for (int j = 0; j < contour[i].PointCount; j++)
            {
                Assert.Equal(25, t.InterpolateZ(new Coordinate(contour[i][j].X, contour[i][j].Y)), 1e-6);
            }
        }
    }

    // ---------- the constrained triangulation ----------

    private static void AssertConstrainedDelaunay(Triangulation t)
    {
        for (int i = 0; i < t.TriangleCount; i++)
        {
            var (a, b, c) = t.Triangle(i);
            var ids = new[] { a, b, c };
            for (int k = 0; k < 3; k++)
            {
                int n = t.Neighbour(i, k);
                if (n == -1 || t.IsConstrained(i, k))
                {
                    continue;
                }

                var apex = new[] { t.Triangle(n).A, t.Triangle(n).B, t.Triangle(n).C }.Single(v => v != ids[k] && v != ids[(k + 1) % 3]);
                Assert.True(InCircle.Sign(t.Vertices[ids[k]], t.Vertices[ids[(k + 1) % 3]], t.Vertices[ids[(k + 2) % 3]], t.Vertices[apex]) <= 0,
                    "edge " + k + " of triangle " + i + " is not locally Delaunay");
            }
        }
    }

    private static bool OnSegment(Coordinate p, Coordinate a, Coordinate b)
    {
        double len2 = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        double u = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / len2;
        if (u < -1e-9 || u > 1 + 1e-9)
        {
            return false;
        }

        double dx = a.X + u * (b.X - a.X) - p.X, dy = a.Y + u * (b.Y - a.Y) - p.Y;
        return Math.Sqrt(dx * dx + dy * dy) < 1e-7;
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void A_constraint_through_the_middle_is_kept_where_Delaunay_would_flip_it()
    {
        // without the constraint the diagonal of the thin rectangle would be the short one (1 0)-(1 4)... here the long one is forced
        var points = G("MULTIPOINT ((0 0), (10 0), (10 1), (0 1))");
        var plain = Triangulation.FromPoints(points);
        var forced = Triangulation.FromPointsAndLines(points, G("LINESTRING (0 0, 10 1)"), F);

        AssertStructure(forced);
        AssertConstrainedDelaunay(forced);
        Assert.Equal(2, forced.TriangleCount);
        Assert.Equal(1, forced.ConstrainedEdges(F).PathCount);
        Assert.Equal(Math.Sqrt(101), forced.ConstrainedEdges(F).Length(), 9);
        Assert.Equal(plain.TriangleCount, forced.TriangleCount);
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void Constraints_between_random_points_are_kept_as_chains_of_edges()
    {
        Gen.Select(Pt.Array[8, 60], Gen.Int[0, 1000].Array[2, 8]).Sample((points, picks) =>
        {
            var distinct = points.Distinct().ToArray();
            if (distinct.Length < 4)
            {
                return;
            }

            var segments = new List<(Coordinate A, Coordinate B)>();
            for (int i = 0; i + 1 < picks.Length; i += 2)
            {
                var a = distinct[picks[i] % distinct.Length];
                var b = distinct[picks[i + 1] % distinct.Length];
                var candidate = (a, b);
                if (a != b && !ConstraintSet.HasProperCrossing([.. segments, candidate]))
                {
                    segments.Add(candidate);
                }
            }

            var lines = F.CreatePolyline(segments.Select(s => (ReadOnlyMemory<Coordinate>)new[] { s.A, s.B }).ToArray(), null);
            var t = Triangulation.FromPointsAndLines(Points(distinct), lines, F);
            if (t.TriangleCount == 0)
            {
                return;
            }

            AssertStructure(t);
            AssertConstrainedDelaunay(t);
            Assert.Equal(Points(distinct).ConvexHull(F).Area(), Area(t), 1e-6 * (1 + Area(t)));

            // every constraint is a chain of constrained edges, and every constrained edge lies on a constraint
            var constrained = t.ConstrainedEdges(F);
            var edges = Enumerable.Range(0, constrained.PathCount).Select(i => (A: Coordinate(constrained[i][0]), B: Coordinate(constrained[i][1]))).ToList();
            foreach (var (a, b) in segments)
            {
                for (int s = 1; s < 20; s++)
                {
                    var p = new Coordinate(a.X + (b.X - a.X) * s / 20, a.Y + (b.Y - a.Y) * s / 20);
                    Assert.Contains(edges, e => OnSegment(p, e.A, e.B));
                }
            }

            foreach (var e in edges)
            {
                var mid = new Coordinate((e.A.X + e.B.X) / 2, (e.A.Y + e.B.Y) / 2);
                Assert.Contains(segments, s => OnSegment(mid, s.A, s.B));
            }
        }, iter: 300);
    }

    private static Coordinate Coordinate(IPoint2D p) => new(p.X, p.Y);

    [Fact]
    [Covers("FromPointsAndLines")]
    public void A_long_break_line_through_many_points_is_kept_and_the_rest_stays_Delaunay()
    {
        var rng = new Random(38);
        var points = Enumerable.Range(0, 2000).Select(_ => new Coordinate(rng.NextDouble() * 1000, rng.NextDouble() * 1000)).OrderBy(p => p.X).ToArray();
        var chain = points.Where((_, i) => i % 40 == 0).ToArray();      // increasing in X: the line never crosses itself
        var line = F.CreatePolyline([chain], null);

        var t = Triangulation.FromPointsAndLines(Points(points), line, F);

        AssertStructure(t);
        AssertConstrainedDelaunay(t);
        Assert.True(t.ConstrainedEdges(F).PathCount >= chain.Length - 1);   // at least one edge per segment
        Assert.Equal(line.Length(), t.ConstrainedEdges(F).Length(), 1e-6);
    }

    [Fact]
    [Covers("FromPolygon")]
    public void A_polygon_with_thousands_of_vertices_is_triangulated()
    {
        var rng = new Random(39);
        int n = 3000;
        var ring = Enumerable.Range(0, n).Select(i =>
        {
            double angle = 2 * Math.PI * i / n, radius = 500 + rng.NextDouble() * 400;
            return new Coordinate(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToList();
        ring.Add(ring[0]);
        var polygon = F.CreatePolygon([new PolygonPart(ring.ToArray())], null);

        var t = Triangulation.FromPolygon(polygon, F);

        Assert.Equal(n - 2, t.TriangleCount);
        Assert.Equal(polygon.Area(), Area(t), 1e-6 * polygon.Area());
        AssertStructure(t);
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void A_constraint_that_passes_through_a_vertex_is_split_there()
    {
        var t = Triangulation.FromPointsAndLines(G("MULTIPOINT ((0 0), (5 0), (10 0), (5 5), (5 -5))"), G("LINESTRING (0 0, 10 0)"), F);

        AssertStructure(t);
        Assert.Equal(2, t.ConstrainedEdges(F).PathCount);
        Assert.Equal(10, t.ConstrainedEdges(F).Length(), 9);
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void Crossing_constraints_are_noded_and_the_crossing_becomes_a_vertex()
    {
        var points = G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10))");

        var t = Triangulation.FromPointsAndLines(points, G("MULTILINESTRING ((0 0, 10 10), (0 10, 10 0))"), F);

        AssertStructure(t);
        Assert.Equal(5, t.Vertices.Count);
        Assert.Contains(t.Vertices, v => v.X == 5 && v.Y == 5);
        Assert.Equal(4, t.ConstrainedEdges(F).PathCount);
        Assert.Equal(4, t.TriangleCount);
        Assert.Equal(100, Area(t), 9);
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void Constraints_that_overlap_or_repeat_are_fine_and_a_polygon_gives_its_rings()
    {
        var points = G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10), (5 5))");

        var overlap = Triangulation.FromPointsAndLines(points, G("MULTILINESTRING ((0 0, 5 5), (5 5, 10 10), (0 0, 10 10), (0 0, 10 10))"), F);
        Assert.Equal(2, overlap.ConstrainedEdges(F).PathCount);

        var ring = Triangulation.FromPointsAndLines(points, G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"), F);
        Assert.Equal(4, ring.ConstrainedEdges(F).PathCount);
        AssertStructure(ring);
        AssertConstrainedDelaunay(ring);
    }

    [Fact]
    [Covers("FromPointsAndLines")]
    public void Arguments_of_the_constrained_triangulation_are_checked()
    {
        var points = G("MULTIPOINT ((0 0), (10 0), (10 10))");

        Assert.Throws<ArgumentNullException>(() => Triangulation.FromPointsAndLines(points, null!, F));
        Assert.Throws<ArgumentNullException>(() => Triangulation.FromPointsAndLines(null!, points, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => Triangulation.FromPointsAndLines(points, G("LINESTRING (0 0, 5 5)"), F, 0));
        Assert.Equal(1, Triangulation.FromPointsAndLines(points, G("POINT EMPTY"), F).TriangleCount);
    }

    // ---------- the triangulation of a polygon ----------

    [Fact]
    [Covers("FromPolygon")]
    public void A_square_a_concave_polygon_and_a_polygon_with_a_hole_are_triangulated_inside()
    {
        var square = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
        Assert.Equal(2, Triangulation.FromPolygon(square).TriangleCount);

        var concave = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 6 10, 6 4, 4 4, 4 10, 0 10, 0 0))");   // a U: 8 vertices
        var tc = Triangulation.FromPolygon(concave);
        Assert.Equal(6, tc.TriangleCount);
        Assert.Equal(concave.Area(), Area(tc), 9);
        AssertStructure(tc);

        var holed = (IPolygon2D)G("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))");
        var th = Triangulation.FromPolygon(holed);
        Assert.Equal(8, th.TriangleCount);                     // 8 vertices - 2 + 2 for the hole
        Assert.Equal(300, Area(th), 9);
        AssertStructure(th);
        Assert.Equal(8, th.ConstrainedEdges(F).PathCount);
        Assert.Equal(120, th.ConstrainedEdges(F).Length(), 9);   // 80 + 40
    }

    [Fact]
    [Covers("FromPolygon")]
    public void The_triangles_of_a_polygon_lie_inside_it_and_cover_it()
    {
        // star-shaped polygons: random radii at increasing angles
        Gen.Select(Gen.Int[5, 40], Gen.Double[0.2, 1].Array[40], Gen.Bool).Sample((n, radii, withHole) =>
        {
            var ring = Enumerable.Range(0, n).Select(i =>
            {
                double angle = 2 * Math.PI * i / n;
                return new Coordinate(100 * radii[i] * Math.Cos(angle), 100 * radii[i] * Math.Sin(angle));
            }).ToList();
            ring.Add(ring[0]);
            var rings = new List<Coordinate[]> { ring.ToArray() };
            if (withHole)
            {
                rings.Add([new Coordinate(-3, -3), new Coordinate(-3, 3), new Coordinate(3, 3), new Coordinate(3, -3), new Coordinate(-3, -3)]);
            }

            var polygon = F.CreatePolygon([new PolygonPart(rings[0], rings.Skip(1).Select(r => (ReadOnlyMemory<Coordinate>)r).ToArray())], null);
            if (!polygon.IsValid())
            {
                return;
            }

            var t = Triangulation.FromPolygon(polygon, F);
            int vertices = n + (withHole ? 4 : 0);
            Assert.Equal(vertices - 2 + (withHole ? 2 : 0), t.TriangleCount);
            Assert.Equal(polygon.Area(), Area(t), 1e-6 * polygon.Area());
            AssertStructure(t);
            AssertConstrainedDelaunay(t);
            for (int i = 0; i < t.TriangleCount; i++)
            {
                var c = Corners(t, i);
                var centroid = new Coordinate((c[0].X + c[1].X + c[2].X) / 3, (c[0].Y + c[1].Y + c[2].Y) / 3);
                Assert.Equal(Location.Inside, polygon.Locate(centroid));
            }
        }, iter: 200);
    }

    [Fact]
    [Covers("FromPolygon")]
    public void The_triangulation_of_a_polygon_has_the_triangle_count_and_area_of_NTS()
    {
        Gen.Select(Gen.Int[5, 40], Gen.Double[0.2, 1].Array[40]).Sample((n, radii) =>
        {
            var ring = Enumerable.Range(0, n).Select(i =>
            {
                double angle = 2 * Math.PI * i / n;
                return new Coordinate(100 * radii[i] * Math.Cos(angle), 100 * radii[i] * Math.Sin(angle));
            }).ToList();
            ring.Add(ring[0]);
            var polygon = F.CreatePolygon([new PolygonPart(ring.ToArray())], null);
            if (!polygon.IsValid())
            {
                return;
            }

            var ours = Triangulation.FromPolygon(polygon, F);
            var nts = NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(Oracle.Nts.ToNts(polygon));

            Assert.Equal(nts.NumGeometries, ours.TriangleCount);
            Assert.Equal(nts.Area, Area(ours), 1e-6 * nts.Area);
        }, iter: 200);
    }

    [Fact]
    [Covers("Triangulate")]
    public void Triangulate_gives_the_triangles_as_polygons()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 6 10, 6 4, 4 4, 4 10, 0 10, 0 0))");

        var triangles = polygon.Triangulate(F);

        Assert.Equal(6, triangles.Count);
        Assert.Equal(polygon.Area(), triangles.Sum(x => x.Area()), 9);
        Assert.All(triangles, x => Assert.True(x.IsValid()));
        Assert.Empty(F.CreatePolygon([], null).Triangulate(F));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Triangulate(F));
    }

    [Fact]
    [Covers("FromPolygon")]
    public void A_self_crossing_polygon_is_read_by_even_odd_after_noding()
    {
        var bowTie = (IPolygon2D)G("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))");

        var t = Triangulation.FromPolygon(bowTie, F);

        Assert.Equal(2, t.TriangleCount);                      // the two triangles; the crossing at (5 5) is a vertex
        Assert.Equal(50, Area(t), 9);
        Assert.Contains(t.Vertices, v => v.X == 5 && v.Y == 5);
        Assert.Equal(0, Triangulation.FromPolygon((IPolygon2D)G("POLYGON ((0 0, 5 5, 10 10, 0 0))"), F).TriangleCount);   // collinear: no area
    }
}
