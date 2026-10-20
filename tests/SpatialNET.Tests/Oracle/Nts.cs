using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SpatialNET.Internal;
using ImpGeometryFactory = SpatialNET.Imp.ImpGeometryFactory;
using ForeignGeometryFactory = SpatialNET.Tests.Foreign.ForeignGeometryFactory;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsFactory = NetTopologySuite.Geometries.GeometryFactory;

namespace SpatialNET.Tests.Oracle;

/// <summary>
/// NTS-Adapter für das Orakel (ADR 0008): WKT über NTS lesen, Geometrien zwischen NTS und den
/// SpatialNET-Interfaces wandeln und NTS mit gleichem Präzisionsmodell (Gitterweite) bereitstellen.
/// </summary>
public static class Nts
{
    private static readonly WKTReader Reader = new();

    /// <summary>Die Implementierungen, gegen die jeder Fixture-Test läuft (ADR 0008).</summary>
    public static readonly (string Name, IGeometryFactory Factory)[] Implementations =
    [
        ("Imp", ImpGeometryFactory.Instance),
        ("Fremd", ForeignGeometryFactory.Instance),
    ];

    public static IGeometryFactory Factory(string implementation) =>
        Implementations.Single(i => i.Name == implementation).Factory;

    public static Geometry Read(string wkt) => Reader.Read(wkt);

    /// <summary>NTS-Factory mit fester Gitterweite, wie sie das Snap-Rounding der Overlays nutzt (ADR 0005).</summary>
    public static NtsFactory Fixed(double gridSize) => new(new PrecisionModel(1 / gridSize));

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

    /// <summary>Wandelt ein Ergebnis in NTS; Polygone über die Teilflächen-Bildung (Even-Odd, ADR 0003).</summary>
    public static Geometry ToNts(IGeometry2D g)
    {
        var f = NtsFactory.Default;
        switch (g)
        {
            case IPoint2D p:
                return f.CreatePoint(ToNts(p.ToCoordinate()));
            case IMultiPoint2D mp:
                return f.CreateMultiPointFromCoords(Rings.Coordinates(mp).Select(ToNts).ToArray());
            case IPolyline2D line:
                var lines = Enumerable.Range(0, line.PathCount)
                    .Select(i => f.CreateLineString(Rings.Coordinates(line[i]).Select(ToNts).ToArray())).ToArray();
                return lines.Length == 1 ? lines[0] : f.CreateMultiLineString(lines);
            case IPolygon2D polygon:
                var polygons = PolygonStructure.Build(polygon)
                    .Select(part => f.CreatePolygon(
                        f.CreateLinearRing(part.Shell.Select(ToNts).ToArray()),
                        part.Holes.Select(h => f.CreateLinearRing(h.Select(ToNts).ToArray())).ToArray()))
                    .ToArray();
                return polygons.Length == 1 ? polygons[0] : f.CreateMultiPolygon(polygons);
            default:
                throw new NotSupportedException(g.GetType().Name);
        }
    }

    public static SpatialNET.Coordinate ToCoordinate(NtsCoordinate c) =>
        double.IsNaN(c.Z) ? new(c.X, c.Y) : new(c.X, c.Y, c.Z);

    /// <summary>Immer 2D: das Orakel vergleicht kein Z, und NTS verträgt keine 2D/3D-Mischung im Overlay.</summary>
    public static NtsCoordinate ToNts(SpatialNET.Coordinate c) => new(c.X, c.Y);

    private static ReadOnlyMemory<SpatialNET.Coordinate>[] Paths(params LineString[] lines) =>
        lines.Where(l => !l.IsEmpty).Select(l => (ReadOnlyMemory<SpatialNET.Coordinate>)l.Coordinates.Select(ToCoordinate).ToArray()).ToArray();

    private static PolygonPart ToPart(Polygon p) => new(
        p.Shell.Coordinates.Select(ToCoordinate).ToArray(),
        p.Holes.Select(h => (ReadOnlyMemory<SpatialNET.Coordinate>)h.Coordinates.Select(ToCoordinate).ToArray()).ToArray());
}
