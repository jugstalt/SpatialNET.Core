namespace SpatialNET.IO;

/// <summary>How Z is handled when writing (ADR 0009).</summary>
public enum ZMode
{
    /// <summary>Z is written if at least one point carries a height other than 0.</summary>
    /// <remarks>Hosts such as gView and WebGIS always deliver points with Z = 0; those stay 2D.</remarks>
    Auto,

    /// <summary>Z is always written; points without height get 0.</summary>
    Always,

    /// <summary>Z is never written.</summary>
    Never,
}

/// <summary>How M is handled when writing (ADR 0015).</summary>
public enum MMode
{
    /// <summary>M is written if at least one point carries a measure; points without M get <c>NaN</c>.</summary>
    Auto,

    /// <summary>M is never written.</summary>
    Never,
}

/// <summary>Options for writing WKT.</summary>
public sealed record WktOptions
{
    /// <summary>The default options.</summary>
    public static WktOptions Default { get; } = new();

    /// <summary>How Z is written; default <see cref="ZMode.Auto"/>.</summary>
    public ZMode ZMode { get; init; } = ZMode.Auto;

    /// <summary>How M is written; default <see cref="MMode.Auto"/>.</summary>
    public MMode MMode { get; init; } = MMode.Auto;
}

/// <summary>
/// Reading and writing WKT (ADR 0009). Reading goes through the geometry factory; geometrically invalid
/// but syntactically correct input is accepted and passed to the factory unchanged.
/// </summary>
/// <remarks>
/// Type mapping: POINT → <see cref="IPoint2D"/>, POINT EMPTY and MULTIPOINT → <see cref="IMultiPoint2D"/>,
/// (MULTI)LINESTRING → <see cref="IPolyline2D"/>, (MULTI)POLYGON → <see cref="IPolygon2D"/>.
/// M is read and written (<c>POINT M</c>, <c>POINT ZM</c>); numbers are invariant and may also be <c>NaN</c>/<c>Infinity</c>.
/// </remarks>
public static class Wkt
{
    /// <summary>Reads a geometry.</summary>
    /// <param name="wkt">The WKT text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wkt"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">The text is not valid WKT; the message names the position.</exception>
    /// <exception cref="NotSupportedException">The text is a GEOMETRYCOLLECTION (use <see cref="ReadAll"/> instead).</exception>
    /// <exception cref="InvalidOperationException">Neither <paramref name="factory"/> nor <see cref="GeometryFactory.Default"/> is set.</exception>
    public static IGeometry2D Read(string wkt, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return new WktReader(wkt, GeometryFactory.Resolve(factory)).ReadDocument(flatten: false)[0];
    }

    /// <summary>Reads a geometry of an expected kind.</summary>
    /// <typeparam name="T">The expected geometry kind, e.g. <see cref="IPolygon2D"/>.</typeparam>
    /// <param name="wkt">The WKT text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">The text is not valid WKT or not a geometry of kind <typeparamref name="T"/>.</exception>
    public static T Read<T>(string wkt, IGeometryFactory? factory = null) where T : IGeometry2D =>
        Read(wkt, factory) is T geometry
            ? geometry
            : throw new FormatException($"The WKT does not describe a geometry of kind {typeof(T).Name}.");

    /// <summary>Reads all geometries; a GEOMETRYCOLLECTION is flattened.</summary>
    /// <param name="wkt">The WKT text.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wkt"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">The text is not valid WKT.</exception>
    public static IReadOnlyList<IGeometry2D> ReadAll(string wkt, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return new WktReader(wkt, GeometryFactory.Resolve(factory)).ReadDocument(flatten: true);
    }

    /// <summary>
    /// Writes the geometry as WKT: leanest type (POLYGON for one part, LINESTRING for one path;
    /// a multipoint stays MULTIPOINT), rings closed, shells counter-clockwise and holes clockwise,
    /// degenerate rings dropped.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="WktOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static string ToWkt(this IGeometry2D geometry, WktOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return WktWriter.Write(geometry, options ?? WktOptions.Default);
    }
}
