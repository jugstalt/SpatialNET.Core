using Proj4Net.Core;
using SpatialNET.Imp;
using Proj4Coordinate = Proj4Net.Core.Coordinate;

namespace SpatialNET.Playground.Services;

/// <summary>
/// Projektion zwischen WGS84 (Leaflet) und dem Rechen-KBS mit Proj4Net (ADR 0010). Transformiert nur Vertices,
/// ohne Verdichtung; die Struktur (Ringe, Pfade) bleibt erhalten.
/// </summary>
public sealed class Projection
{
    private static readonly CoordinateReferenceSystemFactory Crs = new();
    private static readonly CoordinateTransformFactory Transforms = new();
    private static readonly CoordinateReferenceSystem Wgs84 = Crs.CreateFromName("EPSG:4326");

    private readonly ICoordinateTransform _forward;
    private readonly ICoordinateTransform _back;

    public Projection(string epsg)
    {
        var target = Crs.CreateFromName(epsg);
        Epsg = epsg;
        IsGeographic = target.Projection.Unit.Name.Contains("degree", StringComparison.OrdinalIgnoreCase);
        _forward = Transforms.CreateTransform(Wgs84, target);
        _back = Transforms.CreateTransform(target, Wgs84);
    }

    public string Epsg { get; }

    public bool IsGeographic { get; }

    /// <summary>Einheit der Distanz-Parameter (ADR 0010).</summary>
    public string Unit => IsGeographic ? "°" : "m";

    /// <summary>Auto-UTM: Zone aus dem Mittelpunkt (Länge, Breite) in WGS84.</summary>
    public static string AutoUtm(double lon, double lat)
    {
        int zone = Math.Clamp((int)Math.Floor((lon + 180) / 6) + 1, 1, 60);
        return $"EPSG:{(lat >= 0 ? 32600 : 32700) + zone}";
    }

    public IGeometry2D ToProjected(IGeometry2D wgs84) => Map(wgs84, _forward);

    public IGeometry2D ToWgs84(IGeometry2D projected) => Map(projected, _back);

    public static IGeometry2D Map(IGeometry2D g, ICoordinateTransform transform) =>
        Map(g, c =>
        {
            var t = transform.Transform(new Proj4Coordinate(c.X, c.Y), new Proj4Coordinate());
            return new SpatialNET.Coordinate(t.X, t.Y, c.Z);
        });

    /// <summary>Bildet jede Koordinate ab und baut die gleiche Struktur mit der Referenz-Implementierung.</summary>
    public static IGeometry2D Map(IGeometry2D g, Func<SpatialNET.Coordinate, SpatialNET.Coordinate> map)
    {
        IPoint2D P(IPoint2D p)
        {
            var c = map(p is IPoint3D p3 ? new SpatialNET.Coordinate(p.X, p.Y, p3.Z) : new SpatialNET.Coordinate(p.X, p.Y));
            return c.HasZ ? new Point3D(c.X, c.Y, c.Z) : new Imp.Point(c.X, c.Y);
        }

        IPoint2D[] Points(IPointCollection2D pc) => Enumerable.Range(0, pc.PointCount).Select(i => P(pc[i])).ToArray();

        return g switch
        {
            IPoint2D p => P(p),
            IMultiPoint2D mp => new MultiPoint(Points(mp)),
            IPolyline2D line => new Polyline(Enumerable.Range(0, line.PathCount).Select(i => (IPath2D)new Imp.Path(Points(line[i]))).ToArray()),
            IPolygon2D polygon => new Imp.Polygon(Enumerable.Range(0, polygon.RingCount).Select(i => (IRing2D)new Ring(Points(polygon[i]))).ToArray()),
            _ => throw new NotSupportedException(g.GetType().Name),
        };
    }
}
