using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Measures;
using SpatialNET.Tests.Oracle;
using ForeignGeometryFactory = SpatialNET.Tests.Foreign.ForeignGeometryFactory;

namespace SpatialNET.Tests.IO;

public class WktReadTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Theory]
    [InlineData("POINT (1 2)")]
    [InlineData("point(1 2)")]
    [InlineData("  POINT ( 1   2 )  ")]
    public void Reads_a_point_in_any_spelling(string wkt)
    {
        var p = Assert.IsAssignableFrom<IPoint2D>(Wkt.Read(wkt, F));

        Assert.Equal((1d, 2d), (p.X, p.Y));
        Assert.IsNotAssignableFrom<IPoint3D>(p);
    }

    [Theory]
    [InlineData("POINT Z (1 2 3)", 3d)]
    [InlineData("POINT (1 2 3)", 3d)]
    [InlineData("POINT ZM (1 2 3 4)", 3d)]
    [InlineData("POINT (1 2 3 4)", 3d)]
    public void Reads_Z_and_drops_M(string wkt, double z)
    {
        Assert.Equal(z, Assert.IsAssignableFrom<IPoint3D>(Wkt.Read(wkt, F)).Z);
    }

    [Fact]
    public void Drops_M_without_Z()
    {
        Assert.IsNotAssignableFrom<IPoint3D>(Wkt.Read("POINT M (1 2 3)", F));
    }

    [Fact]
    public void Point_empty_becomes_an_empty_multipoint()
    {
        Assert.Equal(0, Assert.IsAssignableFrom<IMultiPoint2D>(Wkt.Read("POINT EMPTY", F)).PointCount);
    }

    [Theory]
    [InlineData("MULTIPOINT ((1 2), (3 4))")]
    [InlineData("MULTIPOINT (1 2, 3 4)")]
    public void Reads_both_multipoint_spellings(string wkt)
    {
        var mp = Wkt.Read<IMultiPoint2D>(wkt, F);

        Assert.Equal(2, mp.PointCount);
        Assert.Equal(3, mp[1].X);
    }

    [Fact]
    public void Reads_lines_as_polylines()
    {
        Assert.Equal(1, Wkt.Read<IPolyline2D>("LINESTRING (0 0, 1 1)", F).PathCount);
        Assert.Equal(2, Wkt.Read<IPolyline2D>("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3, 4 4))", F).PathCount);
        Assert.Equal(0, Wkt.Read<IPolyline2D>("LINESTRING EMPTY", F).PathCount);
    }

    [Fact]
    public void Reads_polygons_as_flat_ring_lists()
    {
        var polygon = Wkt.Read<IPolygon2D>(
            "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2)), ((4 4, 6 4, 6 6, 4 6, 4 4)), EMPTY)", F);

        Assert.Equal(3, polygon.RingCount);
        Assert.Equal(68, polygon.Area());
    }

    [Fact]
    public void Accepts_geometrically_invalid_input()
    {
        // Ring ohne Schlusspunkt und sich kreuzender Ring sind syntaktisch korrekt (ADR 0009).
        Assert.Equal(4, Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 0, 10 10, 0 10))", F)[0].PointCount);
        Assert.Equal(5, Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))", F)[0].PointCount);
    }

    [Fact]
    public void Geometry_collection_is_not_a_single_geometry_but_flattens_in_ReadAll()
    {
        const string wkt = "GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)), POLYGON EMPTY)";

        Assert.Throws<NotSupportedException>(() => Wkt.Read(wkt, F));

        var all = Wkt.ReadAll(wkt, F);
        Assert.Collection(all,
            g => Assert.IsAssignableFrom<IPoint2D>(g),
            g => Assert.IsAssignableFrom<IPolyline2D>(g),
            g => Assert.Equal(0, Assert.IsAssignableFrom<IPolygon2D>(g).RingCount));
    }

    [Theory]
    [InlineData("POINT (1)", 8)]
    [InlineData("POINT (1 2", 10)]
    [InlineData("POLYGN ((0 0, 1 0, 0 1, 0 0))", 0)]
    [InlineData("POINT (1 a)", 9)]
    [InlineData("POINT (1 2) x", 12)]
    [InlineData("POINT Q (1 2)", 6)]
    [InlineData("LINESTRING Z (1 2, 3 4)", 17)]
    public void Syntax_errors_report_the_position(string wkt, int position)
    {
        var ex = Assert.Throws<FormatException>(() => Wkt.Read(wkt, F));

        Assert.Contains($"Position {position}", ex.Message);
    }

    [Fact]
    public void Read_of_the_wrong_kind_throws()
    {
        Assert.Throws<FormatException>(() => Wkt.Read<IPolygon2D>("POINT (1 2)", F));
    }

    [Fact]
    public void Null_text_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Wkt.Read(null!, F));
    }
}

[Collection(GlobalStateCollection.Name)]
public sealed class WktDefaultFactoryTests : IDisposable
{
    private readonly IGeometryFactory? _saved = GeometryFactory.Default;

    public void Dispose() => GeometryFactory.Default = _saved;

    [Fact]
    public void Uses_the_default_factory_and_fails_clearly_without_one()
    {
        GeometryFactory.Default = ForeignGeometryFactory.Instance;
        Assert.IsType<Foreign.Point>(Wkt.Read("POINT (1 2)"));

        GeometryFactory.Default = null;
        Assert.Throws<InvalidOperationException>(() => Wkt.Read("POINT (1 2)"));
    }
}

public class WktWriteTests
{
    [Theory]
    [InlineData("POINT (1 2)", "POINT (1 2)")]
    [InlineData("POINT Z (1 2 3)", "POINT Z (1 2 3)")]
    [InlineData("MULTIPOINT ((1 2))", "MULTIPOINT ((1 2))")]
    [InlineData("POINT EMPTY", "MULTIPOINT EMPTY")]
    [InlineData("MULTILINESTRING ((0 0, 1 1))", "LINESTRING (0 0, 1 1)")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("LINESTRING EMPTY", "LINESTRING EMPTY")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))", "POLYGON ((0 0, 1 0, 1 1, 0 0))")]
    [InlineData("POLYGON EMPTY", "POLYGON EMPTY")]
    [InlineData("POLYGON ((0 0, 1 0, 1 1, 0 1))", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")]
    [InlineData("POLYGON ((0 0, 0 1, 1 1, 1 0, 0 0))", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")]
    [InlineData("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))")]
    [InlineData("POINT (0.1 -1E+300)", "POINT (0.1 -1E+300)")]
    public void Writes_the_slimmest_type_with_normalized_rings(string input, string expected)
    {
        Assert.Equal(expected, Wkt.Read(input, ImpGeometryFactory.Instance).ToWkt());
    }

    [Fact]
    public void Z_mode_auto_omits_zero_heights_of_host_geometries()
    {
        // Die Fremd-Implementierung liefert wie gView/WebGIS immer Z = 0.
        var line = Wkt.Read("LINESTRING (0 0, 1 1)", ForeignGeometryFactory.Instance);

        Assert.Equal("LINESTRING (0 0, 1 1)", line.ToWkt());
        Assert.Equal("LINESTRING Z (0 0 0, 1 1 0)", line.ToWkt(new WktOptions { ZMode = ZMode.Always }));
        Assert.Equal("LINESTRING (0 0, 1 1)",
            Wkt.Read("LINESTRING Z (0 0 5, 1 1 6)", ImpGeometryFactory.Instance).ToWkt(new WktOptions { ZMode = ZMode.Never }));
    }

    [Fact]
    public void NaN_and_infinity_round_trip()
    {
        var wkt = Wkt.Read("POINT (NaN -Infinity)", ImpGeometryFactory.Instance).ToWkt();

        Assert.Equal("POINT (NaN -Infinity)", wkt);
    }

    [Fact]
    public void Output_is_readable_by_NTS_with_equal_measures()
    {
        foreach (var (tag, wkt) in MeasuresOracleTests.Cases)
        {
            var original = Nts.Read(wkt);
            var written = Wkt.Read(wkt, ImpGeometryFactory.Instance).ToWkt(new WktOptions { ZMode = ZMode.Never });
            var reread = Nts.Read(written);

            MeasuresOracleTests.AssertClose(original.Area, reread.Area);
            Assert.True(original.IsEmpty == reread.IsEmpty, tag);
        }
    }
}

public class WktRoundTripPropertyTests
{
    private static readonly Gen<double> AnyFinite = Gen.Double.Where(double.IsFinite);

    [Fact]
    public void Coordinates_round_trip_bit_exact()
    {
        Gen.Select(AnyFinite, AnyFinite, AnyFinite).Array[2, 20].Sample(points =>
        {
            ReadOnlyMemory<SpatialNET.Coordinate>[] paths = [points.Select(p => new SpatialNET.Coordinate(p.Item1, p.Item2, p.Item3)).ToArray()];
            var line = ImpGeometryFactory.Instance.CreatePolyline(paths, null);

            var reread = Wkt.Read<IPolyline2D>(line.ToWkt(new WktOptions { ZMode = ZMode.Always }), ImpGeometryFactory.Instance);

            for (int i = 0; i < points.Length; i++)
            {
                var p = Assert.IsAssignableFrom<IPoint3D>(reread[0][i]);
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item1), BitConverter.DoubleToInt64Bits(p.X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item2), BitConverter.DoubleToInt64Bits(p.Y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item3), BitConverter.DoubleToInt64Bits(p.Z));
            }
        });
    }

    [Fact]
    public void Written_wkt_is_stable_under_read_and_write()
    {
        Gen.Int[3, 30].SelectMany(n => Gen.Double[0.5, 100].Array[n]).Sample(radii =>
        {
            int n = radii.Length;
            var ring = Enumerable.Range(0, n + 1)
                .Select(i => new SpatialNET.Coordinate(5e5 + radii[i % n] * Math.Cos(2 * Math.PI * i / n), 5e6 + radii[i % n] * Math.Sin(2 * Math.PI * i / n)))
                .ToArray();
            ring[^1] = ring[0];
            var polygon = ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring)], null);

            var once = polygon.ToWkt();
            Assert.Equal(once, Wkt.Read(once, ImpGeometryFactory.Instance).ToWkt());
        });
    }
}
