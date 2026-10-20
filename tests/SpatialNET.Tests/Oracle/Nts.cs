using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ImpGeometryFactory = SpatialNET.Imp.ImpGeometryFactory;
using ForeignGeometryFactory = SpatialNET.Tests.Foreign.ForeignGeometryFactory;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace SpatialNET.Tests.Oracle;

/// <summary>
/// Vorläufiger NTS-Adapter für das Orakel: liest WKT mit NTS und baut daraus Geometrien über eine
/// SpatialNET-Factory. Der vollständige Orakel-Adapter samt Fixture-Loader folgt mit #22.
/// </summary>
public static class Nts
{
    private static readonly WKTReader Reader = new();

    /// <summary>Die Implementierungen, gegen die jeder Vergleich läuft (ADR 0008).</summary>
    public static readonly (string Name, IGeometryFactory Factory)[] Implementations =
    [
        ("Imp", ImpGeometryFactory.Instance),
        ("Fremd", ForeignGeometryFactory.Instance),
    ];

    public static Geometry Read(string wkt) => Reader.Read(wkt);

    public static IGeometry2D ToSpatial(Geometry g, IGeometryFactory factory) => g switch
    {
        Point { IsEmpty: true } => factory.CreateMultiPoint([], null),
        Point p => factory.CreatePoint(ToCoordinate(p.Coordinate), null),
        MultiPoint mp => factory.CreateMultiPoint(mp.Coordinates.Select(ToCoordinate).ToArray(), null),
        LineString l => factory.CreatePolyline(Paths(l), null),
        MultiLineString ml => factory.CreatePolyline(Paths(ml.Geometries.Cast<LineString>().ToArray()), null),
        Polygon p => factory.CreatePolygon(p.IsEmpty ? [] : [ToPart(p)], null),
        MultiPolygon mp => factory.CreatePolygon(mp.Geometries.Cast<Polygon>().Select(ToPart).ToArray(), null),
        _ => throw new NotSupportedException(g.GeometryType),
    };

    public static SpatialNET.Coordinate ToCoordinate(NtsCoordinate c) =>
        double.IsNaN(c.Z) ? new(c.X, c.Y) : new(c.X, c.Y, c.Z);

    private static ReadOnlyMemory<SpatialNET.Coordinate>[] Paths(params LineString[] lines) =>
        lines.Where(l => !l.IsEmpty).Select(l => (ReadOnlyMemory<SpatialNET.Coordinate>)l.Coordinates.Select(ToCoordinate).ToArray()).ToArray();

    private static PolygonPart ToPart(Polygon p) => new(
        p.Shell.Coordinates.Select(ToCoordinate).ToArray(),
        p.Holes.Select(h => (ReadOnlyMemory<SpatialNET.Coordinate>)h.Coordinates.Select(ToCoordinate).ToArray()).ToArray());
}
