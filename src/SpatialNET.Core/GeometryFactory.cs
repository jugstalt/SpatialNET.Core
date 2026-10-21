namespace SpatialNET;

/// <summary>
/// A polygon part: a shell together with the holes that lie directly inside it (ADR 0003).
/// </summary>
/// <param name="Shell">The shell; closed, counter-clockwise.</param>
/// <param name="Holes">The holes; closed, clockwise.</param>
public readonly record struct PolygonPart(
    ReadOnlyMemory<Coordinate> Shell,
    ReadOnlyMemory<ReadOnlyMemory<Coordinate>> Holes = default);

/// <summary>
/// The geometry factory: creates the result geometries of an algorithm from coordinates
/// as host geometries (ADR 0004).
/// </summary>
/// <remarks>
/// <para>Contract for implementations:</para>
/// <list type="bullet">
/// <item>Thread-safe or stateless – <see cref="GeometryFactory.Default"/> is used by all threads.</item>
/// <item>Buffers that are passed in are copied and not referenced after the call.</item>
/// <item>Empty input (0 points, paths or parts) produces empty geometries, never <c>null</c>.</item>
/// <item>Z may be dropped; if <see cref="SupportsZ"/> is <c>false</c>, algorithms produce no Z at all.</item>
/// </list>
/// <para>The <c>source</c> parameter is the algorithm's primary input geometry (or <c>null</c>);
/// the host can take metadata such as the spatial reference from it.</para>
/// </remarks>
public interface IGeometryFactory
{
    /// <summary>Whether the created geometries are meant to carry Z.</summary>
    bool SupportsZ { get; }

    /// <summary>Creates a point.</summary>
    /// <param name="coordinate">The coordinate.</param>
    /// <param name="source">The primary input geometry or <c>null</c>.</param>
    IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source);

    /// <summary>Creates a multipoint; an empty one if there are no coordinates.</summary>
    /// <param name="coordinates">The points.</param>
    /// <param name="source">The primary input geometry or <c>null</c>.</param>
    IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source);

    /// <summary>Creates a polyline; an empty one if there are no paths.</summary>
    /// <param name="paths">The paths as coordinate sequences.</param>
    /// <param name="source">The primary input geometry or <c>null</c>.</param>
    IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source);

    /// <summary>Creates a polygon from parts; an empty one if there are no parts.</summary>
    /// <param name="parts">The parts (valid, closed, shell counter-clockwise).</param>
    /// <param name="source">The primary input geometry or <c>null</c>.</param>
    IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source);
}

/// <summary>Global access to the default <see cref="IGeometryFactory"/> (ADR 0004).</summary>
public static class GeometryFactory
{
    /// <summary>
    /// The factory that algorithms use when their <c>factory</c> parameter is <c>null</c>.
    /// Initially <c>null</c>; hosts, the reference implementation and the playground set it at startup.
    /// </summary>
    public static IGeometryFactory? Default { get; set; }

    /// <summary>
    /// Returns <paramref name="factory"/> or, if <c>null</c>, <see cref="Default"/>.
    /// </summary>
    /// <param name="factory">The explicitly passed factory or <c>null</c>.</param>
    /// <exception cref="InvalidOperationException">Neither <paramref name="factory"/> nor <see cref="Default"/> is set.</exception>
    public static IGeometryFactory Resolve(IGeometryFactory? factory) =>
        factory ?? Default ?? throw new InvalidOperationException(
            "No geometry factory: neither was a factory passed to the algorithm, " +
            "nor is SpatialNET.GeometryFactory.Default set.");
}
