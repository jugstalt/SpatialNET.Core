using System.Text;
using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.IO;

/// <summary>
/// Builds protobuf bytes field by field, independently of the encoder, so that expectations are not derived from the code
/// under test. Used to reproduce the mapbox/mvt-fixtures expectations.
/// </summary>
public sealed class PbfBuilder
{
    private readonly List<byte> _bytes = [];

    public PbfBuilder Varint(ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            _bytes.Add(value == 0 ? b : (byte)(b | 0x80));
        }
        while (value != 0);

        return this;
    }

    public PbfBuilder Key(int field, int wire) => Varint((ulong)((field << 3) | wire));

    public PbfBuilder VarintField(int field, ulong value) => Key(field, 0).Varint(value);

    public PbfBuilder Bytes(int field, byte[] bytes)
    {
        Key(field, 2).Varint((ulong)bytes.Length);
        _bytes.AddRange(bytes);
        return this;
    }

    public PbfBuilder Message(int field, PbfBuilder message) => Bytes(field, message.ToArray());

    public PbfBuilder String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

    public PbfBuilder Packed(int field, params uint[] values)
    {
        var p = new PbfBuilder();
        foreach (var v in values)
        {
            p.Varint(v);
        }

        return Message(field, p);
    }

    public PbfBuilder Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    public byte[] ToArray() => _bytes.ToArray();

    /// <summary>A layer in reference field order: version, name, features, keys, values, extent.</summary>
    public static PbfBuilder Layer(string name, PbfBuilder[] features, string[]? keys = null, PbfBuilder[]? values = null, uint extent = 4096, uint version = 2)
    {
        var l = new PbfBuilder().VarintField(15, version).String(1, name);
        foreach (var f in features) l.Message(2, f);
        foreach (var k in keys ?? []) l.String(3, k);
        foreach (var v in values ?? []) l.Message(4, v);
        return l.VarintField(5, extent);
    }

    public static PbfBuilder Feature(int type, uint[] geometry, ulong? id = null, uint[]? tags = null)
    {
        var f = new PbfBuilder();
        if (id is { } i) f.VarintField(1, i);
        if (tags is { Length: > 0 }) f.Packed(2, tags);
        f.VarintField(3, (ulong)type);
        if (geometry.Length > 0) f.Packed(4, geometry);
        return f;
    }

    public static byte[] Tile(params PbfBuilder[] layers)
    {
        var t = new PbfBuilder();
        foreach (var l in layers) t.Message(3, l);
        return t.ToArray();
    }
}

/// <summary>Command encoding as given by the examples of the vector tile spec (section 4.3.5).</summary>
public class MvtGeometryEncodingTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    public static TheoryData<string, MvtGeometryType, uint[]> SpecExamples => new()
    {
        { "POINT (25 17)", MvtGeometryType.Point, [9, 50, 34] },
        { "MULTIPOINT ((5 7), (3 2))", MvtGeometryType.Point, [17, 10, 14, 3, 9] },
        { "LINESTRING (2 2, 2 10, 10 10)", MvtGeometryType.LineString, [9, 4, 4, 18, 0, 16, 16, 0] },
        { "MULTILINESTRING ((2 2, 2 10, 10 10), (1 1, 3 5))", MvtGeometryType.LineString, [9, 4, 4, 18, 0, 16, 16, 0, 9, 17, 17, 10, 4, 8] },
        { "POLYGON ((3 6, 8 12, 20 34, 3 6))", MvtGeometryType.Polygon, [9, 6, 12, 18, 10, 12, 24, 44, 15] },
        {
            "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0)), ((11 11, 20 11, 20 20, 11 20, 11 11), (13 13, 13 17, 17 17, 17 13, 13 13)))",
            MvtGeometryType.Polygon,
            [9, 0, 0, 26, 20, 0, 0, 20, 19, 0, 15, 9, 22, 2, 26, 18, 0, 0, 18, 17, 0, 15, 9, 4, 13, 26, 0, 8, 8, 0, 0, 7, 15]
        },
    };

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void Encode_matches_spec_example(string wkt, MvtGeometryType type, uint[] expected)
    {
        var (actualType, commands) = MvtGeometry.Encode(Wkt.Read(wkt, F));
        Assert.Equal(type, actualType);
        Assert.Equal(expected, commands);
    }

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void Decode_of_spec_example_gives_the_geometry_back(string wkt, MvtGeometryType type, uint[] commands)
    {
        var decoded = MvtGeometry.Decode(type, commands, F);
        var (_, again) = MvtGeometry.Encode(decoded);
        Assert.Equal(commands, again);
        Assert.Equal(Wkt.Read(wkt, F).ToWkt(), decoded.ToWkt());
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(-1, 1u)]
    [InlineData(1, 2u)]
    [InlineData(-2, 3u)]
    [InlineData(int.MaxValue, 4294967294u)]
    [InlineData(int.MinValue, 4294967295u)]
    public void ZigZag_round_trips(int value, uint encoded)
    {
        Assert.Equal(encoded, MvtGeometry.ZigZag(value));
        Assert.Equal(value, MvtGeometry.UnZigZag(encoded));
    }

    [Fact]
    public void Command_packs_id_and_count()
    {
        Assert.Equal(9u, MvtGeometry.Command(MvtGeometry.MoveTo, 1));
        Assert.Equal(26u, MvtGeometry.Command(MvtGeometry.LineTo, 3));
        Assert.Equal(15u, MvtGeometry.Command(MvtGeometry.ClosePath, 1));
    }

    [Fact]
    public void Encode_orients_rings_and_drops_repeated_points()
    {
        // Shell given with negative area in tile coordinates, a repeated point and a closing point.
        var (_, commands) = MvtGeometry.Encode(Wkt.Read("POLYGON ((0 0, 0 10, 0 10, 10 10, 10 0, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))", F));
        var rings = DecodeRings(commands);
        Assert.Equal(2, rings.Count);
        Assert.Equal(4, rings[0].Count);
        Assert.True(Area(rings[0]) > 0, "exterior ring must have positive area");
        Assert.True(Area(rings[1]) < 0, "interior ring must have negative area");
    }

    [Fact]
    public void Encode_drops_degenerate_parts()
    {
        Assert.Equal(MvtGeometryType.Unknown, MvtGeometry.Encode(Wkt.Read("POLYGON ((0 0, 0.2 0, 0.2 0.2, 0 0))", F)).Type);
        Assert.Equal(MvtGeometryType.Unknown, MvtGeometry.Encode(Wkt.Read("LINESTRING (1 1, 1.2 1.1)", F)).Type);
        Assert.Null(MvtFeature.Create(Wkt.Read("POLYGON ((0 0, 5 0, 10 0, 0 0))", F)));
    }

    [Theory]
    [InlineData(MvtGeometryType.Point, new uint[] { 9, 50 })]            // missing parameter
    [InlineData(MvtGeometryType.Point, new uint[] { 18, 2, 2 })]         // LineTo in a point
    [InlineData(MvtGeometryType.LineString, new uint[] { 18, 2, 2 })]    // LineTo without MoveTo
    [InlineData(MvtGeometryType.LineString, new uint[] { 9, 2, 2, 15 })] // ClosePath in a line
    [InlineData(MvtGeometryType.Polygon, new uint[] { 9, 0, 0, 26, 20, 0, 0, 20, 19, 0 })] // ring not closed
    [InlineData(MvtGeometryType.Polygon, new uint[] { 12 })]             // unknown command id 4
    [InlineData(MvtGeometryType.Unknown, new uint[] { 9, 2, 2 })]
    public void Decode_rejects_invalid_commands(MvtGeometryType type, uint[] commands) =>
        Assert.Throws<FormatException>(() => MvtGeometry.Decode(type, commands, F));

    internal static List<List<(long X, long Y)>> DecodeRings(IReadOnlyList<uint> commands)
    {
        var rings = new List<List<(long X, long Y)>>();
        long x = 0, y = 0;
        for (int i = 0; i < commands.Count;)
        {
            uint c = commands[i++];
            int id = (int)(c & 7), count = (int)(c >> 3);
            if (id == 7)
            {
                continue;
            }

            for (int k = 0; k < count; k++)
            {
                x += MvtGeometry.UnZigZag(commands[i++]);
                y += MvtGeometry.UnZigZag(commands[i++]);
                if (id == 1) rings.Add([]);
                rings[^1].Add((x, y));
            }
        }

        return rings;
    }

    internal static long Area(List<(long X, long Y)> ring)
    {
        long sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum;
    }
}

/// <summary>
/// Byte-level conformance: the expectations of mapbox/mvt-fixtures (valid fixtures), reproduced with an independent protobuf
/// builder in reference field order.
/// </summary>
public class MvtConformanceTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static MvtTile SingleFeature(MvtFeature feature, string name = "hello")
    {
        var tile = new MvtTile();
        var layer = new MvtLayer(name);
        layer.Features.Add(feature);
        tile.Layers.Add(layer);
        return tile;
    }

    [Fact]
    public void Fixture_001_empty_tile_is_zero_bytes()
    {
        Assert.Empty(Mvt.Encode(new MvtTile()));
        Assert.Empty(Mvt.Decode([]).Layers);
    }

    [Fact]
    public void Fixture_002_single_point_without_id_literal_bytes()
    {
        byte[] expected =
        [
            0x1A, 0x15,                                   // Tile.layers, 21 bytes
            0x78, 0x02,                                   // version = 2
            0x0A, 0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', // name
            0x12, 0x07, 0x18, 0x01, 0x22, 0x03, 0x09, 0x32, 0x22, // feature: type POINT, geometry [9, 50, 34]
            0x28, 0x80, 0x20,                             // extent = 4096
        ];

        var feature = MvtFeature.Create(Wkt.Read("POINT (25 17)", F))!;
        Assert.Equal(expected, Mvt.Encode(SingleFeature(feature)));

        var decoded = Mvt.Decode(expected);
        var layer = Assert.Single(decoded.Layers);
        Assert.Equal("hello", layer.Name);
        Assert.Equal(2u, layer.Version);
        Assert.Equal(4096u, layer.Extent);
        var f = Assert.Single(layer.Features);
        Assert.Null(f.Id);
        Assert.Equal("POINT (25 17)", f.ToGeometry(F).ToWkt());
    }

    [Fact]
    public void Fixture_003_point_with_id()
    {
        var expected = PbfBuilder.Tile(PbfBuilder.Layer("hello", [PbfBuilder.Feature(1, [9, 50, 34], id: 1)]));
        var feature = MvtFeature.Create(Wkt.Read("POINT (25 17)", F), id: 1)!;
        Assert.Equal(expected, Mvt.Encode(SingleFeature(feature)));
        Assert.Equal(1UL, Mvt.Decode(expected).Layers[0].Features[0].Id);
    }

    public static TheoryData<string, MvtValue, PbfBuilder> ValueFixtures => new()
    {
        { "string", MvtValue.String("hello"), new PbfBuilder().String(1, "hello") },
        { "float", MvtValue.Float(3.1f), new PbfBuilder().Key(2, 5).Raw(BitConverter.GetBytes(3.1f)) },
        { "double", MvtValue.Double(1.23), new PbfBuilder().Key(3, 1).Raw(BitConverter.GetBytes(1.23)) },
        { "int", MvtValue.Int(6), new PbfBuilder().VarintField(4, 6) },
        { "int negative", MvtValue.Int(-1), new PbfBuilder().VarintField(4, ulong.MaxValue) },
        { "uint", MvtValue.UInt(87948), new PbfBuilder().VarintField(5, 87948) },
        { "sint", MvtValue.SInt(-87948), new PbfBuilder().VarintField(6, 87948 * 2 - 1) },
        { "bool", MvtValue.Bool(true), new PbfBuilder().VarintField(7, 1) },
    };

    [Theory]
    [MemberData(nameof(ValueFixtures))]
    public void Fixtures_032_to_038_value_types(string name, MvtValue value, PbfBuilder valueMessage)
    {
        _ = name;
        var expected = PbfBuilder.Tile(PbfBuilder.Layer("hello",
            [PbfBuilder.Feature(1, [9, 50, 34], tags: [0, 0])], ["key1"], [valueMessage]));

        var feature = new MvtFeature
        {
            GeometryType = MvtGeometryType.Point,
            Geometry = [9, 50, 34],
            Attributes = [new("key1", value)],
        };

        Assert.Equal(expected, Mvt.Encode(SingleFeature(feature)));
        var decoded = Mvt.Decode(expected).Layers[0].Features[0].Attributes[0];
        Assert.Equal("key1", decoded.Key);
        Assert.Equal(value, decoded.Value);
    }

    [Fact]
    public void Keys_and_values_are_deduplicated_in_order_of_first_use()
    {
        var expected = PbfBuilder.Tile(PbfBuilder.Layer("hello",
            [
                PbfBuilder.Feature(1, [9, 50, 34], id: 1, tags: [0, 0, 1, 1]),
                PbfBuilder.Feature(1, [9, 2, 2], id: 2, tags: [1, 1, 0, 2]),
            ],
            ["name", "kind"],
            [new PbfBuilder().String(1, "a"), new PbfBuilder().String(1, "x"), new PbfBuilder().String(1, "b")]));

        var layer = new MvtLayer("hello");
        layer.Features.Add(MvtFeature.Create(Wkt.Read("POINT (25 17)", F), [new("name", "a"), new("kind", "x")], 1)!);
        layer.Features.Add(MvtFeature.Create(Wkt.Read("POINT (1 1)", F), [new("kind", "x"), new("name", "b"), new("skipped", null)], 2)!);
        var tile = new MvtTile();
        tile.Layers.Add(layer);

        Assert.Equal(expected, Mvt.Encode(tile));
    }

    [Fact]
    public void Multiple_layers_with_polygon_and_line()
    {
        var expected = PbfBuilder.Tile(
            PbfBuilder.Layer("water", [PbfBuilder.Feature(3, [9, 6, 12, 18, 10, 12, 24, 44, 15])], extent: 512),
            PbfBuilder.Layer("roads", [PbfBuilder.Feature(2, [9, 4, 4, 18, 0, 16, 16, 0])]));

        var tile = new MvtTile();
        var water = new MvtLayer("water") { Extent = 512 };
        water.Features.Add(MvtFeature.Create(Wkt.Read("POLYGON ((3 6, 8 12, 20 34, 3 6))", F))!);
        var roads = new MvtLayer("roads");
        roads.Features.Add(MvtFeature.Create(Wkt.Read("LINESTRING (2 2, 2 10, 10 10)", F))!);
        tile.Layers.Add(water);
        tile.Layers.Add(roads);

        Assert.Equal(expected, Mvt.Encode(tile));
        Assert.Equal(["water", "roads"], Mvt.Decode(expected).Layers.Select(l => l.Name));
    }

    [Fact]
    public void Duplicate_layer_names_are_rejected()
    {
        var tile = new MvtTile();
        tile.Layers.Add(new MvtLayer("a"));
        tile.Layers.Add(new MvtLayer("a"));
        Assert.Throws<ArgumentException>(() => Mvt.Encode(tile));
    }

    [Fact]
    public void Decoder_accepts_other_field_order_unpacked_fields_and_unknown_fields()
    {
        var feature = new PbfBuilder()
            .VarintField(4, 9).VarintField(4, 50).VarintField(4, 34) // unpacked geometry before type
            .VarintField(3, 1)
            .VarintField(2, 0).VarintField(2, 0)                       // unpacked tags
            .VarintField(99, 7)                                        // unknown field
            .VarintField(1, 42);
        var layer = new PbfBuilder()
            .VarintField(5, 256)
            .String(3, "k")
            .Message(4, new PbfBuilder().VarintField(5, 3))
            .Message(2, feature)
            .String(1, "reordered")
            .Key(20, 1).Raw(0, 0, 0, 0, 0, 0, 0, 0);                   // unknown fixed64
        var bytes = PbfBuilder.Tile(layer);

        var l = Assert.Single(Mvt.Decode(bytes).Layers);
        Assert.Equal("reordered", l.Name);
        Assert.Equal(1u, l.Version); // proto default
        Assert.Equal(256u, l.Extent);
        var f = Assert.Single(l.Features);
        Assert.Equal(42UL, f.Id);
        Assert.Equal(MvtValue.UInt(3), f.Attributes[0].Value);
        Assert.Equal("POINT (25 17)", f.ToGeometry(F).ToWkt());
    }

    [Theory]
    [InlineData(new byte[] { 0x1A, 0x05, 0x0A })]                          // truncated
    [InlineData(new byte[] { 0x1A, 0x02, 0x28, 0x01 })]                    // layer without name
    [InlineData(new byte[] { 0x1A, 0x08, 0x0A, 0x01, 0x61, 0x12, 0x03, 0x12, 0x01, 0x00 })] // odd number of tags
    [InlineData(new byte[] { 0x1A, 0x09, 0x0A, 0x01, 0x61, 0x12, 0x04, 0x12, 0x02, 0x00, 0x00 })] // tag refers to missing key
    public void Decoder_rejects_invalid_tiles(byte[] bytes) =>
        Assert.Throws<FormatException>(() => Mvt.Decode(bytes));
}

public class MvtRoundTripTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Fact]
    public void Encode_decode_encode_is_stable_and_keeps_everything()
    {
        var tile = new MvtTile();
        var a = new MvtLayer("a") { Extent = 8192 };
        a.Features.Add(MvtFeature.Create(Wkt.Read("MULTIPOLYGON (((0 0, 100 0, 100 100, 0 100, 0 0), (10 10, 10 20, 20 20, 20 10, 10 10)), ((200 200, 300 200, 300 300, 200 200)))", F),
            [new("s", "text äöü"), new("f", 1.5f), new("d", -2.25), new("i", -7), new("u", 7UL), new("b", false), new("m", MvtValue.Int(-3))], 12345)!);
        a.Features.Add(MvtFeature.Create(Wkt.Read("MULTILINESTRING ((-10 -10, 50 50), (4100 0, 4100 4000))", F), [new("s", "text äöü")])!);
        var b = new MvtLayer("b");
        b.Features.Add(MvtFeature.Create(Wkt.Read("MULTIPOINT ((1 2), (3 4), (-5 6))", F), id: ulong.MaxValue)!);
        tile.Layers.Add(a);
        tile.Layers.Add(b);

        var bytes = Mvt.Encode(tile);
        var decoded = Mvt.Decode(bytes);
        Assert.Equal(bytes, Mvt.Encode(decoded));

        var fa = decoded["a"]!.Features;
        Assert.Equal(8192u, decoded["a"]!.Extent);
        Assert.Equal(12345UL, fa[0].Id);
        Assert.Equal(tile.Layers[0].Features[0].Attributes, fa[0].Attributes);
        Assert.Equal(MvtValueKind.SInt, fa[0].Attributes[3].Value.Kind);
        Assert.Equal(MvtValueKind.UInt, fa[0].Attributes[4].Value.Kind);

        var polygon = Assert.IsAssignableFrom<IPolygon2D>(fa[0].ToGeometry(F));
        Assert.Equal(3, polygon.RingCount);
        Assert.Equal(100 * 100 - 100 + 100 * 100 / 2.0, polygon.Area(), 6);
        Assert.Equal("MULTILINESTRING ((-10 -10, 50 50), (4100 0, 4100 4000))", fa[1].ToGeometry(F).ToWkt());
        Assert.Equal(ulong.MaxValue, decoded["b"]!.Features[0].Id);
        Assert.Equal("MULTIPOINT ((1 2), (3 4), (-5 6))", decoded["b"]!.Features[0].ToGeometry(F).ToWkt());
    }

    [Fact]
    public void Gzip_round_trips_and_decode_detects_it()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 0, 0, 0);
        builder.AddFeature("p", Wkt.Read("POINT (0 0)", F), [new("name", "null island")], 1);
        var raw = builder.ToBytes();
        var gz = builder.ToBytes(gzip: true);

        Assert.True(Mvt.IsGzip(gz));
        Assert.False(Mvt.IsGzip(raw));
        Assert.Equal(raw, Mvt.Gunzip(gz));
        Assert.Equal(raw, Mvt.Encode(Mvt.Decode(gz)));
        Assert.Throws<FormatException>(() => Mvt.Gunzip(new byte[] { 0x1F, 0x8B, 0, 1, 2 }));
    }

    [Fact]
    public void MvtValue_From_maps_clr_types()
    {
        Assert.Equal(MvtValueKind.String, MvtValue.From("x").Kind);
        Assert.Equal(MvtValueKind.Bool, MvtValue.From(true).Kind);
        Assert.Equal(MvtValueKind.Float, MvtValue.From(1f).Kind);
        Assert.Equal(MvtValueKind.Double, MvtValue.From(1d).Kind);
        Assert.Equal(MvtValueKind.Double, MvtValue.From(1m).Kind);
        Assert.Equal(MvtValue.UInt(5), MvtValue.From(5));
        Assert.Equal(MvtValue.SInt(-5), MvtValue.From(-5L));
        Assert.Equal(MvtValue.UInt(5), MvtValue.From((byte)5));
        Assert.Equal(MvtValue.String("2026-11-08T00:00:00.0000000"), MvtValue.From(new DateTime(2026, 11, 8).ToString("o")));
        Assert.Equal(MvtValueKind.String, MvtValue.From(new DateTime(2026, 11, 8)).Kind);
    }
}

public class TileGridTests
{
    private const double E = TileGrid.WebMercatorHalfExtent;

    [Fact]
    public void WebMercator_zoom_0_covers_the_world()
    {
        var grid = TileGrid.WebMercator();
        Assert.Equal(25, grid.LevelCount);
        Assert.Equal(new Envelope2D(-E, -E, E, E), grid.TileEnvelope(0, 0, 0));
        Assert.Equal(156543.03392804097, grid.Resolution(0), 8);
    }

    [Fact]
    public void WebMercator_xyz_rows_count_from_the_top()
    {
        var grid = TileGrid.WebMercator();
        Assert.Equal(new Envelope2D(-E, 0, 0, E), grid.TileEnvelope(1, 0, 0));
        Assert.Equal(new Envelope2D(0, -E, E, 0), grid.TileEnvelope(1, 1, 1));
        var c = grid.TileEnvelope(10, 356, 558).Center;
        Assert.Equal((356L, 558L), grid.TileAt(10, c.X, c.Y));
    }

    [Fact]
    public void Scale_and_resolution_convert_at_96_dpi()
    {
        double scale0 = TileGrid.ResolutionToScale(TileGrid.WebMercatorResolution(0));
        Assert.Equal(591658710.90913117, scale0, 3);
        Assert.Equal(TileGrid.WebMercatorResolution(0), TileGrid.ScaleToResolution(scale0), 9);
    }

    [Theory]
    [InlineData(591658710.90913117, 0)]
    [InlineData(591658710.90913117 / 1024, 10)]
    [InlineData(591658710.90913117 / (1 << 20), 20)]
    public void Scale_matching_a_standard_zoom_is_detected(double scale, int zoom)
    {
        Assert.True(TileGrid.TryGetWebMercatorZoom(scale, out int z));
        Assert.Equal(zoom, z);
    }

    [Theory]
    [InlineData(500000.0)]
    [InlineData(591658710.90913117 / 1000)]
    [InlineData(-1.0)]
    public void Scale_not_matching_a_standard_zoom_is_rejected(double scale)
    {
        Assert.False(TileGrid.TryGetWebMercatorZoom(scale, out int z));
        Assert.Equal(-1, z);
    }

    [Fact]
    public void Rounded_scales_need_a_tolerance()
    {
        Assert.False(TileGrid.TryGetWebMercatorZoom(577792.0, out _, dpi: 96, relativeTolerance: 1e-9));
        Assert.True(TileGrid.TryGetWebMercatorZoom(577792.0, out int z, relativeTolerance: 1e-6));
        Assert.Equal(10, z);
    }

    [Fact]
    public void Grid_level_matching_webmercator_is_detected()
    {
        var scales = Enumerable.Range(0, 20).Select(z => TileGrid.ResolutionToScale(TileGrid.WebMercatorResolution(z))).ToArray();
        var grid = TileGrid.FromScales(-E, E, scales);
        Assert.True(grid.TryGetWebMercatorZoom(7, out int zoom));
        Assert.Equal(7, zoom);

        var lowerLeft = TileGrid.FromScales(-E, -E, scales, origin: TileOrigin.LowerLeft);
        Assert.False(lowerLeft.TryGetWebMercatorZoom(7, out _));
        var gk = TileGrid.FromScales(-115309.65, 5400000, scales);
        Assert.False(gk.TryGetWebMercatorZoom(7, out _));
    }

    // EPSG:31256 (MGI / Austria GK East): a gView-like grid with scales and both origin corners.
    private static readonly double[] GkScales = [500000, 250000, 100000, 50000, 10000, 1000];

    [Fact]
    public void Custom_grid_upper_left_origin()
    {
        var grid = TileGrid.FromScales(-115000, 375000, GkScales, dpi: 96);
        double res = 1000 * 0.0254 / 96; // level 5
        Assert.Equal(res, grid.Resolution(5), 12);
        var env = grid.TileEnvelope(5, 2, 3);
        Assert.Equal(-115000 + 3 * 256 * res, env.MinX, 9);
        Assert.Equal(375000 - 3 * 256 * res, env.MinY, 9);
        Assert.Equal(375000 - 2 * 256 * res, env.MaxY, 9);
        Assert.Equal((2L, 3L), grid.TileAt(5, env.Center.X, env.Center.Y));
        Assert.Equal(1000, grid.Scale(5), 9);
    }

    [Fact]
    public void Custom_grid_lower_left_origin()
    {
        var grid = TileGrid.FromScales(-115000, 150000, GkScales, origin: TileOrigin.LowerLeft, tileWidth: 512, tileHeight: 256);
        double res = grid.Resolution(4);
        var env = grid.TileEnvelope(4, 2, 3);
        Assert.Equal(-115000 + 3 * 512 * res, env.MinX, 9);
        Assert.Equal(150000 + 2 * 256 * res, env.MinY, 9);
        Assert.Equal(150000 + 3 * 256 * res, env.MaxY, 9);
        Assert.Equal((2L, 3L), grid.TileAt(4, env.Center.X, env.Center.Y));
        Assert.Equal((2L, 3L, 4L, 5L), grid.TileRange(4, new Envelope2D(env.MinX + 1, env.MinY + 1, env.MaxX + 2 * env.Width - 1, env.MaxY + 2 * env.Height - 1)));
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileGrid(0, 0, [1, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileGrid(0, 0, [1], tileWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TileGrid.WebMercator().TileEnvelope(25, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TileGrid.ScaleToResolution(1000, 0));
    }
}

public class MvtTileBuilderTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private const double E = TileGrid.WebMercatorHalfExtent;

    private static MvtFeature Only(MvtTileBuilder builder) => Assert.Single(Assert.Single(builder.Build().Layers).Features);

    private static List<List<(long X, long Y)>> Rings(MvtFeature f) => MvtGeometryEncodingTests.DecodeRings(f.Geometry);

    [Fact]
    public void Transform_maps_tile_corners_to_0_and_extent_with_y_down()
    {
        var t = new MvtTileTransform(TileGrid.WebMercator(), 1, 0, 0);
        Assert.Equal(new Coordinate(0, 0), t.ToTile(new Coordinate(-E, E)));
        Assert.Equal(new Coordinate(4096, 4096), t.ToTile(new Coordinate(0, 0)));
        Assert.Equal(new Coordinate(-E, E), t.ToWorld(0, 0));
        Assert.Equal(new Envelope2D(-E - 64 * t.UnitWidth, -64 * t.UnitHeight, 64 * t.UnitWidth, E + 64 * t.UnitHeight), t.BufferedEnvelope(64));
    }

    [Fact]
    public void Polygon_larger_than_tile_is_clipped_to_the_buffer_with_mvt_winding()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 1, 0, 0);
        Assert.True(builder.AddFeature("land", Wkt.Read($"POLYGON ((-30000000 -30000000, 30000000 -30000000, 30000000 30000000, -30000000 30000000, -30000000 -30000000))", F)));
        var f = Only(builder);
        Assert.Equal(MvtGeometryType.Polygon, f.GeometryType);
        var ring = Assert.Single(Rings(f));
        Assert.True(MvtGeometryEncodingTests.Area(ring) > 0);
        Assert.Equal(-64, ring.Min(p => p.X));
        Assert.Equal(4160, ring.Max(p => p.X));
        Assert.Equal(-64, ring.Min(p => p.Y));
        Assert.Equal(4160, ring.Max(p => p.Y));
        Assert.Equal(4, ring.Count);
    }

    [Fact]
    public void Polygon_with_hole_gets_exterior_positive_and_interior_negative()
    {
        // In world coordinates (y up) the shell is counter-clockwise; in tile coordinates (y down) it must come out
        // as positive area (clockwise on screen) and the hole as negative.
        var grid = TileGrid.WebMercator();
        var env = grid.TileEnvelope(3, 2, 5);
        double x0 = env.MinX, y0 = env.MinY, w = env.Width;
        var wkt = FormattableString.Invariant(
            $"POLYGON (({x0 + 0.1 * w} {y0 + 0.1 * w}, {x0 + 0.9 * w} {y0 + 0.1 * w}, {x0 + 0.9 * w} {y0 + 0.9 * w}, {x0 + 0.1 * w} {y0 + 0.9 * w}, {x0 + 0.1 * w} {y0 + 0.1 * w}), ") +
            FormattableString.Invariant($"({x0 + 0.4 * w} {y0 + 0.4 * w}, {x0 + 0.4 * w} {y0 + 0.6 * w}, {x0 + 0.6 * w} {y0 + 0.6 * w}, {x0 + 0.6 * w} {y0 + 0.4 * w}, {x0 + 0.4 * w} {y0 + 0.4 * w}))");
        var builder = new MvtTileBuilder(grid, 3, 2, 5);
        builder.AddFeature("l", Wkt.Read(wkt, F), id: 9);
        var f = Only(builder);
        var rings = Rings(f);
        Assert.Equal(2, rings.Count);
        Assert.True(MvtGeometryEncodingTests.Area(rings[0]) > 0);
        Assert.True(MvtGeometryEncodingTests.Area(rings[1]) < 0);
        Assert.Equal(410, rings[0].Min(p => p.X));
        Assert.Equal(3686, rings[0].Max(p => p.Y));
        Assert.Equal(9UL, f.Id);

        // Back to world: the area matches the input within the rounding of one tile unit.
        var world = Assert.IsAssignableFrom<IPolygon2D>(f.ToGeometry(F, builder.Transform));
        Assert.Equal(Wkt.Read(wkt, F).Area(), world.Area(), Wkt.Read(wkt, F).Area() * 1e-3);
    }

    [Fact]
    public void Multipolygon_parts_are_written_shell_then_holes_and_outside_parts_dropped()
    {
        var grid = TileGrid.WebMercator();
        var builder = new MvtTileBuilder(grid, 0, 0, 0, new MvtTileOptions { SimplifyTolerance = 0 });
        double u = 2 * E / 4096; // one tile unit
        string Sq(double x, double y, double s) => FormattableString.Invariant($"({x} {y}, {x + s} {y}, {x + s} {y + s}, {x} {y + s}, {x} {y})");
        var wkt = $"MULTIPOLYGON (({Sq(-E + 100 * u, E - 400 * u, 300 * u)}, {Sq(-E + 200 * u, E - 300 * u, 100 * u)}), ({Sq(0, 0, 500 * u)}), ({Sq(3 * E, 3 * E, 10)}))";
        Assert.True(builder.AddFeature("m", Wkt.Read(wkt, F)));
        var rings = Rings(Only(builder));
        Assert.Equal(3, rings.Count);
        Assert.Equal([true, false, true], rings.Select(r => MvtGeometryEncodingTests.Area(r) > 0));
        Assert.Equal(300L * 300 * 2, MvtGeometryEncodingTests.Area(rings[0]));
        Assert.Equal(-100L * 100 * 2, MvtGeometryEncodingTests.Area(rings[1]));
    }

    [Fact]
    public void Geometries_outside_or_below_one_unit_are_dropped()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 2, 1, 1);
        Assert.False(builder.AddFeature("x", Wkt.Read("POLYGON ((15000000 15000000, 16000000 15000000, 16000000 16000000, 15000000 15000000))", F)));
        Assert.False(builder.AddFeature("x", Wkt.Read("POLYGON ((-5000000 5000000, -4999999 5000000, -4999999 5000001, -5000000 5000000))", F)));
        Assert.False(builder.AddFeature("x", Wkt.Read("LINESTRING (-5000000 5000000, -4999999 5000001)", F)));
        Assert.False(builder.AddFeature("x", Wkt.Read("POINT (19000000 19000000)", F)));
        Assert.True(builder.IsEmpty);
        Assert.Empty(Mvt.Decode(builder.ToBytes()).Layers);
    }

    [Fact]
    public void Line_crossing_the_tile_is_clipped_at_the_buffer()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 1, 0, 0);
        Assert.True(builder.AddFeature("roads", Wkt.Read(FormattableString.Invariant($"LINESTRING (-30000000 {E / 2}, 30000000 {E / 2})"), F)));
        var f = Only(builder);
        Assert.Equal(MvtGeometryType.LineString, f.GeometryType);
        var path = Assert.Single(Rings(f));
        Assert.Equal([(-64L, 2048L), (4160L, 2048L)], path.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void Points_in_the_buffer_are_kept_points_beyond_are_dropped()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 1, 0, 0);
        double u = E / 4096;
        var wkt = FormattableString.Invariant($"MULTIPOINT (({-E - 10 * u} {E}), ({-E - 100 * u} {E}), ({-E / 2} {E / 2}))");
        Assert.True(builder.AddFeature("p", Wkt.Read(wkt, F)));
        Assert.Equal("MULTIPOINT ((-10 0), (2048 2048))", Only(builder).ToGeometry(F).ToWkt());
    }

    [Fact]
    public void Simplification_removes_sub_pixel_detail()
    {
        var grid = TileGrid.WebMercator();
        double u = E / 4096; // zoom 1: one tile unit
        var zigzag = string.Join(", ", Enumerable.Range(0, 200).Select(i => FormattableString.Invariant($"{-E + i * 20 * u} {E / 2 + (i % 2) * 3 * u}")));
        var line = Wkt.Read($"LINESTRING ({zigzag})", F);

        var simplified = new MvtTileBuilder(grid, 1, 0, 0);
        simplified.AddFeature("l", line);
        var unsimplified = new MvtTileBuilder(grid, 1, 0, 0, new MvtTileOptions { SimplifyTolerance = 0 });
        unsimplified.AddFeature("l", line);

        Assert.Equal(2, Rings(Only(simplified))[0].Count);    // 3 units = 0.19 px < 1 px
        Assert.True(Rings(Only(unsimplified))[0].Count > 100);
    }

    [Fact]
    public void Lower_left_and_upper_left_grids_give_the_same_tile_for_the_same_envelope()
    {
        // EPSG:31256 extent; the same tile once addressed from the top, once from the bottom.
        double[] scales = [100000, 10000];
        var ul = TileGrid.FromScales(-115000, 375000, scales);
        var ll = TileGrid.FromScales(-115000, 375000 - 8 * 256 * ul.Resolution(1), scales, origin: TileOrigin.LowerLeft);
        Assert.Equal(ul.TileEnvelope(1, 2, 4), ll.TileEnvelope(1, 5, 4));

        var env = ul.TileEnvelope(1, 2, 4);
        var c = env.Center;
        var geometry = Wkt.Read(FormattableString.Invariant(
            $"POLYGON (({c.X - 100} {c.Y - 50}, {c.X + 100} {c.Y - 50}, {c.X + 100} {c.Y + 300}, {c.X - 100} {c.Y - 50}))"), F);

        var a = new MvtTileBuilder(ul, 1, 2, 4);
        var b = new MvtTileBuilder(ll, 1, 5, 4);
        a.AddFeature("gk", geometry, [new("name", "x")], 1);
        b.AddFeature("gk", geometry, [new("name", "x")], 1);
        Assert.Equal(a.ToBytes(), b.ToBytes());

        // The apex lies north (world y up) and therefore above the centre in tile coordinates (y down).
        var ring = Assert.Single(Rings(Only(a)));
        Assert.True(ring.Min(p => p.Y) < 2048);
        Assert.True(MvtGeometryEncodingTests.Area(ring) > 0);
    }

    [Fact]
    public void ToTileGeometry_returns_the_cleaned_tile_geometry()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 0, 0, 0);
        var g = builder.ToTileGeometry(Wkt.Read("POINT (0 0)", F), F);
        Assert.Equal("POINT (2048 2048)", g!.ToWkt());
        Assert.Null(builder.ToTileGeometry(Wkt.Read("POINT (1e9 0)", F), F));
    }

    [Fact]
    public void Layers_keep_the_order_of_their_first_feature()
    {
        var builder = new MvtTileBuilder(TileGrid.WebMercator(), 0, 0, 0);
        builder.AddFeature("b", Wkt.Read("POINT (0 0)", F));
        builder.AddFeature("a", Wkt.Read("POINT (1 1)", F));
        builder.AddFeature("b", Wkt.Read("POINT (2 2)", F));
        var tile = Mvt.Decode(builder.ToBytes(gzip: true));
        Assert.Equal(["b", "a"], tile.Layers.Select(l => l.Name));
        Assert.Equal(2, tile["b"]!.Features.Count);
    }
}
