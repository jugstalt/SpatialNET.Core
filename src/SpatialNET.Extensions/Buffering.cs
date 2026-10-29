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

        if (raw.Count == 1 && ConvexOnGrid(raw[0], grid) is { } convex)
        {
            return [convex];
        }

        var lines = raw.Select(ring => (ring, 0, true)).ToList();
        var engine = new OverlayEngine([null, null], grid, [FillRule.Positive, FillRule.Positive], lines);
        return engine.Areas(h => engine.InsideLeft(h, 0));

        void Add(Coordinate[]? ring)
        {
            if (ring is { Length: >= 4 }) raw.Add(ring);
        }
    }

    /// <summary>
    /// The union of a single raw ring without the overlay (the circle around a point), where its result is known: the ring is strictly
    /// convex and counter-clockwise, every vertex lies more than <see cref="ConvexMargin"/> grid units from the lines of the edges it does not
    /// touch (so the snap rounding routes no edge through another hot pixel and adds no crossing), and the rounded ring is strictly convex
    /// too. The overlay then gives exactly the rounded vertices, from the first one on; otherwise <c>null</c>.
    /// </summary>
    private static PolygonPart? ConvexOnGrid(Coordinate[] ring, GridModel grid)
    {
        int n = ring.Length - 1;
        if (n < 3 || !Rings.SameXY(ring[0], ring[n]))
        {
            return null;
        }

        var x = new double[n];
        var y = new double[n];
        var rounded = new GridPoint[n];
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(ring[i].X) || !double.IsFinite(ring[i].Y)) return null;
            x[i] = grid.ToUnits(ring[i].X);
            y[i] = grid.ToUnits(ring[i].Y);
            rounded[i] = grid.Round(ring[i]);
        }

        // strictly convex, once around (the turns add up to 360°, not a multiple)
        double turning = 0, roundedTurning = 0;
        for (int i = 0; i < n; i++)
        {
            int previous = (i + n - 1) % n, next = (i + 1) % n;
            if (Orientation.Orient2D(ring[previous], ring[i], ring[next]) <= 0
                || GridPoint.Orient(rounded[previous], rounded[i], rounded[next]) <= 0)
            {
                return null;
            }

            turning += Turn(x[previous], y[previous], x[i], y[i], x[next], y[next]);
            roundedTurning += Turn(rounded[previous].X, rounded[previous].Y, rounded[i].X, rounded[i].Y, rounded[next].X, rounded[next].Y);
        }

        if (turning > 3 * Math.PI || roundedTurning > 3 * Math.PI)
        {
            return null;
        }

        // On a convex ring, the distance to the line of an edge grows and falls along the other vertices: the nearest are its neighbours.
        for (int i = 0; i < n; i++)
        {
            int a = i, b = (i + 1) % n;
            if (Distance(a, b, (i + n - 1) % n) <= ConvexMargin || Distance(a, b, (i + 2) % n) <= ConvexMargin) return null;
        }

        var shell = new Coordinate[n + 1];
        for (int i = 0; i < n; i++) shell[i] = grid.ToCoordinate(rounded[i]);
        shell[n] = shell[0];
        return new PolygonPart(shell);

        double Distance(int a, int b, int c)
        {
            double dx = x[b] - x[a], dy = y[b] - y[a];
            return Math.Abs(dx * (y[c] - y[a]) - dy * (x[c] - x[a])) / Math.Sqrt(dx * dx + dy * dy);
        }

        static double Turn(double x0, double y0, double x1, double y1, double x2, double y2)
        {
            double ux = x1 - x0, uy = y1 - y0, vx = x2 - x1, vy = y2 - y1;
            return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        }
    }

    /// <summary>
    /// Distance in grid units beyond which a hot pixel (a half-open unit square around a rounded vertex, within √2 of the vertex) cannot
    /// touch a line; with room for the rounding of the conversion to units.
    /// </summary>
    private const double ConvexMargin = 3;

    /// <summary>Whether a ring is completely eroded at distance <paramref name="r"/> (envelope narrower than 2r, as in JTS).</summary>
    private static bool ErodedCompletely(Coordinate[] ring, double r)
    {
        double minX = ring.Min(c => c.X), maxX = ring.Max(c => c.X), minY = ring.Min(c => c.Y), maxY = ring.Max(c => c.Y);
        return Math.Min(maxX - minX, maxY - minY) < 2 * r;
    }

    private static PolygonPart Flat(PolygonPart part) =>
        IsFlat(part.Shell.Span) && part.Holes.ToArray().All(h => IsFlat(h.Span)) ? part : new(part.Shell.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray(),
            part.Holes.ToArray().Select(h => (ReadOnlyMemory<Coordinate>)h.ToArray().Select(c => new Coordinate(c.X, c.Y)).ToArray()).ToArray());

    private static bool IsFlat(ReadOnlySpan<Coordinate> ring)
    {
        foreach (var c in ring)
        {
            if (c.HasZ || c.HasM) return false;
        }

        return true;
    }

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
