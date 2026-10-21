namespace SpatialNET.Imp;

/// <summary>
/// The geometry factory of the reference implementation. Stateless and therefore thread-safe;
/// all coordinates are copied into new, immutable objects.
/// </summary>
/// <param name="supportsZ">
/// Whether coordinates with a height are created as <see cref="Point3D"/>. If <c>false</c>, no Z is created.
/// </param>
/// <param name="supportsM">
/// Whether coordinates with a measure are created as <see cref="PointM"/> or <see cref="Point3DM"/>
/// (ADR 0015). If <c>false</c>, no M is created.
/// </param>
public sealed class ImpGeometryFactory(bool supportsZ, bool supportsM) : IGeometryFactory
{
    /// <summary>A shared instance with Z and M support.</summary>
    public static ImpGeometryFactory Instance { get; } = new();

    /// <summary>Creates a factory with the given Z support and with M support.</summary>
    /// <param name="supportsZ">Whether Z is created.</param>
    public ImpGeometryFactory(bool supportsZ = true) : this(supportsZ, true)
    {
    }

    /// <inheritdoc />
    public bool SupportsZ { get; } = supportsZ;

    /// <inheritdoc />
    public bool SupportsM { get; } = supportsM;

    /// <inheritdoc />
    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) => ToPoint(coordinate);

    /// <inheritdoc />
    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        new MultiPoint(ToPoints(coordinates));

    /// <inheritdoc />
    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source)
    {
        var result = new IPath2D[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            result[i] = new Path(ToPoints(paths[i].Span));
        }

        return new Polyline(result);
    }

    /// <inheritdoc />
    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<IRing2D>();
        foreach (var part in parts)
        {
            rings.Add(new Ring(ToPoints(part.Shell.Span)));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(new Ring(ToPoints(hole.Span)));
            }
        }

        return new Polygon(rings.ToArray());
    }

    private IPoint2D ToPoint(Coordinate c) =>
        (SupportsZ && c.HasZ, SupportsM && c.HasM) switch
        {
            (true, true) => new Point3DM(c.X, c.Y, c.Z, c.M),
            (true, false) => new Point3D(c.X, c.Y, c.Z),
            (false, true) => new PointM(c.X, c.Y, c.M),
            _ => new Point(c.X, c.Y),
        };

    private IPoint2D[] ToPoints(ReadOnlySpan<Coordinate> coordinates)
    {
        var points = new IPoint2D[coordinates.Length];
        for (int i = 0; i < coordinates.Length; i++)
        {
            points[i] = ToPoint(coordinates[i]);
        }

        return points;
    }
}
