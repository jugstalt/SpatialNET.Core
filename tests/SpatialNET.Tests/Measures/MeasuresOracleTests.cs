using NetTopologySuite.Algorithm;
using NetTopologySuite.Operation.Distance;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Measures;

/// <summary>Vergleich der Maße mit dem NTS-Orakel, je Fall gegen Imp und Fremd-Implementierung.</summary>
public class MeasuresOracleTests
{
    // Fälle nach Pflicht-Tag-Katalog (ADR 0008); werden mit #22 in Fixture-Dateien überführt.
    public static readonly (string Tag, string Wkt)[] Cases =
    [
        ("normal", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
        ("normal-cw", "POLYGON ((0 0, 0 10, 10 10, 10 0, 0 0))"),
        ("empty", "POLYGON EMPTY"),
        ("degenerate-ring", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (5 5, 6 6, 5 5))"),
        ("collinear-overlap", "POLYGON ((0 0, 5 0, 10 0, 10 10, 0 10, 0 0))"),
        ("vertex-touch", "MULTIPOLYGON (((0 0, 5 0, 5 5, 0 5, 0 0)), ((5 5, 10 5, 10 10, 5 10, 5 5)))"),
        ("hole-touch", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (0 5, 5 2, 5 8, 0 5))"),
        ("near-coincident", "POLYGON ((0 0, 10 0, 10.000000000001 5, 10 10, 0 10, 0 0))"),
        ("large-coords", "POLYGON ((533000.12 5212000.34, 533040.56 5212000.34, 533040.56 5212030.78, 533000.12 5212030.78, 533000.12 5212000.34))"),
        ("z", "POLYGON Z ((0 0 1, 10 0 2, 10 10 3, 0 10 4, 0 0 1))"),
        ("multi", "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0)), ((10 0, 13 0, 13 3, 10 3, 10 0)))"),
        ("nested", "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2)), ((4 4, 6 4, 6 6, 4 6, 4 4)))"),
        ("line", "LINESTRING (0 0, 3 4, 3 10)"),
        ("multi-line", "MULTILINESTRING ((0 0, 3 4), (10 10, 10 20))"),
        ("point", "POINT (3 4)"),
        ("multi-point", "MULTIPOINT ((0 0), (2 0), (2 6))"),
    ];

    public static TheoryData<string, string> CaseData()
    {
        var data = new TheoryData<string, string>();
        foreach (var (tag, _) in Cases)
            foreach (var (impl, _) in Nts.Implementations)
                data.Add(tag, impl);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseData))]
    public void Area_matches_NTS(string tag, string implementation)
    {
        var (expected, actual) = Load(tag, implementation);

        AssertClose(expected.Area, actual.Area());
    }

    [Theory]
    [MemberData(nameof(CaseData))]
    public void Length_matches_NTS(string tag, string implementation)
    {
        var (expected, actual) = Load(tag, implementation);

        AssertClose(expected.Length, actual.Length());
    }

    [Theory]
    [MemberData(nameof(CaseData))]
    public void Envelope_matches_NTS(string tag, string implementation)
    {
        var (expected, actual) = Load(tag, implementation);
        var envelope = actual.Envelope();

        if (expected.IsEmpty)
        {
            Assert.True(envelope.IsEmpty);
            return;
        }

        var e = expected.EnvelopeInternal;
        Assert.Equal(new Envelope2D(e.MinX, e.MinY, e.MaxX, e.MaxY), envelope);
    }

    [Theory]
    [MemberData(nameof(CaseData))]
    public void Centroid_matches_NTS(string tag, string implementation)
    {
        var (expected, actual) = Load(tag, implementation);
        var centroid = actual.Centroid();

        if (expected.IsEmpty)
        {
            Assert.Null(centroid);
            return;
        }

        var c = Centroid.GetCentroid(expected);
        Assert.NotNull(centroid);
        AssertClose(c.X, centroid.Value.X);
        AssertClose(c.Y, centroid.Value.Y);
        Assert.False(centroid.Value.HasZ);
    }

    public static readonly (string Tag, string A, string B)[] DistanceCases =
    [
        ("point-polygon", "POINT (15 5)", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
        ("point-in-polygon", "POINT (5 5)", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
        ("point-in-hole", "POINT (5 5)", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))"),
        ("polygon-in-hole", "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))"),
        ("line-crosses-polygon", "LINESTRING (-5 5, 15 5)", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
        ("line-line", "LINESTRING (0 0, 10 0)", "LINESTRING (3 2, 7 5)"),
        ("crossing-lines", "LINESTRING (0 0, 10 10)", "LINESTRING (0 10, 10 0)"),
        ("multipoint-line", "MULTIPOINT ((0 5), (20 20))", "LINESTRING (0 0, 10 0)"),
        ("polygons-apart", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", "POLYGON ((4 5, 5 5, 5 6, 4 6, 4 5))"),
        ("large-coords", "POINT (533100 5212100)", "LINESTRING (533000 5212000, 533200 5212000)"),
    ];

    public static TheoryData<string, string> DistanceData()
    {
        var data = new TheoryData<string, string>();
        foreach (var (tag, _, _) in DistanceCases)
            foreach (var (impl, _) in Nts.Implementations)
                data.Add(tag, impl);
        return data;
    }

    [Theory]
    [MemberData(nameof(DistanceData))]
    public void Distance_matches_NTS(string tag, string implementation)
    {
        var (_, wktA, wktB) = DistanceCases.Single(c => c.Tag == tag);
        var factory = Nts.Implementations.Single(i => i.Name == implementation).Factory;
        var a = Nts.Read(wktA);
        var b = Nts.Read(wktB);

        var expected = DistanceOp.Distance(a, b);

        AssertClose(expected, Nts.ToSpatial(a, factory).Distance(Nts.ToSpatial(b, factory)));
        AssertClose(expected, Nts.ToSpatial(b, factory).Distance(Nts.ToSpatial(a, factory)));
    }

    [Fact]
    public void Distance_to_empty_is_NaN_unlike_NTS()
    {
        // Bewusste Abweichung (ADR 0012): NTS liefert 0.
        var factory = Nts.Implementations[0].Factory;
        var point = Nts.ToSpatial(Nts.Read("POINT (1 1)"), factory);
        var empty = Nts.ToSpatial(Nts.Read("POLYGON EMPTY"), factory);

        Assert.True(double.IsNaN(point.Distance(empty)));
        Assert.Equal(0, DistanceOp.Distance(Nts.Read("POINT (1 1)"), Nts.Read("POLYGON EMPTY")));
    }

    private static (NetTopologySuite.Geometries.Geometry Expected, IGeometry2D Actual) Load(string tag, string implementation)
    {
        var wkt = Cases.Single(c => c.Tag == tag).Wkt;
        var factory = Nts.Implementations.Single(i => i.Name == implementation).Factory;
        var expected = Nts.Read(wkt);
        return (expected, Nts.ToSpatial(expected, factory));
    }

    internal static void AssertClose(double expected, double actual, double relative = 1e-9)
    {
        double tolerance = relative * Math.Max(1, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Erwartet {expected:R}, erhalten {actual:R} (Toleranz {tolerance:R}).");
    }
}
