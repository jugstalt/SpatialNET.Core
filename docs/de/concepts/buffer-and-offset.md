# Buffer und Offset-Kurven

`Buffer` vergrößert oder verkleinert eine Geometrie um eine Distanz. `OffsetCurve` liefert eine parallele Linie zu einer Polylinie oder zum Rand eines Polygons. Beide bauen auf dem Overlay-Kern auf ([Overlays, Füllregeln und das Gitter](overlay-and-grid.md)); Standardwerte und Parameter sind bewusst JTS-/NetTopologySuite-kompatibel, damit die Ergebnisse nah am Orakel bleiben, mit dem die Tests vergleichen ([ADR 0006](../../adr/0006-buffer-roh-offset-union-positive-offsetcurve-jts.de.md)).

## Buffer

```csharp
var line = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)");
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

// A buffer around a line with flat ends: a 10 x 4 rectangle.
Console.WriteLine(line.Buffer(2, new BufferOptions { CapStyle = CapStyle.Flat }).Area()); // 40

// Round corners (default) versus mitre corners.
Console.WriteLine(Math.Round(square.Buffer(1).Area(), 2));                                         // 143.12
Console.WriteLine(square.Buffer(1, new BufferOptions { JoinStyle = JoinStyle.Mitre }).Area());      // 144

// A negative distance shrinks a polygon.
Console.WriteLine(square.Buffer(-2).ToWkt()); // POLYGON ((2 2, 8 2, 8 8, 2 8, 2 2))
```

`Buffer(this IGeometry2D, double distance, BufferOptions? options = null, IGeometryFactory? factory = null, double? gridSize = null)` liefert immer ein `IPolygon2D`. Die Distanz gilt in Karteneinheiten; ein Buffer in geografischen Koordinaten (Grad) ist verzerrt, deshalb vorher projizieren.

### Wie er gebaut wird

Der Buffer entsteht aus dem **Roh-Offset** jedes Rings bzw. jeder Linie (mit Joins und Caps). Weil der Roh-Offset sich an konkaven Ecken und engen Stellen selbst überlappt, wird er mit einer einzigen `Union(FillRule.Positive)` im Overlay-Kern bereinigt. Eine eigene Trimmlogik für Selbstschnitte gibt es nicht.

### Optionen

| Option | Standard | Bedeutung |
|---|---|---|
| `JoinStyle` | `Round` | Ecken an der Außenseite: `Round` (Bogen), `Mitre` (verlängerte Kanten, begrenzt durch `MitreLimit`), `Bevel` (direkte Verbindung) |
| `CapStyle` | `Round` | Linienenden: `Round` (Halbkreis), `Flat` (am Endpunkt abgeschnitten), `Square` (um die Distanz verlängert) |
| `QuadrantSegments` | 8 | Segmente je Viertelkreis |
| `ArcTolerance` | `null` | Größte Sehnenabweichung der Bögen in Karteneinheiten; wenn gesetzt, hat sie Vorrang vor `QuadrantSegments` |
| `MitreLimit` | 5 | Größtes Verhältnis Spitzenabstand / Distanz; darüber wird die Spitze abgeschnitten |
| `SimplifyFactor` | 0,01 | Faktor (× \|Distanz\|) der Eingabe-Vereinfachung, die winzige Einbuchtungen entfernt; 0 = aus |
| `SingleSided` | `false` | Nur eine Seite von Linien puffern (positiv links, negativ rechts), flache Enden |

`BufferOptions` und `OffsetOptions` sind Records mit `init`-Properties und einer `Default`-Instanz.

### Negative Distanzen und Randfälle

- Eine negative Distanz bei einem Polygon puffert **nach innen**: Das Polygon wird um \|d\| verkleinert. Schmale Teile können verschwinden, oder das Polygon zerfällt in mehrere Teilflächen.
- Verschwindet ein Polygon vollständig, ist das Ergebnis ein leeres Polygon.
- Ein Punkt oder eine Linie mit Distanz ≤ 0 liefert ein leeres Polygon (außer `SingleSided`).
- Ein Polygon mit Distanz 0 liefert die per Even-Odd normalisierte Eingabe.
- Ungültige Eingaben werden per Even-Odd interpretiert, ohne Exception. Exceptions gibt es nur bei einer Distanz, die `NaN` oder unendlich ist, und bei ungültigen Optionen.
- Buffer-Ergebnisse sind 2D.

## Offset-Kurven

```csharp
// Offset curves: positive distance to the left, negative to the right of the direction of travel.
Console.WriteLine(line.OffsetCurve(2).ToWkt());  // LINESTRING (0 2, 10 2)
Console.WriteLine(line.OffsetCurve(-2).ToWkt()); // LINESTRING (0 -2, 10 -2)
```

`OffsetCurve` gibt es für `IPolyline2D` und `IPolygon2D`, das Ergebnis ist immer ein `IPolyline2D`.

- **Polylinien:** Eine positive Distanz liegt links in Laufrichtung, eine negative rechts.
- **Polygone:** Eine positive Distanz geht nach außen, eine negative nach innen, **unabhängig von der Ring-Orientierung**. Das Ergebnis ist der Rand von `Buffer(d)` als geschlossene Pfade.
- **Zusage:** Das Ergebnis liegt auf dem Rand des Buffers. Das folgt dem Ansatz von JTS `OffsetCurve`: ein Buffer mit flachen Enden, der Roh-Offset der gewünschten Seite und der Abgleich mit dem Buffer-Rand. Hat der Offset mehrere getrennte Abschnitte (Engstellen, Selbstschnitte der Eingabe), ist das Ergebnis eine Polylinie mit mehreren Pfaden.
- **`Joined`:** `OffsetOptions.Joined` verbindet die Teilstücke zu einem Pfad; dieser darf sich dann selbst schneiden.
- **Z und M:** Offset-Kurven interpolieren Z und M auf dem zugehörigen Quellsegment, wenn die Factory sie unterstützt. `Buffer` erzeugt beides nicht.

`OffsetOptions` hat dieselben Join-, Bogen-, Mitre- und Vereinfachungs-Optionen wie `BufferOptions`, dazu `Joined`.
