using System.Globalization;
using System.Text;
using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>Writes geometries as WKT in the leanest type, with normalized orientation (ADR 0009).</summary>
internal static class WktWriter
{
    public static string Write(IGeometry2D geometry, WktOptions options)
    {
        var sb = new StringBuilder();
        switch (geometry)
        {
            case IPoint2D p:
                var c = p.ToCoordinate();
                bool pz = WithZ(options.ZMode, [c]);
                sb.Append(pz ? "POINT Z (" : "POINT (");
                AppendCoordinate(sb, c, pz);
                sb.Append(')');
                break;

            case IMultiPoint2D mp:
                var points = Rings.Coordinates(mp);
                bool mz = WithZ(options.ZMode, points);
                sb.Append(mz ? "MULTIPOINT Z " : "MULTIPOINT ");
                if (points.Length == 0)
                {
                    sb.Append("EMPTY");
                }
                else
                {
                    AppendList(sb, points, (b, pt) => { b.Append('('); AppendCoordinate(b, pt, mz); b.Append(')'); });
                }

                break;

            case IPolyline2D line:
                var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(path => path.Length > 0).ToArray();
                bool lz = WithZ(options.ZMode, paths.SelectMany(path => path));
                if (paths.Length <= 1)
                {
                    sb.Append(lz ? "LINESTRING Z " : "LINESTRING ");
                    if (paths.Length == 0) sb.Append("EMPTY");
                    else AppendSequence(sb, paths[0], lz);
                }
                else
                {
                    sb.Append(lz ? "MULTILINESTRING Z " : "MULTILINESTRING ");
                    AppendList(sb, paths, (b, path) => AppendSequence(b, path, lz));
                }

                break;

            case IPolygon2D polygon:
                var parts = PolygonStructure.Build(polygon);
                bool az = WithZ(options.ZMode, parts.SelectMany(part => part.Holes.Prepend(part.Shell)).SelectMany(r => r));
                if (parts.Count <= 1)
                {
                    sb.Append(az ? "POLYGON Z " : "POLYGON ");
                    if (parts.Count == 0) sb.Append("EMPTY");
                    else AppendPart(sb, parts[0], az);
                }
                else
                {
                    sb.Append(az ? "MULTIPOLYGON Z " : "MULTIPOLYGON ");
                    AppendList(sb, parts, (b, part) => AppendPart(b, part, az));
                }

                break;

            default:
                throw new NotSupportedException($"{geometry.GetType().FullName} is not a known geometry kind.");
        }

        return sb.ToString();
    }

    private static bool WithZ(ZMode mode, IEnumerable<Coordinate> coordinates) => mode switch
    {
        ZMode.Always => true,
        ZMode.Never => false,
        _ => coordinates.Any(c => c.HasZ && c.Z != 0),
    };

    private static void AppendPart(StringBuilder sb, Part part, bool z) =>
        AppendList(sb, part.Holes.Prepend(part.Shell).ToArray(), (b, ring) => AppendSequence(b, ring, z));

    private static void AppendSequence(StringBuilder sb, Coordinate[] sequence, bool z) =>
        AppendList(sb, sequence, (b, c) => AppendCoordinate(b, c, z));

    private static void AppendList<T>(StringBuilder sb, IReadOnlyList<T> items, Action<StringBuilder, T> append)
    {
        sb.Append('(');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            append(sb, items[i]);
        }

        sb.Append(')');
    }

    private static void AppendCoordinate(StringBuilder sb, Coordinate c, bool z)
    {
        sb.Append(Number(c.X)).Append(' ').Append(Number(c.Y));
        if (z)
        {
            sb.Append(' ').Append(Number(c.HasZ ? c.Z : 0));
        }
    }

    /// <summary>Invariant and round-trip exact (shortest representation that reads back bit-exactly).</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
