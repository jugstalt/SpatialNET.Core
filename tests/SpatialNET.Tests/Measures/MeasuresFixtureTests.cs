using NetTopologySuite.Algorithm;
using NetTopologySuite.Operation.Distance;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Measures;

/// <summary>Fixture-Tests der Maße gegen das NTS-Orakel, je Fixture für Imp und Fremd-Implementierung.</summary>
public class MeasuresFixtureTests
{
    public static TheoryData<string, string> AreaFixtures => FixtureCatalog.Theory("area");
    public static TheoryData<string, string> LengthFixtures => FixtureCatalog.Theory("length");
    public static TheoryData<string, string> EnvelopeFixtures => FixtureCatalog.Theory("envelope");
    public static TheoryData<string, string> CentroidFixtures => FixtureCatalog.Theory("centroid");
    public static TheoryData<string, string> DistanceFixtures => FixtureCatalog.Theory("distance");

    [Theory]
    [MemberData(nameof(AreaFixtures))]
    public void Area(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);

        OracleAssert.Close(Nts.Read(fixture.A).Area, fixture.ReadA(Nts.Factory(implementation)).Area(), fixture, "area");
    }

    [Theory]
    [MemberData(nameof(LengthFixtures))]
    public void Length(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);

        OracleAssert.Close(Nts.Read(fixture.A).Length, fixture.ReadA(Nts.Factory(implementation)).Length(), fixture, "length");
    }

    [Theory]
    [MemberData(nameof(EnvelopeFixtures))]
    public void Envelope(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var expected = Nts.Read(fixture.A);
        var actual = fixture.ReadA(Nts.Factory(implementation)).Envelope();

        if (expected.IsEmpty)
        {
            Assert.True(actual.IsEmpty);
            return;
        }

        var e = expected.EnvelopeInternal;
        Assert.Equal(new Envelope2D(e.MinX, e.MinY, e.MaxX, e.MaxY), actual);
    }

    [Theory]
    [MemberData(nameof(CentroidFixtures))]
    public void Centroid(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var expected = Nts.Read(fixture.A);
        var actual = fixture.ReadA(Nts.Factory(implementation)).Centroid();

        if (expected.IsEmpty)
        {
            Assert.Null(actual);
            return;
        }

        var c = NetTopologySuite.Algorithm.Centroid.GetCentroid(expected);
        Assert.NotNull(actual);
        OracleAssert.Close(c.X, actual.Value.X, fixture, "centroid");
        OracleAssert.Close(c.Y, actual.Value.Y, fixture, "centroid");
        Assert.False(actual.Value.HasZ);
    }

    [Theory]
    [MemberData(nameof(DistanceFixtures))]
    public void Distance(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (Nts.Read(fixture.A), Nts.Read(fixture.B!));
        double expected = fixture.Expected is not null ? fixture.ExpectedNumber()
            : a.IsEmpty || b.IsEmpty ? double.NaN // bewusste Abweichung: NTS liefert 0 (ADR 0012)
            : DistanceOp.Distance(a, b);

        OracleAssert.Close(expected, fixture.ReadA(factory).Distance(fixture.ReadB(factory)), fixture, "distance");
        OracleAssert.Close(expected, fixture.ReadB(factory).Distance(fixture.ReadA(factory)), fixture, "distance");
    }
}
