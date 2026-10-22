using CsCheck;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;
using NtsLocation = NetTopologySuite.Geometries.Location;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace SpatialNET.Tests.Validation;

/// <summary>
/// <c>MakeValid</c> (ADR 0020). NTS <c>GeometryFixer</c> has the OGC meaning, so it is no oracle; the oracle is an
/// independent even-odd area: the input rings are noded and polygonised with NTS, and a face counts if an odd number
/// of input rings contain it.
/// </summary>
internal static class EvenOdd
{
    public static double Area(Geometry g)
    {
        var rings = new List<LineString>();
        foreach (var polygon in Enumerable.Range(0, g.NumGeometries).Select(g.GetGeometryN).OfType<NtsPolygon>())
        {
            if (polygon.IsEmpty) continue;
            rings.Add(polygon.ExteriorRing);
            rings.AddRange(polygon.InteriorRings);
        }

        if (rings.Count == 0)
        {
            return 0;
        }

        var polygonizer = new Polygonizer();
        polygonizer.Add(UnaryUnionOp.Union(rings.Cast<Geometry>().ToList()));
        double area = 0;
        foreach (var face in polygonizer.GetPolygons())
        {
            var point = face.InteriorPoint.Coordinate;
            int containing = rings.Count(r => RayCrossingCounter.LocatePointInRing(point, r.Coordinates) == NtsLocation.Interior);
            if (containing % 2 == 1)
            {
                area += face.Area;
            }
        }

        return area;
    }
}

public class MakeValidFixtureTests
{
    public static TheoryData<string, string> MakeValidFixtures => FixtureCatalog.Theory("makeValid");

    [Theory]
    [MemberData(nameof(MakeValidFixtures))]
    public void MakeValid(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);

        var actual = geometry.MakeValid(factory: factory);

        Assert.NotSame(geometry, actual);
        Assert.True(actual.IsValid(), "The result of MakeValid must be valid.");
        switch (geometry)
        {
            case IPolygon2D:
                OracleAssert.Close(EvenOdd.Area(Nts.Read(fixture.A)), actual.Area(), 1e-6);
                break;
            case IPolyline2D line:
                OracleAssert.Close(line.Length(), actual.Length(), 1e-9); // only duplicates and degenerate paths go
                break;
            default:
                Assert.Equal(Internal.GeometryAccess.AllPoints(geometry).Count(), Internal.GeometryAccess.AllPoints(actual).Count());
                break;
        }
    }
}

public class MakeValidTests
{
    /// <summary>The same area and the same vertices; the start vertex of a ring and the order of the parts may differ.</summary>
    internal static void AssertSameGeometry(IGeometry2D expected, IGeometry2D actual)
    {
        static (double, double)[] Vertices(IGeometry2D g) =>
            Internal.GeometryAccess.AllPoints(g).Select(p => (p.X, p.Y)).Distinct().Order().ToArray();

        Assert.Equal(expected.Area(), actual.Area(), 1e-9);
        Assert.Equal(Vertices(expected), Vertices(actual));
    }

    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    [Fact]
    public void A_bowtie_becomes_two_triangles()
    {
        var bowtie = (IPolygon2D)G("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))");

        var valid = bowtie.MakeValid(factory: F);

        Assert.False(bowtie.IsValid());
        Assert.True(valid.IsValid());
        Assert.Equal(50, valid.Area(), 6);
        Assert.Equal(2, Internal.PolygonStructure.Build(valid).Count);
    }

    [Fact]
    public void An_inverted_hole_touching_the_shell_and_duplicate_rings_are_repaired()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (0 0, 5 2, 2 5, 0 0), (0 0, 5 2, 2 5, 0 0))");

        var valid = polygon.MakeValid(factory: F);

        Assert.True(valid.IsValid());
        Assert.Equal(100, valid.Area(), 6); // the identical rings cancel under even-odd
    }

    [Fact]
    public void Overlapping_shells_follow_the_fill_rule()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (5 5, 15 5, 15 15, 5 15, 5 5))");

        Assert.Equal(150, polygon.MakeValid(FillRule.EvenOdd, F).Area(), 6);   // the overlap is a hole
        Assert.Equal(175, polygon.MakeValid(FillRule.NonZero, F).Area(), 6);   // the second ring has the same orientation: union
    }

    [Fact]
    public void A_valid_polygon_keeps_its_area_is_a_new_object_and_MakeValid_is_idempotent()
    {
        var square = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))");

        var valid = square.MakeValid(factory: F);

        Assert.NotSame(square, valid);
        Assert.Equal(64, valid.Area(), 9);
        AssertSameGeometry(valid, valid.MakeValid(factory: F));
    }

    [Fact]
    public void A_polygon_without_area_and_an_empty_polygon_become_empty()
    {
        Assert.Equal(0, ((IPolygon2D)G("POLYGON ((0 0, 5 5, 10 10, 0 0))")).MakeValid(factory: F).RingCount);
        Assert.Equal(0, ((IPolygon2D)G("POLYGON EMPTY")).MakeValid(factory: F).RingCount);
    }

    [Fact]
    public void Non_finite_coordinates_are_removed_first()
    {
        var ring = new Coordinate[] { new(0, 0), new(10, 0), new(double.NaN, 5), new(10, 10), new(0, 10), new(0, 0) };
        var polygon = F.CreatePolygon([new PolygonPart(ring)], null);

        var valid = polygon.MakeValid(factory: F);

        Assert.True(valid.IsValid());
        Assert.Equal(100, valid.Area(), 6);
    }

    [Fact]
    public void Polylines_lose_duplicates_non_finite_points_and_short_paths_but_keep_crossings()
    {
        var line = F.CreatePolyline(
        [
            new Coordinate[] { new(0, 0), new(0, 0), new(5, 5), new(double.PositiveInfinity, 1), new(10, 0) },
            new Coordinate[] { new(3, 3), new(3, 3) },        // one distinct point
            new Coordinate[] { new(0, 5), new(10, 5) },
        ], null);

        var valid = line.MakeValid(F);

        Assert.True(valid.IsValid());
        Assert.Equal("MULTILINESTRING ((0 0, 5 5, 10 0), (0 5, 10 5))", valid.ToWkt());
        Assert.Equal(1, ((IPolyline2D)G("LINESTRING (0 0, 10 10, 10 0, 0 10)")).MakeValid(F).PathCount); // a crossing line is valid
    }

    [Fact]
    public void Points_lose_only_non_finite_ones_and_keep_duplicates()
    {
        var points = F.CreateMultiPoint([new Coordinate(1, 1), new Coordinate(1, 1), new Coordinate(double.NaN, 2)], null);

        var valid = points.MakeValid(F);

        Assert.Equal(2, valid.PointCount);
    }

    [Fact]
    public void The_general_overload_dispatches_by_kind_and_handles_points()
    {
        Assert.IsAssignableFrom<IPolygon2D>(G("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))").MakeValid(factory: F));
        Assert.IsAssignableFrom<IPolyline2D>(G("LINESTRING (0 0, 1 1)").MakeValid(factory: F));
        Assert.IsAssignableFrom<IMultiPoint2D>(G("MULTIPOINT ((0 0))").MakeValid(factory: F));
        Assert.IsAssignableFrom<IPoint2D>(G("POINT (1 2)").MakeValid(factory: F));
        Assert.IsAssignableFrom<IMultiPoint2D>(new MPoint(new Coordinate(double.NaN, 1)).MakeValid(factory: F)); // not finite: empty multipoint
    }

    [Fact]
    public void Z_and_M_are_carried_and_restricted_to_the_factory()
    {
        var polygon = MGeo.Polygon(MGeo.Square(0, 0, 10, 10, MGeo.WithM));

        var kept = MGeo.Coordinates(polygon.MakeValid(factory: ZM));
        var dropped = MGeo.Coordinates(polygon.MakeValid(factory: new MFactory(supportsZ: false, supportsM: false)));

        Assert.All(kept, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, 1e-6));
        Assert.All(dropped, c => Assert.False(c.HasM));
    }

    [Fact]
    public void Null_arguments_and_an_invalid_grid_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).MakeValid(factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).MakeValid(F));
        Assert.Throws<ArgumentNullException>(() => ((IMultiPoint2D)null!).MakeValid(F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).MakeValid(factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IPolygon2D)G("POLYGON ((0 0, 1 0, 1 1, 0 0))")).MakeValid(factory: F, gridSize: 0));
    }
}

/// <summary>Property tests of <c>MakeValid</c> (ADR 0020).</summary>
public class MakeValidPropertyTests
{
    private static void AssertSameGeometry(IGeometry2D expected, IGeometry2D actual) => MakeValidTests.AssertSameGeometry(expected, actual);

    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static readonly Gen<(int X, int Y)[]> Ring = Gen.Select(Gen.Int[0, 20], Gen.Int[0, 20]).Array[3, 9];

    private static string Wkt_(IEnumerable<(int X, int Y)[]> rings) =>
        "POLYGON (" + string.Join(", ", rings.Select(r => "(" + string.Join(", ", r.Append(r[0]).Select(p => $"{p.X} {p.Y}")) + ")")) + ")";

    [Fact]
    public void The_result_is_valid_idempotent_and_has_the_even_odd_area()
    {
        Gen.Select(Ring, Ring.Array[0, 2]).Sample((first, others) =>
        {
            string wkt = Wkt_(others.Prepend(first));
            var polygon = (IPolygon2D)Wkt.Read(wkt, F);

            var valid = polygon.MakeValid(factory: F);

            Assert.True(valid.IsValid(), wkt);
            Assert.Equal(EvenOdd.Area(Nts.Read(wkt)), valid.Area(), 1e-6);
            AssertSameGeometry(valid, valid.MakeValid(factory: F));
        });
    }

    [Fact]
    public void A_valid_rectangle_keeps_its_area()
    {
        Gen.Select(Gen.Int[0, 50], Gen.Int[0, 50], Gen.Int[1, 30], Gen.Int[1, 30]).Sample((x, y, w, h) =>
        {
            var polygon = (IPolygon2D)Wkt.Read($"POLYGON (({x} {y}, {x + w} {y}, {x + w} {y + h}, {x} {y + h}, {x} {y}))", F);

            Assert.Equal(w * h, polygon.MakeValid(factory: F).Area(), 9);
        });
    }

    [Fact]
    public void A_union_of_overlapping_squares_as_one_polygon_is_merged_under_NonZero()
    {
        Gen.Select(Gen.Int[0, 15], Gen.Int[0, 15], Gen.Int[1, 15], Gen.Int[1, 15]).Sample((dx, dy, w, h) =>
        {
            // two counter-clockwise rectangles that overlap, in one polygon
            string a = "(0 0, 20 0, 20 20, 0 20, 0 0)";
            string b = $"({dx} {dy}, {dx + w} {dy}, {dx + w} {dy + h}, {dx} {dy + h}, {dx} {dy})";
            var polygon = (IPolygon2D)Wkt.Read($"POLYGON ({a}, {b})", F);
            var square = (IPolygon2D)Wkt.Read($"POLYGON ({a})", F);
            var other = (IPolygon2D)Wkt.Read($"POLYGON ({b})", F);

            var merged = polygon.MakeValid(FillRule.NonZero, F);

            Assert.Equal(square.Union(other, factory: F).Area(), merged.Area(), 1e-6);
        });
    }
}
