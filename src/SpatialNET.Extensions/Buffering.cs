using SpatialNET.Internal;
using SpatialNET.Internal.Buffer;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>Shape of the corners on the outside (ADR 0006).</summary>
public enum JoinStyle
{
    /// <summary>Circular arc.</summary>
    Round,

    /// <summary>Mitre (intersection of the offset lines), limited by <see cref="BufferOptions.MitreLimit"/>.</summary>
    Mitre,

    /// <summary>Straight connection of the offset ends.</summary>
    Bevel,
}

/// <summary>Shape of the line ends (ADR 0006).</summary>
public enum CapStyle
{
    /// <summary>Semicircle.</summary>
    Round,

    /// <summary>Cut off straight at the end point.</summary>
    Flat,

    /// <summary>Extended by the distance and cut off straight.</summary>
    Square,
}

/// <summary>Options for <see cref="Buffering.Buffer"/>; defaults as in JTS (ADR 0006).</summary>
public sealed record BufferOptions
{
    /// <summary>The default options.</summary>
    public static BufferOptions Default { get; } = new();

    /// <summary>Corners on the outside; default <see cref="JoinStyle.Round"/>.</summary>
    public JoinStyle JoinStyle { get; init; } = JoinStyle.Round;

    /// <summary>Line ends; default <see cref="CapStyle.Round"/>.</summary>
    public CapStyle CapStyle { get; init; } = CapStyle.Round;

    /// <summary>Segments per quarter circle; default 8.</summary>
    public int QuadrantSegments { get; init; } = 8;

    /// <summary>Largest chord deviation of the arcs in map units; if set, it takes precedence over <see cref="QuadrantSegments"/>.</summary>
    public double? ArcTolerance { get; init; }

    /// <summary>Largest ratio of mitre distance to distance for <see cref="JoinStyle.Mitre"/>; default 5.</summary>
    public double MitreLimit { get; init; } = 5;

    /// <summary>Factor of the input simplification (× |distance|); default 0.01, 0 = off.</summary>
    public double SimplifyFactor { get; init; } = 0.01;

    /// <summary>Buffer only one side of lines (positive left, negative right), flat ends.</summary>
    public bool SingleSided { get; init; }
}

/// <summary>Buffer as raw offset + union with <see cref="FillRule.Positive"/> (ADR 0006).</summary>
public static class Buffering
{
    /// <summary>
    /// The buffer: all points within a distance ≤ |<paramref name="distance"/>| (for a negative distance, the area
    /// shrunk inwards by |d|). Results are 2D and valid.
    /// </summary>
    /// <remarks>
    /// Polygon with d = 0 → input normalized by even-odd; completely eroded → empty; point/line with d ≤ 0 →
    /// empty (except one-sided). Invalid input is interpreted by even-odd.
    /// </remarks>
    /// <param name="geometry">The geometry.</param>
    /// <param name="distance">The distance in map units.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="BufferOptions.Default"/>.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">The grid size; <c>null</c> = cascade (global default, otherwise automatic).</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distance not finite or invalid options.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static IPolygon2D Buffer(this IGeometry2D geometry, double distance, BufferOptions? options = null,
        IGeometryFactory? factory = null, double? gridSize = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        options ??= BufferOptions.Default;
        Validate(distance, options);
        var f = GeometryFactory.Resolve(factory);
        var parts = Parts(geometry, distance, options, GridModel.Resolve(gridSize, geometry));
        return f.CreatePolygon(parts.Select(Flat).ToArray(), geometry);
    }

    /// <summary>The parts of the buffer (shell counter-clockwise) – also used by OffsetCurve.</summary>
    internal static List<PolygonPart> Parts(IGeometry2D geometry, double distance, BufferOptions options, GridModel grid)
    {
        if (GeometryAccess.IsEmpty(geometry))
        {
            return [];
        }

        if (geometry is IPolygon2D polygon && distance == 0)
        {
            var normalized = new OverlayEngine(polygon, null, grid);
            return normalized.Areas(h => normalized.InsideLeft(h, 0));
        }

        bool singleSided = options.SingleSided && geometry is IPolyline2D;
        if (geometry is not IPolygon2D && (singleSided ? distance == 0 : distance <= 0))
        {
            return [];
        }

        var builder = new OffsetBuilder(options, distance);
        var raw = new List<Coordinate[]>();
        switch (geometry)
        {
            case IPoint2D p:
                Add(builder.Point(p.ToCoordinate()));
                break;
            case IMultiPoint2D mp:
                foreach (var c in Rings.Coordinates(mp)) Add(builder.Point(c));
                break;
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                {
                    var path = Rings.Coordinates(line[i]);
                    if (path.Length > 0) Add(singleSided ? builder.SingleSided(path) : builder.Line(path));
                }

                break;
            case IPolygon2D poly:
                double r = Math.Abs(distance);
                foreach (var part in PolygonStructure.Build(poly))
                {
                    if (distance < 0 && ErodedCompletely(part.Shell, r)) continue;
                    Add(builder.Ring(part.Shell));
                    foreach (var hole in part.Holes)
                    {
                        if (distance > 0 && ErodedCompletely(hole, r)) continue; // Loch wird ganz gefüllt
                        Add(builder.Ring(hole));
                    }
                }

                break;
        }

        var lines = raw.Select(ring => (ring, 0, true)).ToList();
        var engine = new OverlayEngine([null, null], grid, [FillRule.Positive, FillRule.Positive], lines);
        return engine.Areas(h => engine.InsideLeft(h, 0));

        void Add(Coordinate[]? ring)
        {
            if (ring is { Length: >= 4 }) raw.Add(ring);
        }
    }

    /// <summary>Whether a ring is completely eroded at distance <paramref name="r"/> (envelope narrower than 2r, as in JTS).</summary>
    private static bool ErodedCompletely(Coordinate[] ring, double r)
    {
        double minX = ring.Min(c => c.X), maxX = ring.Max(c => c.X), minY = ring.Min(c => c.Y), maxY = ring.Max(c => c.Y);
        return Math.Min(maxX - minX, maxY - minY) < 2 * r;
    }

    private static PolygonPart Flat(PolygonPart part) =>
        new(part.Shell.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray(),
            part.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)h.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray()).ToArray());

    internal static void Validate(double distance, BufferOptions options)
    {
        if (!double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be finite.");
        if (options.QuadrantSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.QuadrantSegments, "QuadrantSegments must be ≥ 1.");
        if (!(options.MitreLimit > 0) || !double.IsFinite(options.MitreLimit))
            throw new ArgumentOutOfRangeException(nameof(options), options.MitreLimit, "MitreLimit must be finite and > 0.");
        if (!(options.SimplifyFactor >= 0) || !double.IsFinite(options.SimplifyFactor))
            throw new ArgumentOutOfRangeException(nameof(options), options.SimplifyFactor, "SimplifyFactor must be finite and ≥ 0.");
        if (options.ArcTolerance is { } tol && (!(tol > 0) || !double.IsFinite(tol)))
            throw new ArgumentOutOfRangeException(nameof(options), tol, "ArcTolerance must be finite and > 0.");
    }
}
