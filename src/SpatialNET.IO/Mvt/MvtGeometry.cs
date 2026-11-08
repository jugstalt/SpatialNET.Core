using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>
/// A geometry in integer tile coordinates, ready for command encoding: points as one list, lines as paths, polygons as open
/// rings (no closing point) with each exterior ring (positive area) followed by its interior rings (negative area).
/// </summary>
internal sealed record TileShape(MvtGeometryType Type, List<(int X, int Y)[]> Parts)
{
    /// <summary>Largest absolute tile coordinate, so that every delta fits into a zigzag-encoded int32.</summary>
    public const int MaxCoordinate = 1 << 30;

    /// <summary>
    /// Converts a geometry by <paramref name="map"/> into tile coordinates, rounds, drops repeated points and degenerate parts
    /// and orients the rings; <c>null</c> if nothing is left.
    /// </summary>
    public static TileShape? FromTileGeometry(IGeometry2D geometry, Func<Coordinate, Coordinate> map)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var parts = new List<(int X, int Y)[]>();
        switch (geometry)
        {
            case IPoint2D point:
                parts.Add([Round(map(point.ToCoordinate()))]);
                return new TileShape(MvtGeometryType.Point, parts);

            case IMultiPoint2D multiPoint:
                var points = Rings.Coordinates(multiPoint).Select(c => Round(map(c))).ToArray();
                if (points.Length == 0)
                {
                    return null;
                }

                parts.Add(points);
                return new TileShape(MvtGeometryType.Point, parts);

            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                {
                    var path = Quantize(Rings.Coordinates(line[i]), map);
                    if (path.Count >= 2)
                    {
                        parts.Add(path.ToArray());
                    }
                }

                return parts.Count == 0 ? null : new TileShape(MvtGeometryType.LineString, parts);

            default:
                foreach (var part in PolygonStructure.Build((IPolygon2D)geometry))
                {
                    if (QuantizeRing(part.Shell, map, exterior: true) is not { } shell)
                    {
                        continue; // the holes of a collapsed shell go with it
                    }

                    parts.Add(shell);
                    foreach (var hole in part.Holes)
                    {
                        if (QuantizeRing(hole, map, exterior: false) is { } ring)
                        {
                            parts.Add(ring);
                        }
                    }
                }

                return parts.Count == 0 ? null : new TileShape(MvtGeometryType.Polygon, parts);
        }
    }

    /// <summary>Twice the signed area by the shoelace formula; positive = exterior ring in MVT terms.</summary>
    public static long DoubleArea(IReadOnlyList<(long X, long Y)> ring)
    {
        long sum = 0;
        for (int i = 0, n = ring.Count; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum;
    }

    private static (int X, int Y)[]? QuantizeRing(IEnumerable<Coordinate> closed, Func<Coordinate, Coordinate> map, bool exterior)
    {
        var ring = Quantize(closed, map);
        while (ring.Count > 1 && ring[^1] == ring[0])
        {
            ring.RemoveAt(ring.Count - 1);
        }

        if (ring.Count < 3)
        {
            return null;
        }

        long area = DoubleArea(ring.Select(p => ((long)p.X, (long)p.Y)).ToList());
        if (area == 0)
        {
            return null;
        }

        if (area > 0 != exterior)
        {
            ring.Reverse();
        }

        return ring.ToArray();
    }

    private static List<(int X, int Y)> Quantize(IEnumerable<Coordinate> coordinates, Func<Coordinate, Coordinate> map)
    {
        var result = new List<(int X, int Y)>();
        foreach (var c in coordinates)
        {
            var p = Round(map(c));
            if (result.Count == 0 || result[^1] != p)
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static (int X, int Y) Round(Coordinate c) => (RoundOne(c.X), RoundOne(c.Y));

    private static int RoundOne(double v)
    {
        if (!double.IsFinite(v) || Math.Abs(v) > MaxCoordinate)
        {
            throw new ArgumentException($"The tile coordinate {v} is not finite or outside ±{MaxCoordinate}.");
        }

        return (int)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// The MVT geometry encoding (spec section 4.3): command integers <c>(id &amp; 0x7) | (count &lt;&lt; 3)</c> with
/// MoveTo = 1, LineTo = 2 and ClosePath = 7, followed by zigzag-encoded coordinate deltas relative to a cursor that
/// persists across the parts of a feature.
/// </summary>
public static class MvtGeometry
{
    /// <summary>The MoveTo command id.</summary>
    public const int MoveTo = 1;

    /// <summary>The LineTo command id.</summary>
    public const int LineTo = 2;

    /// <summary>The ClosePath command id.</summary>
    public const int ClosePath = 7;

    /// <summary>A command integer.</summary>
    /// <param name="id">The command id: <see cref="MoveTo"/>, <see cref="LineTo"/> or <see cref="ClosePath"/>.</param>
    /// <param name="count">The repeat count, 0 ≤ count &lt; 2^29.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is out of range.</exception>
    public static uint Command(int id, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(count, 1 << 29);
        return (uint)(id & 0x7) | ((uint)count << 3);
    }

    /// <summary>The zigzag encoding of a parameter integer: <c>(n &lt;&lt; 1) ^ (n &gt;&gt; 31)</c>.</summary>
    /// <param name="value">The value.</param>
    public static uint ZigZag(int value) => ProtoWriter.ZigZag32(value);

    /// <summary>The inverse of <see cref="ZigZag"/>.</summary>
    /// <param name="value">The encoded value.</param>
    public static int UnZigZag(uint value) => ProtoReader.UnZigZag32(value);

    /// <summary>
    /// Encodes a geometry that is in tile coordinates (see <see cref="MvtFeature.Create"/> for the rounding and cleaning rules).
    /// </summary>
    /// <param name="tileGeometry">The geometry in tile coordinates.</param>
    /// <returns>The geometry type and the command integers; <see cref="MvtGeometryType.Unknown"/> and none if nothing is left.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tileGeometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not finite or outside ±2^30.</exception>
    public static (MvtGeometryType Type, uint[] Commands) Encode(IGeometry2D tileGeometry)
    {
        ArgumentNullException.ThrowIfNull(tileGeometry);
        var shape = TileShape.FromTileGeometry(tileGeometry, c => c);
        return shape is null ? (MvtGeometryType.Unknown, []) : (shape.Type, Encode(shape));
    }

    internal static uint[] Encode(TileShape shape)
    {
        var result = new List<uint>();
        int cx = 0, cy = 0;

        void Param((int X, int Y) p)
        {
            result.Add(ZigZag(p.X - cx));
            result.Add(ZigZag(p.Y - cy));
            cx = p.X;
            cy = p.Y;
        }

        foreach (var part in shape.Parts)
        {
            if (shape.Type == MvtGeometryType.Point)
            {
                result.Add(Command(MoveTo, part.Length));
                foreach (var p in part)
                {
                    Param(p);
                }

                continue;
            }

            result.Add(Command(MoveTo, 1));
            Param(part[0]);
            result.Add(Command(LineTo, part.Length - 1));
            for (int i = 1; i < part.Length; i++)
            {
                Param(part[i]);
            }

            if (shape.Type == MvtGeometryType.Polygon)
            {
                result.Add(Command(ClosePath, 1));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Decodes command integers into a geometry (see <see cref="MvtFeature.ToGeometry"/>). Polygon rings are classified by the
    /// sign of their area in tile coordinates (positive = exterior); a ring with zero area is dropped, an interior ring before
    /// any exterior ring is read as an exterior ring (lenient towards version 1 tiles).
    /// </summary>
    /// <param name="type">The geometry type.</param>
    /// <param name="commands">The command integers.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="transform">If given, coordinates are converted from tile to world coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">The commands are invalid for the type, or the type is <see cref="MvtGeometryType.Unknown"/>.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D Decode(MvtGeometryType type, IReadOnlyList<uint> commands, IGeometryFactory? factory = null, MvtTileTransform? transform = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var f = GeometryFactory.Resolve(factory);
        if (type is not (MvtGeometryType.Point or MvtGeometryType.LineString or MvtGeometryType.Polygon))
        {
            throw new FormatException($"Geometry type {type} cannot be decoded.");
        }

        var parts = new List<List<(long X, long Y)>>();
        var closed = new List<bool>();
        long x = 0, y = 0;
        int i = 0;
        while (i < commands.Count)
        {
            uint command = commands[i++];
            int id = (int)(command & 0x7);
            int count = (int)(command >> 3);
            switch (id)
            {
                case MoveTo:
                    for (int k = 0; k < count; k++)
                    {
                        Read(ref i, ref x, ref y);
                        if (type != MvtGeometryType.Point || parts.Count == 0)
                        {
                            parts.Add([]);
                            closed.Add(false);
                        }

                        parts[^1].Add((x, y));
                    }

                    break;

                case LineTo:
                    if (type == MvtGeometryType.Point || parts.Count == 0 || closed[^1])
                    {
                        throw new FormatException($"LineTo without an open MoveTo at command index {i - 1}.");
                    }

                    for (int k = 0; k < count; k++)
                    {
                        Read(ref i, ref x, ref y);
                        parts[^1].Add((x, y));
                    }

                    break;

                case ClosePath:
                    if (type != MvtGeometryType.Polygon || parts.Count == 0 || closed[^1])
                    {
                        throw new FormatException($"ClosePath outside a polygon ring at command index {i - 1}.");
                    }

                    closed[^1] = true;
                    break;

                default:
                    throw new FormatException($"Unknown command id {id} at command index {i - 1}.");
            }
        }

        Coordinate ToCoordinate((long X, long Y) p) =>
            transform is null ? new Coordinate(p.X, p.Y) : transform.ToWorld(p.X, p.Y);

        switch (type)
        {
            case MvtGeometryType.Point:
                var points = parts.SelectMany(p => p).Select(ToCoordinate).ToArray();
                return points.Length == 1 ? f.CreatePoint(points[0], null) : f.CreateMultiPoint(points, null);

            case MvtGeometryType.LineString:
                var paths = parts.Where(p => p.Count >= 2).Select(p => (ReadOnlyMemory<Coordinate>)p.Select(ToCoordinate).ToArray()).ToArray();
                return f.CreatePolyline(paths, null);

            default:
                var polygonParts = new List<(Coordinate[] Shell, List<ReadOnlyMemory<Coordinate>> Holes)>();
                for (int k = 0; k < parts.Count; k++)
                {
                    if (!closed[k])
                    {
                        throw new FormatException("A polygon ring is not closed by ClosePath.");
                    }

                    long area = TileShape.DoubleArea(parts[k]);
                    if (area == 0)
                    {
                        continue;
                    }

                    var ring = Oriented(parts[k].Select(ToCoordinate).ToList(), counterClockwise: area > 0 || polygonParts.Count == 0);
                    if (area > 0 || polygonParts.Count == 0)
                    {
                        polygonParts.Add((ring, []));
                    }
                    else
                    {
                        polygonParts[^1].Holes.Add(ring);
                    }
                }

                return f.CreatePolygon(polygonParts.Select(p => new PolygonPart(p.Shell, p.Holes.ToArray())).ToArray(), null);
        }

        void Read(ref int index, ref long px, ref long py)
        {
            if (index + 1 >= commands.Count)
            {
                throw new FormatException($"Missing parameters at command index {index}.");
            }

            px += UnZigZag(commands[index++]);
            py += UnZigZag(commands[index++]);
        }
    }

    /// <summary>Closes the ring and orients it in its final coordinates (shell counter-clockwise, hole clockwise, factory contract).</summary>
    private static Coordinate[] Oriented(List<Coordinate> ring, bool counterClockwise)
    {
        ring.Add(ring[0]);
        double sum = 0;
        for (int i = 0; i + 1 < ring.Count; i++)
        {
            sum += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        }

        if (sum > 0 != counterClockwise)
        {
            ring.Reverse();
        }

        return ring.ToArray();
    }
}
