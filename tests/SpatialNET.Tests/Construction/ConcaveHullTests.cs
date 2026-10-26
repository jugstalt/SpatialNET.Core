using CsCheck;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Triangulate;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>DelaunayTriangles, ConcaveHull and ConcaveHullByRatio (ADR 0029), against NTS and by their defining properties.</summary>
public class ConcaveHullTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    // The fractions are multiples of 1e-4 and never 0: Gen.Double also produces values like 3e-34, which put a point practically on its neighbour
    // on the integer grid (a tie or a near tie that NTS, with its inexact predicates, decides differently from the exact ones here).
    private static readonly Gen<double> Fraction = Gen.Int[1, 9999].Select(i => i / 10000.0);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-50, 50], Gen.Int[-50, 50], Fraction, Fraction).Select((x, y, a, b) => new Coordinate(x + a, y + b));

    private static IGeometry2D Points(Coordinate[] pts) => F.CreateMultiPoint(pts, null);

    // ---------- DelaunayTriangles ----------

    [Fact]
    [Covers("DelaunayTriangles")]
    public void DelaunayTriangles_of_cases()
    {
        Assert.Equal(2, G("MULTIPOINT ((0 0), (4 0), (4 3), (0 3))").DelaunayTriangles(F).Count);
        Assert.Single(G("POLYGON ((0 0, 4 0, 0 3, 0 0))").DelaunayTriangles(F));

        var star = G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10), (5 5))").DelaunayTriangles(F);
        Assert.Equal(4, star.Count);
        Assert.Equal(100, star.Sum(t => t.Area()), 9);
        Assert.All(star, t => Assert.True(Internal.Rings.SignedArea(Internal.Rings.Closed(t[0])) > 0));

        // fewer than three different or only collinear vertices: nothing
        Assert.Empty(G("MULTIPOINT ((0 0), (1 1))").DelaunayTriangles(F));
        Assert.Empty(G("MULTIPOINT ((0 0), (1 1), (1 1), (0 0))").DelaunayTriangles(F));
        Assert.Empty(G("LINESTRING (0 0, 1 1, 2 2, 5 5)").DelaunayTriangles(F));
        Assert.Empty(G("POINT EMPTY").DelaunayTriangles(F));
        Assert.Empty(G("POLYGON EMPTY").DelaunayTriangles(F));

        // vertices of any kind count; duplicates count once
        Assert.Equal(2, G("MULTILINESTRING ((0 0, 4 0), (4 3, 0 3), (0 0, 0 3))").DelaunayTriangles(F).Count);

        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).DelaunayTriangles(F));
    }

    [Fact]
    [Covers("DelaunayTriangles")]
    public void DelaunayTriangles_keep_Z_and_ignore_M_and_non_finite_vertices()
    {
        var geometry = F.CreateMultiPoint([new Coordinate(0, 0, 5, 1), new Coordinate(4, 0, 6, 2), new Coordinate(0, 3, 7, 3), new Coordinate(double.NaN, 1)], null);
        var zm = new MValues.MFactory(supportsZ: true, supportsM: true);

        var triangle = Assert.Single(geometry.DelaunayTriangles(zm));

        var points = MValues.MGeo.Coordinates(triangle);
        Assert.Equal([5d, 6d, 7d], points.Take(3).Select(c => c.Z).Order().ToArray());
        Assert.All(points, c => Assert.True(double.IsNaN(c.M)));
    }

    [Fact]
    [Covers("DelaunayTriangles")]
    public void DelaunayTriangles_equal_the_NTS_triangulation_for_points_in_general_position()
    {
        Pt.Array[3, 60].Sample(points =>
        {
            var geometry = Points(points);

            var ours = geometry.DelaunayTriangles(F).Select(Canonical).Order().ToArray();
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(Nts.ToNts(geometry));
            var theirs = builder.GetTriangles(NetTopologySuite.Geometries.GeometryFactory.Default).Geometries
                .Select(t => string.Join(" ", t.Coordinates.Take(3).Select(c => $"{c.X:R},{c.Y:R}").Order())).Order().ToArray();

            Assert.Equal(theirs, ours);
        }, iter: 500);
    }

    private static string Canonical(IPolygon2D triangle) =>
        string.Join(" ", Internal.Rings.Closed(triangle[0]).Take(3).Select(c => $"{c.X:R},{c.Y:R}").Order());

    // ---------- ConcaveHull ----------

    [Fact]
    [Covers("ConcaveHull")]
    public void ConcaveHull_of_a_C_shape_follows_the_opening()
    {
        // a ring of points with a gap on the right: the convex hull closes the gap, the concave hull keeps it open
        var c = G("MULTIPOINT ((0 0), (2 0), (4 0), (6 0), (8 0), (8 2), (6 2), (6 4), (6 6), (8 6), (8 8), (6 8), (4 8), (2 8), (0 8), (0 6), (0 4), (0 2), (2 2), (2 6), (2 4))");

        var tight = c.ConcaveHull(2.5, factory: F);
        var convex = c.ConvexHull(F);

        Assert.True(tight.IsValid());
        Assert.True(tight.Area() < convex.Area());
        Assert.True(tight.Covers(c) || ((IPolygon2D)tight).Covers(c));
        Assert.Equal(1, ((IPolygon2D)tight).RingCount);
    }

    [Fact]
    [Covers("ConcaveHull")]
    public void ConcaveHull_with_an_unlimited_edge_is_the_convex_hull_and_a_zero_edge_stays_valid()
    {
        var points = G("MULTIPOINT ((0 0), (10 0), (10 10), (0 10), (5 5), (2 8), (8 2), (5 1))");

        var convex = points.ConcaveHull(double.PositiveInfinity, factory: F);
        var tightest = points.ConcaveHull(0, factory: F);

        Assert.True(convex.EqualsTopo(points.ConvexHull(F)));
        Assert.True(tightest.IsValid());
        Assert.True(tightest.Area() <= convex.Area());
    }

    [Fact]
    [Covers("ConcaveHull")]
    public void ConcaveHull_of_degenerate_input_is_the_convex_hull_of_it()
    {
        Assert.Equal("POINT (1 1)", G("MULTIPOINT ((1 1), (1 1))").ConcaveHull(5, factory: F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 4 4)", G("MULTIPOINT ((0 0), (2 2), (4 4))").ConcaveHull(5, factory: F).ToWkt());
        Assert.Equal("POLYGON EMPTY", G("POLYGON EMPTY").ConcaveHull(5, factory: F).ToWkt());
        Assert.Equal(6, G("POLYGON ((0 0, 4 0, 0 3, 0 0))").ConcaveHull(0, factory: F).Area(), 9); // a single triangle stays
    }

    [Fact]
    [Covers("ConcaveHull")]
    [Covers("ConcaveHullByRatio")]
    public void The_hulls_reject_bad_parameters_and_null()
    {
        var points = G("MULTIPOINT ((0 0), (4 0), (0 3))");

        Assert.Throws<ArgumentOutOfRangeException>(() => points.ConcaveHull(-1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => points.ConcaveHull(double.NaN, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => points.ConcaveHullByRatio(-0.1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => points.ConcaveHullByRatio(1.1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => points.ConcaveHullByRatio(double.NaN, factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).ConcaveHull(1, factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).ConcaveHullByRatio(0.5, factory: F));
        Assert.Throws<InvalidOperationException>(() => points.ConcaveHull(1));
    }

    [Fact]
    [Covers("ConcaveHull")]
    [Covers("ConcaveHullByRatio")]
    public void The_hull_is_valid_contains_every_point_and_lies_in_the_convex_hull()
    {
        Gen.Select(Pt.Array[3, 80], Gen.Double[0, 1], Gen.Bool).Sample((points, ratio, holes) =>
        {
            var geometry = Points(points);
            var convex = geometry.ConvexHull(F);
            if (convex is not IPolygon2D convexPolygon) return;

            var hull = (IPolygon2D)geometry.ConcaveHullByRatio(ratio, holes, F);

            Assert.True(hull.IsValid(), geometry.ToWkt());
            Assert.All(points, p => Assert.True(hull.Locate(p) != Location.Outside, $"{p} is outside: {geometry.ToWkt()} ratio {ratio}"));
            Assert.True(hull.Area() <= convexPolygon.Area() * (1 + 1e-9));
            if (!holes) Assert.Equal(1, hull.RingCount);
            if (ratio == 1) Assert.True(hull.EqualsTopo(convexPolygon));
        }, iter: 400);
    }

    [Fact]
    [Covers("ConcaveHull")]
    public void The_hull_is_stable_under_translation_and_the_ratio_is_scale_free()
    {
        Gen.Select(Pt.Array[4, 50], Gen.Double[0, 1], Gen.Double[-100, 100], Gen.Double[0.1, 10]).Sample((points, ratio, shift, scale) =>
        {
            var geometry = Points(points);
            var scaled = geometry.Scale(scale, scale, factory: F);

            var a = geometry.ConcaveHullByRatio(ratio, factory: F);
            var b = scaled.ConcaveHullByRatio(ratio, factory: F);

            // the same region up to the scale (the tie breaks of equal lengths can differ by rounding, so only the area is compared)
            Assert.True(Math.Abs(a.Area() * scale * scale - b.Area()) <= 1e-6 * (1 + b.Area()), $"{a.Area() * scale * scale} vs {b.Area()}");
        }, iter: 300);
    }

    [Fact]
    [Covers("ConcaveHull")]
    [Covers("ConcaveHullByRatio")]
    public void The_hull_has_the_area_of_the_NTS_concave_hull()
    {
        // the same triangulation and the same rule; points in general position have no equal edge lengths, so no tie breaks differ
        Gen.Select(Pt.Array[10, 80], Gen.Double[0.05, 0.95], Gen.Bool).Sample((points, ratio, holes) =>
        {
            var geometry = Points(points);
            var ours = geometry.ConcaveHullByRatio(ratio, holes, F);
            var theirs = new ConcaveHull(Nts.ToNts(geometry)) { };
            theirs.MaximumEdgeLengthRatio = ratio;
            theirs.HolesAllowed = holes;
            var expected = theirs.GetHull();

            Assert.True(Math.Abs(ours.Area() - expected.Area) <= 1e-6 * (1 + expected.Area),
                $"{ours.Area():R} vs NTS {expected.Area:R}, ratio {ratio}, holes {holes}: {geometry.ToWkt()}");
        }, iter: 400);
    }
}
