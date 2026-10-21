using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>Writes geometries as ISO WKB (little endian) in the leanest type, with normalized orientation (ADR 0016).</summary>
internal static class WkbWriter
{
    private const int Point = 1, LineString = 2, Polygon = 3, MultiPoint = 4, MultiLineString = 5, MultiPolygon = 6;

    public static byte[] Write(IGeometry2D geometry, WkbOptions options)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream); // BinaryWriter is always little endian

        switch (geometry)
        {
            case IPoint2D p:
                var c = p.ToCoordinate();
                var (pz, pm) = Dimensions(options, [c]);
                Header(w, Point, pz, pm);
                WriteCoordinate(w, c, pz, pm);
                break;

            case IMultiPoint2D mp:
                var points = Rings.Coordinates(mp);
                var (mz, mm) = Dimensions(options, points);
                Header(w, MultiPoint, mz, mm);
                w.Write((uint)points.Length);
                foreach (var point in points)
                {
                    Header(w, Point, mz, mm);
                    WriteCoordinate(w, point, mz, mm);
                }

                break;

            case IPolyline2D line:
                var paths = Enumerable.Range(0, line.PathCount).Select(i => Rings.Coordinates(line[i])).Where(path => path.Length > 0).ToArray();
                var (lz, lm) = Dimensions(options, paths.SelectMany(path => path));
                if (paths.Length <= 1)
                {
                    Header(w, LineString, lz, lm);
                    WriteSequence(w, paths.Length == 0 ? [] : paths[0], lz, lm);
                }
                else
                {
                    Header(w, MultiLineString, lz, lm);
                    w.Write((uint)paths.Length);
                    foreach (var path in paths)
                    {
                        Header(w, LineString, lz, lm);
                        WriteSequence(w, path, lz, lm);
                    }
                }

                break;

            case IPolygon2D polygon:
                var parts = PolygonStructure.Build(polygon);
                var (az, am) = Dimensions(options, parts.SelectMany(part => part.Holes.Prepend(part.Shell)).SelectMany(r => r));
                if (parts.Count <= 1)
                {
                    Header(w, Polygon, az, am);
                    WritePart(w, parts.Count == 0 ? null : parts[0], az, am);
                }
                else
                {
                    Header(w, MultiPolygon, az, am);
                    w.Write((uint)parts.Count);
                    foreach (var part in parts)
                    {
                        Header(w, Polygon, az, am);
                        WritePart(w, part, az, am);
                    }
                }

                break;

            default:
                throw new NotSupportedException($"{geometry.GetType().FullName} is not a known geometry kind.");
        }

        w.Flush();
        return stream.ToArray();
    }

    private static (bool Z, bool M) Dimensions(WkbOptions options, IEnumerable<Coordinate> coordinates)
    {
        var all = coordinates as IReadOnlyCollection<Coordinate> ?? coordinates.ToArray();
        bool z = options.ZMode switch
        {
            ZMode.Always => true,
            ZMode.Never => false,
            _ => all.Any(c => c.HasZ && c.Z != 0),
        };
        bool m = options.MMode == MMode.Auto && all.Any(c => c.HasM);
        return (z, m);
    }

    private static void Header(BinaryWriter w, int type, bool z, bool m)
    {
        w.Write((byte)1);
        w.Write((uint)(type + (z ? 1000 : 0) + (m ? 2000 : 0)));
    }

    private static void WritePart(BinaryWriter w, Part? part, bool z, bool m)
    {
        if (part is null)
        {
            w.Write(0u);
            return;
        }

        w.Write((uint)(1 + part.Holes.Count));
        WriteSequence(w, part.Shell, z, m);
        foreach (var hole in part.Holes)
        {
            WriteSequence(w, hole, z, m);
        }
    }

    private static void WriteSequence(BinaryWriter w, Coordinate[] sequence, bool z, bool m)
    {
        w.Write((uint)sequence.Length);
        foreach (var c in sequence)
        {
            WriteCoordinate(w, c, z, m);
        }
    }

    private static void WriteCoordinate(BinaryWriter w, Coordinate c, bool z, bool m)
    {
        if (!double.IsFinite(c.X) || !double.IsFinite(c.Y) || double.IsInfinity(c.Z) || double.IsInfinity(c.M))
        {
            throw new ArgumentException("WKB cannot hold a coordinate with a non-finite X or Y, or an infinite Z or M.");
        }

        w.Write(c.X);
        w.Write(c.Y);
        if (z)
        {
            w.Write(c.HasZ ? c.Z : 0);
        }

        if (m)
        {
            w.Write(c.M); // NaN if the point has no M
        }
    }
}
