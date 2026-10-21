# Mess- und Punkt-Helfer: nächste Punkte, Abstandstest, Punkt auf der Fläche, Rand, Verdichten, Umkehren

Sechs kleine Helfer schließen Lücken, auf die Hosts täglich stoßen und die die vorhandenen Algorithmen nicht abdecken: **`NearestPoints`** (die zwei Punkte, die `Distance` bilden), **`IsWithinDistance`** (ein schneller Ja/Nein-Abstandstest), **`PointOnSurface`** (ein Punkt, der auf der Geometrie liegt, für Beschriftungen), **`Boundary`**, **`Densify`** und **`Reverse`**. Sie folgen den schon festgelegten Regeln: punktförmige und skalare Ergebnisse sind **Werte** ohne Factory (ADR 0012), geometrische Ergebnisse kommen aus der Factory und werden auf deren Fähigkeiten beschränkt (ADR 0004, ADR 0015), leere und ungültige Eingaben werfen nie, und ungültige Zahlenparameter werfen `ArgumentOutOfRangeException`. Sie bringen kein neues Konzept mit, deshalb teilen sie sich einen ADR.

*English version: [0019-mess-und-punkt-helfer-werte-statt-geometrien.md](0019-mess-und-punkt-helfer-werte-statt-geometrien.md)*

## Consequences

- **Ort:** `NearestPoints`, `IsWithinDistance` und `PointOnSurface` kommen zu `Measures` (Abstands- und Punktwerte, wie `Distance` und `Centroid`). `Boundary`, `Densify` und `Reverse` stehen in einer neuen statischen Klasse `Constructions` (geometrische Ergebnisse über die Factory). `Measures` wird `partial`. Beide liegen in `SpatialNET.Extensions`, Namespace `SpatialNET`; für die Package Validation ist das nur eine Ergänzung.
- **`NearestPoints(IGeometry2D a, IGeometry2D b)`** liefert `(Coordinate OnA, Coordinate OnB)?`:
  - `null`, wenn eine Geometrie leer ist (wie `Distance` dann `NaN` ist, ADR 0012); der Abstand der beiden Punkte ist `Distance(a, b)`.
  - Schneiden sich die Geometrien, oder liegt eine in der Fläche der anderen, ist der Punkt auf beiden Seiten derselbe (ein Schnittpunkt oder ein Punkt der inneren Geometrie, der in der Fläche liegt).
  - Unter mehreren gleich nahen Paaren gewinnt das **erste** in Eingabereihenfolge, das Ergebnis ist also deterministisch.
  - Z und M der beiden Punkte werden **auf ihrem Segment interpoliert** nach den Regeln aus ADR 0015 und ADR 0017 (nur wenn beide Enden sie tragen, an einem Stützpunkt dessen eigene Werte). Die Koordinaten einer Kreuzung zweier Segmente werden in Gleitkomma gerechnet, beide Seiten können sich daher um Rundung unterscheiden.
  - Es gibt keine `ShortestLine`; wer die Linie braucht, baut sie mit `factory.CreatePolyline` in einer Zeile.
- **`IsWithinDistance(IGeometry2D a, IGeometry2D b, double distance)`** liefert `bool`: Der Abstand ist höchstens `distance`; `distance = 0` heißt, die Geometrien berühren oder überlappen sich. Leere Geometrien ergeben `false`. Ein negatives, `NaN` oder unendliches `distance` wirft `ArgumentOutOfRangeException`. Der Test bricht beim ersten Segmentpaar innerhalb des Abstands ab und beginnt mit einer Envelope-Prüfung, bei großen Eingaben ist er also schneller als der Vergleich mit `Distance`.
- **`PointOnSurface(IGeometry2D)`** liefert `Coordinate?` ohne Z und M (wie `Centroid`), `null` bei einer leeren Geometrie. Der Punkt liegt **auf der Geometrie**:
  - **Polygon:** ein innerer Punkt aus dem breitesten inneren Abschnitt einer waagerechten Linie durch die Envelope, zwischen den beiden Stützpunkthöhen, die ihrer Mitte am nächsten liegen (das Verfahren von JTS `InteriorPointArea`); Löcher und Teilflächen behandelt Even-Odd über alle Ringe. Ein Polygon ohne Fläche fällt auf einen Stützpunkt seiner Ringe zurück.
  - **Polylinie:** der innere Stützpunkt (kein Pfadende), der dem Schwerpunkt am nächsten liegt; gibt es keinen, der Punkt bei der halben Länge entlang der Linie.
  - **Punkt und Multipunkt:** der Punkt, der dem Schwerpunkt am nächsten liegt, bei Gleichstand der erste.
  - Das Ergebnis ist deterministisch. Es ist nicht in jedem Fall derselbe Punkt wie bei JTS oder NTS, deshalb prüft das Orakel die Eigenschaft (auf der Geometrie, im Inneren, wo eine Fläche eines hat), nicht Gleichheit.
- **`Boundary`:**
  - `Boundary(IPolygon2D)` liefert `IPolyline2D`: die Ringe, normalisiert (Außenringe gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn, entartete Ringe entfallen, ADR 0003), ein Pfad je Ring.
  - `Boundary(IPolyline2D)` liefert `IMultiPoint2D`: die Endpunkte nach der **Mod-2-Regel** aus ADR 0013 (ein Endpunkt, der ungerade oft vorkommt; geschlossene Pfade haben keinen Rand).
  - Punkt und Multipunkt haben einen leeren Rand, geliefert als leerer `IMultiPoint2D`. Eine allgemeine Überladung auf `IGeometry2D` liefert `IGeometry2D`.
  - Z und M der Ringe und Endpunkte bleiben erhalten, beschränkt auf die Factory.
- **`Densify(maxSegmentLength)`** für `IPolyline2D`, `IPolygon2D` und eine allgemeine Überladung auf `IGeometry2D` (Punkte werden unverändert kopiert):
  - Ein Segment, das länger als `maxSegmentLength` ist, wird in `ceil(Länge / maxSegmentLength)` gleich lange Stücke geteilt; kürzere Segmente bleiben unberührt. Die Form ändert sich nicht, ursprüngliche Stützpunkte bleiben erhalten. Das ist die Regel des NTS-`Densifier`.
  - Z und M der neuen Stützpunkte werden interpoliert (beide Enden nötig, ADR 0015). Polygonringe werden nach der Normalisierung aus ADR 0003 verdichtet, Außenringe bleiben also gegen den Uhrzeigersinn und das Polygon bleibt gültig.
  - `maxSegmentLength` muss endlich und größer als 0 sein, sonst `ArgumentOutOfRangeException`. Ein so kleiner Wert, dass das Ergebnis mehr als 50 Millionen Stützpunkte hätte, wirft ebenfalls `ArgumentOutOfRangeException`, statt den Speicher zu erschöpfen.
- **`Reverse(IPolyline2D)`:** kehrt jeden Pfad und die Reihenfolge der Pfade um, das Ergebnis ist also dieselbe Linie rückwärts durchlaufen. Ein `Reverse` für Polygone gibt es nicht: Ihre Orientierung ist normalisiert (ADR 0003), Umkehren hätte keine Wirkung.
- **Exceptions (ADR 0012):** `ArgumentNullException` bei `null`, `NotSupportedException` bei unbekannten Arten, `InvalidOperationException` ohne Factory (bei den geometrischen Ergebnissen). Leere Geometrien antworten wie oben mit `null`, `false` oder einer leeren Geometrie.
- **Tests (ADR 0008):**
  - Fixtures mit NTS-Orakel für `nearestPoints` (`DistanceOp.NearestPoints`: der Abstand des gelieferten Paares gleicht dem Soll-Abstand, und jeder Punkt liegt auf seiner Geometrie; die Punkte selbst dürfen sich bei Gleichstand unterscheiden), `isWithinDistance` (`IsWithinDistance`), `pointOnSurface` (die Eigenschaftsprüfung oben), `boundary` (`Boundary`) und `densify` (`Densifier`). Sie übernehmen die Eingaben der vorhandenen Fixtures von `distance` und `centroid`, der Pflicht-Tag-Katalog ist damit abgedeckt, mit begründeten Ausnahmen in `_catalog.json`.
  - `Reverse` ist durch `[Covers]` und handgeschriebene Tests gegen NTS `Reverse` abgedeckt.
  - Property-Tests (CsCheck): `Distance` gleicht dem Abstand der `NearestPoints`; `IsWithinDistance(d)` stimmt mit `Distance <= d` überein; zweimal `Reverse` gibt die Linie zurück; `Densify` behält die Länge und jeden ursprünglichen Stützpunkt und begrenzt die Segmentlänge; der Rand eines Polygons hat die Länge seines Umfangs; der Punkt auf einem Polygon liegt darin.
- **Playground und Dokumentation:** Deskriptoren für alle sechs Methoden (ADR 0011), ein Abschnitt in der Konzeptseite zu Maßen und Konstruktionen, Glossareinträge.

## Considered Options

- `NearestPoints` liefert eine Linie `ShortestLine` (verworfen: Ein Wert braucht keine Factory, und der Aufrufer baut die Linie in einer Zeile; das Tupel kann außerdem „keine nächsten Punkte“ als `null` ausdrücken)
- `IsWithinDistance` als `Distance(a, b) <= d` (verworfen: Das rechnet jedes Segmentpaar; der eigene Test bricht früh ab und beginnt mit den Envelopes)
- Den inneren Punkt wie JTS `InteriorPoint` nennen (verworfen: `PointOnSurface` ist der OGC-Name; „interior point“ ist ein Glossar-Alias)
- `PointOnSurface` liefert einen Punkt mit Z und M (verworfen: Der Punkt wird berechnet und ist im Allgemeinen kein ursprünglicher Stützpunkt; auch `Centroid` liefert eine einfache `Coordinate`)
- Immer den JTS-Punkt liefern (verworfen: Eine genaue Kopie der JTS-Wahl würde das Projekt an dessen Interna binden; die Eigenschaft ist, was Aufrufer brauchen)
- Rand eines Punktes als leeres Polygon oder leere Collection (verworfen: Es gibt keine Geometry-Collection (ADR 0002); ein leerer Multipunkt ist der nächste Typ niedrigerer Dimension)
- Rand eines Polygons als Multipunkt der Stützpunkte (verworfen: Das ist nicht der Rand)
- `Densify` mit fester Schrittweite statt gleicher Stücke (verworfen: Es lässt ein kurzes letztes Stück übrig; gleiche Stücke entsprechen NTS und halten die Segmente gleichmäßig)
- Ein `Reverse` für Polygone (verworfen: Nach der Normalisierung ohne Wirkung)
- Sechs ADRs, einer je Methode (verworfen: Sie führen kein eigenes Konzept ein; die Regeln sind die aus ADR 0004, 0012, 0013, 0015)
