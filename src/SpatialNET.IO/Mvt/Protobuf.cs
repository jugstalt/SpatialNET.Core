using System.Buffers.Binary;
using System.Text;

namespace SpatialNET.IO;

/// <summary>Protobuf wire types (only those the MVT schema and unknown-field skipping need).</summary>
internal enum WireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5,
}

/// <summary>A minimal hand-written protobuf writer: varint, zigzag, fixed32/64 and length-delimited fields.</summary>
internal sealed class ProtoWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public byte[] ToArray() => _bytes.ToArray();

    public void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        _bytes.Add((byte)value);
    }

    public void WriteTag(int field, WireType type) => WriteVarint(((ulong)field << 3) | (uint)type);

    public void WriteVarintField(int field, ulong value)
    {
        WriteTag(field, WireType.Varint);
        WriteVarint(value);
    }

    public void WriteBytesField(int field, ReadOnlySpan<byte> bytes)
    {
        WriteTag(field, WireType.LengthDelimited);
        WriteVarint((ulong)bytes.Length);
        foreach (var b in bytes)
        {
            _bytes.Add(b);
        }
    }

    public void WriteStringField(int field, string value) => WriteBytesField(field, Encoding.UTF8.GetBytes(value));

    public void WriteMessageField(int field, ProtoWriter message)
    {
        WriteTag(field, WireType.LengthDelimited);
        WriteVarint((ulong)message._bytes.Count);
        _bytes.AddRange(message._bytes);
    }

    public void WritePackedUInt32Field(int field, IReadOnlyList<uint> values)
    {
        var packed = new ProtoWriter();
        foreach (var v in values)
        {
            packed.WriteVarint(v);
        }

        WriteMessageField(field, packed);
    }

    public void WriteFixed32Field(int field, float value)
    {
        WriteTag(field, WireType.Fixed32);
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(span, value);
        _bytes.AddRange(span.ToArray());
    }

    public void WriteFixed64Field(int field, double value)
    {
        WriteTag(field, WireType.Fixed64);
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        _bytes.AddRange(span.ToArray());
    }

    public static uint ZigZag32(int value) => (uint)((value << 1) ^ (value >> 31));

    public static ulong ZigZag64(long value) => (ulong)((value << 1) ^ (value >> 63));
}

/// <summary>A minimal protobuf reader over a byte array; errors throw <see cref="FormatException"/> naming the offset.</summary>
internal sealed class ProtoReader(byte[] data, int start, int end)
{
    private int _pos = start;

    public ProtoReader(byte[] data) : this(data, 0, data.Length)
    {
    }

    public bool HasMore => _pos < end;

    public ulong ReadVarint()
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (_pos >= end)
            {
                throw Error("Truncated varint");
            }

            byte b = data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw Error("Varint longer than 10 bytes");
    }

    public (int Field, WireType Type) ReadTag()
    {
        ulong tag = ReadVarint();
        int field = (int)(tag >> 3);
        if (field == 0)
        {
            throw Error("Field number 0");
        }

        return (field, (WireType)(tag & 7));
    }

    public ProtoReader ReadSubMessage()
    {
        int length = ReadLength();
        var sub = new ProtoReader(data, _pos, _pos + length);
        _pos += length;
        return sub;
    }

    public string ReadString()
    {
        int length = ReadLength();
        try
        {
            return new UTF8Encoding(false, true).GetString(data, _pos, length);
        }
        catch (DecoderFallbackException e)
        {
            throw new FormatException($"Invalid UTF-8 string at byte offset {_pos}.", e);
        }
        finally
        {
            _pos += length;
        }
    }

    public float ReadFixed32()
    {
        Need(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(_pos, 4));
        _pos += 4;
        return value;
    }

    public double ReadFixed64()
    {
        Need(8);
        var value = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(_pos, 8));
        _pos += 8;
        return value;
    }

    /// <summary>Reads a repeated uint32 field in packed or unpacked encoding into <paramref name="target"/>.</summary>
    public void ReadUInt32s(WireType type, List<uint> target)
    {
        if (type == WireType.LengthDelimited)
        {
            var packed = ReadSubMessage();
            while (packed.HasMore)
            {
                target.Add(checked((uint)packed.ReadVarint()));
            }
        }
        else if (type == WireType.Varint)
        {
            target.Add(checked((uint)ReadVarint()));
        }
        else
        {
            throw Error($"Unexpected wire type {type} for a repeated uint32");
        }
    }

    public void Expect(WireType actual, WireType expected, string what)
    {
        if (actual != expected)
        {
            throw Error($"Unexpected wire type {actual} for {what}");
        }
    }

    public void Skip(WireType type)
    {
        switch (type)
        {
            case WireType.Varint: ReadVarint(); break;
            case WireType.Fixed64: Need(8); _pos += 8; break;
            case WireType.Fixed32: Need(4); _pos += 4; break;
            case WireType.LengthDelimited: _pos += ReadLength(); break;
            default: throw Error($"Unsupported wire type {(int)type}");
        }
    }

    public static int UnZigZag32(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    public static long UnZigZag64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private int ReadLength()
    {
        ulong length = ReadVarint();
        if (length > (ulong)(end - _pos))
        {
            throw Error("Length-delimited field exceeds the message");
        }

        return (int)length;
    }

    private void Need(int count)
    {
        if (end - _pos < count)
        {
            throw Error("Truncated fixed-size field");
        }
    }

    private FormatException Error(string message) => new($"{message} at byte offset {_pos}.");
}
