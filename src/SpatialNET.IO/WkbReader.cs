using System.Buffers.Binary;

namespace SpatialNET.IO;

/// <summary>Parser for ISO WKB and EWKB; creates geometries through the factory (ADR 0016).</summary>
internal sealed class WkbReader(byte[] data, IGeometryFactory factory)
{
    private readonly record struct Header(bool LittleEndian, int Type, bool HasZ, bool HasM)
    {
        public int Dimensions => 2 + (HasZ ? 1 : 0) + (HasM ? 1 : 0);
    }

    private int _pos;

    /// <summary>Reads exactly one geometry; a GeometryCollection is flattened if <paramref name="flatten"/> is set.</summary>
    public List<IGeometry2D> ReadDocument(bool flatten)
    {
        var result = new List<IGeometry2D>();
        ReadGeometry(result, flatten);
        if (_pos < data.Length)
        {
            throw Error("Unexpected bytes after the end of the geometry");
        }

        return result;
    }

    private void ReadGeometry(List<IGeometry2D> result, bool flatten)
    {
        int start = _pos;
        var h = ReadHeader();

        switch (h.Type)
        {
            case 1:
                var p = ReadCoordinate(h);
                // The WKB form of POINT EMPTY (as in WKT, ADR 0009): a multipoint without points.
                result.Add(IsEmptyPoint(p) ? factory.CreateMultiPoint([], null) : factory.CreatePoint(p, null));
                return;

            case 2:
                var path = ReadSequence(h);
                result.Add(factory.CreatePolyline(path.Length == 0 ? [] : [path], null));
                return;

            case 3:
                var part = ReadPolygonBody(h);
                result.Add(factory.CreatePolygon(part is null ? [] : [part.Value], null));
                return;

            case 4:
                var points = new List<Coordinate>();
                for (uint i = ReadCount(h, 5 + 16); i > 0; i--)
                {
                    var ph = ReadHeader();
                    ExpectType(ph, 1, "MultiPoint");
                    var c = ReadCoordinate(ph);
                    if (!IsEmptyPoint(c))
                    {
                        points.Add(c);
                    }
                }

                result.Add(factory.CreateMultiPoint(points.ToArray(), null));
                return;

            case 5:
                var paths = new List<ReadOnlyMemory<Coordinate>>();
                for (uint i = ReadCount(h, 9); i > 0; i--)
                {
                    var lh = ReadHeader();
                    ExpectType(lh, 2, "MultiLineString");
                    var line = ReadSequence(lh);
                    if (line.Length > 0)
                    {
                        paths.Add(line);
                    }
                }

                result.Add(factory.CreatePolyline(paths.ToArray(), null));
                return;

            case 6:
                var parts = new List<PolygonPart>();
                for (uint i = ReadCount(h, 9); i > 0; i--)
                {
                    var gh = ReadHeader();
                    ExpectType(gh, 3, "MultiPolygon");
                    if (ReadPolygonBody(gh) is { } body)
                    {
                        parts.Add(body);
                    }
                }

                result.Add(factory.CreatePolygon(parts.ToArray(), null));
                return;

            case 7:
                if (!flatten)
                {
                    _pos = start;
                    throw new NotSupportedException(
                        "GeometryCollection is not supported as a single geometry (ADR 0016); Wkb.ReadAll reads it as a list.");
                }

                for (uint i = ReadCount(h, 9); i > 0; i--)
                {
                    ReadGeometry(result, flatten);
                }

                return;

            case >= 8 and <= 17:
                _pos = start;
                throw new NotSupportedException(
                    $"WKB geometry type {h.Type} ({TypeName(h.Type)}) is not supported (ADR 0016); curves and other surface types must be linearized by the host.");

            default:
                _pos = start;
                throw Error($"Unknown WKB geometry type {h.Type}");
        }
    }

    private Header ReadHeader()
    {
        int start = _pos;
        byte order = ReadByte();
        if (order > 1)
        {
            _pos = start;
            throw Error($"Invalid byte order {order}");
        }

        bool littleEndian = order == 1;
        uint raw = ReadUInt32(littleEndian);
        // EWKB: Z, M and SRID are flag bits. ISO: the dimension is part of the code (1000 = Z, 2000 = M,
        // 3000 = ZM). Both may occur together, for example ISO codes with the SRID flag (as NTS writes them).
        bool hasZ = (raw & 0x80000000) != 0;
        bool hasM = (raw & 0x40000000) != 0;
        bool hasSrid = (raw & 0x20000000) != 0;
        uint type = raw & 0x0FFFFFFF;
        uint dimension = type / 1000;
        if (dimension > 3)
        {
            _pos = start;
            throw Error($"Unknown WKB geometry type {type}");
        }

        hasZ |= dimension is 1 or 3;
        hasM |= dimension is 2 or 3;
        type %= 1000;

        if (hasSrid)
        {
            ReadUInt32(littleEndian); // read and discarded (ADR 0016)
        }

        return new Header(littleEndian, (int)Math.Min(type, int.MaxValue), hasZ, hasM);
    }

    private PolygonPart? ReadPolygonBody(Header h)
    {
        var rings = new List<Coordinate[]>();
        for (uint i = ReadCount(h, 4); i > 0; i--)
        {
            var ring = ReadSequence(h);
            if (ring.Length > 0)
            {
                rings.Add(ring);
            }
        }

        return rings.Count == 0
            ? null
            : new PolygonPart(rings[0], rings.Skip(1).Select(r => (ReadOnlyMemory<Coordinate>)r).ToArray());
    }

    private Coordinate[] ReadSequence(Header h)
    {
        uint count = ReadCount(h, h.Dimensions * 8);
        var result = new Coordinate[count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ReadCoordinate(h);
        }

        return result;
    }

    private Coordinate ReadCoordinate(Header h)
    {
        double x = ReadDouble(h.LittleEndian);
        double y = ReadDouble(h.LittleEndian);
        double z = h.HasZ ? ReadDouble(h.LittleEndian) : double.NaN;
        double m = h.HasM ? ReadDouble(h.LittleEndian) : double.NaN;
        return new Coordinate(x, y, z, m);
    }

    private static bool IsEmptyPoint(Coordinate c) => double.IsNaN(c.X) && double.IsNaN(c.Y);

    /// <summary>
    /// Reads an element count and rejects one that cannot fit into the remaining bytes, before anything is
    /// allocated (a forged header must not cause an out-of-memory error).
    /// </summary>
    private uint ReadCount(Header h, int minimumBytesPerElement)
    {
        int start = _pos;
        uint count = ReadUInt32(h.LittleEndian);
        if (count > (uint)((data.Length - _pos) / minimumBytesPerElement))
        {
            _pos = start;
            throw Error($"Element count {count} does not fit into the remaining data");
        }

        return count;
    }

    private void ExpectType(Header h, int expected, string container)
    {
        if (h.Type != expected)
        {
            throw Error($"A {container} may only contain geometries of type {expected}, found {h.Type}");
        }
    }

    private static string TypeName(int type) => type switch
    {
        8 => "CircularString",
        9 => "CompoundCurve",
        10 => "CurvePolygon",
        11 => "MultiCurve",
        12 => "MultiSurface",
        13 => "Curve",
        14 => "Surface",
        15 => "PolyhedralSurface",
        16 => "TIN",
        _ => "Triangle",
    };

    private byte ReadByte()
    {
        Need(1);
        return data[_pos++];
    }

    private uint ReadUInt32(bool littleEndian)
    {
        Need(4);
        var span = data.AsSpan(_pos, 4);
        _pos += 4;
        return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    private double ReadDouble(bool littleEndian)
    {
        Need(8);
        var span = data.AsSpan(_pos, 8);
        _pos += 8;
        return littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(span) : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    private void Need(int bytes)
    {
        if (data.Length - _pos < bytes)
        {
            throw Error("Unexpected end of data");
        }
    }

    private FormatException Error(string message) => new($"{message} at byte {_pos}.");
}
