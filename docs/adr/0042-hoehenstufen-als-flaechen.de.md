# Höhenstufen als Flächen

Höhenlinien (ADR 0037) zeichnen, wo eine Fläche eine bestimmte Höhe hat. Eine Karte, eine Hochwasseranalyse oder ein Nutzungsplan fragt oft nach den **Flächen**: alles zwischen 100 und 120 m, alles unter dem Hochwasserstand, die Zonen einer Höhenkarte in Farben. `ContourBands` liefert sie als Polygone, eines je Stufe, exakt an den Höhenlinien geschnitten.

*English version: [0042-hoehenstufen-als-flaechen.md](0042-hoehenstufen-als-flaechen.md)*

## Consequences

- **Ort:** ein Mitglied von `Triangulation` (ADR 0037) in einer eigenen Datei, `Triangulation.Bands.cs` in `SpatialNET.Extensions`. Für die Paketvalidierung und die API-Verfolgung ist das nur eine Ergänzung (ADR 0028).
- **API:**

  ```csharp
  IReadOnlyList<IPolygon2D> ContourBands(IReadOnlyList<double> levels, IGeometryFactory? factory = null)
  ```

  Die Höhen müssen endlich und streng aufsteigend sein. Das Ergebnis hat **ein Polygon mehr, als es Höhen gibt**: Stufe 0 ist alles unter der ersten Höhe, Stufe `i` von Höhe `i − 1` bis (ohne) Höhe `i`, die letzte alles ab der letzten Höhe, wie bei `SlopeZones` (ADR 0041). Eine Stufe ohne Fläche ist ein leeres Polygon, ohne Höhen ist die eine Stufe die ganze Fläche. Dreiecke mit einem Eckpunkt ohne Z gehören zu keiner Stufe. Die Ecken auf einer Höhenlinie haben die Höhe als Z, die anderen behalten ihres.
- **Die Stufen überdecken die Fläche und passen exakt.** Über einem Dreieck ist die Fläche eine Ebene, also ist eine Stufe ein konvexes Stück des Dreiecks:
  - Jedes Dreieck wird mit jeder Höhe geschnitten, die es überspannt (Sutherland–Hodgman gegen die Höhe der Ecken), immer vom **ursprünglichen Dreieck** aus, sodass die Schnittpunkte auf seinen Kanten aus der ganzen Kante kommen.
  - Der Schnittpunkt auf einer Kante wird **aus den beiden Eckpunkten der Kante in fester Reihenfolge** berechnet (zuerst der mit der kleineren Id), sodass die beiden Dreiecke, die die Kante teilen, bis auf das letzte Bit denselben Punkt bekommen und die Stücke ohne Lücke und Überlappung zusammenpassen. Eine Ecke, die genau auf der Höhe liegt, ist der Punkt selbst und nicht ein Punkt, der einen Rundungsfehler daneben liegt.
  - Ein Dreieck, das **genau auf der Höhe** einer Höhenlinie eben ist, gehört zur Stufe darüber, und ein Stück, das exakt entartet ist (alle Ecken auf einer Linie), wird verworfen.
  - Die Stücke einer Stufe werden nicht mit dem Overlay vereinigt. Die Kanten, die zwei Stücke in entgegengesetzter Richtung haben, liegen innerhalb der Stufe und heben sich auf; was übrig bleibt, ist die Grenze, die mit der Maschinerie von `SlopeZones` (ADR 0041) zu Ringen verkettet wird: Ringe im Uhrzeigersinn sind Löcher und gehören zum kleinsten Ring, der sie enthält, und ein Ring, der sich in einem Eckpunkt selbst berührt, wird dort aufgetrennt.
  - **Splitter.** Wo ein Dreieck ein Splitter ist (ein Winkel unter einem Millionstel Grad: drei Ecken, die bis auf einen Rundungsfehler auf einer Linie liegen), kann ein Loch seine Schale um diesen Fehler überlappen, und die Struktur eines Polygons wird aus der Lage seiner Ringe gelesen, das Polygon würde also falsch gelesen. Eine Triangulierung, die so ein Dreieck hat, prüft jede Stufe und macht eine ungültige mit `MakeValid` (ADR 0020) gültig, was einen Durchlauf über jedes Polygon kostet; eine Triangulierung ohne eines zahlt das nicht.
- **Geschwindigkeit.** Einmalmessungen (`--quick-terrain`, Release-Build): sieben Höhen auf 10.000 Punkten brauchen etwa 0,15 s, auf 50.000 Punkten etwa 0,4 s.
- **Tests (ADR 0008):** eine Rampe in Streifen, ein ebenes Dreieck auf einer Höhe, eine Pyramide, deren Stufe ein Loch hat, die Fläche jeder Stufe einer Ebene gegen ein unabhängiges Schneiden des Quadrats, Überdeckung und Lage zufälliger Punkte bei zufälligem Gelände, Fehler; der Stresstest auf schmutzigen Punkten (gültige Polygone, die Flächen ergeben die Summe) über mehrere Minuten ohne Fund.
- **Playground und Dokumentation:** der Eintrag `contourBands` (Höhen durch Kommas getrennt oder ein Intervall), ein Abschnitt in der Konzeptseite „Triangulierung, Voronoi und Gelände“, Glossareintrag.
- **Nicht jetzt:** geglättete Höhenlinien (die Linien sind Polygonzüge durch die Schnittpunkte, mit einem kleinen Knick an jeder Kante; `Refine` oder `InterpolateNaturalNeighbour` auf einem Raster machen sie glatter), Stufen zwischen Höhen, die je Gebiet verschieden sind (ein Hochwasserstand, der entlang eines Flusses steigt), und Beschriftungen.

## Considered Options

- Die Dreiecke einer Stufe nach dem Schneiden mit `UnionAll` vereinigen (verworfen: Die Schnittpunkte benachbarter Dreiecke würden sich im letzten Bit unterscheiden, und das Overlay müsste sie reparieren, auf Kosten der Geschwindigkeit, die die exakten Schnittpunkte unnötig machen)
- Die Höhenlinien mit `Polygonize` zu Flächen machen (verworfen: Die Höhenlinien aller Höhen müssten mit dem Rand der Fläche verknotet und die Flächen danach den Stufen zugeordnet werden, was langsamer ist und das Z der Ecken verliert)
- Ein Stück verwerfen, dessen Fläche in Gleitkomma null ist (verworfen: Ein Splitter von wenigen Einheiten der letzten Stelle bekommt ein falsches Vorzeichen, und die Kanten, gegen die sich seine Nachbarn aufheben müssen, gehen verloren; der Stresstest hat das gefunden, und nur ein exakt entartetes Stück darf verworfen werden)
- Jede Stufe mit `MakeValid` gültig machen (verworfen: Die Prüfung eines großen Polygons ist nicht umsonst, und sie wird nur neben einem Splitter gebraucht)
- Ein ebenes Dreieck auf einer Höhe in beiden Stufen oder in der unteren (verworfen: In beiden würde seine Fläche doppelt zählen, und die obere Stufe ist die Konvention von `SlopeZones` und der üblichen Definition einer Klasse „ab einer Höhe“)
