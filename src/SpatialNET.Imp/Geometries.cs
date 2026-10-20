namespace SpatialNET.Imp;

/// <summary>Ein unveränderlicher Punkt ohne Höhe.</summary>
/// <param name="x">Die X-Koordinate.</param>
/// <param name="y">Die Y-Koordinate.</param>
public class Point(double x, double y) : IPoint2D
{
    /// <inheritdoc />
    public double X { get; } = x;

    /// <inheritdoc />
    public double Y { get; } = y;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y})";
}

/// <summary>Ein unveränderlicher Punkt mit Höhe.</summary>
/// <param name="x">Die X-Koordinate.</param>
/// <param name="y">Die Y-Koordinate.</param>
/// <param name="z">Die Höhe.</param>
public sealed class Point3D(double x, double y, double z) : Point(x, y), IPoint3D
{
    /// <inheritdoc />
    public double Z { get; } = z;

    /// <inheritdoc />
    public override string ToString() => $"({X} {Y} {Z})";
}

/// <summary>Gemeinsame Basis der unveränderlichen Punktfolgen.</summary>
public abstract class PointSequence : IPointCollection2D
{
    private readonly IPoint2D[] _points;

    /// <summary>Legt die Folge aus einer Kopie von <paramref name="points"/> an.</summary>
    /// <param name="points">Die Punkte.</param>
    protected PointSequence(ReadOnlySpan<IPoint2D> points) => _points = points.ToArray();

    /// <inheritdoc />
    public int PointCount => _points.Length;

    /// <inheritdoc />
    public IPoint2D this[int index] => _points[index];
}

/// <summary>Ein unveränderlicher Multipunkt.</summary>
/// <param name="points">Die Punkte; werden kopiert.</param>
public sealed class MultiPoint(params ReadOnlySpan<IPoint2D> points) : PointSequence(points), IMultiPoint2D;

/// <summary>Ein unveränderlicher Pfad.</summary>
/// <param name="points">Die Punkte; werden kopiert.</param>
public class Path(params ReadOnlySpan<IPoint2D> points) : PointSequence(points), IPath2D;

/// <summary>Ein unveränderlicher Ring.</summary>
/// <param name="points">Die Punkte; werden kopiert. Der Schlusspunkt darf fehlen.</param>
public sealed class Ring(params ReadOnlySpan<IPoint2D> points) : Path(points), IRing2D;

/// <summary>Eine unveränderliche Polylinie.</summary>
/// <param name="paths">Die Pfade; das Feld wird kopiert.</param>
public sealed class Polyline(params ReadOnlySpan<IPath2D> paths) : IPolyline2D
{
    private readonly IPath2D[] _paths = paths.ToArray();

    /// <inheritdoc />
    public int PathCount => _paths.Length;

    /// <inheritdoc />
    public IPath2D this[int index] => _paths[index];
}

/// <summary>Ein unveränderliches Polygon als flache Ringliste.</summary>
/// <param name="rings">Die Ringe; das Feld wird kopiert.</param>
public sealed class Polygon(params ReadOnlySpan<IRing2D> rings) : IPolygon2D
{
    private readonly IRing2D[] _rings = rings.ToArray();

    /// <inheritdoc />
    public int RingCount => _rings.Length;

    /// <inheritdoc />
    public IRing2D this[int index] => _rings[index];
}
