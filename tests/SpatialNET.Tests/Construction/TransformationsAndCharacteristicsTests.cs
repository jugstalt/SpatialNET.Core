using CsCheck;
using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries.Utilities;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>
/// Translate, Scale, Rotate, Transform, IsSimple, IsClosed, IsRing, EqualsExact and HausdorffDistance (ADR 0025). Coordinate-wise
/// operations have no topological edge cases, so hand-written cases and property tests against NTS take the place of fixtures.
/// </summary>
public class TransformationsTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static string Wkt_(IGeometry2D g) => g.ToWkt();

    [Fact]
    [Covers("Translate")]
    public void Translate_moves_every_kind_and_keeps_Z_and_M()
    {
        Assert.Equal("POINT (4 6)", Wkt_(G("POINT (1 2)").Translate(3, 4, factory: F)));
        Assert.Equal("MULTIPOINT ((4 6), (5 7))", Wkt_(G("MULTIPOINT ((1 2), (2 3))").Translate(3, 4, factory: F)));
        Assert.Equal("LINESTRING (3 4, 4 5)", Wkt_(G("LINESTRING (0 0, 1 1)").Translate(3, 4, factory: F)));
        Assert.Equal("POLYGON ((3 4, 5 4, 5 6, 3 6, 3 4))", Wkt_(G("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))").Translate(3, 4, factory: F)));

        var zm = new MFactory(supportsZ: true, supportsM: true);
        var moved = (IPolyline2D)new MPolyline([new[] { new Coordinate(0, 0, 5, 7), new Coordinate(1, 1, 6, 8) }]).Translate(10, 20, factory: zm);
        Assert.Equal([new Coordinate(10, 20, 5, 7), new Coordinate(11, 21, 6, 8)], MGeo.Coordinates(moved));

        var withoutZ = new MFactory(supportsZ: false, supportsM: true);
        Assert.Equal([new Coordinate(10, 20, double.NaN, 7), new Coordinate(11, 21, double.NaN, 8)],
            MGeo.Coordinates(new MPolyline([new[] { new Coordinate(0, 0, 5, 7), new Coordinate(1, 1, 6, 8) }]).Translate(10, 20, factory: withoutZ)));
    }

    [Fact]
    [Covers("Scale")]
    public void Scale_about_an_origin_and_mirroring_keep_polygons_valid()
    {
        var square = (IPolygon2D)G("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0), (0.5 0.5, 0.5 1.5, 1.5 1.5, 1.5 0.5, 0.5 0.5))");

        var doubled = square.Scale(2, 3, new Coordinate(1, 1), factory: F);
        Assert.Equal(square.Area() * 6, doubled.Area(), 9);
        Assert.Equal(new Envelope2D(-1, -2, 3, 4), doubled.Envelope());

        var mirrored = square.Scale(-1, 1, factory: F);
        Assert.True(mirrored.IsValid());
        Assert.Equal(square.Area(), mirrored.Area(), 9);
        Assert.Equal(new Envelope2D(-2, 0, 0, 2), mirrored.Envelope());
        // shell counter-clockwise, hole clockwise again
        Assert.True(Internal.Rings.SignedArea(Internal.Rings.Closed(mirrored[0])) > 0);
        Assert.True(Internal.Rings.SignedArea(Internal.Rings.Closed(mirrored[1])) < 0);

        var flat = square.Scale(1, 0, factory: F); // collapses
        Assert.Equal(0, flat.Area());
    }

    [Fact]
    [Covers("Rotate")]
    public void Rotate_by_quarter_turns_is_exact()
    {
        var line = (IPolyline2D)G("LINESTRING (1 0, 2 0, 2 1)");

        Assert.Equal("LINESTRING (0 1, 0 2, -1 2)", Wkt_(line.Rotate(Math.PI / 2, factory: F)));
        Assert.Equal("LINESTRING (-1 0, -2 0, -2 -1)", Wkt_(line.Rotate(Math.PI, factory: F)));
        Assert.Equal("LINESTRING (0 -1, 0 -2, 1 -2)", Wkt_(line.Rotate(-Math.PI / 2, factory: F)));
        Assert.Equal("LINESTRING (1 0, 2 0, 2 1)", Wkt_(line.Rotate(2 * Math.PI, factory: F)));
        Assert.Equal("LINESTRING (2 1, 2 2, 1 2)", Wkt_(line.Rotate(Math.PI / 2, new Coordinate(1, 1), factory: F)));
        Assert.Equal("POINT (0 1)", Wkt_(G("POINT (1 0)").Rotate(5 * Math.PI / 2, factory: F)));
    }

    [Fact]
    [Covers("Transform")]
    public void Transform_maps_every_vertex_and_rejects_non_finite_results()
    {
        var polygon = G("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))");

        Assert.Equal("POLYGON ((0 0, 8 0, 8 4, 0 4, 0 0))", Wkt_(polygon.Transform(c => c with { X = c.X * 2 }, factory: F)));
        Assert.Throws<InvalidOperationException>(() => polygon.Transform(c => new Coordinate(double.NaN, c.Y), factory: F));
        Assert.Throws<InvalidOperationException>(() => G("LINESTRING (0 0, 1 1)").Translate(double.MaxValue, 0, factory: F).Translate(double.MaxValue, 0, factory: F));
        Assert.Throws<ArgumentNullException>(() => polygon.Transform(null!, factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Transform(c => c, factory: F));
    }

    [Fact]
    [Covers("Translate")]
    [Covers("Scale")]
    [Covers("Rotate")]
    public void Invalid_parameters_empty_geometries_and_null_are_handled()
    {
        var line = G("LINESTRING (0 0, 1 1)");

        Assert.Throws<ArgumentOutOfRangeException>(() => line.Translate(double.NaN, 0, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Translate(0, double.PositiveInfinity, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Scale(double.NaN, 1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Scale(1, 1, new Coordinate(double.NaN, 0), factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Rotate(double.NegativeInfinity, factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Translate(1, 1, factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Rotate(1, factory: F));

        Assert.Equal("LINESTRING EMPTY", Wkt_(G("LINESTRING EMPTY").Translate(1, 1, factory: F)));
        Assert.Equal("POLYGON EMPTY", Wkt_(G("POLYGON EMPTY").Rotate(1, factory: F)));
        Assert.Equal("MULTIPOINT EMPTY", Wkt_(G("MULTIPOINT EMPTY").Scale(2, 2, factory: F)));
    }

    // ---------- property tests against NTS ----------

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-50, 50], Gen.Int[-50, 50], Gen.Double[0, 1]).Select((x, y, e) => new Coordinate(x + e, y - e / 3));

    private static IGeometry2D Polygon(Coordinate[] shell) =>
        F.CreatePolygon([new PolygonPart(shell.Append(shell[0]).ToArray())], null).MakeValid(factory: F, gridSize: 1e-3);

    [Fact]
    [Covers("Rotate")]
    public void Rotate_Scale_and_Translate_agree_with_the_NTS_affine_transformation()
    {
        Gen.Select(Pt.Array[3, 8], Gen.Double[-7, 7], Gen.Double[-3, 3], Gen.Double[-3, 3], Gen.Double[-20, 20], Gen.Double[-20, 20], Pt).Sample((points, angle, sx, sy, dx, dy, origin) =>
        {
            var line = F.CreatePolyline([points], null);

            var ours = line.Rotate(angle, origin, factory: F).Scale(sx, sy, origin, factory: F).Translate(dx, dy, factory: F);

            var affine = AffineTransformation.RotationInstance(angle, origin.X, origin.Y);
            affine.Compose(AffineTransformation.ScaleInstance(sx, sy, origin.X, origin.Y));
            affine.Compose(AffineTransformation.TranslationInstance(dx, dy));
            var expected = affine.Transform(Nts.ToNts(line));

            var (a, b) = (Internal.GeometryAccess.AllPoints(ours).Select(Internal.GeometryAccess.ToCoordinate).ToArray(), expected.Coordinates);
            Assert.Equal(b.Length, a.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.True(Math.Abs(a[i].X - b[i].X) <= 1e-9 * (1 + Math.Abs(b[i].X)) && Math.Abs(a[i].Y - b[i].Y) <= 1e-9 * (1 + Math.Abs(b[i].Y)),
                    $"{a[i]} vs {b[i]}");
            }
        });
    }

    [Fact]
    [Covers("Rotate")]
    [Covers("Scale")]
    public void Rigid_motions_and_mirroring_keep_area_and_validity()
    {
        Gen.Select(Pt.Array[3, 8], Gen.Double[-7, 7], Gen.Double[-20, 20], Gen.Double[-20, 20], Gen.Bool).Sample((points, angle, dx, dy, mirror) =>
        {
            var polygon = Polygon(points);
            if (Internal.GeometryAccess.IsEmpty(polygon)) return;

            var moved = polygon.Rotate(angle, factory: F).Translate(dx, dy, factory: F);
            var mirrored = mirror ? moved.Scale(-1, 1, factory: F) : moved;

            Assert.True(Math.Abs(polygon.Area() - mirrored.Area()) <= 1e-9 * (1 + polygon.Area()), $"{polygon.Area():R} vs {mirrored.Area():R}");
            Assert.True(mirrored.IsValid(), mirrored.ToWkt());
        });
    }
}

public class CharacteristicsTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    [Theory]
    [Covers("IsSimple")]
    [InlineData("POINT (1 1)", true)]
    [InlineData("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", true)]
    [InlineData("MULTIPOINT ((1 1), (2 2))", true)]
    [InlineData("MULTIPOINT ((1 1), (2 2), (1 1))", false)]
    [InlineData("LINESTRING EMPTY", true)]
    [InlineData("LINESTRING (0 0, 4 4)", true)]
    [InlineData("LINESTRING (0 0, 4 4, 8 0)", true)]
    [InlineData("LINESTRING (0 0, 4 4, 0 4, 4 0)", false)]                      // proper crossing
    [InlineData("LINESTRING (0 0, 4 0, 4 4, 0 4, 0 0)", true)]                  // closed ring
    [InlineData("LINESTRING (0 0, 4 0, 4 4, 0 4, 2 0)", false)]                 // end point on an own segment
    [InlineData("LINESTRING (0 0, 4 0, 4 4, 0 0, 4 0)", false)]                 // revisits a vertex
    [InlineData("LINESTRING (0 0, 4 0, 2 0)", false)]                           // doubles back
    [InlineData("LINESTRING (0 0, 2 0, 4 0)", true)]                            // collinear continuation
    [InlineData("LINESTRING (0 0, 0 0, 4 4)", true)]                            // repeated point
    [InlineData("MULTILINESTRING ((0 0, 4 0), (4 0, 4 4))", true)]              // meet at end points
    [InlineData("MULTILINESTRING ((0 0, 4 0), (2 -2, 2 2))", false)]            // cross
    [InlineData("MULTILINESTRING ((0 0, 4 0), (2 0, 2 2))", false)]             // T: end point on the interior of another
    [InlineData("MULTILINESTRING ((0 0, 4 0), (2 0, 6 0))", false)]             // overlap
    [InlineData("MULTILINESTRING ((0 0, 2 0), (2 0, 2 2), (2 0, 2 -2))", true)] // three ends in one point
    [InlineData("MULTILINESTRING ((0 0, 2 0), (2 0, 2 2), (2 0, 2 -2), (2 0, 4 0))", true)] // four ends in one point
    [InlineData("MULTILINESTRING ((0 0, 4 0, 4 4, 0 4, 0 0), (0 0, -3 -3))", false)] // a ring touched at its start
    public void IsSimple_of_cases(string wkt, bool expected)
    {
        Assert.Equal(expected, G(wkt).IsSimple());
    }

    [Theory]
    [Covers("IsClosed")]
    [InlineData("LINESTRING EMPTY", false)]
    [InlineData("LINESTRING (0 0, 4 0)", false)]
    [InlineData("LINESTRING (0 0, 4 0, 4 4, 0 0)", true)]
    [InlineData("MULTILINESTRING ((0 0, 4 0, 4 4, 0 0), (9 9, 8 8, 9 8, 9 9))", true)]
    [InlineData("MULTILINESTRING ((0 0, 4 0, 4 4, 0 0), (9 9, 8 8))", false)]
    public void IsClosed_of_cases(string wkt, bool expected)
    {
        Assert.Equal(expected, ((IPolyline2D)G(wkt)).IsClosed());
    }

    [Theory]
    [Covers("IsRing")]
    [InlineData("LINESTRING EMPTY", false)]
    [InlineData("LINESTRING (0 0, 4 0, 4 4, 0 0)", true)]
    [InlineData("LINESTRING (0 0, 4 0, 0 0)", false)]                              // too few points / doubles back
    [InlineData("LINESTRING (0 0, 4 4, 0 4, 4 0, 0 0)", false)]                    // closed, not simple
    [InlineData("LINESTRING (0 0, 4 0, 4 4)", false)]                              // open
    [InlineData("MULTILINESTRING ((0 0, 4 0, 4 4, 0 0), (9 9, 8 8, 9 8, 9 9))", false)] // two paths
    public void IsRing_of_cases(string wkt, bool expected)
    {
        Assert.Equal(expected, ((IPolyline2D)G(wkt)).IsRing());
    }

    [Fact]
    [Covers("EqualsExact")]
    public void EqualsExact_compares_the_structure_as_given()
    {
        Assert.True(G("POINT (1 2)").EqualsExact(G("POINT (1 2)")));
        Assert.False(G("POINT (1 2)").EqualsExact(G("POINT (1 2.0000001)")));
        Assert.True(G("POINT (1 2)").EqualsExact(G("POINT (1 2.0000001)"), 1e-6));
        Assert.False(G("POINT (1 2)").EqualsExact(G("MULTIPOINT ((1 2))")));                 // other kind
        Assert.False(G("LINESTRING (0 0, 1 1)").EqualsExact(G("LINESTRING (1 1, 0 0)")));    // direction counts
        Assert.False(G("MULTIPOINT ((0 0), (1 1))").EqualsExact(G("MULTIPOINT ((1 1), (0 0))"))); // order counts
        Assert.True(G("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))").EqualsExact(G("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")));
        Assert.False(G("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))").EqualsExact(G("POLYGON ((4 0, 4 4, 0 4, 0 0, 4 0))"))); // other start
        Assert.True(G("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))").EqualsTopo(G("POLYGON ((4 0, 4 4, 0 4, 0 0, 4 0))")));
        Assert.True(G("POLYGON EMPTY").EqualsExact(G("POLYGON EMPTY")));
        Assert.False(G("POLYGON EMPTY").EqualsExact(G("LINESTRING EMPTY")));
        Assert.Throws<ArgumentOutOfRangeException>(() => G("POINT (1 2)").EqualsExact(G("POINT (1 2)"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => G("POINT (1 2)").EqualsExact(G("POINT (1 2)"), double.NaN));
        Assert.Throws<ArgumentNullException>(() => G("POINT (1 2)").EqualsExact(null!));
    }

    // ---------- property tests against NTS ----------

    private static readonly Gen<Coordinate> Cell = Gen.Select(Gen.Int[0, 5], Gen.Int[0, 5]).Select((x, y) => new Coordinate(x, y));

    private static readonly Gen<Coordinate[]> Path = Cell.Array[2, 5].Select(p => p.Where((c, i) => i == 0 || c != p[i - 1]).ToArray()).Where(p => p.Length >= 2);

    [Fact]
    [Covers("IsSimple")]
    public void IsSimple_agrees_with_NTS_on_lines_on_a_small_grid()
    {
        Path.Array[1, 3].Sample(paths =>
        {
            var line = F.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null);

            var expected = Nts.ToNts(line).IsSimple;

            Assert.True(expected == line.IsSimple(), $"{line.ToWkt()}: NTS {expected}");
        }, iter: 20_000);
    }

    [Fact]
    [Covers("IsRing")]
    [Covers("IsClosed")]
    public void IsClosed_and_IsRing_agree_with_NTS_on_single_paths()
    {
        Path.Sample(path =>
        {
            var line = F.CreatePolyline([path], null);
            var nts = (NetTopologySuite.Geometries.LineString)Nts.ToNts(line);

            Assert.Equal(nts.IsClosed, line.IsClosed());
            Assert.Equal(nts.IsRing, line.IsRing());
        }, iter: 20_000);
    }

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-30, 30], Gen.Int[-30, 30], Gen.Double[0, 1]).Select((x, y, e) => new Coordinate(x + e, y - e / 3));

    private static IGeometry2D Make(int kind, Coordinate[] pts) => kind switch
    {
        0 => F.CreatePolygon([new PolygonPart(pts.Append(pts[0]).ToArray())], null).MakeValid(factory: F, gridSize: 1e-3),
        1 => F.CreatePolyline([pts], null).MakeValid(F),
        _ => F.CreateMultiPoint(pts, null),
    };

    [Fact]
    [Covers("HausdorffDistance")]
    public void HausdorffDistance_agrees_with_the_NTS_discrete_distance()
    {
        Gen.Select(Gen.Int[0, 2], Pt.Array[3, 9], Gen.Int[0, 2], Pt.Array[1, 6], Gen.Int[1, 5]).Sample((k1, p1, k2, p2, pieces) =>
        {
            var (a, b) = (Make(k1, p1), Make(k2, p2));
            if (Internal.GeometryAccess.IsEmpty(a) || Internal.GeometryAccess.IsEmpty(b)) return;

            double fraction = 1.0 / pieces;
            double expected = DiscreteHausdorffDistance.Distance(Nts.ToNts(a), Nts.ToNts(b), fraction);

            Assert.Equal(expected, a.HausdorffDistance(b, fraction), 1e-9);
            Assert.Equal(a.HausdorffDistance(b, fraction), b.HausdorffDistance(a, fraction), 12);
        });
    }

    [Fact]
    [Covers("HausdorffDistance")]
    public void HausdorffDistance_of_cases()
    {
        Assert.Equal(0, G("LINESTRING (0 0, 10 0)").HausdorffDistance(G("LINESTRING (0 0, 10 0)")));
        Assert.Equal(5, G("POINT (0 0)").HausdorffDistance(G("POINT (3 4)")));
        // the apex of the second line is 5 away from the first
        Assert.Equal(5, G("LINESTRING (0 0, 10 0)").HausdorffDistance(G("LINESTRING (0 0, 5 5, 10 0)")), 12);
        Assert.True(double.IsNaN(G("POINT EMPTY").HausdorffDistance(G("POINT (1 1)"))));
        Assert.Throws<ArgumentOutOfRangeException>(() => G("POINT (1 1)").HausdorffDistance(G("POINT (2 2)"), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => G("POINT (1 1)").HausdorffDistance(G("POINT (2 2)"), 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => G("POINT (1 1)").HausdorffDistance(G("POINT (2 2)"), double.NaN));
        Assert.Throws<ArgumentNullException>(() => G("POINT (1 1)").HausdorffDistance(null!));
    }
}
