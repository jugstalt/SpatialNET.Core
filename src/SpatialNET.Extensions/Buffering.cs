using SpatialNET.Internal;
using SpatialNET.Internal.Buffer;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>Form der Ecken an der Außenseite (ADR 0006).</summary>
public enum JoinStyle
{
    /// <summary>Kreisbogen.</summary>
    Round,

    /// <summary>Spitze (Schnitt der Offset-Linien), begrenzt durch <see cref="BufferOptions.MitreLimit"/>.</summary>
    Mitre,

    /// <summary>Gerade Verbindung der Offset-Enden.</summary>
    Bevel,
}

/// <summary>Form der Linienenden (ADR 0006).</summary>
public enum CapStyle
{
    /// <summary>Halbkreis.</summary>
    Round,

    /// <summary>Gerade am Endpunkt abgeschnitten.</summary>
    Flat,

    /// <summary>Um die Distanz verlängert und gerade abgeschnitten.</summary>
    Square,
}

/// <summary>Optionen für <see cref="Buffering.Buffer"/>; Defaults wie JTS (ADR 0006).</summary>
public sealed record BufferOptions
{
    /// <summary>Die Standard-Optionen.</summary>
    public static BufferOptions Default { get; } = new();

    /// <summary>Ecken an der Außenseite; Standard <see cref="JoinStyle.Round"/>.</summary>
    public JoinStyle JoinStyle { get; init; } = JoinStyle.Round;

    /// <summary>Linienenden; Standard <see cref="CapStyle.Round"/>.</summary>
    public CapStyle CapStyle { get; init; } = CapStyle.Round;

    /// <summary>Segmente je Viertelkreis; Standard 8.</summary>
    public int QuadrantSegments { get; init; } = 8;

    /// <summary>Größte Sehnenabweichung der Bögen in Karteneinheiten; gesetzt hat sie Vorrang vor <see cref="QuadrantSegments"/>.</summary>
    public double? ArcTolerance { get; init; }

    /// <summary>Größtes Verhältnis Spitzenabstand / Distanz bei <see cref="JoinStyle.Mitre"/>; Standard 5.</summary>
    public double MitreLimit { get; init; } = 5;

    /// <summary>Faktor der Eingabe-Vereinfachung (· |Distanz|); Standard 0,01, 0 = aus.</summary>
    public double SimplifyFactor { get; init; } = 0.01;

    /// <summary>Nur eine Seite von Linien puffern (positiv links, negativ rechts), flache Enden.</summary>
    public bool SingleSided { get; init; }
}

/// <summary>Buffer als Roh-Offset + Union mit <see cref="FillRule.Positive"/> (ADR 0006).</summary>
public static class Buffering
{
    /// <summary>
    /// Der Buffer: alle Punkte im Abstand ≤ |<paramref name="distance"/>| (bzw. für negative Distanz die um |d| nach
    /// innen verkleinerte Fläche). Ergebnisse sind 2D und gültig.
    /// </summary>
    /// <remarks>
    /// Polygon mit d = 0 → per Even-Odd normalisierte Eingabe; vollständig erodiert → leer; Punkt/Linie mit d ≤ 0 →
    /// leer (außer einseitig). Ungültige Eingaben werden nach Even-Odd interpretiert.
    /// </remarks>
    /// <param name="geometry">Die Geometrie.</param>
    /// <param name="distance">Die Distanz in Karteneinheiten.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="BufferOptions.Default"/>.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> ist <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Distanz nicht endlich oder ungültige Optionen.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> ist keine bekannte Geometrie-Art.</exception>
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

    /// <summary>Die Teilflächen des Buffers (außen gegen den Uhrzeigersinn) – auch für OffsetCurve.</summary>
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

    /// <summary>Ob ein Ring bei Abstand <paramref name="r"/> vollständig erodiert (Envelope schmaler als 2r, wie JTS).</summary>
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
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "Die Distanz muss endlich sein.");
        if (options.QuadrantSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.QuadrantSegments, "QuadrantSegments muss ≥ 1 sein.");
        if (!(options.MitreLimit > 0) || !double.IsFinite(options.MitreLimit))
            throw new ArgumentOutOfRangeException(nameof(options), options.MitreLimit, "MitreLimit muss endlich und > 0 sein.");
        if (!(options.SimplifyFactor >= 0) || !double.IsFinite(options.SimplifyFactor))
            throw new ArgumentOutOfRangeException(nameof(options), options.SimplifyFactor, "SimplifyFactor muss endlich und ≥ 0 sein.");
        if (options.ArcTolerance is { } tol && (!(tol > 0) || !double.IsFinite(tol)))
            throw new ArgumentOutOfRangeException(nameof(options), tol, "ArcTolerance muss endlich und > 0 sein.");
    }
}
