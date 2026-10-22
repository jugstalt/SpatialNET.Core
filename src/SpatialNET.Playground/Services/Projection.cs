using Proj4Net.Core;
using SpatialNET.Imp;
using Proj4Coordinate = Proj4Net.Core.Coordinate;

namespace SpatialNET.Playground.Services;

/// <summary>
/// Projection between WGS84 (Leaflet) and the computation CRS with Proj4Net (ADR 0010). Transforms vertices only,
/// without densifying; the structure (rings, paths) is kept, and so are Z and M.
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

    /// <summary>The unit of the distance parameters (ADR 0010).</summary>
    public string Unit => IsGeographic ? "°" : "m";

    /// <summary>Auto-UTM: the zone of the centre (longitude, latitude) in WGS84.</summary>
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
            return new SpatialNET.Coordinate(t.X, t.Y, c.Z, c.M);
        });

    /// <summary>Maps every coordinate and builds the same structure with the reference implementation.</summary>
    public static IGeometry2D Map(IGeometry2D g, Func<SpatialNET.Coordinate, SpatialNET.Coordinate> map)
    {
        IPoint2D P(IPoint2D p)
        {
            var c = map(new SpatialNET.Coordinate(p.X, p.Y, p is IPoint3D p3 ? p3.Z : double.NaN, p is IPointM pm ? pm.M : double.NaN));
            return (c.HasZ, c.HasM) switch
            {
                (false, false) => new Imp.Point(c.X, c.Y),
                (true, false) => new Point3D(c.X, c.Y, c.Z),
                (false, true) => new PointM(c.X, c.Y, c.M),
                _ => new Point3DM(c.X, c.Y, c.Z, c.M),
            };
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
