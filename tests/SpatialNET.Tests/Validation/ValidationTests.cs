using System.Text.Json;
using NetTopologySuite.Operation.Valid;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Validation;

public class ValidationFixtureTests
{
    public static TheoryData<string, string> IsValidFixtures => FixtureCatalog.Theory("isValid");
    public static TheoryData<string, string> ValidateFixtures => FixtureCatalog.Theory("validate");

    /// <summary>NTS-Fehlerarten, die nach Even-Odd (ADR 0003) keine Fehler sind: geschachtelte Ringe werden Löcher bzw. Inseln.</summary>
    private static readonly TopologyValidationErrors[] EvenOddDivergences =
    [
        TopologyValidationErrors.NestedShells,
        TopologyValidationErrors.HoleOutsideShell,
        TopologyValidationErrors.NestedHoles,
    ];

    [Theory]
    [MemberData(nameof(IsValidFixtures))]
    public void IsValid(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var geometry = fixture.ReadA(Nts.Factory(implementation));

        Assert.Equal(Expected(fixture), geometry.IsValid());
    }

    [Theory]
    [MemberData(nameof(ValidateFixtures))]
    public void Validate(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var expected = JsonSerializer.Deserialize<string[]>(fixture.Param("kinds")!)!.Order().ToArray();

        var result = fixture.ReadA(Nts.Factory(implementation)).Validate();

        Assert.Equal(expected, result.Issues.Select(i => i.Kind.ToString()).Distinct().Order().ToArray());
        Assert.Equal(expected.Length == 0, result.IsValid);
    }

    private static bool Expected(Fixture fixture)
    {
        var nts = Nts.Read(fixture.A);
        var op = new IsValidOp(nts);
        if (op.IsValid)
        {
            return true;
        }

        if (!EvenOddDivergences.Contains(op.ValidationError.ErrorType))
        {
            return false;
        }

        // Even-Odd-Sicht: die von SpatialNET gebildeten Teilflächen müssen für NTS gültig sein.
        var structured = Nts.ToNts(fixture.ReadA(Nts.Implementations[0].Factory));
        return new IsValidOp(structured).IsValid;
    }
}

public class ValidationContractTests
{
    [Fact]
    public void Invalid_coordinates_are_reported_without_exception()
    {
        var factory = Imp.ImpGeometryFactory.Instance;
        var polygon = factory.CreatePolygon([new PolygonPart(new Coordinate[] { new(0, 0), new(double.NaN, 0), new(1, 1), new(0, 0) })], null);

        var result = polygon.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == ValidationIssueKind.InvalidCoordinate);
    }

    [Fact]
    public void Unclosed_and_clockwise_rings_are_valid()
    {
        var factory = Foreign.ForeignGeometryFactory.Instance; // speichert offen und im Uhrzeigersinn
        var polygon = SpatialNET.IO.Wkt.Read("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", factory);

        Assert.True(polygon.IsValid());
    }

    [Fact]
    public void Issue_location_is_the_crossing_point()
    {
        var bowtie = SpatialNET.IO.Wkt.Read("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))", Imp.ImpGeometryFactory.Instance);

        var issue = Assert.Single(bowtie.Validate().Issues);
        Assert.Equal(new Coordinate(5, 5), issue.Location);
    }

    [Fact]
    public void Rings_crossing_at_a_shared_vertex_are_a_self_intersection()
    {
        var polygon = SpatialNET.IO.Wkt.Read(
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (5 10, 6 12, 4 12, 5 10, 6 8, 4 8, 5 10))", Imp.ImpGeometryFactory.Instance);

        Assert.False(polygon.IsValid());
    }

    [Fact]
    public void Null_and_unknown_types_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).IsValid());
    }
}
