using NetTopologySuite.Simplify;
using SpatialNET.Imp;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

public class HullAndSimplifyFixtureTests
{
    public static TheoryData<string, string> HullFixtures => FixtureCatalog.Theory("convexHull");

    // Punkte haben kein Simplify (ADR 0012)
    public static TheoryData<string, string> SimplifyFixtures => FixtureCatalog.Theory("simplify", f => !f.A.TrimStart().StartsWith("POINT", StringComparison.OrdinalIgnoreCase) && !f.A.TrimStart().StartsWith("MULTIPOINT", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [MemberData(nameof(HullFixtures))]
    public void ConvexHull(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var input = Nts.Read(fixture.A);

        var actual = fixture.ReadA(factory).ConvexHull(factory);

        OracleAssert.SameGeometry(input.ConvexHull(), actual, GridOf(input), fixture);
    }

    [Theory]
    [MemberData(nameof(SimplifyFixtures))]
    public void Simplify(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var input = fixture.ReadA(factory);
        double tolerance = fixture.Param("tolerance", 0);
        bool preserve = fixture.Param("preserveTopology") is "True" or "true";

        IGeometry2D actual = input is IPolygon2D polygon
            ? polygon.Simplify(tolerance, preserve, factory)
            : ((IPolyline2D)input).Simplify(tolerance, preserve, factory);

        var source = input is IPolygon2D ? Nts.ToNts(input) : Nts.Read(fixture.A);
        var expected = preserve
            ? NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(source, tolerance)
            : DouglasPeuckerSimplifier.Simplify(source, tolerance);
        if (input is IPolygon2D) Assert.True(actual.IsValid(), "Vereinfachte Polygone sind gültig.");
        OracleAssert.SameGeometry(expected, actual, GridOf(source), fixture);
    }

    /// <summary>Ohne Rundung: Vergleichsgitter im Bereich der Gleitkomma-Genauigkeit.</summary>
    private static double GridOf(NetTopologySuite.Geometries.Geometry g) =>
        g.IsEmpty ? 1e-12 : 1e-12 * Math.Max(1, Math.Max(Math.Abs(g.EnvelopeInternal.MaxX), Math.Abs(g.EnvelopeInternal.MaxY)));
}

[Collection(GlobalStateCollection.Name)]
public class HullAndSimplifyContractTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Fact]
    public void Null_and_invalid_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).ConvexHull(F));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Simplify(1, factory: F));

        var line = SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (0 0, 1 1)", F);
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Simplify(-1, factory: F));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Simplify(double.NaN, factory: F));
    }

    [Fact]
    public void Hull_respects_SupportsZ_and_never_carries_M()
    {
        var points = SpatialNET.IO.Wkt.Read("MULTIPOINT ZM ((0 0 1 10), (4 0 2 20), (4 4 3 30), (0 4 4 40), (2 2 5 50))", F);

        var withZ = Assert.IsAssignableFrom<IPolygon2D>(points.ConvexHull(F));
        var flat = Assert.IsAssignableFrom<IPolygon2D>(points.ConvexHull(new SpatialNET.Imp.ImpGeometryFactory(supportsZ: false)));

        Assert.All(Enumerable.Range(0, withZ[0].PointCount), i =>
        {
            var c = SpatialNET.Internal.GeometryAccess.ToCoordinate(withZ[0][i]);
            Assert.True(c.HasZ);
            Assert.False(c.HasM);
        });
        Assert.All(Enumerable.Range(0, flat[0].PointCount), i =>
        {
            var c = SpatialNET.Internal.GeometryAccess.ToCoordinate(flat[0][i]);
            Assert.False(c.HasZ);
            Assert.False(c.HasM);
        });
    }

    [Fact]
    public void Hull_degenerates_like_JTS()
    {
        Assert.IsAssignableFrom<IPoint2D>(SpatialNET.IO.Wkt.Read("MULTIPOINT ((1 1), (1 1))", F).ConvexHull(F));
        Assert.Equal(0, Assert.IsAssignableFrom<IPolygon2D>(SpatialNET.IO.Wkt.Read("MULTIPOINT EMPTY", F).ConvexHull(F)).RingCount);
        var line = Assert.IsAssignableFrom<IPolyline2D>(SpatialNET.IO.Wkt.Read("MULTIPOINT ((0 0), (2 2), (1 1))", F).ConvexHull(F));
        Assert.Equal(2, line[0].PointCount);
    }

    [Fact]
    public void Simplify_keeps_original_vertices_with_Z_and_passes_the_source()
    {
        var foreign = Foreign.ForeignGeometryFactory.Instance;
        var line = (Foreign.Polyline)SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING Z (0 0 1, 1 0.1 2, 2 0 3, 3 5 4)", foreign);
        line.SrsId = 31256;

        var result = (Foreign.Polyline)line.Simplify(0.5, factory: foreign);

        Assert.Equal(31256, result.SrsId);
        Assert.Equal([1d, 3d, 4d], Enumerable.Range(0, result[0].PointCount).Select(i => result[0][i].Z));
    }

    [Fact]
    public void Missing_factory_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var saved = GeometryFactory.Default;
            GeometryFactory.Default = null;
            try
            {
                SpatialNET.IO.Wkt.Read("POINT (1 1)", F).ConvexHull();
            }
            finally
            {
                GeometryFactory.Default = saved;
            }
        });
    }
}
