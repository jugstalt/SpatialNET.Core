using System.Globalization;

namespace SpatialNET.IO;

/// <summary>The geometry type of a vector tile feature (MVT 2.1, <c>GeomType</c>).</summary>
public enum MvtGeometryType
{
    /// <summary>Unknown geometry type; the geometry is not interpreted.</summary>
    Unknown = 0,

    /// <summary>Point or multipoint.</summary>
    Point = 1,

    /// <summary>Linestring or multilinestring.</summary>
    LineString = 2,

    /// <summary>Polygon or multipolygon.</summary>
    Polygon = 3,
}

/// <summary>The protobuf field a <see cref="MvtValue"/> is stored in (MVT 2.1, <c>Value</c> message).</summary>
public enum MvtValueKind
{
    /// <summary><c>string_value</c> (field 1).</summary>
    String = 1,

    /// <summary><c>float_value</c> (field 2).</summary>
    Float = 2,

    /// <summary><c>double_value</c> (field 3).</summary>
    Double = 3,

    /// <summary><c>int_value</c> (field 4, int64).</summary>
    Int = 4,

    /// <summary><c>uint_value</c> (field 5, uint64).</summary>
    UInt = 5,

    /// <summary><c>sint_value</c> (field 6, zigzag sint64).</summary>
    SInt = 6,

    /// <summary><c>bool_value</c> (field 7).</summary>
    Bool = 7,
}

/// <summary>
/// An attribute value of a vector tile feature. The <see cref="Kind"/> fixes the protobuf field, so that a decoded value is
/// encoded again into the same bytes. Values of the same kind and value are deduplicated in the layer's value table.
/// </summary>
public readonly record struct MvtValue
{
    private MvtValue(MvtValueKind kind, string? text, double number, long integer, ulong unsigned, bool flag)
    {
        Kind = kind;
        _text = text;
        _number = number;
        _integer = integer;
        _unsigned = unsigned;
        _flag = flag;
    }

    private readonly string? _text;
    private readonly double _number;
    private readonly long _integer;
    private readonly ulong _unsigned;
    private readonly bool _flag;

    /// <summary>The protobuf field the value is stored in.</summary>
    public MvtValueKind Kind { get; }

    /// <summary>
    /// The value as a CLR object: <see cref="string"/>, <see cref="float"/>, <see cref="double"/>, <see cref="long"/> (Int and SInt),
    /// <see cref="ulong"/> (UInt) or <see cref="bool"/>.
    /// </summary>
    public object Value => Kind switch
    {
        MvtValueKind.String => _text!,
        MvtValueKind.Float => (float)_number,
        MvtValueKind.Double => _number,
        MvtValueKind.Int or MvtValueKind.SInt => _integer,
        MvtValueKind.UInt => _unsigned,
        _ => _flag,
    };

    /// <summary>Creates a <c>string_value</c>.</summary>
    /// <param name="value">The string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public static MvtValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(MvtValueKind.String, value, 0, 0, 0, false);
    }

    /// <summary>Creates a <c>float_value</c>.</summary>
    /// <param name="value">The value.</param>
    public static MvtValue Float(float value) => new(MvtValueKind.Float, null, value, 0, 0, false);

    /// <summary>Creates a <c>double_value</c>.</summary>
    /// <param name="value">The value.</param>
    public static MvtValue Double(double value) => new(MvtValueKind.Double, null, value, 0, 0, false);

    /// <summary>Creates an <c>int_value</c> (int64, negative values take ten bytes).</summary>
    /// <param name="value">The value.</param>
    public static MvtValue Int(long value) => new(MvtValueKind.Int, null, 0, value, 0, false);

    /// <summary>Creates a <c>uint_value</c>.</summary>
    /// <param name="value">The value.</param>
    public static MvtValue UInt(ulong value) => new(MvtValueKind.UInt, null, 0, 0, value, false);

    /// <summary>Creates a <c>sint_value</c> (zigzag encoded).</summary>
    /// <param name="value">The value.</param>
    public static MvtValue SInt(long value) => new(MvtValueKind.SInt, null, 0, value, 0, false);

    /// <summary>Creates a <c>bool_value</c>.</summary>
    /// <param name="value">The value.</param>
    public static MvtValue Bool(bool value) => new(MvtValueKind.Bool, null, 0, 0, 0, value);

    /// <summary>
    /// Converts a CLR value: <see cref="string"/> → String, <see cref="bool"/> → Bool, <see cref="float"/> → Float,
    /// <see cref="double"/> and <see cref="decimal"/> → Double, unsigned integers → UInt, signed integers → UInt when ≥ 0 and
    /// SInt when negative, <see cref="MvtValue"/> unchanged; anything else (dates, GUIDs, …) → String in the invariant culture.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>; MVT has no null value.</exception>
    public static MvtValue From(object value) => value switch
    {
        null => throw new ArgumentNullException(nameof(value)),
        MvtValue v => v,
        string s => String(s),
        bool b => Bool(b),
        float f => Float(f),
        double d => Double(d),
        decimal m => Double((double)m),
        byte or ushort or uint or ulong => UInt(Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
        sbyte or short or int or long => Convert.ToInt64(value, CultureInfo.InvariantCulture) is var l && l < 0 ? SInt(l) : UInt((ulong)l),
        IFormattable formattable => String(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => String(value.ToString() ?? string.Empty),
    };

    /// <summary>The value in the invariant culture.</summary>
    public override string ToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;

    internal void WriteTo(ProtoWriter w)
    {
        switch (Kind)
        {
            case MvtValueKind.String: w.WriteStringField(1, _text!); break;
            case MvtValueKind.Float: w.WriteFixed32Field(2, (float)_number); break;
            case MvtValueKind.Double: w.WriteFixed64Field(3, _number); break;
            case MvtValueKind.Int: w.WriteVarintField(4, unchecked((ulong)_integer)); break;
            case MvtValueKind.UInt: w.WriteVarintField(5, _unsigned); break;
            case MvtValueKind.SInt: w.WriteVarintField(6, ProtoWriter.ZigZag64(_integer)); break;
            default: w.WriteVarintField(7, _flag ? 1UL : 0UL); break;
        }
    }
}

/// <summary>
/// A feature of a vector tile layer: optional id, geometry type, the geometry as MVT command integers in tile coordinates and
/// the attributes. Build it from a tile-coordinate geometry with <see cref="Create"/> or let <see cref="MvtTileBuilder"/> do the
/// transform from world coordinates.
/// </summary>
public sealed class MvtFeature
{
    /// <summary>The feature id (<c>id</c>, field 1); <c>null</c> = not written. Hosts typically use the object id.</summary>
    public ulong? Id { get; init; }

    /// <summary>The geometry type.</summary>
    public MvtGeometryType GeometryType { get; init; }

    /// <summary>The geometry as command integers (MoveTo/LineTo/ClosePath with zigzag-encoded deltas), see <see cref="MvtGeometry"/>.</summary>
    public IReadOnlyList<uint> Geometry { get; init; } = [];

    /// <summary>The attributes in order; keys and values are deduplicated per layer on encoding.</summary>
    public IReadOnlyList<KeyValuePair<string, MvtValue>> Attributes { get; init; } = [];

    /// <summary>
    /// Creates a feature from a geometry that is already in tile coordinates (y down, 0..extent): coordinates are rounded to
    /// integers, repeated points and degenerate parts dropped and polygon rings oriented as MVT requires.
    /// </summary>
    /// <param name="tileGeometry">The geometry in tile coordinates.</param>
    /// <param name="attributes">The attributes; <c>null</c> values are skipped (MVT has no null), a repeated key keeps its first value.</param>
    /// <param name="id">The feature id or <c>null</c>.</param>
    /// <returns>The feature, or <c>null</c> if nothing is left of the geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tileGeometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not finite or outside the 32-bit range.</exception>
    public static MvtFeature? Create(IGeometry2D tileGeometry, IEnumerable<KeyValuePair<string, object?>>? attributes = null, ulong? id = null)
    {
        ArgumentNullException.ThrowIfNull(tileGeometry);
        var shape = TileShape.FromTileGeometry(tileGeometry, c => c);
        return shape is null ? null : FromShape(shape, attributes, id);
    }

    internal static MvtFeature FromShape(TileShape shape, IEnumerable<KeyValuePair<string, object?>>? attributes, ulong? id) => new()
    {
        Id = id,
        GeometryType = shape.Type,
        Geometry = MvtGeometry.Encode(shape),
        Attributes = ConvertAttributes(attributes),
    };

    /// <summary>
    /// Decodes the geometry: points → <see cref="IPoint2D"/> (one) or <see cref="IMultiPoint2D"/>, lines → <see cref="IPolyline2D"/>,
    /// polygons → <see cref="IPolygon2D"/> (exterior rings by positive area in tile coordinates, interior rings by negative area).
    /// </summary>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="transform">If given, coordinates are converted from tile to world coordinates; otherwise they stay tile coordinates.</param>
    /// <exception cref="FormatException">The command integers are invalid or the geometry type is <see cref="MvtGeometryType.Unknown"/>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IGeometry2D ToGeometry(IGeometryFactory? factory = null, MvtTileTransform? transform = null) =>
        MvtGeometry.Decode(GeometryType, Geometry, factory, transform);

    private static KeyValuePair<string, MvtValue>[] ConvertAttributes(IEnumerable<KeyValuePair<string, object?>>? attributes)
    {
        if (attributes is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<KeyValuePair<string, MvtValue>>();
        foreach (var (key, value) in attributes)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(attributes));
            if (value is not null && seen.Add(key))
            {
                result.Add(new(key, MvtValue.From(value)));
            }
        }

        return result.ToArray();
    }
}

/// <summary>A layer of a vector tile: name, version, extent and features.</summary>
/// <param name="name">The layer name, unique within the tile.</param>
public sealed class MvtLayer(string name)
{
    /// <summary>The default extent: 4096 units per tile edge.</summary>
    public const uint DefaultExtent = 4096;

    /// <summary>The layer name.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The spec version; default 2.</summary>
    public uint Version { get; init; } = 2;

    /// <summary>The number of tile units per tile edge; default <see cref="DefaultExtent"/>.</summary>
    public uint Extent { get; init; } = DefaultExtent;

    /// <summary>The features.</summary>
    public IList<MvtFeature> Features { get; } = new List<MvtFeature>();
}

/// <summary>A vector tile: a list of layers (MVT 2.1).</summary>
public sealed class MvtTile
{
    /// <summary>The layers; names must be unique.</summary>
    public IList<MvtLayer> Layers { get; } = new List<MvtLayer>();

    /// <summary>The layer with the given name or <c>null</c>.</summary>
    /// <param name="name">The layer name.</param>
    public MvtLayer? this[string name] => Layers.FirstOrDefault(l => l.Name == name);
}
