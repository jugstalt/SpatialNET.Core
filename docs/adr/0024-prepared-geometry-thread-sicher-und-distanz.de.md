# PreparedGeometry: thread-sicher, mit Distanz

`PreparedGeometry` (ADR 0013) ist jetzt **nach dem Bauen thread-sicher** und beantwortet **Distanzfragen**: `Distance`, `IsWithinDistance` und `NearestPoints`. Die Thread-Sicherheit kommt daher, dass der interne Gitterindex über die Segmente durch den unveränderlichen `SpatialIndex<int>` (ADR 0014) ersetzt wurde.

*English version: [0024-prepared-geometry-thread-sicher-und-distanz.md](0024-prepared-geometry-thread-sicher-und-distanz.md)*

## Consequences

- **Thread-Sicherheit:** Eine `PreparedGeometry` hält nur Daten, die nach dem Konstruktor nie mehr verändert werden (den Schnappschuss der Koordinaten und den unveränderlichen `SpatialIndex<int>` über die Envelopes ihrer Segmente); jede Abfrage nutzt lokalen Zustand. Beliebig viele Threads dürfen dieselbe Instanz abfragen, eine vorbereitete Geometrie lässt sich also einmal bauen und teilen, etwa in einem Dienst. Der frühere Hinweis „eine Instanz je Thread“ entfällt. Der alte interne Gitterindex `SegmentIndex` ist entfernt.
- **Gemessen (Einmallauf im Stil von BenchmarkDotNet, `--quick-prepared`, sternförmiges Polygon, 2.000 Punktabfragen und 500 kurze Prüflinien):**

  | | 1.000 Stützpunkte, Gitter | 1.000 Stützpunkte, Baum | 100.000 Stützpunkte, Gitter | 100.000 Stützpunkte, Baum |
  |---|---|---|---|---|
  | Prepare | 0,39 ms | 0,9–1,4 ms | 102 ms | 68–82 ms |
  | 2.000 `Locate` | 2,25 ms | 3,3 ms | 105 ms | 100–105 ms |
  | 500 `Intersects` | 1,05 ms | 1,1–1,4 ms | 25 ms | 22–27 ms |

  Bei kleinen Polygonen ist der Baum langsamer (Vorbereiten etwa zwei- bis dreimal, `Locate` etwa 45 %, `Intersects` bis 30 %), absolut eine Millisekunde oder weniger je Polygon. Bei Polygonen mit vielen Stützpunkten, wo sich das Vorbereiten lohnt, ist er gleich schnell oder schneller. Beide liegen weit vor den unvorbereiteten Prädikaten, die für dieselben 500 Prüflinien bei 100.000 Stützpunkten 35 bis 60 Sekunden brauchen. Der schlechtere kleine Fall wird für die Thread-Sicherheit in Kauf genommen.
- **`Distance(IGeometry2D other)`** liefert den kleinsten Abstand, `0`, wenn sich die Geometrien treffen (entschieden über den exakten `Intersects`-Pfad), und `double.NaN`, wenn eine leer ist.
- **`IsWithinDistance(IGeometry2D other, double distance)`** ist `Distance <= distance` mit vorzeitigem Abbruch: Zuerst schließt der Abstand der Envelopes ferne Geometrien aus, und die Suche endet beim ersten Segmentpaar innerhalb des Abstands. Ein negativer oder nicht endlicher `distance` wirft `ArgumentOutOfRangeException` wie `Measures.IsWithinDistance` (ADR 0012, ADR 0019); leer ergibt `false`.
- **`NearestPoints(IGeometry2D other)`** liefert `(Coordinate OnPrepared, Coordinate OnOther)?`, `null` wenn eine leer ist. Treffen sich die Geometrien, kommt auf beiden Seiten derselbe Punkt, wie bei `NearestPoints` (ADR 0019). Bei getrennten Geometrien wird für jeden Stützpunkt von `other` über `SpatialIndex<int>.Nearest` das nächste Segment der vorbereiteten gesucht, es kommen die Stützpunkte der vorbereiteten Geometrie innerhalb des besten Abstands hinzu, und das endgültige Paar wird aus den Segmenten berechnet. Bei mehreren gleich nahen Paaren kann das Paar von der unvorbereiteten Methode abweichen; der Abstand ist derselbe.
- **Nicht vorbereitet:** `Intersection`, `Clip` und die übrigen Overlays. Sie laufen über den Overlay-Kern, den ein Segmentindex nicht einfach beschleunigt; `Clip` auf ein Envelope ist ohnehin billig. `Locate` (ADR 0022) bleibt.
- **Korrektur nebenbei:** Ein Punkt außerhalb des Envelopes eines vorbereiteten Polygons ist ohne Abfrage `Outside` (vorher konnte ein ungültiges Abfrage-Envelope entstehen); der Schnappschuss behält das Z eines Punkts, wie es angegeben ist, statt `0` zu erfinden, sodass `NearestPoints` mit dem unvorbereiteten Ergebnis übereinstimmt.
- **Tests (ADR 0008):** Ein Property-Test vergleicht `Distance`, `NearestPoints` (die Punkte liegen auf den Geometrien und verwirklichen den Abstand) und `IsWithinDistance` mit den unvorbereiteten Methoden für zufällige Polygone, Linien und Multipunkte; Fälle für Löcher, sich berührende und schneidende Geometrien, leere und ungültige Argumente; ein Test und ein Stresstest lassen 16 Threads gegen eine Instanz laufen und vergleichen mit den sequentiellen Antworten. Alle vorhandenen Prädikat-Tests laufen unverändert gegen den Baum.
- **Benchmarks:** `PreparedBenchmarks` (Vorbereiten, `Locate`, `Intersects` vorbereitet und unvorbereitet, 1.000 und 100.000 Stützpunkte) ersetzen `SegmentIndexBenchmarks`.

## Considered Options

- Gitterindex behalten und dokumentieren, dass `PreparedGeometry` nicht thread-sicher ist (verworfen: Der übliche Einsatz ist ein gemeinsames, langlebiges Objekt; eine Instanz je Thread verschwendet Speicher und ist leicht falsch zu machen)
- Gitterindex behalten und per Sperre thread-sicher machen (verworfen: serialisiert genau die Abfragen, die parallel laufen sollen)
- Segmentindex durch `SpatialIndex<int>` ersetzen (gewählt: Der Baum ist unveränderlich und schon getestet; er ist bei kleinen Polygonen langsamer, das wird in Kauf genommen)
- Vorbereitetes `Intersection` und `Clip` (verworfen: keine einfache Beschleunigung, siehe oben)
- Ein eigener Typ `PreparedDistance` (verworfen: Ein Objekt je vorbereiteter Geometrie ist einfacher zu nutzen)
