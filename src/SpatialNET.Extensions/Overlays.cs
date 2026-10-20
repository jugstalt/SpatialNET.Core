using SpatialNET.Internal.Overlay;

namespace SpatialNET;

/// <summary>
/// Overlay-Operationen (ADR 0005/0012): Snap-Rounding auf die Gitterweite (<c>gridSize</c> →
/// <see cref="Precision.DefaultGridSize"/> → automatisch), Windungszahlen mit wählbarer <see cref="FillRule"/>,
/// Strict-Semantik (Ergebnisse dimensionshomogen, Berührungs- und Kollapsreste fallen weg). Ergebnisse sind
/// gültig, Ringe geschlossen, Außenringe gegen den Uhrzeigersinn; Z an neuen Knoten wird auf dem
/// Originalsegment interpoliert, bei Kreuzungen gilt das Z des ersten Operanden.
/// </summary>
/// <remarks>
/// Typisierte Overloads liefern den genauen Ergebnistyp; die allgemeine <see cref="IGeometry2D"/>-Overload
/// verzweigt zur Laufzeit. Ist der erste Operand ein Punkt bzw. Multipunkt, ist das Ergebnis ein Multipunkt.
/// Union und SymDifference gibt es nur für gleiche Dimension.
/// </remarks>
public static class Overlays
{
    // ---------- Intersection ----------

    /// <summary>Schnittfläche zweier Polygone.</summary>
    /// <param name="a">Das erste Polygon.</param>
    /// <param name="b">Das zweite Polygon.</param>
    /// <param name="fillRule">Die Füllregel für beide Operanden; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolygon2D Intersection(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Die Teile der Linie im Inneren und auf dem Rand des Polygons.</summary>
    /// <param name="a">Die Polylinie.</param>
    /// <param name="b">Das Polygon.</param>
    /// <param name="fillRule">Die Füllregel des Polygons; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Intersection(this IPolyline2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Die Teile der Linie im Inneren und auf dem Rand des Polygons.</summary>
    /// <param name="a">Das Polygon.</param>
    /// <param name="b">Die Polylinie.</param>
    /// <param name="fillRule">Die Füllregel des Polygons; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Intersection(this IPolygon2D a, IPolyline2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Gemeinsame Linienteile (Überlappungen); reine Kreuzungspunkte fallen nach Strict weg.</summary>
    /// <param name="a">Die erste Polylinie.</param>
    /// <param name="b">Die zweite Polylinie.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Intersection(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Die Punkte, die in <paramref name="b"/> (Inneres oder Rand) liegen.</summary>
    /// <param name="a">Der Multipunkt.</param>
    /// <param name="b">Die andere Geometrie.</param>
    /// <param name="fillRule">Die Füllregel, falls <paramref name="b"/> ein Polygon ist.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D Intersection(this IMultiPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Der Punkt, wenn er in <paramref name="b"/> (Inneres oder Rand) liegt, sonst ein leerer Multipunkt.</summary>
    /// <param name="a">Der Punkt.</param>
    /// <param name="b">Die andere Geometrie.</param>
    /// <param name="fillRule">Die Füllregel, falls <paramref name="b"/> ein Polygon ist.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D Intersection(this IPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    /// <summary>Schnitt beliebiger Geometrien; Ergebnistyp nach Strict-Semantik (kleinste Dimension).</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die zweite Geometrie.</param>
    /// <param name="fillRule">Die Füllregel für Polygone; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IGeometry2D Intersection(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Intersection, a, b, fillRule, factory, gridSize);

    // ---------- Union ----------

    /// <summary>Vereinigung zweier Polygone; mit <see cref="FillRule.NonZero"/> verschmelzen z. B. überlappende Außenringe.</summary>
    /// <param name="a">Das erste Polygon.</param>
    /// <param name="b">Das zweite Polygon.</param>
    /// <param name="fillRule">Die Füllregel für beide Operanden; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolygon2D Union(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Union, a, b, fillRule, factory, gridSize);

    /// <summary>Vereinigung zweier Polylinien, genodet und zu maximalen Pfaden verbunden.</summary>
    /// <param name="a">Die erste Polylinie.</param>
    /// <param name="b">Die zweite Polylinie.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Union(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Union, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Vereinigung zweier Multipunkte.</summary>
    /// <param name="a">Der erste Multipunkt.</param>
    /// <param name="b">Der zweite Multipunkt.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D Union(this IMultiPoint2D a, IMultiPoint2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Union, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Vereinigung gleichdimensionaler Geometrien.</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die zweite Geometrie.</param>
    /// <param name="fillRule">Die Füllregel für Polygone; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="NotSupportedException">Die Dimensionen sind verschieden.</exception>
    public static IGeometry2D Union(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Union, a, b, fillRule, factory, gridSize);

    // ---------- Difference ----------

    /// <summary>Das erste Polygon ohne die Fläche des zweiten.</summary>
    /// <param name="a">Das Polygon.</param>
    /// <param name="b">Das abzuziehende Polygon.</param>
    /// <param name="fillRule">Die Füllregel für beide Operanden; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolygon2D Difference(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Das Polygon (normalisiert); Linien haben keine Fläche, die abgezogen werden könnte.</summary>
    /// <param name="a">Das Polygon.</param>
    /// <param name="b">Die Polylinie.</param>
    /// <param name="fillRule">Die Füllregel des Polygons; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolygon2D Difference(this IPolygon2D a, IPolyline2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Die Teile der Linie außerhalb des Polygons (Randteile fallen weg).</summary>
    /// <param name="a">Die Polylinie.</param>
    /// <param name="b">Das Polygon.</param>
    /// <param name="fillRule">Die Füllregel des Polygons; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Difference(this IPolyline2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Die Teile der ersten Linie, die nicht auf der zweiten liegen.</summary>
    /// <param name="a">Die Polylinie.</param>
    /// <param name="b">Die abzuziehende Polylinie.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D Difference(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.Difference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Die Punkte, die nicht in <paramref name="b"/> (Inneres oder Rand) liegen.</summary>
    /// <param name="a">Der Multipunkt.</param>
    /// <param name="b">Die andere Geometrie.</param>
    /// <param name="fillRule">Die Füllregel, falls <paramref name="b"/> ein Polygon ist.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D Difference(this IMultiPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Der Punkt, wenn er nicht in <paramref name="b"/> liegt, sonst ein leerer Multipunkt.</summary>
    /// <param name="a">Der Punkt.</param>
    /// <param name="b">Die andere Geometrie.</param>
    /// <param name="fillRule">Die Füllregel, falls <paramref name="b"/> ein Polygon ist.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D Difference(this IPoint2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    /// <summary>Differenz beliebiger Geometrien; das Ergebnis hat den Typ von <paramref name="a"/>.</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die abzuziehende Geometrie.</param>
    /// <param name="fillRule">Die Füllregel für Polygone; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IGeometry2D Difference(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.Difference, a, b, fillRule, factory, gridSize);

    // ---------- SymDifference ----------

    /// <summary>Die Flächen, die zu genau einem der Polygone gehören.</summary>
    /// <param name="a">Das erste Polygon.</param>
    /// <param name="b">Das zweite Polygon.</param>
    /// <param name="fillRule">Die Füllregel für beide Operanden; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolygon2D SymDifference(this IPolygon2D a, IPolygon2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolygon2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, fillRule, factory, gridSize);

    /// <summary>Die Linienteile, die zu genau einer der Polylinien gehören.</summary>
    /// <param name="a">Die erste Polylinie.</param>
    /// <param name="b">Die zweite Polylinie.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IPolyline2D SymDifference(this IPolyline2D a, IPolyline2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IPolyline2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Die Punkte, die zu genau einem der Multipunkte gehören.</summary>
    /// <param name="a">Der erste Multipunkt.</param>
    /// <param name="b">Der zweite Multipunkt.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    public static IMultiPoint2D SymDifference(this IMultiPoint2D a, IMultiPoint2D b, IGeometryFactory? factory = null, double? gridSize = null) =>
        (IMultiPoint2D)OverlayRunner.Run(OverlayOp.SymDifference, a, b, FillRule.EvenOdd, factory, gridSize);

    /// <summary>Symmetrische Differenz gleichdimensionaler Geometrien.</summary>
    /// <param name="a">Die erste Geometrie.</param>
    /// <param name="b">Die zweite Geometrie.</param>
    /// <param name="fillRule">Die Füllregel für Polygone; Standard EvenOdd.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="gridSize">Die Gitterweite; <c>null</c> = Kaskade.</param>
    /// <exception cref="NotSupportedException">Die Dimensionen sind verschieden.</exception>
    public static IGeometry2D SymDifference(this IGeometry2D a, IGeometry2D b, FillRule fillRule = FillRule.EvenOdd, IGeometryFactory? factory = null, double? gridSize = null) =>
        OverlayRunner.Run(OverlayOp.SymDifference, a, b, fillRule, factory, gridSize);
}
