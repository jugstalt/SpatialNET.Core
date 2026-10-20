# Buffer als Roh-Offset + Union(Positive), OffsetCurve nach JTS-Muster

Der Buffer entsteht aus dem Roh-Offset jedes Rings bzw. jeder Linie (mit Joins und Caps), der anschließend mit `Union(FillRule.Positive)` im eigenen Overlay (ADR 0005) bereinigt wird. Die Offset-Kurve folgt dem Muster von JTS `OffsetCurve`: Buffer mit flachen Enden, Roh-Offset der gewünschten Seite, Abgleich mit dem Buffer-Rand. Es gibt keine eigene Trimmlogik für Selbstschnitte. So gibt es im Produkt nur einen Codepfad, der auf dem Overlay-Kern aufsetzt, und das Verhalten bleibt eng am NTS-Orakel. Defaults und Parameter sind deshalb bewusst JTS-kompatibel.

## Consequences

- **API:** `Buffer(this IGeometry2D, double distance, BufferOptions? options = null, IGeometryFactory? factory = null, double? gridSize = null)` gibt immer `IPolygon2D` zurück. `OffsetCurve` gibt es auf `IPolyline2D` und `IPolygon2D` mit `OffsetOptions`, Rückgabe immer `IPolyline2D`. Die Optionen sind records mit `init`-Properties und `static Default`. Die Join-Parameter teilen beide.
- **Defaults (wie JTS):** Join `Round`, Cap `Round`, `MitreLimit = 5`, `QuadrantSegments = 8`, Input-Simplifier 0,01·|d|, `SingleSided` optional. Eine optionale `ArcTolerance` (max. Sehnenabweichung in Karteneinheiten) hat Vorrang vor `QuadrantSegments`, wenn sie gesetzt ist.
- **Offset-Richtung:** Bei Polylinien bedeutet positiv links in Laufrichtung. Bei Polygonen bedeutet positiv nach außen und negativ nach innen, **unabhängig von der Ring-Orientierung** (die laut ADR 0003 ignoriert wird). Das Ergebnis ist der Rand von `Buffer(d)` als geschlossene Pfade. Ein optionaler Joined-Modus verbindet Teilstücke zu einem Pfad.
- **Negative Distanz** bei Polygonen bedeutet Pufferung **nach innen**: Das Polygon wird um |d| verkleinert. Schmale Teile können dabei verschwinden oder es zerfällt in mehrere Teilflächen.
- **Randfälle:**
  - Polygon verschwindet beim negativen Buffer vollständig → leeres Polygon
  - Linie oder Punkt mit d ≤ 0 → leeres Polygon
  - Polygon mit d = 0 → per Even-Odd normalisierte Eingabe
  - Ungültige Eingaben werden per Even-Odd interpretiert, ohne Exception
  - Exceptions nur bei `NaN`/`Infinity` als Distanz oder ungültigen Optionen
- **Z:** Buffer-Ergebnisse sind 2D. Offset-Kurven interpolieren Z auf dem jeweils zugehörigen Quellsegment (sofern `SupportsZ`).
- Die **Kapsel-Union** (je Segment eine Kapsel, alles vereinigt) existiert nur im Testprojekt als zweiter Referenzpfad neben NTS.

## Considered Options

- Kapsel-Union als interner Fallback (verworfen: Snap-Rounding wirft keine Topologie-Ausnahmen, ADR 0005)
- JTS-`BufferBuilder` mit Tiefen-Labeling (bleibt allenfalls Optimierung, kein eigenes Subsystem)
- Minkowski-Summe, Straight Skeleton (verworfen für Buffer)
- Optionen als einzelne optionale Argumente (verworfen: lange Signaturen, jede neue Option bricht)

Recherche: #12 (`research/buffer-offset`).
