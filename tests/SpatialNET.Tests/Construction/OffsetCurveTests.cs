using NetTopologySuite.Operation.Buffer;
using SpatialNET.Internal.Noding;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;
using NtsOffsetCurve = NetTopologySuite.Operation.Buffer.OffsetCurve;

namespace SpatialNET.Tests.Construction;

/// <summary>OffsetCurve gegen NTS OffsetCurve mit gleichen Parametern (ADR 0006/0008).</summary>
public class OffsetCurveFixtureTests
{
    public static TheoryData<string, string> Fixtures => FixtureCatalog.Theory("offsetCurve");

    [Theory, MemberData(nameof(Fixtures))]
    public void OffsetCurve(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var input = fixture.ReadA(factory);
        double distance = fixture.Param("distance", 0);
        var options = new OffsetOptions
        {
            JoinStyle = Enum.Parse<JoinStyle>(fixture.Param("join") ?? "Round"),
            Joined = fixture.Param("joined") is "True" or "true",
        };

        var actual = input switch
        {
            IPolygon2D polygon => polygon.OffsetCurve(distance, options, factory),
            _ => ((IPolyline2D)input).OffsetCurve(distance, options, factory),
        };

        if (fixture.Tags.Contains("self-crossing")) return; // nur ohne Exception

        var parameters = new BufferParameters
        {
            QuadrantSegments = options.QuadrantSegments,
            JoinStyle = options.JoinStyle switch
            {
                JoinStyle.Mitre => NetTopologySuite.Operation.Buffer.JoinStyle.Mitre,
                JoinStyle.Bevel => NetTopologySuite.Operation.Buffer.JoinStyle.Bevel,
                _ => NetTopologySuite.Operation.Buffer.JoinStyle.Round,
            },
            MitreLimit = options.MitreLimit,
        };
        var source = Nts.ToNts(input);
        var expected = new NtsOffsetCurve(source, distance, parameters) { Joined = options.Joined }.GetCurve();

        double grid = GridModel.Resolve(null, input).Size;
        var tolerance = fixture with
        {
            Tolerance = new Dictionary<string, double>(fixture.Tolerance)
            {
                ["symdiff"] = fixture.ToleranceFor("symdiff", 1e-6),
                ["hausdorff"] = fixture.ToleranceFor("hausdorff", Math.Abs(distance) * 0.02 + 2 * grid * Math.Sqrt(2)),
            },
        };
        OracleAssert.SameGeometry(expected, actual, grid, tolerance);
    }
}

[Covers("OffsetCurve")]
public class OffsetCurveContractTests
{
    private static readonly IGeometryFactory F = Imp.ImpGeometryFactory.Instance;

    [Fact]
    public void Positive_distance_is_left_and_Z_comes_from_the_source_segment()
    {
        var line = SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING Z (0 0 0, 10 0 10)", F);

        var curve = line.OffsetCurve(2, factory: F);

        var start = Assert.IsAssignableFrom<IPoint3D>(curve[0][0]);
        Assert.Equal(2, start.Y, 12);
        Assert.Equal(0, start.Z, 9);
        Assert.Equal(10, ((IPoint3D)curve[0][1]).Z, 9);
    }

    [Fact]
    public void Polygon_offset_is_outward_regardless_of_orientation()
    {
        var clockwise = SpatialNET.IO.Wkt.Read<IPolygon2D>("POLYGON ((0 0, 0 10, 10 10, 10 0, 0 0))", F);

        var curve = clockwise.OffsetCurve(1, factory: F);

        Assert.All(Internal.GeometryAccess.AllPoints(curve), p => Assert.True(p.X < 0 || p.X > 10 || p.Y < 0 || p.Y > 10));
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        var line = SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0)", F);

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).OffsetCurve(1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.OffsetCurve(double.PositiveInfinity, factory: F));
    }
}
