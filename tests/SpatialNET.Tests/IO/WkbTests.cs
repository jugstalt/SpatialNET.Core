using System.Buffers.Binary;
using CsCheck;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Oracle;
using ForeignGeometryFactory = SpatialNET.Tests.Foreign.ForeignGeometryFactory;

namespace SpatialNET.Tests.IO;

/// <summary>Builds WKB byte by byte, so that the reader is tested against bytes that do not come from our writer.</summary>
internal sealed class WkbBuilder(bool littleEndian = true)
{
    private readonly List<byte> _bytes = [];

    public WkbBuilder Order() => Byte((byte)(littleEndian ? 1 : 0));

    public WkbBuilder Byte(byte value)
    {
        _bytes.Add(value);
        return this;
    }

    public WkbBuilder UInt(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        else BinaryPrimitives.WriteUInt32BigEndian(span, value);
        _bytes.AddRange(span.ToArray());
        return this;
    }

    public WkbBuilder Double(params double[] values)
    {
        Span<byte> span = stackalloc byte[8];
        foreach (var value in values)
        {
            if (littleEndian) BinaryPrimitives.WriteDoubleLittleEndian(span, value);
            else BinaryPrimitives.WriteDoubleBigEndian(span, value);
            _bytes.AddRange(span.ToArray());
        }

        return this;
    }

    /// <summary>Byte order and type code.</summary>
    public WkbBuilder Header(uint type) => Order().UInt(type);

    public byte[] ToArray() => _bytes.ToArray();
}

public class WkbReadTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static Coordinate Single(IGeometry2D g) => Internal.GeometryAccess.ToCoordinate(Assert.IsAssignableFrom<IPoint2D>(g));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_a_point_in_either_byte_order(bool littleEndian)
    {
        var bytes = new WkbBuilder(littleEndian).Header(1).Double(1.5, -2).ToArray();

        Assert.Equal(new Coordinate(1.5, -2), Single(Wkb.Read(bytes, F)));
    }

    [Theory]
    [InlineData(1001u, true, false)]
    [InlineData(2001u, false, true)]
    [InlineData(3001u, true, true)]
    public void Reads_ISO_Z_M_and_ZM(uint type, bool z, bool m)
    {
        var values = new List<double> { 1, 2 };
        if (z) values.Add(3);
        if (m) values.Add(4);
        var bytes = new WkbBuilder().Header(type).Double(values.ToArray()).ToArray();

        var c = Single(Wkb.Read(bytes, F));

        Assert.Equal(z, c.HasZ);
        Assert.Equal(m, c.HasM);
        if (z) Assert.Equal(3, c.Z);
        if (m) Assert.Equal(4, c.M);
    }

    [Fact]
    public void Reads_EWKB_flags_with_and_without_SRID()
    {
        var withSrid = new WkbBuilder().Header(0x80000000u | 0x20000000u | 1).UInt(4326).Double(1, 2, 3).ToArray();
        var zm = new WkbBuilder().Header(0x80000000u | 0x40000000u | 1).Double(1, 2, 3, 4).ToArray();
        var m = new WkbBuilder().Header(0x40000000u | 1).Double(1, 2, 7).ToArray();

        Assert.Equal(new Coordinate(1, 2, 3), Single(Wkb.Read(withSrid, F)));
        Assert.Equal(new Coordinate(1, 2, 3, 4), Single(Wkb.Read(zm, F)));
        Assert.Equal(new Coordinate(1, 2, M: 7), Single(Wkb.Read(m, F)));
    }

    [Fact]
    public void M_of_zero_is_a_measure()
    {
        var bytes = new WkbBuilder().Header(2001).Double(1, 2, 0).ToArray();

        Assert.True(Single(Wkb.Read(bytes, F)).HasM);
    }

    [Fact]
    public void Drops_Z_and_M_if_the_factory_does_not_support_them()
    {
        var bytes = new WkbBuilder().Header(3001).Double(1, 2, 3, 4).ToArray();

        Assert.Equal(new Coordinate(1, 2), Single(Wkb.Read(bytes, new ImpGeometryFactory(supportsZ: false, supportsM: false))));
    }

    [Fact]
    public void Reads_the_NaN_point_as_an_empty_multipoint()
    {
        var bytes = new WkbBuilder().Header(1).Double(double.NaN, double.NaN).ToArray();

        Assert.Equal(0, Assert.IsAssignableFrom<IMultiPoint2D>(Wkb.Read(bytes, F)).PointCount);
    }

    [Fact]
    public void Reads_nested_geometries_with_their_own_byte_order()
    {
        var bytes = new WkbBuilder(littleEndian: false).Header(4).UInt(2).ToArray()
            .Concat(new WkbBuilder(littleEndian: true).Header(1).Double(1, 2).ToArray())
            .Concat(new WkbBuilder(littleEndian: false).Header(1).Double(3, 4).ToArray())
            .ToArray();

        var mp = Assert.IsAssignableFrom<IMultiPoint2D>(Wkb.Read(bytes, F));

        Assert.Equal(new[] { new Coordinate(1, 2), new Coordinate(3, 4) }, Internal.Rings.Coordinates(mp));
    }

    [Fact]
    public void Reads_lines_and_polygons_with_holes()
    {
        var line = new WkbBuilder().Header(2).UInt(2).Double(0, 0, 1, 1).ToArray();
        var polygon = new WkbBuilder().Header(3).UInt(2)
            .UInt(5).Double(0, 0, 10, 0, 10, 10, 0, 10, 0, 0)
            .UInt(5).Double(2, 2, 2, 8, 8, 8, 8, 2, 2, 2).ToArray();

        Assert.Equal("LINESTRING (0 0, 1 1)", Wkb.Read(line, F).ToWkt());
        Assert.Equal("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))", Wkb.Read(polygon, F).ToWkt());
    }

    [Fact]
    public void Reads_multi_geometries()
    {
        var multiLine = new WkbBuilder().Header(5).UInt(2)
            .Header(2).UInt(2).Double(0, 0, 1, 1)
            .Header(2).UInt(2).Double(2, 2, 3, 3).ToArray();
        var multiPolygon = new WkbBuilder().Header(6).UInt(2)
            .Header(3).UInt(1).UInt(4).Double(0, 0, 1, 0, 1, 1, 0, 0)
            .Header(3).UInt(1).UInt(4).Double(5, 5, 6, 5, 6, 6, 5, 5).ToArray();

        Assert.Equal("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", Wkb.Read(multiLine, F).ToWkt());
        Assert.Equal("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", Wkb.Read(multiPolygon, F).ToWkt());
    }

    [Fact]
    public void GeometryCollection_is_flattened_by_ReadAll_and_rejected_by_Read()
    {
        var bytes = new WkbBuilder().Header(7).UInt(2)
            .Header(1).Double(1, 2)
            .Header(2).UInt(2).Double(0, 0, 1, 1).ToArray();

        Assert.Throws<NotSupportedException>(() => Wkb.Read(bytes, F));

        var all = Wkb.ReadAll(bytes, F);
        Assert.Equal(2, all.Count);
        Assert.IsAssignableFrom<IPoint2D>(all[0]);
        Assert.IsAssignableFrom<IPolyline2D>(all[1]);
    }

    [Theory]
    [InlineData(8u, "CircularString")]
    [InlineData(9u, "CompoundCurve")]
    [InlineData(10u, "CurvePolygon")]
    [InlineData(11u, "MultiCurve")]
    [InlineData(12u, "MultiSurface")]
    [InlineData(15u, "PolyhedralSurface")]
    [InlineData(16u, "TIN")]
    [InlineData(17u, "Triangle")]
    public void Curves_and_other_surfaces_are_not_supported(uint type, string name)
    {
        var bytes = new WkbBuilder().Header(type).UInt(0).ToArray();

        var ex = Assert.Throws<NotSupportedException>(() => Wkb.Read(bytes, F));

        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void Read_of_the_wrong_kind_throws()
    {
        var bytes = new WkbBuilder().Header(1).Double(1, 2).ToArray();

        Assert.Throws<FormatException>(() => Wkb.Read<IPolygon2D>(bytes, F));
        Assert.IsAssignableFrom<IPoint2D>(Wkb.Read<IPoint2D>(bytes, F));
    }

    [Fact]
    public void Rejects_malformed_input_with_the_byte_offset()
    {
        var unknownOrder = new byte[] { 2, 1, 0, 0, 0 };
        var unknownType = new WkbBuilder().Header(99).ToArray();
        var unknownDimension = new WkbBuilder().Header(4001).Double(1, 2).ToArray();
        var truncated = new WkbBuilder().Header(1).Double(1).ToArray();
        var trailing = new WkbBuilder().Header(1).Double(1, 2).Byte(0).ToArray();
        var wrongChild = new WkbBuilder().Header(4).UInt(1).Header(2).UInt(0).ToArray();

        Assert.Contains("byte order", Assert.Throws<FormatException>(() => Wkb.Read(unknownOrder, F)).Message);
        Assert.Contains("type 99", Assert.Throws<FormatException>(() => Wkb.Read(unknownType, F)).Message);
        Assert.Throws<FormatException>(() => Wkb.Read(unknownDimension, F));
        Assert.Contains("end of data", Assert.Throws<FormatException>(() => Wkb.Read(truncated, F)).Message);
        Assert.Contains("byte 21", Assert.Throws<FormatException>(() => Wkb.Read(trailing, F)).Message);
        Assert.Throws<FormatException>(() => Wkb.Read(wrongChild, F));
        Assert.Throws<FormatException>(() => Wkb.Read([], F));
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    public void A_forged_count_is_rejected_before_anything_is_allocated(uint type)
    {
        var bytes = new WkbBuilder().Header(type).UInt(uint.MaxValue).ToArray();

        Assert.Throws<FormatException>(() => Wkb.ReadAll(bytes, F));
    }

    [Fact]
    public void Truncating_any_valid_input_never_crashes()
    {
        var valid = Wkt.Read("MULTIPOLYGON ZM (((0 0 1 2, 10 0 1 2, 10 10 1 2, 0 0 1 2), (2 2 1 2, 8 2 1 2, 8 8 1 2, 2 2 1 2)))", F).ToWkb();

        for (int length = 0; length < valid.Length; length++)
        {
            Assert.Throws<FormatException>(() => Wkb.Read(valid.AsSpan(0, length), F));
        }

        Assert.IsAssignableFrom<IPolygon2D>(Wkb.Read(valid, F));
    }
}

[Collection(GlobalStateCollection.Name)]
public sealed class WkbDefaultFactoryTests : IDisposable
{
    private readonly IGeometryFactory? _saved = GeometryFactory.Default;

    public void Dispose() => GeometryFactory.Default = _saved;

    [Fact]
    public void Uses_the_default_factory_and_fails_clearly_without_one()
    {
        var bytes = new WkbBuilder().Header(1).Double(1, 2).ToArray();

        GeometryFactory.Default = ForeignGeometryFactory.Instance;
        Assert.IsType<Foreign.Point>(Wkb.Read(bytes));

        GeometryFactory.Default = null;
        Assert.Throws<InvalidOperationException>(() => Wkb.Read(bytes));
    }
}

public class WkbWriteTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Fact]
    public void Writes_ISO_little_endian_without_SRID()
    {
        var expected = new WkbBuilder().Header(1).Double(1, 2).ToArray();

        Assert.Equal(expected, Wkt.Read("POINT (1 2)", F).ToWkb());
    }

    [Theory]
    [InlineData("POINT Z (1 2 3)", 1001u)]
    [InlineData("POINT M (1 2 3)", 2001u)]
    [InlineData("POINT ZM (1 2 3 4)", 3001u)]
    [InlineData("LINESTRING ZM (0 0 1 2, 1 1 3 4)", 3002u)]
    [InlineData("MULTIPOINT M ((1 2 3))", 2004u)]
    [InlineData("POLYGON M ((0 0 1, 1 0 2, 1 1 3, 0 0 1))", 2003u)]
    public void Writes_the_ISO_type_code(string wkt, uint type)
    {
        var bytes = Wkt.Read(wkt, F).ToWkb();

        Assert.Equal((byte)1, bytes[0]);
        Assert.Equal(type, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, 4)));
    }

    [Theory]
    [InlineData("POINT (1 2)", "POINT (1 2)")]
    [InlineData("POINT EMPTY", "MULTIPOINT EMPTY")]
    [InlineData("MULTIPOINT ((1 2), (3 4))", "MULTIPOINT ((1 2), (3 4))")]
    [InlineData("LINESTRING EMPTY", "LINESTRING EMPTY")]
    [InlineData("MULTILINESTRING ((0 0, 1 1))", "LINESTRING (0 0, 1 1)")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("POLYGON EMPTY", "POLYGON EMPTY")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))", "POLYGON ((0 0, 1 0, 1 1, 0 0))")]
    [InlineData("POLYGON ((0 0, 1 0, 1 1, 0 1))", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")]
    [InlineData("POLYGON ((0 0, 0 1, 1 1, 1 0, 0 0))", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")]
    [InlineData("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [InlineData("POINT ZM (0.1 -1E+300 3 4)", "POINT ZM (0.1 -1E+300 3 4)")]
    [InlineData("LINESTRING M (0 0 1, 1 1 NaN)", "LINESTRING M (0 0 1, 1 1 NaN)")]
    [InlineData("POINT M (1 2 0)", "POINT M (1 2 0)")]
    public void Writes_the_slimmest_type_with_the_same_structure_as_WKT(string input, string expected)
    {
        var geometry = Wkt.Read(input, F);

        Assert.Equal(expected, Wkb.Read(geometry.ToWkb(), F).ToWkt());
        Assert.Equal(expected, geometry.ToWkt());
    }

    [Fact]
    public void ZMode_and_MMode_control_the_dimensions()
    {
        var geometry = Wkt.Read("LINESTRING ZM (0 0 1 2, 1 1 3 4)", F);

        Assert.Equal("LINESTRING Z (0 0 1, 1 1 3)",
            Wkb.Read(geometry.ToWkb(new WkbOptions { MMode = MMode.Never }), F).ToWkt());
        Assert.Equal("LINESTRING M (0 0 2, 1 1 4)",
            Wkb.Read(geometry.ToWkb(new WkbOptions { ZMode = ZMode.Never }), F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 1 1)",
            Wkb.Read(geometry.ToWkb(new WkbOptions { ZMode = ZMode.Never, MMode = MMode.Never }), F).ToWkt());
    }

    [Fact]
    public void Z_mode_auto_omits_zero_heights_of_host_geometries()
    {
        var line = Wkt.Read("LINESTRING (0 0, 1 1)", ForeignGeometryFactory.Instance); // always Z = 0

        Assert.Equal(new WkbBuilder().Header(2).UInt(2).Double(0, 0, 1, 1).ToArray(), line.ToWkb());
        Assert.Equal(1002u, BinaryPrimitives.ReadUInt32LittleEndian(line.ToWkb(new WkbOptions { ZMode = ZMode.Always }).AsSpan(1, 4)));
    }

    [Fact]
    public void Points_without_M_get_NaN_if_others_have_it()
    {
        var line = F.CreatePolyline([new Coordinate[] { new(0, 0, M: 1), new(1, 1) }], null);

        var read = Assert.IsAssignableFrom<IPolyline2D>(Wkb.Read(line.ToWkb(), F));

        Assert.True(Internal.GeometryAccess.ToCoordinate(read[0][0]).HasM);
        Assert.False(Internal.GeometryAccess.ToCoordinate(read[0][1]).HasM);
    }

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 1d)]
    public void Non_finite_coordinates_throw(double x, double y)
    {
        var line = F.CreatePolyline([new Coordinate[] { new(0, 0), new(x, y) }], null);

        Assert.Throws<ArgumentException>(() => line.ToWkb());
    }

    [Fact]
    public void Null_geometry_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Wkb.ToWkb(null!));
    }
}

public class WkbOracleTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    public static TheoryData<string> Geometries =>
    [
        "POINT (1 2)",
        "MULTIPOINT ((1 2), (3 4), (5 6))",
        "LINESTRING (0 0, 10 5, 20 -3.25)",
        "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3, 4 2))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))",
        "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0)), ((10 10, 14 10, 14 14, 10 14, 10 10), (11 11, 11 13, 13 13, 13 11, 11 11)))",
        "POINT Z (1 2 3)",
        "LINESTRING Z (0 0 1, 10 5 2, 20 -3.25 3)",
        "POLYGON Z ((0 0 1, 10 0 2, 10 10 3, 0 10 4, 0 0 1))",
        "POINT M (1 2 3)",
        "LINESTRING M (0 0 1, 10 5 2)",
        "POINT ZM (1 2 3 4)",
        "LINESTRING ZM (0 0 1 9, 10 5 2 8)",
        "POLYGON ZM ((0 0 1 5, 10 0 2 6, 10 10 3 7, 0 10 4 8, 0 0 1 5))",
    ];

    [Theory]
    [MemberData(nameof(Geometries))]
    public void NTS_reads_what_we_write(string wkt)
    {
        var bytes = Wkt.Read(wkt, F).ToWkb();

        var nts = new WKBReader().Read(bytes);

        AssertSameCoordinates(Nts.Read(wkt).Normalized(), nts.Normalized());
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void We_read_what_NTS_writes(string wkt)
    {
        var geometry = Nts.Read(wkt);
        geometry.SRID = 4326;
        bool z = wkt.Contains(" Z ", StringComparison.Ordinal) || wkt.Contains(" ZM ", StringComparison.Ordinal);
        bool m = wkt.Contains(" M ", StringComparison.Ordinal) || wkt.Contains(" ZM ", StringComparison.Ordinal);

        foreach (var order in new[] { ByteOrder.LittleEndian, ByteOrder.BigEndian })
        {
            var ewkb = new WKBWriter(order, handleSRID: true, emitZ: z, emitM: m).Write(geometry);
            var ours = Wkb.Read(ewkb, F);

            Assert.Equal(Wkt.Read(wkt, F).ToWkt(), ours.ToWkt());
        }
    }

    private static void AssertSameCoordinates(Geometry expected, Geometry actual)
    {
        var e = expected.Coordinates;
        var a = actual.Coordinates;
        Assert.Equal(e.Length, a.Length);
        for (int i = 0; i < e.Length; i++)
        {
            Assert.Equal(e[i].X, a[i].X);
            Assert.Equal(e[i].Y, a[i].Y);
            Assert.Equal(double.IsNaN(e[i].Z), double.IsNaN(a[i].Z));
            if (!double.IsNaN(e[i].Z)) Assert.Equal(e[i].Z, a[i].Z);
            Assert.Equal(double.IsNaN(e[i].M), double.IsNaN(a[i].M));
            if (!double.IsNaN(e[i].M)) Assert.Equal(e[i].M, a[i].M);
        }
    }
}

public class WkbPropertyTests
{
    private static readonly Gen<Coordinate> Vertex =
        Gen.Select(Gen.Double[-1e6, 1e6], Gen.Double[-1e6, 1e6], Gen.Double[-1e4, 1e4], Gen.Double[-1e4, 1e4], Gen.Int[0, 3])
            .Select((x, y, z, m, kind) => new Coordinate(x, y, kind is 1 or 3 ? z : double.NaN, kind is 2 or 3 ? m : double.NaN));

    private static IGeometry2D Build(int kind, Coordinate[] vertices, IGeometryFactory f)
    {
        switch (kind)
        {
            case 0:
                return f.CreateMultiPoint(vertices, null);
            case 1:
                return f.CreatePolyline([vertices.AsMemory()], null);
            case 2:
                return f.CreatePolyline([vertices.AsMemory(0, vertices.Length / 2), vertices.AsMemory(vertices.Length / 2)], null);
            default:
                // two disjoint squares with the vertices' Z and M, so that the polygon is always valid
                var parts = new[] { 0, 1 }.Select(k =>
                {
                    double ox = k * 100;
                    var v = vertices[k % vertices.Length];
                    return new PolygonPart(new Coordinate[]
                    {
                        new(ox, 0, v.Z, v.M), new(ox + 10, 0, v.Z, v.M), new(ox + 10, 10, v.Z, v.M), new(ox, 10, v.Z, v.M), new(ox, 0, v.Z, v.M),
                    });
                }).ToArray();
                return f.CreatePolygon(parts, null);
        }
    }

    [Fact]
    public void Round_trip_keeps_the_structure_of_WKT_for_the_reference_implementation()
    {
        Gen.Select(Gen.Int[0, 3], Vertex.Array[2, 12]).Sample((kind, vertices) =>
        {
            var geometry = Build(kind, vertices, ImpGeometryFactory.Instance);

            var reread = Wkb.Read(geometry.ToWkb(), ImpGeometryFactory.Instance);

            Assert.Equal(geometry.ToWkt(), reread.ToWkt());
        });
    }

    [Fact]
    public void Round_trip_is_exact_for_the_foreign_implementation_in_XY()
    {
        var options = new WktOptions { ZMode = ZMode.Never, MMode = MMode.Never };
        Gen.Select(Gen.Int[0, 3], Vertex.Array[2, 12]).Sample((kind, vertices) =>
        {
            var geometry = Build(kind, vertices, ForeignGeometryFactory.Instance);

            var reread = Wkb.Read(geometry.ToWkb(), ForeignGeometryFactory.Instance);

            if (kind < 3)
            {
                Assert.Equal(geometry.ToWkt(options), reread.ToWkt(options));
            }
            else
            {
                // The foreign implementation stores rings open and reversed, so the start vertex of a ring may move.
                Assert.Equal(((IPolygon2D)geometry).Area(), ((IPolygon2D)reread).Area());
                Assert.Equal(XySet(geometry), XySet(reread));
            }
        });
    }

    private static (double X, double Y)[] XySet(IGeometry2D g) =>
        Internal.GeometryAccess.AllPoints(g).Select(p => (p.X, p.Y)).Distinct().OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();

    [Fact]
    public void Reading_arbitrary_bytes_never_crashes_apart_from_FormatException_and_NotSupportedException()
    {
        Gen.Byte.Array[0, 80].Sample(bytes =>
        {
            try
            {
                Wkb.ReadAll(bytes, ImpGeometryFactory.Instance);
            }
            catch (Exception e) when (e is FormatException or NotSupportedException)
            {
                // expected for random bytes
            }
        }, iter: 2000);
    }
}
