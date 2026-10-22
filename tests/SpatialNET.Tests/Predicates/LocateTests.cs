using CsCheck;
using NetTopologySuite.Algorithm;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;
using NtsLocation = NetTopologySuite.Geometries.Location;

namespace SpatialNET.Tests.Predicates;

/// <summary>
/// <c>Locate</c> against the NTS <c>PointLocator</c> (ADR 0022/0008): the test probes every vertex, every segment midpoint,
/// the centre of the envelope and points just outside, for the reference and the foreign implementation.
/// </summary>
public class LocateFixtureTests
{
    public static TheoryData<string, string> LocateFixtures => FixtureCatalog.Theory("locate");

    private static Location Map(NtsLocation location) => location switch
    {
        NtsLocation.Interior => Location.Inside,
        NtsLocation.Boundary => Location.Boundary,
        _ => Location.Outside,
    };

    [Theory]
    [MemberData(nameof(LocateFixtures))]
    public void Locate(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);
        // the oracle gets polygons in SpatialNET semantics (even-odd, rings without area ignored, ADR 0003)
        var nts = Nts.ToNts(geometry);

        var vertices = nts.Coordinates.ToList();
        var probes = new List<NetTopologySuite.Geometries.Coordinate>(vertices);
        for (int i = 1; i < vertices.Count; i++)
        {
            probes.Add(new((vertices[i - 1].X + vertices[i].X) / 2, (vertices[i - 1].Y + vertices[i].Y) / 2));
        }

        if (!nts.IsEmpty)
        {
            var e = nts.EnvelopeInternal;
            probes.Add(new((e.MinX + e.MaxX) / 2, (e.MinY + e.MaxY) / 2));
            probes.Add(new(e.MaxX + 1, e.MaxY + 1));
            probes.Add(new(e.MinX - 1, e.MinY - 1));
        }
        else
        {
            probes.Add(new(0, 0));
        }

        var locator = new PointLocator();
        foreach (var probe in probes)
        {
            var expected = Map(locator.Locate(probe, nts));
            var actual = geometry.Locate(new Coordinate(probe.X, probe.Y));

            Assert.True(expected == actual, $"{id} at ({probe.X:R} {probe.Y:R}): expected {expected}, got {actual}.");
        }
    }
}

public class LocateTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static Coordinate C(double x, double y) => new(x, y);

    private const string Donut = "POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))";

    [Fact]
    public void A_polygon_has_inside_boundary_and_outside_and_a_hole_is_outside()
    {
        var donut = G(Donut);

        Assert.Equal(Location.Inside, donut.Locate(C(2, 2)));
        Assert.Equal(Location.Boundary, donut.Locate(C(0, 10)));
        Assert.Equal(Location.Boundary, donut.Locate(C(0, 0)));
        Assert.Equal(Location.Outside, donut.Locate(C(10, 10)));    // in the hole
        Assert.Equal(Location.Boundary, donut.Locate(C(5, 10)));    // on the ring of the hole
        Assert.Equal(Location.Outside, donut.Locate(C(25, 10)));
        Assert.Equal(Location.Inside, donut.Locate(new Imp.Point(2, 2)));  // the IPoint2D overload
    }

    [Fact]
    public void An_island_in_a_hole_is_inside_and_orientation_does_not_matter()
    {
        var polygon = G("POLYGON ((0 0, 0 30, 30 30, 30 0, 0 0), (5 5, 25 5, 25 25, 5 25, 5 5), (10 10, 10 20, 20 20, 20 10, 10 10))");

        Assert.Equal(Location.Inside, polygon.Locate(C(2, 2)));
        Assert.Equal(Location.Outside, polygon.Locate(C(7, 7)));   // the hole
        Assert.Equal(Location.Inside, polygon.Locate(C(15, 15)));  // the island in the hole
    }

    [Fact]
    public void A_polyline_has_its_boundary_at_the_ends_by_the_mod_2_rule()
    {
        var line = G("LINESTRING (0 0, 10 0, 10 10)");
        var star = G("MULTILINESTRING ((0 0, 5 5), (10 0, 5 5), (5 10, 5 5))");
        var chain = G("MULTILINESTRING ((0 0, 5 5), (5 5, 10 0))");
        var closed = G("LINESTRING (0 0, 10 0, 10 10, 0 0)");

        Assert.Equal(Location.Boundary, line.Locate(C(0, 0)));
        Assert.Equal(Location.Inside, line.Locate(C(10, 0)));    // a vertex in the middle
        Assert.Equal(Location.Inside, line.Locate(C(5, 0)));
        Assert.Equal(Location.Outside, line.Locate(C(5, 1)));
        Assert.Equal(Location.Boundary, star.Locate(C(5, 5)));   // three ends meet: odd
        Assert.Equal(Location.Inside, chain.Locate(C(5, 5)));    // two ends meet: even
        Assert.Equal(Location.Inside, closed.Locate(C(0, 0)));   // a closed path has no boundary
    }

    [Fact]
    public void Points_have_no_boundary()
    {
        Assert.Equal(Location.Inside, G("POINT (1 2)").Locate(C(1, 2)));
        Assert.Equal(Location.Outside, G("POINT (1 2)").Locate(C(1, 3)));
        Assert.Equal(Location.Inside, G("MULTIPOINT ((0 0), (5 5))").Locate(C(5, 5)));
        Assert.Equal(Location.Outside, G("MULTIPOINT ((0 0), (5 5))").Locate(C(5, 6)));
    }

    [Fact]
    public void Empty_geometries_are_outside_and_invalid_polygons_follow_even_odd()
    {
        Assert.Equal(Location.Outside, G("POLYGON EMPTY").Locate(C(0, 0)));
        Assert.Equal(Location.Outside, G("LINESTRING EMPTY").Locate(C(0, 0)));
        Assert.Equal(Location.Outside, G("MULTIPOINT EMPTY").Locate(C(0, 0)));

        var bowtie = G("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))");
        Assert.Equal(Location.Inside, bowtie.Locate(C(8, 5)));
        Assert.Equal(Location.Outside, bowtie.Locate(C(5, 8)));
        Assert.Equal(Location.Boundary, bowtie.Locate(C(5, 5)));
    }

    [Fact]
    public void A_point_a_hair_outside_a_vertex_is_decided_exactly()
    {
        // Found by a stress test: the edge from (-36 0) to (-31.4 -46) crosses the ray 1e-80 to the right of the point, which a
        // floating-point crossing computation rounds to the point itself; the exact test counts it, so the point is outside.
        var triangle = G("POLYGON ((-30 9, -36 0, -31.4 -46, -30 9))");

        Assert.Equal(Location.Outside, triangle.Locate(C(-36, -9.862E-80)));
        Assert.Equal(Location.Outside, triangle.Prepare().Locate(C(-36, -9.862E-80)));
        Assert.Equal(Location.Boundary, triangle.Locate(C(-36, 0)));
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        var square = G("POLYGON ((0 0, 1 0, 1 1, 0 0))");

        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Locate(C(0, 0)));
        Assert.Throws<ArgumentNullException>(() => square.Locate((IPoint2D)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Locate(C(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Locate(C(0, double.PositiveInfinity)));
    }

    [Fact]
    public void The_prepared_geometry_gives_the_same_answers()
    {
        var donut = G(Donut);
        var prepared = donut.Prepare();

        foreach (var p in new[] { C(2, 2), C(0, 10), C(10, 10), C(5, 10), C(25, 10), C(20, 20), C(-1, 5) })
        {
            Assert.Equal(donut.Locate(p), prepared.Locate(p));
        }

        Assert.Equal(Location.Boundary, G("LINESTRING (0 0, 10 0)").Prepare().Locate(C(0, 0)));
        Assert.Equal(Location.Outside, G("POLYGON EMPTY").Prepare().Locate(C(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.Locate(C(double.NaN, 0)));
    }
}

public class LocatePropertyTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IPolygon2D Rectangle(int x, int y, int w, int h, bool hole)
    {
        var shell = new Coordinate[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x, y) };
        ReadOnlyMemory<Coordinate>[] holes = hole && w >= 4 && h >= 4
            ? [new Coordinate[] { new(x + 1, y + 1), new(x + 1, y + h - 1), new(x + w - 1, y + h - 1), new(x + w - 1, y + 1), new(x + 1, y + 1) }]
            : [];
        return F.CreatePolygon([new PolygonPart(shell, holes)], null);
    }

    [Fact]
    public void Locate_agrees_with_the_predicates_and_the_prepared_geometry()
    {
        Gen.Select(Gen.Int[-10, 10], Gen.Int[-10, 10], Gen.Int[1, 12], Gen.Int[1, 12], Gen.Bool, Gen.Int[-12, 24], Gen.Int[-12, 24]).Sample((x, y, w, h, hole, px, py) =>
        {
            var polygon = Rectangle(x, y, w, h, hole);
            var point = F.CreatePoint(new Coordinate(px, py), null);

            var location = polygon.Locate(new Coordinate(px, py));

            Assert.Equal(location == Location.Inside, polygon.Contains(point));
            Assert.Equal(location == Location.Outside, polygon.Disjoint(point));
            Assert.Equal(location == Location.Boundary, polygon.Intersects(point) && !polygon.Contains(point));
            Assert.Equal(location, polygon.Prepare().Locate(new Coordinate(px, py)));
        });
    }
}
