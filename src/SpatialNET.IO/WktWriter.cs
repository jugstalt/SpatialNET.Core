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
                bool pz = WithZ(options.ZMode, [c]), pm = WithM(options.MMode, [c]);
                sb.Append("POINT").Append(Dim(pz, pm)).Append(" (");
                AppendCoordinate(sb, c, pz, pm);
                sb.Append(')');
                break;

            case IMultiPoint2D mp:
                var points = Rings.Coordinates(mp);
                bool mz = WithZ(options.ZMode, points), mm = WithM(options.MMode, points);
                sb.Append("MULTIPOINT").Append(Dim(mz, mm)).Append(' ');
                if (points.Length == 0)
                {
                    sb.Append("EMPTY");
                }
                else
                {
                    AppendList(sb, points, (b, pt) => { b.Append('('); AppendCoordinate(b, pt, mz, mm); b.Append(')'); });
                }

                break;

            case IPolyline2D line:
                var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(path => path.Length > 0).ToArray();
                bool lz = WithZ(options.ZMode, paths.SelectMany(path => path)), lm = WithM(options.MMode, paths.SelectMany(path => path));
                if (paths.Length <= 1)
                {
                    sb.Append("LINESTRING").Append(Dim(lz, lm)).Append(' ');
                    if (paths.Length == 0) sb.Append("EMPTY");
                    else AppendSequence(sb, paths[0], lz, lm);
                }
                else
                {
                    sb.Append("MULTILINESTRING").Append(Dim(lz, lm)).Append(' ');
                    AppendList(sb, paths, (b, path) => AppendSequence(b, path, lz, lm));
                }

                break;

            case IPolygon2D polygon:
                var parts = PolygonStructure.Build(polygon);
                var ringCoordinates = parts.SelectMany(part => part.Holes.Prepend(part.Shell)).SelectMany(r => r).ToArray();
                bool az = WithZ(options.ZMode, ringCoordinates), am = WithM(options.MMode, ringCoordinates);
                if (parts.Count <= 1)
                {
                    sb.Append("POLYGON").Append(Dim(az, am)).Append(' ');
                    if (parts.Count == 0) sb.Append("EMPTY");
                    else AppendPart(sb, parts[0], az, am);
                }
                else
                {
                    sb.Append("MULTIPOLYGON").Append(Dim(az, am)).Append(' ');
                    AppendList(sb, parts, (b, part) => AppendPart(b, part, az, am));
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

    private static bool WithM(MMode mode, IEnumerable<Coordinate> coordinates) =>
        mode == MMode.Auto && coordinates.Any(c => c.HasM);

    private static string Dim(bool z, bool m) => (z, m) switch
    {
        (true, true) => " ZM",
        (true, false) => " Z",
        (false, true) => " M",
        _ => "",
    };

    private static void AppendPart(StringBuilder sb, Part part, bool z, bool m) =>
        AppendList(sb, part.Holes.Prepend(part.Shell).ToArray(), (b, ring) => AppendSequence(b, ring, z, m));

    private static void AppendSequence(StringBuilder sb, Coordinate[] sequence, bool z, bool m) =>
        AppendList(sb, sequence, (b, c) => AppendCoordinate(b, c, z, m));

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

    private static void AppendCoordinate(StringBuilder sb, Coordinate c, bool z, bool m)
    {
        sb.Append(Number(c.X)).Append(' ').Append(Number(c.Y));
        if (z)
        {
            sb.Append(' ').Append(Number(c.HasZ ? c.Z : 0));
        }

        if (m)
        {
            sb.Append(' ').Append(Number(c.M)); // NaN if the point has no M
        }
    }

    /// <summary>Invariant and round-trip exact (shortest representation that reads back bit-exactly).</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
