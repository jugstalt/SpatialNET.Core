namespace SpatialNET.Imp;

/// <summary>An immutable point without height.</summary>
/// <param name="x">The X coordinate.</param>
/// <param name="y">The Y coordinate.</param>
internal class Point(double x, double y) : IPoint2D
{
    /// <inheritdoc />
    public double X { get; } = x;

    /// <inheritdoc />
    public double Y { get; } = y;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y})";
}

/// <summary>An immutable point with height.</summary>
/// <param name="x">The X coordinate.</param>
/// <param name="y">The Y coordinate.</param>
/// <param name="z">The height.</param>
internal sealed class Point3D(double x, double y, double z) : Point(x, y), IPoint3D
{
    /// <inheritdoc />
    public double Z { get; } = z;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y} {Z})";
}

/// <summary>An immutable point with a measure (M), without height (ADR 0015).</summary>
/// <param name="x">The X coordinate.</param>
/// <param name="y">The Y coordinate.</param>
/// <param name="m">The measure.</param>
internal sealed class PointM(double x, double y, double m) : Point(x, y), IPointM
{
    /// <inheritdoc />
    public double M { get; } = m;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y} M{M})";
}

/// <summary>An immutable point with height and measure (ADR 0015).</summary>
/// <param name="x">The X coordinate.</param>
/// <param name="y">The Y coordinate.</param>
/// <param name="z">The height.</param>
/// <param name="m">The measure.</param>
internal sealed class Point3DM(double x, double y, double z, double m) : Point(x, y), IPoint3D, IPointM
{
    /// <inheritdoc />
    public double Z { get; } = z;

    /// <inheritdoc />
    public double M { get; } = m;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y} {Z} M{M})";
}

/// <summary>Common base of the immutable point sequences.</summary>
internal abstract class PointSequence : IPointCollection2D
{
    private readonly IPoint2D[] _points;

    /// <summary>Creates the sequence from a copy of <paramref name="points"/>.</summary>
    /// <param name="points">The points.</param>
    protected PointSequence(ReadOnlySpan<IPoint2D> points) => _points = points.ToArray();

    /// <inheritdoc />
    public int PointCount => _points.Length;

    /// <inheritdoc />
    public IPoint2D this[int index] => _points[index];
}

/// <summary>An immutable multipoint.</summary>
/// <param name="points">The points; they are copied.</param>
internal sealed class MultiPoint(params ReadOnlySpan<IPoint2D> points) : PointSequence(points), IMultiPoint2D;

/// <summary>An immutable path.</summary>
/// <param name="points">The points; they are copied.</param>
internal class Path(params ReadOnlySpan<IPoint2D> points) : PointSequence(points), IPath2D;

/// <summary>An immutable ring.</summary>
/// <param name="points">The points; they are copied. The closing point may be omitted.</param>
internal sealed class Ring(params ReadOnlySpan<IPoint2D> points) : Path(points), IRing2D;

/// <summary>An immutable polyline.</summary>
/// <param name="paths">The paths; the array is copied.</param>
internal sealed class Polyline(params ReadOnlySpan<IPath2D> paths) : IPolyline2D
{
    private readonly IPath2D[] _paths = paths.ToArray();

    /// <inheritdoc />
    public int PathCount => _paths.Length;

    /// <inheritdoc />
    public IPath2D this[int index] => _paths[index];
}

/// <summary>An immutable polygon as a flat list of rings.</summary>
/// <param name="rings">The rings; the array is copied.</param>
internal sealed class Polygon(params ReadOnlySpan<IRing2D> rings) : IPolygon2D
{
    private readonly IRing2D[] _rings = rings.ToArray();

    /// <inheritdoc />
    public int RingCount => _rings.Length;

    /// <inheritdoc />
    public IRing2D this[int index] => _rings[index];
}
