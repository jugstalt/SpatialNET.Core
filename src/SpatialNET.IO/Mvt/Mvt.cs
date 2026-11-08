using System.IO.Compression;

namespace SpatialNET.IO;

/// <summary>
/// Encoding and decoding of Mapbox Vector Tiles (MVT 2.1, https://github.com/mapbox/vector-tile-spec) with a hand-written
/// protobuf codec, plus gzip helpers. Geometry in a tile is in integer tile coordinates; <see cref="MvtTileBuilder"/> converts
/// world coordinates of any tile grid.
/// </summary>
/// <remarks>
/// Field order on writing follows the common reference encoders: layer = version, name, features, keys, values, extent;
/// feature = id (if set), tags (if any), type, geometry (if any). Keys and values are deduplicated per layer in order of
/// first use. Reading accepts any field order, packed and unpacked repeated fields, skips unknown fields and decompresses
/// gzip input automatically.
/// </remarks>
public static class Mvt
{
    /// <summary>Encodes the tile into protobuf bytes (not compressed).</summary>
    /// <param name="tile">The tile.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tile"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Two layers have the same name.</exception>
    public static byte[] Encode(MvtTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var w = new ProtoWriter();
        foreach (var layer in tile.Layers)
        {
            if (!names.Add(layer.Name))
            {
                throw new ArgumentException($"The layer name '{layer.Name}' occurs more than once.", nameof(tile));
            }

            w.WriteMessageField(3, EncodeLayer(layer));
        }

        return w.ToArray();
    }

    /// <summary>Decodes a tile; gzip-compressed input is decompressed first.</summary>
    /// <param name="data">The tile bytes.</param>
    /// <exception cref="FormatException">The data is not a valid vector tile; the message names the byte offset.</exception>
    public static MvtTile Decode(ReadOnlySpan<byte> data)
    {
        var bytes = IsGzip(data) ? Gunzip(data) : data.ToArray();
        var tile = new MvtTile();
        var r = new ProtoReader(bytes);
        while (r.HasMore)
        {
            var (field, type) = r.ReadTag();
            if (field == 3)
            {
                r.Expect(type, WireType.LengthDelimited, "Tile.layers");
                tile.Layers.Add(DecodeLayer(r.ReadSubMessage()));
            }
            else
            {
                r.Skip(type);
            }
        }

        return tile;
    }

    /// <summary>Compresses bytes with gzip (as tiles are commonly stored and served with <c>Content-Encoding: gzip</c>).</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="level">The compression level; default <see cref="CompressionLevel.Optimal"/>.</param>
    public static byte[] Gzip(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, level, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Decompresses gzip bytes.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <exception cref="FormatException">The data is not valid gzip.</exception>
    public static byte[] Gunzip(ReadOnlySpan<byte> data)
    {
        try
        {
            using var input = new MemoryStream(data.ToArray());
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new FormatException("The data is not valid gzip.", e);
        }
    }

    /// <summary>Whether the bytes start with the gzip magic number (0x1F 0x8B).</summary>
    /// <param name="data">The bytes.</param>
    public static bool IsGzip(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;

    private static ProtoWriter EncodeLayer(MvtLayer layer)
    {
        var keys = new Dictionary<string, uint>(StringComparer.Ordinal);
        var values = new Dictionary<MvtValue, uint>();
        var features = new List<ProtoWriter>();
        foreach (var feature in layer.Features)
        {
            var f = new ProtoWriter();
            if (feature.Id is { } id)
            {
                f.WriteVarintField(1, id);
            }

            if (feature.Attributes.Count > 0)
            {
                var tags = new List<uint>(feature.Attributes.Count * 2);
                foreach (var (key, value) in feature.Attributes)
                {
                    if (!keys.TryGetValue(key, out uint k))
                    {
                        keys[key] = k = (uint)keys.Count;
                    }

                    if (!values.TryGetValue(value, out uint v))
                    {
                        values[value] = v = (uint)values.Count;
                    }

                    tags.Add(k);
                    tags.Add(v);
                }

                f.WritePackedUInt32Field(2, tags);
            }

            f.WriteVarintField(3, (ulong)feature.GeometryType);
            if (feature.Geometry.Count > 0)
            {
                f.WritePackedUInt32Field(4, feature.Geometry);
            }

            features.Add(f);
        }

        var w = new ProtoWriter();
        w.WriteVarintField(15, layer.Version);
        w.WriteStringField(1, layer.Name);
        foreach (var f in features)
        {
            w.WriteMessageField(2, f);
        }

        foreach (var key in keys.OrderBy(k => k.Value))
        {
            w.WriteStringField(3, key.Key);
        }

        foreach (var value in values.OrderBy(v => v.Value))
        {
            var vw = new ProtoWriter();
            value.Key.WriteTo(vw);
            w.WriteMessageField(4, vw);
        }

        w.WriteVarintField(5, layer.Extent);
        return w;
    }

    private sealed record RawFeature(ulong? Id, MvtGeometryType Type, List<uint> Tags, List<uint> Geometry);

    private static MvtLayer DecodeLayer(ProtoReader r)
    {
        string? name = null;
        uint version = 1, extent = MvtLayer.DefaultExtent;
        var keys = new List<string>();
        var values = new List<MvtValue>();
        var raw = new List<RawFeature>();
        while (r.HasMore)
        {
            var (field, type) = r.ReadTag();
            switch (field)
            {
                case 15:
                    r.Expect(type, WireType.Varint, "Layer.version");
                    version = checked((uint)r.ReadVarint());
                    break;
                case 1:
                    r.Expect(type, WireType.LengthDelimited, "Layer.name");
                    name = r.ReadString();
                    break;
                case 2:
                    r.Expect(type, WireType.LengthDelimited, "Layer.features");
                    raw.Add(DecodeFeature(r.ReadSubMessage()));
                    break;
                case 3:
                    r.Expect(type, WireType.LengthDelimited, "Layer.keys");
                    keys.Add(r.ReadString());
                    break;
                case 4:
                    r.Expect(type, WireType.LengthDelimited, "Layer.values");
                    values.Add(DecodeValue(r.ReadSubMessage()));
                    break;
                case 5:
                    r.Expect(type, WireType.Varint, "Layer.extent");
                    extent = checked((uint)r.ReadVarint());
                    break;
                default:
                    r.Skip(type);
                    break;
            }
        }

        var layer = new MvtLayer(name ?? throw new FormatException("A layer has no name."))
        {
            Version = version,
            Extent = extent,
        };

        foreach (var f in raw)
        {
            if (f.Tags.Count % 2 != 0)
            {
                throw new FormatException($"A feature in layer '{layer.Name}' has an odd number of tags.");
            }

            var attributes = new KeyValuePair<string, MvtValue>[f.Tags.Count / 2];
            for (int i = 0; i < attributes.Length; i++)
            {
                uint k = f.Tags[2 * i], v = f.Tags[2 * i + 1];
                if (k >= keys.Count || v >= values.Count)
                {
                    throw new FormatException($"A feature in layer '{layer.Name}' refers to a missing key or value.");
                }

                attributes[i] = new(keys[(int)k], values[(int)v]);
            }

            layer.Features.Add(new MvtFeature
            {
                Id = f.Id,
                GeometryType = f.Type,
                Geometry = f.Geometry.ToArray(),
                Attributes = attributes,
            });
        }

        return layer;
    }

    private static RawFeature DecodeFeature(ProtoReader r)
    {
        ulong? id = null;
        var geometryType = MvtGeometryType.Unknown;
        var tags = new List<uint>();
        var geometry = new List<uint>();
        while (r.HasMore)
        {
            var (field, type) = r.ReadTag();
            switch (field)
            {
                case 1:
                    r.Expect(type, WireType.Varint, "Feature.id");
                    id = r.ReadVarint();
                    break;
                case 2:
                    r.ReadUInt32s(type, tags);
                    break;
                case 3:
                    r.Expect(type, WireType.Varint, "Feature.type");
                    ulong t = r.ReadVarint();
                    geometryType = t <= 3 ? (MvtGeometryType)t : MvtGeometryType.Unknown;
                    break;
                case 4:
                    r.ReadUInt32s(type, geometry);
                    break;
                default:
                    r.Skip(type);
                    break;
            }
        }

        return new RawFeature(id, geometryType, tags, geometry);
    }

    private static MvtValue DecodeValue(ProtoReader r)
    {
        MvtValue? result = null;
        while (r.HasMore)
        {
            var (field, type) = r.ReadTag();
            switch (field)
            {
                case 1:
                    r.Expect(type, WireType.LengthDelimited, "Value.string_value");
                    result = MvtValue.String(r.ReadString());
                    break;
                case 2:
                    r.Expect(type, WireType.Fixed32, "Value.float_value");
                    result = MvtValue.Float(r.ReadFixed32());
                    break;
                case 3:
                    r.Expect(type, WireType.Fixed64, "Value.double_value");
                    result = MvtValue.Double(r.ReadFixed64());
                    break;
                case 4:
                    r.Expect(type, WireType.Varint, "Value.int_value");
                    result = MvtValue.Int(unchecked((long)r.ReadVarint()));
                    break;
                case 5:
                    r.Expect(type, WireType.Varint, "Value.uint_value");
                    result = MvtValue.UInt(r.ReadVarint());
                    break;
                case 6:
                    r.Expect(type, WireType.Varint, "Value.sint_value");
                    result = MvtValue.SInt(ProtoReader.UnZigZag64(r.ReadVarint()));
                    break;
                case 7:
                    r.Expect(type, WireType.Varint, "Value.bool_value");
                    result = MvtValue.Bool(r.ReadVarint() != 0);
                    break;
                default:
                    r.Skip(type);
                    break;
            }
        }

        return result ?? throw new FormatException("A value message has no value.");
    }
}
