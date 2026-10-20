using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Union;
using SpatialNET.Internal.Noding;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>Buffer gegen NTS BufferOp mit gleichen Parametern und gegen die Kapsel-Union (ADR 0006/0008).</summary>
public class BufferFixtureTests
{
    public static TheoryData<string, string> Fixtures => FixtureCatalog.Theory("buffer");

    [Theory, MemberData(nameof(Fixtures))]
    public void Buffer(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var input = fixture.ReadA(factory);
        double distance = fixture.Param("distance", 0);
        var options = Options(fixture);

        var actual = input.Buffer(distance, options, factory);

        Assert.True(actual.IsValid(), "Buffer-Ergebnisse sind gültig.");
        if (fixture.Tags.Contains("self-crossing")) return;

        var parameters = new BufferParameters
        {
            QuadrantSegments = options.QuadrantSegments,
            EndCapStyle = options.CapStyle switch { CapStyle.Flat => EndCapStyle.Flat, CapStyle.Square => EndCapStyle.Square, _ => EndCapStyle.Round },
            JoinStyle = options.JoinStyle switch { JoinStyle.Mitre => NetTopologySuite.Operation.Buffer.JoinStyle.Mitre, JoinStyle.Bevel => NetTopologySuite.Operation.Buffer.JoinStyle.Bevel, _ => NetTopologySuite.Operation.Buffer.JoinStyle.Round },
            MitreLimit = options.MitreLimit,
            SimplifyFactor = options.SimplifyFactor,
            IsSingleSided = options.SingleSided,
        };
        var source = Nts.ToNts(input); // Even-Odd-Semantik der Eingabe (ADR 0003)
        var expected = BufferOp.Buffer(source, distance, parameters);

        double grid = GridModel.Resolve(null, input).Size;
        var tolerance = fixture with
        {
            Tolerance = new Dictionary<string, double>(fixture.Tolerance)
            {
                ["symdiff"] = fixture.ToleranceFor("symdiff", OracleAssert.BufferArea),
                ["hausdorff"] = fixture.ToleranceFor("hausdorff", Math.Abs(distance) * 0.02 + 2 * grid * Math.Sqrt(2)),
            },
        };
        OracleAssert.SameGeometry(expected, actual, grid, tolerance);
        OracleAssert.Close(expected.Area, actual.Area(), fixture.ToleranceFor("symdiff", OracleAssert.BufferArea));
    }

    [Theory, MemberData(nameof(Fixtures))]
    public void Capsule_union_reference_for_round_line_buffers(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var options = Options(fixture);
        double distance = fixture.Param("distance", 0);
        if (!fixture.A.TrimStart().StartsWith("LINESTRING", StringComparison.OrdinalIgnoreCase)
            || options != BufferOptions.Default || distance <= 0 || fixture.A.Contains("EMPTY"))
        {
            return;
        }

        var factory = Nts.Factory(implementation);
        var line = (NetTopologySuite.Geometries.LineString)Nts.Read(fixture.A);
        var capsules = new List<Geometry>();
        for (int i = 1; i < line.NumPoints; i++)
        {
            var segment = line.Factory.CreateLineString([line.GetCoordinateN(i - 1), line.GetCoordinateN(i)]);
            capsules.Add(segment.Buffer(distance, new BufferParameters { QuadrantSegments = 64, SimplifyFactor = 0 }));
        }

        var reference = UnaryUnionOp.Union(capsules);
        var actual = fixture.ReadA(factory).Buffer(distance, factory: factory);

        // grobe Bögen (8 je Viertelkreis) gegen feine Kapseln: Fläche auf 1 % genau
        OracleAssert.Close(reference.Area, actual.Area(), 1e-2);
    }

    private static BufferOptions Options(Fixture fixture) => new()
    {
        JoinStyle = Enum.Parse<JoinStyle>(fixture.Param("join") ?? "Round"),
        CapStyle = Enum.Parse<CapStyle>(fixture.Param("cap") ?? "Round"),
        QuadrantSegments = (int)fixture.Param("quadrantSegments", 8),
        MitreLimit = fixture.Param("mitreLimit", 5),
        SingleSided = fixture.Param("singleSided") == "True" || fixture.Param("singleSided") == "true",
    };
}

[Covers("Buffer")]
public class BufferContractTests
{
    private static readonly IGeometryFactory F = Imp.ImpGeometryFactory.Instance;

    [Fact]
    public void Invalid_arguments_throw()
    {
        var p = SpatialNET.IO.Wkt.Read("POINT (0 0)", F);

        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Buffer(1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Buffer(double.NaN, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Buffer(1, new BufferOptions { QuadrantSegments = 0 }, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Buffer(1, new BufferOptions { ArcTolerance = -1 }, F));
    }

    [Fact]
    public void Arc_tolerance_bounds_the_chord_error()
    {
        var p = SpatialNET.IO.Wkt.Read("POINT (0 0)", F);

        var coarse = p.Buffer(100, new BufferOptions { ArcTolerance = 1 }, F);
        var fine = p.Buffer(100, new BufferOptions { ArcTolerance = 0.01 }, F);

        Assert.True(Math.PI * 100 * 100 - coarse.Area() > Math.PI * 100 * 100 - fine.Area());
        Assert.All(Internal.GeometryAccess.AllPoints(fine), q => Assert.Equal(100, Math.Sqrt(q.X * q.X + q.Y * q.Y), 6));
    }

    [Fact]
    public void Results_are_2D()
    {
        var line = SpatialNET.IO.Wkt.Read("LINESTRING Z (0 0 5, 10 0 10)", F);

        Assert.All(Internal.GeometryAccess.AllPoints(line.Buffer(1, factory: F)), q => Assert.IsNotAssignableFrom<IPoint3D>(q));
    }
}
