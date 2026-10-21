using NetTopologySuite.Densify;
using NetTopologySuite.Operation.Distance;
using SpatialNET.Internal;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Measures;

/// <summary>
/// Fixture tests of <c>NearestPoints</c>, <c>IsWithinDistance</c>, <c>PointOnSurface</c>, <c>Boundary</c> and
/// <c>Densify</c> against NTS (ADR 0019/0008), each for the reference and the foreign implementation. Where the
/// result is not unique (nearest points among ties, the point on a surface) the test checks the property.
/// </summary>
public class MeasureHelpersFixtureTests
{
    public static TheoryData<string, string> NearestPointsFixtures => FixtureCatalog.Theory("nearestPoints");
    public static TheoryData<string, string> IsWithinDistanceFixtures => FixtureCatalog.Theory("isWithinDistance");
    public static TheoryData<string, string> PointOnSurfaceFixtures => FixtureCatalog.Theory("pointOnSurface");
    public static TheoryData<string, string> BoundaryFixtures => FixtureCatalog.Theory("boundary");
    public static TheoryData<string, string> DensifyFixtures => FixtureCatalog.Theory("densify");

    /// <summary>Absolute tolerance that grows with the magnitude of the coordinates (UTM values lose about 1e-9).</summary>
    private static double Epsilon(params string?[] wkt)
    {
        double max = wkt.Where(w => w is not null).Select(w => Nts.Read(w!)).Where(g => !g.IsEmpty)
            .Select(g => g.EnvelopeInternal)
            .Select(e => new[] { e.MinX, e.MaxX, e.MinY, e.MaxY }.Max(Math.Abs)).DefaultIfEmpty(0).Max();
        return 1e-9 * Math.Max(1, max / 1e3);
    }

    [Theory]
    [MemberData(nameof(NearestPointsFixtures))]
    public void NearestPoints(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));
        var (na, nb) = (Nts.Read(fixture.A), Nts.Read(fixture.B!));

        var actual = a.NearestPoints(b);

        if (na.IsEmpty || nb.IsEmpty)
        {
            Assert.Null(actual); // deliberate: NTS has no nearest points either
            return;
        }

        double eps = Epsilon(fixture.A, fixture.B);
        double expected = DistanceOp.Distance(na, nb);
        Assert.NotNull(actual);
        var (onA, onB) = actual.Value;
        Assert.True(Math.Abs(Rings.Distance(onA, onB) - expected) <= eps, $"Distance of the pair {Rings.Distance(onA, onB):R}, expected {expected:R}.");
        Assert.True(factory.CreatePoint(onA, null).Distance(a) <= eps, "The first point does not lie on a.");
        Assert.True(factory.CreatePoint(onB, null).Distance(b) <= eps, "The second point does not lie on b.");
    }

    [Theory]
    [MemberData(nameof(IsWithinDistanceFixtures))]
    public void IsWithinDistance(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));
        var (na, nb) = (Nts.Read(fixture.A), Nts.Read(fixture.B!));
        double distance = fixture.Param("distance", 0);

        bool expected = !na.IsEmpty && !nb.IsEmpty && na.IsWithinDistance(nb, distance); // empty: false (as Distance is NaN)

        Assert.Equal(expected, a.IsWithinDistance(b, distance));
        Assert.Equal(expected, b.IsWithinDistance(a, distance));
    }

    [Theory]
    [MemberData(nameof(PointOnSurfaceFixtures))]
    public void PointOnSurface(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);
        var nts = Nts.Read(fixture.A);

        var actual = geometry.PointOnSurface();

        if (nts.IsEmpty)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        var point = factory.CreatePoint(actual.Value, null);
        Assert.False(actual.Value.HasZ || actual.Value.HasM);
        Assert.True(point.Distance(geometry) <= Epsilon(fixture.A), "The point does not lie on the geometry.");
        if (geometry is IPolygon2D polygon && nts.Area > 0 && Nts.ToNts(polygon).IsValid)
        {
            Assert.True(polygon.Contains(point), "The point of a polygon with area must lie in its interior.");
        }
    }

    [Theory]
    [MemberData(nameof(BoundaryFixtures))]
    public void Boundary(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);

        var actual = geometry.Boundary(factory);

        // the oracle gets the geometry in SpatialNET semantics (even-odd, degenerate rings ignored, ADR 0003)
        var expected = Nts.ToNts(geometry).Boundary;
        OracleAssert.SameGeometry(expected, actual, Epsilon(fixture.A), fixture);
    }

    [Theory]
    [MemberData(nameof(DensifyFixtures))]
    public void Densify(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);
        double max = fixture.Param("maxSegmentLength", 1);

        var actual = geometry.Densify(max, factory);

        var expected = Densifier.Densify(Nts.ToNts(geometry), max);
        OracleAssert.SameGeometry(expected, actual, Epsilon(fixture.A), fixture);
        Assert.Equal(expected.NumPoints, Nts.ToNts(actual).NumPoints);
    }
}
