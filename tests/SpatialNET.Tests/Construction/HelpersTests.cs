using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>Hand-written cases for the measure and point helpers of ADR 0019.</summary>
[Covers("Reverse")]
public class HelpersTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    // ---------- NearestPoints ----------

    [Fact]
    public void NearestPoints_of_two_lines()
    {
        var pair = G("LINESTRING (0 0, 10 0)").NearestPoints(G("LINESTRING (3 2, 7 5)"))!.Value;

        Assert.Equal(C(3, 0), pair.OnA);
        Assert.Equal(C(3, 2), pair.OnB);
    }

    [Fact]
    public void NearestPoints_of_crossing_segments_is_the_crossing_on_both_sides()
    {
        var pair = G("LINESTRING (0 0, 10 10)").NearestPoints(G("LINESTRING (0 10, 10 0)"))!.Value;

        Assert.Equal(5, pair.OnA.X, 1e-12);
        Assert.Equal(5, pair.OnA.Y, 1e-12);
        Assert.Equal(5, pair.OnB.X, 1e-12);
        Assert.Equal(5, pair.OnB.Y, 1e-12);
    }

    [Fact]
    public void NearestPoints_inside_an_area_is_the_same_point()
    {
        var pair = G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))").NearestPoints(G("POINT (4 4)"))!.Value;

        Assert.Equal(C(4, 4), pair.OnA);
        Assert.Equal(C(4, 4), pair.OnB);
    }

    [Fact]
    public void NearestPoints_of_a_point_in_a_hole_lies_on_the_hole_boundary()
    {
        var polygon = G("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 15 5, 15 15, 5 15, 5 5))");

        var pair = polygon.NearestPoints(G("POINT (7 10)"))!.Value;

        Assert.Equal(C(5, 10), pair.OnA);
        Assert.Equal(C(7, 10), pair.OnB);
    }

    [Fact]
    public void NearestPoints_returns_the_first_of_equally_near_pairs()
    {
        var pair = G("MULTIPOINT ((0 0), (10 0))").NearestPoints(G("POINT (5 0)"))!.Value;

        Assert.Equal(C(0, 0), pair.OnA);
    }

    [Fact]
    public void NearestPoints_interpolates_Z_and_M_on_the_segments()
    {
        var a = new MPolyline([new[] { C(0, 0, 0, 0), C(10, 0, 10, 100) }]);
        var b = new MPoint(C(4, 5, 7, 9));

        var pair = a.NearestPoints(b)!.Value;

        Assert.Equal(C(4, 0, 4, 40), pair.OnA);
        Assert.Equal(C(4, 5, 7, 9), pair.OnB);
    }

    [Fact]
    public void NearestPoints_of_empty_geometries_is_null_and_null_arguments_throw()
    {
        Assert.Null(G("POINT EMPTY").NearestPoints(G("POINT (1 1)")));
        Assert.Null(G("POINT (1 1)").NearestPoints(G("POLYGON EMPTY")));
        Assert.Throws<ArgumentNullException>(() => G("POINT (1 1)").NearestPoints(null!));
    }

    // ---------- IsWithinDistance ----------

    [Fact]
    public void IsWithinDistance_compares_with_the_distance()
    {
        var a = G("LINESTRING (0 0, 10 0)");
        var b = G("POINT (5 3)");

        Assert.True(a.IsWithinDistance(b, 3));
        Assert.True(a.IsWithinDistance(b, 4));
        Assert.False(a.IsWithinDistance(b, 2.999));
        Assert.False(a.IsWithinDistance(G("POINT (50 50)"), 10)); // far apart: decided by the envelopes
    }

    [Fact]
    public void IsWithinDistance_of_zero_means_touching_or_overlapping()
    {
        var square = G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        Assert.True(square.IsWithinDistance(G("POINT (5 5)"), 0));    // inside
        Assert.True(square.IsWithinDistance(G("POINT (10 5)"), 0));   // on the boundary
        Assert.False(square.IsWithinDistance(G("POINT (10.5 5)"), 0));
    }

    [Fact]
    public void IsWithinDistance_validates_the_distance_and_treats_empty_as_false()
    {
        var a = G("POINT (0 0)");

        Assert.Throws<ArgumentOutOfRangeException>(() => a.IsWithinDistance(a, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.IsWithinDistance(a, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.IsWithinDistance(a, double.PositiveInfinity));
        Assert.False(a.IsWithinDistance(G("POINT EMPTY"), 100));
    }

    // ---------- PointOnSurface ----------

    [Fact]
    public void PointOnSurface_of_a_polygon_with_a_hole_lies_in_the_ring_of_area()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (2 2, 18 2, 18 18, 2 18, 2 2))");

        var point = polygon.PointOnSurface()!.Value;

        Assert.True(polygon.Contains(F.CreatePoint(point, null)));
    }

    [Fact]
    public void PointOnSurface_of_a_c_shaped_polygon_is_not_the_centroid_outside()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 2, 2 2, 2 8, 10 8, 10 10, 0 10, 0 0))");

        Assert.False(polygon.Contains(F.CreatePoint(polygon.Centroid()!.Value, null))); // the centroid is in the gap
        Assert.True(polygon.Contains(F.CreatePoint(polygon.PointOnSurface()!.Value, null)));
    }

    [Fact]
    public void PointOnSurface_of_lines_and_points()
    {
        Assert.Equal(C(10, 0), G("LINESTRING (0 0, 10 0, 10 10)").PointOnSurface());       // the interior vertex
        Assert.Equal(C(5, 0), G("LINESTRING (0 0, 10 0)").PointOnSurface());               // no interior vertex: the middle
        Assert.Equal(C(3, 3), G("MULTIPOINT ((0 0), (3 3), (10 10))").PointOnSurface());   // nearest to the centroid
        Assert.Equal(C(1, 2), G("POINT (1 2)").PointOnSurface());
    }

    [Fact]
    public void PointOnSurface_of_an_empty_geometry_is_null_and_drops_Z_and_M()
    {
        Assert.Null(G("POLYGON EMPTY").PointOnSurface());
        Assert.Equal(C(1, 2), new MPoint(C(1, 2, 3, 4)).PointOnSurface());
    }

    // ---------- Boundary ----------

    [Fact]
    public void Boundary_of_a_polygon_is_its_normalized_rings()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 0 10, 10 10, 10 0, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))"); // shell clockwise, hole counter-clockwise

        var boundary = polygon.Boundary(F);

        Assert.Equal("MULTILINESTRING ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))", boundary.ToWkt());
    }

    [Fact]
    public void Boundary_of_a_polyline_uses_the_mod_2_rule()
    {
        Assert.Equal("MULTIPOINT ((0 0), (10 0))", ((IPolyline2D)G("LINESTRING (0 0, 10 0)")).Boundary(F).ToWkt());
        Assert.Equal("MULTIPOINT EMPTY", ((IPolyline2D)G("LINESTRING (0 0, 10 0, 10 10, 0 0)")).Boundary(F).ToWkt()); // closed
        // three paths meet in (5 5): odd, so it is a boundary point; the two ends that meet only twice are not
        var star = (IPolyline2D)G("MULTILINESTRING ((0 0, 5 5), (10 0, 5 5), (5 10, 5 5))");
        Assert.Equal("MULTIPOINT ((0 0), (5 5), (10 0), (5 10))", star.Boundary(F).ToWkt());
        var chain = (IPolyline2D)G("MULTILINESTRING ((0 0, 5 5), (5 5, 10 0))");
        Assert.Equal("MULTIPOINT ((0 0), (10 0))", chain.Boundary(F).ToWkt()); // (5 5) occurs twice
    }

    [Fact]
    public void Boundary_of_points_is_an_empty_multipoint_and_of_empty_geometries_is_empty()
    {
        Assert.Equal(0, Assert.IsAssignableFrom<IMultiPoint2D>(G("POINT (1 2)").Boundary(F)).PointCount);
        Assert.Equal(0, Assert.IsAssignableFrom<IMultiPoint2D>(G("MULTIPOINT ((1 2))").Boundary(F)).PointCount);
        Assert.Equal(0, Assert.IsAssignableFrom<IPolyline2D>(G("POLYGON EMPTY").Boundary(F)).PathCount);
        Assert.Equal(0, Assert.IsAssignableFrom<IMultiPoint2D>(G("LINESTRING EMPTY").Boundary(F)).PointCount);
    }

    [Fact]
    public void Boundary_keeps_Z_and_M_and_restricts_them_to_the_factory()
    {
        var line = new MPolyline([new[] { C(0, 0, 1, 10), C(10, 0, 2, 20) }]);

        var kept = Assert.IsAssignableFrom<IMultiPoint2D>(line.Boundary(ZM));
        var dropped = Assert.IsAssignableFrom<IMultiPoint2D>(line.Boundary(new MFactory(false, false)));

        Assert.Equal(new[] { C(0, 0, 1, 10), C(10, 0, 2, 20) }, MGeo.Coordinates(kept));
        Assert.All(MGeo.Coordinates(dropped), c => Assert.False(c.HasZ || c.HasM));
    }

    // ---------- Densify ----------

    [Fact]
    public void Densify_splits_long_segments_into_equal_pieces()
    {
        var line = (IPolyline2D)G("LINESTRING (0 0, 10 0, 10 1)");

        Assert.Equal("LINESTRING (0 0, 2.5 0, 5 0, 7.5 0, 10 0, 10 1)", line.Densify(3, F).ToWkt()); // 10 → 4 pieces; 1 stays
    }

    [Fact]
    public void Densify_interpolates_Z_and_M_and_keeps_original_vertices()
    {
        var line = new MPolyline([new[] { C(0, 0, 0, 0), C(10, 0, 10, 100) }]);

        var dense = line.Densify(5, ZM);

        Assert.Equal(new[] { C(0, 0, 0, 0), C(5, 0, 5, 50), C(10, 0, 10, 100) }, MGeo.Coordinates(dense));
    }

    [Fact]
    public void Densify_keeps_a_polygon_valid_and_normalized()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 0 4, 4 4, 4 0, 0 0))"); // clockwise input

        var dense = polygon.Densify(2, F);

        Assert.True(dense.IsValid());
        Assert.Equal(16, dense.Area());
        Assert.Equal("POLYGON ((0 0, 2 0, 4 0, 4 2, 4 4, 2 4, 0 4, 0 2, 0 0))", dense.ToWkt());
    }

    [Fact]
    public void Densify_copies_points_and_works_through_the_general_overload()
    {
        Assert.Equal("POINT (1 2)", G("POINT (1 2)").Densify(1, F).ToWkt());
        Assert.Equal("MULTIPOINT ((1 2), (3 4))", G("MULTIPOINT ((1 2), (3 4))").Densify(1, F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 1 0, 2 0)", G("LINESTRING (0 0, 2 0)").Densify(1, F).ToWkt());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e-12)] // would create an unreasonable number of vertices
    public void Densify_rejects_an_invalid_or_unreasonable_length(double max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => G("LINESTRING (0 0, 100000 0)").Densify(max, F));
    }

    // ---------- Reverse ----------

    [Fact]
    public void Reverse_reverses_every_path_and_the_order_of_the_paths()
    {
        var line = (IPolyline2D)G("MULTILINESTRING ((0 0, 1 1, 2 0), (5 5, 6 6))");

        var reversed = line.Reverse(F);

        Assert.Equal("MULTILINESTRING ((6 6, 5 5), (2 0, 1 1, 0 0))", reversed.ToWkt());
        Assert.Equal("MULTILINESTRING ((0 0, 1 1, 2 0), (5 5, 6 6))", reversed.Reverse(F).ToWkt());
    }

    [Fact]
    public void Reverse_agrees_with_NTS_and_keeps_Z_and_M()
    {
        var line = (IPolyline2D)G("LINESTRING ZM (0 0 1 10, 5 5 2 20, 9 0 3 30)");

        var reversed = line.Reverse(F);

        Assert.Equal("LINESTRING ZM (9 0 3 30, 5 5 2 20, 0 0 1 10)", reversed.ToWkt());
        Assert.Equal(Nts.Read("LINESTRING (0 0, 5 5, 9 0)").Reverse().Coordinates.Select(c => (c.X, c.Y)),
            MGeo.Coordinates(((IPolyline2D)G("LINESTRING (0 0, 5 5, 9 0)")).Reverse(ZM)).Select(c => (c.X, c.Y)));
    }

    [Fact]
    public void Reverse_of_an_empty_line_is_empty()
    {
        Assert.Equal(0, ((IPolyline2D)G("LINESTRING EMPTY")).Reverse(F).PathCount);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Reverse(F));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Boundary(F));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Boundary(F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Boundary(F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Densify(1, F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).PointOnSurface());
    }
}

/// <summary>Property tests of the helpers (ADR 0019).</summary>
public class HelpersPropertyTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static readonly Gen<(double X, double Y)[]> Vertices = Gen.Select(Gen.Double[-100, 100], Gen.Double[-100, 100]).Array[2, 8];

    private static IPolyline2D Line((double X, double Y)[] v) =>
        F.CreatePolyline([v.Select(p => new Coordinate(p.X, p.Y)).ToArray()], null);

    private static IPolygon2D Rect(double x, double y, double w, double h, bool hole)
    {
        var shell = new Coordinate[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x, y) };
        var holes = hole
            ? new[] { (ReadOnlyMemory<Coordinate>)new Coordinate[] { new(x + w / 4, y + h / 4), new(x + w / 4, y + h * 3 / 4), new(x + w * 3 / 4, y + h * 3 / 4), new(x + w * 3 / 4, y + h / 4), new(x + w / 4, y + h / 4) } }
            : [];
        return F.CreatePolygon([new PolygonPart(shell, holes)], null);
    }

    [Fact]
    public void Distance_is_the_distance_of_the_nearest_points_and_IsWithinDistance_agrees()
    {
        Gen.Select(Vertices, Vertices, Gen.Double[0, 2]).Sample((a, b, factor) =>
        {
            var (la, lb) = (Line(a), Line(b));

            var pair = la.NearestPoints(lb)!.Value;
            double distance = la.Distance(lb);

            Assert.Equal(distance, Internal.Rings.Distance(pair.OnA, pair.OnB), 1e-9);
            Assert.True(F.CreatePoint(pair.OnA, null).Distance(la) <= 1e-9);
            Assert.True(F.CreatePoint(pair.OnB, null).Distance(lb) <= 1e-9);
            double d = distance * factor;
            Assert.Equal(distance <= d, la.IsWithinDistance(lb, d));
        });
    }

    [Fact]
    public void Reverse_twice_gives_the_line_back()
    {
        Vertices.Sample(v =>
        {
            var line = Line(v);

            Assert.Equal(line.ToWkt(), line.Reverse(F).Reverse(F).ToWkt());
        });
    }

    [Fact]
    public void Densify_keeps_the_length_and_all_vertices_and_bounds_the_segments()
    {
        Gen.Select(Vertices, Gen.Double[1, 40]).Sample((v, max) =>
        {
            var line = Line(v);

            var dense = line.Densify(max, F);

            var original = Internal.Rings.Coordinates(line[0]);
            var points = Internal.Rings.Coordinates(dense[0]);
            Assert.Equal(line.Length(), dense.Length(), 1e-6);
            Assert.All(original, c => Assert.Contains(points, p => p.X == c.X && p.Y == c.Y));
            for (int i = 1; i < points.Length; i++)
            {
                Assert.True(Internal.Rings.Distance(points[i - 1], points[i]) <= max * (1 + 1e-9));
            }
        });
    }

    [Fact]
    public void The_boundary_of_a_polygon_has_the_length_of_its_perimeter_and_the_point_on_it_lies_inside()
    {
        Gen.Select(Gen.Double[-50, 50], Gen.Double[-50, 50], Gen.Double[1, 40], Gen.Double[1, 40], Gen.Bool).Sample((x, y, w, h, hole) =>
        {
            var polygon = Rect(x, y, w, h, hole);

            Assert.Equal(polygon.Length(), polygon.Boundary(F).Length(), 1e-9);
            Assert.True(polygon.Contains(F.CreatePoint(polygon.PointOnSurface()!.Value, null)));
        });
    }
}
