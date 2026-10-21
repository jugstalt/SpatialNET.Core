using NetTopologySuite.LinearReferencing;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// Fixture-Tests der Längen-Varianten gegen die NTS-<see cref="LengthIndexedLine"/> (ADR 0017), je Fixture für
/// Imp und Fremd-Implementierung. Die M-Varianten hat NTS nicht; sie prüfen <c>LinearReferencingTests</c>.
/// </summary>
public class LinearReferencingFixtureTests
{
    public static TheoryData<string, string> InterpolateFixtures => FixtureCatalog.Theory("interpolate");
    public static TheoryData<string, string> ProjectFixtures => FixtureCatalog.Theory("project");
    public static TheoryData<string, string> SubstringFixtures => FixtureCatalog.Theory("substring");

    [Theory]
    [MemberData(nameof(InterpolateFixtures))]
    public void Interpolate(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var line = (IPolyline2D)fixture.ReadA(Nts.Factory(implementation));
        double distance = fixture.Param("distance", 0);
        var nts = Nts.Read(fixture.A);

        var actual = line.Interpolate(distance);

        if (nts.IsEmpty)
        {
            Assert.Null(actual);
            return;
        }

        var expected = new LengthIndexedLine(nts).ExtractPoint(distance);
        Assert.NotNull(actual);
        OracleAssert.Close(expected.X, actual.Value.X, fixture, "length");
        OracleAssert.Close(expected.Y, actual.Value.Y, fixture, "length");
        if (fixture.Tags.Contains("z"))
        {
            OracleAssert.Close(expected.Z, actual.Value.Z, fixture, "length");
        }
    }

    [Theory]
    [MemberData(nameof(ProjectFixtures))]
    public void Project(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var line = (IPolyline2D)fixture.ReadA(factory);
        var point = (IPoint2D)fixture.ReadB(factory);
        var nts = Nts.Read(fixture.A);

        double actual = line.Project(point);

        if (nts.IsEmpty)
        {
            Assert.True(double.IsNaN(actual)); // NTS has no answer for an empty line
            return;
        }

        double expected = new LengthIndexedLine(nts).IndexOf(new NetTopologySuite.Geometries.Coordinate(point.X, point.Y));
        OracleAssert.Close(expected, actual, fixture, "length");
    }

    [Theory]
    [MemberData(nameof(SubstringFixtures))]
    public void Substring(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var line = (IPolyline2D)fixture.ReadA(factory);
        double from = fixture.Param("from", 0), to = fixture.Param("to", 0);
        var nts = Nts.Read(fixture.A);

        var actual = line.Substring(from, to, factory);

        if (nts.IsEmpty || fixture.Expected is not null)
        {
            Assert.Equal(0, actual.PathCount); // documented deviation: no length, no piece (ADR 0017)
            return;
        }

        var expected = new LengthIndexedLine(nts).ExtractLine(from, to);
        var expectedCoordinates = expected.Coordinates;
        var actualCoordinates = Internal.GeometryAccess.AllPoints(actual).ToArray();

        OracleAssert.Close(expected.Length, actual.Length(), fixture, "length");
        Assert.Equal(expectedCoordinates.Length, actualCoordinates.Length);
        for (int i = 0; i < expectedCoordinates.Length; i++)
        {
            OracleAssert.Close(expectedCoordinates[i].X, actualCoordinates[i].X, fixture, "length");
            OracleAssert.Close(expectedCoordinates[i].Y, actualCoordinates[i].Y, fixture, "length");
        }
    }
}
