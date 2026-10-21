namespace SpatialNET.IO;

/// <summary>Options for writing WKB.</summary>
public sealed record WkbOptions
{
    /// <summary>The default options.</summary>
    public static WkbOptions Default { get; } = new();

    /// <summary>How Z is written; default <see cref="ZMode.Auto"/>.</summary>
    public ZMode ZMode { get; init; } = ZMode.Auto;

    /// <summary>How M is written; default <see cref="MMode.Auto"/>.</summary>
    public MMode MMode { get; init; } = MMode.Auto;
}

/// <summary>
/// Reading and writing WKB (ADR 0016). Reading accepts ISO WKB and EWKB in either byte order and creates the
/// geometries through the geometry factory; writing produces ISO WKB in little endian.
/// </summary>
/// <remarks>
/// Type mapping as for WKT (ADR 0009): Point → <see cref="IPoint2D"/>, a point with NaN coordinates and
/// MultiPoint → <see cref="IMultiPoint2D"/>, (Multi)LineString → <see cref="IPolyline2D"/>,
/// (Multi)Polygon → <see cref="IPolygon2D"/>. An SRID in an EWKB header is read and discarded. Curves and
/// other surface types are not supported.
/// </remarks>
public static class Wkb
{
    /// <summary>Reads a geometry.</summary>
    /// <param name="wkb">The WKB bytes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">The data is not valid WKB; the message names the byte offset.</exception>
    /// <exception cref="NotSupportedException">The data is a GeometryCollection (use <see cref="ReadAll"/> instead), a curve or another unsupported type.</exception>
    /// <exception cref="InvalidOperationException">Neither <paramref name="factory"/> nor <see cref="GeometryFactory.Default"/> is set.</exception>
    public static IGeometry2D Read(ReadOnlySpan<byte> wkb, IGeometryFactory? factory = null) =>
        new WkbReader(wkb.ToArray(), GeometryFactory.Resolve(factory)).ReadDocument(flatten: false)[0];

    /// <summary>Reads a geometry of an expected kind.</summary>
    /// <typeparam name="T">The expected geometry kind, e.g. <see cref="IPolygon2D"/>.</typeparam>
    /// <param name="wkb">The WKB bytes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">The data is not valid WKB or not a geometry of kind <typeparamref name="T"/>.</exception>
    public static T Read<T>(ReadOnlySpan<byte> wkb, IGeometryFactory? factory = null) where T : IGeometry2D =>
        Read(wkb, factory) is T geometry
            ? geometry
            : throw new FormatException($"The WKB does not describe a geometry of kind {typeof(T).Name}.");

    /// <summary>Reads all geometries; a GeometryCollection is flattened.</summary>
    /// <param name="wkb">The WKB bytes.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">The data is not valid WKB.</exception>
    public static IReadOnlyList<IGeometry2D> ReadAll(ReadOnlySpan<byte> wkb, IGeometryFactory? factory = null) =>
        new WkbReader(wkb.ToArray(), GeometryFactory.Resolve(factory)).ReadDocument(flatten: true);

    /// <summary>
    /// Writes the geometry as ISO WKB (little endian): leanest type (Polygon for one part, LineString for one
    /// path; a multipoint stays MultiPoint), rings closed, shells counter-clockwise and holes clockwise,
    /// degenerate rings dropped. No SRID is written.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="WkbOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A coordinate has a non-finite X or Y, or an infinite Z or M.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static byte[] ToWkb(this IGeometry2D geometry, WkbOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return WkbWriter.Write(geometry, options ?? WkbOptions.Default);
    }
}
