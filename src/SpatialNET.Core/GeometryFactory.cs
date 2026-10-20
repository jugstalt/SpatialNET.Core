namespace SpatialNET;

/// <summary>
/// Eine Teilfläche: ein Außenring mit den Löchern, die direkt in ihm liegen (ADR 0003).
/// </summary>
/// <param name="Shell">Der Außenring; geschlossen, gegen den Uhrzeigersinn.</param>
/// <param name="Holes">Die Löcher; geschlossen, im Uhrzeigersinn.</param>
public readonly record struct PolygonPart(
    ReadOnlyMemory<Coordinate> Shell,
    ReadOnlyMemory<ReadOnlyMemory<Coordinate>> Holes = default);

/// <summary>
/// Die Geometrie-Factory: erzeugt die Ergebnis-Geometrien eines Algorithmus aus Koordinaten
/// als Host-Geometrien (ADR 0004).
/// </summary>
/// <remarks>
/// <para>Vertrag für Implementierungen:</para>
/// <list type="bullet">
/// <item>Thread-sicher bzw. zustandslos – <see cref="GeometryFactory.Default"/> wird von allen Threads genutzt.</item>
/// <item>Übergebene Puffer werden kopiert und nach dem Aufruf nicht mehr referenziert.</item>
/// <item>Leere Eingaben (0 Punkte, Pfade bzw. Teilflächen) erzeugen leere Geometrien, nie <c>null</c>.</item>
/// <item>Z darf verworfen werden; bei <see cref="SupportsZ"/> = <c>false</c> liefern Algorithmen gar kein Z.</item>
/// </list>
/// <para>Der Parameter <c>source</c> ist die primäre Eingabe-Geometrie des Algorithmus (oder <c>null</c>);
/// daraus kann der Host Metadaten wie die Spatial Reference übernehmen.</para>
/// </remarks>
public interface IGeometryFactory
{
    /// <summary>Ob die erzeugten Geometrien Z tragen sollen.</summary>
    bool SupportsZ { get; }

    /// <summary>Erzeugt einen Punkt.</summary>
    /// <param name="coordinate">Die Koordinate.</param>
    /// <param name="source">Die primäre Eingabe-Geometrie oder <c>null</c>.</param>
    IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source);

    /// <summary>Erzeugt einen Multipunkt; ohne Koordinaten einen leeren.</summary>
    /// <param name="coordinates">Die Punkte.</param>
    /// <param name="source">Die primäre Eingabe-Geometrie oder <c>null</c>.</param>
    IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source);

    /// <summary>Erzeugt eine Polylinie; ohne Pfade eine leere.</summary>
    /// <param name="paths">Die Pfade als Koordinatenfolgen.</param>
    /// <param name="source">Die primäre Eingabe-Geometrie oder <c>null</c>.</param>
    IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source);

    /// <summary>Erzeugt ein Polygon aus Teilflächen; ohne Teilflächen ein leeres.</summary>
    /// <param name="parts">Die Teilflächen (gültig, geschlossen, außen gegen den Uhrzeigersinn).</param>
    /// <param name="source">Die primäre Eingabe-Geometrie oder <c>null</c>.</param>
    IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source);
}

/// <summary>Globaler Zugang zur Standard-<see cref="IGeometryFactory"/> (ADR 0004).</summary>
public static class GeometryFactory
{
    /// <summary>
    /// Die Factory, die Algorithmen verwenden, wenn ihr <c>factory</c>-Parameter <c>null</c> ist.
    /// Anfangs <c>null</c>; Hosts, Referenz-Implementierung und Playground setzen sie beim Start.
    /// </summary>
    public static IGeometryFactory? Default { get; set; }

    /// <summary>
    /// Liefert <paramref name="factory"/> oder, wenn <c>null</c>, <see cref="Default"/>.
    /// </summary>
    /// <param name="factory">Die explizit übergebene Factory oder <c>null</c>.</param>
    /// <exception cref="InvalidOperationException">Weder <paramref name="factory"/> noch <see cref="Default"/> ist gesetzt.</exception>
    public static IGeometryFactory Resolve(IGeometryFactory? factory) =>
        factory ?? Default ?? throw new InvalidOperationException(
            "Keine Geometrie-Factory: Weder wurde dem Algorithmus eine Factory übergeben, " +
            "noch ist SpatialNET.GeometryFactory.Default gesetzt.");
}
