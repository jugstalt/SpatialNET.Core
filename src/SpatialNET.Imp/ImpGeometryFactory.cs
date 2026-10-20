namespace SpatialNET.Imp;

/// <summary>
/// Die Geometrie-Factory der Referenz-Implementierung. Zustandslos und damit thread-sicher;
/// alle Koordinaten werden in neue, unveränderliche Objekte kopiert.
/// </summary>
/// <param name="supportsZ">
/// Ob Koordinaten mit Höhe als <see cref="Point3D"/> erzeugt werden. Bei <c>false</c> entstehen
/// immer <see cref="Point"/> ohne Z.
/// </param>
public sealed class ImpGeometryFactory(bool supportsZ = true) : IGeometryFactory
{
    /// <summary>Eine gemeinsame Instanz mit Z-Unterstützung.</summary>
    public static ImpGeometryFactory Instance { get; } = new();

    /// <inheritdoc />
    public bool SupportsZ { get; } = supportsZ;

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
        SupportsZ && c.HasZ ? new Point3D(c.X, c.Y, c.Z) : new Point(c.X, c.Y);

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
