# Voronoi-Diagramm und die Kanten der Delaunay-Triangulierung

Das **Voronoi-Diagramm** ist die duale Form der Delaunay-Triangulierung (ADR 0029): Für jeden Punkt liefert es die Fläche, die ihm näher liegt als jedem anderen (das Thiessen-Polygon). Ein GIS braucht es für Einzugsgebiete („welcher Hydrant, welche Schule ist die nächste, und wie viel vom Bezirk versorgt sie“), für die Gewichtung von Messwerten nach Fläche und für Nachbarschaften. **`VoronoiDiagram`** liefert die Zellen, **`VoronoiEdges`** die Linien zwischen ihnen, und **`DelaunayEdges`** die Kanten der Triangulierung, aus der die Zellen entstehen.

*English version: [0036-voronoi-diagramm.md](0036-voronoi-diagramm.md)*

## Consequences

- **Ort:** `VoronoiDiagram`, `VoronoiEdges` und `DelaunayEdges` in `Shapes` (einer partiellen Klasse, ADR 0026) und der Werttyp `VoronoiCell` in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **API:**

  ```csharp
  IReadOnlyList<VoronoiCell> VoronoiDiagram(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
  IPolyline2D VoronoiEdges(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
  IPolyline2D DelaunayEdges(this IGeometry2D geometry, IGeometryFactory? factory = null)
  readonly record struct VoronoiCell(Coordinate Site, IPolygon2D Cell)
  ```

  Die Punkte sind die Stützpunkte einer beliebigen Geometrie, genau wie bei `DelaunayTriangles` (ADR 0029): X und Y, Stützpunkte mit gleichem X und Y zählen einmal, nicht endliche werden ignoriert.
- **Das Ergebnis behält die Zelle bei ihrem Punkt.** `DelaunayTriangles` liefert eine Liste in beliebiger Reihenfolge, weil niemand fragt, welches Dreieck zu welchem Punkt gehört. Bei einem Voronoi-Diagramm ist die Zuordnung der ganze Zweck, jeder Eintrag ist also eine `VoronoiCell` mit dem Punkt (mit seinem Z, falls er eins hat; M bleibt nicht erhalten) und seiner Zelle. Die Liste hat **einen Eintrag je verschiedenem Punkt, in der Reihenfolge seines ersten Vorkommens**.
- **Die Zuschnittfläche gehört zur Frage.** Die Zellen am Rand der Punkte sind unbegrenzt und müssen zugeschnitten werden. Ohne `clip` ist die Fläche die **Envelope der Punkte, auf jeder Seite um die Hälfte ihrer größeren Ausdehnung erweitert** (um 1, wenn sie keine Ausdehnung hat, etwa bei einem einzelnen Punkt); mit einem Polygon werden die Zellen damit zugeschnitten, was ein Einzugsgebiet innerhalb einer Grenze braucht. Ein Punkt außerhalb des Polygons kann trotzdem eine Zelle darin haben; eine Zelle, die das Polygon nicht erreicht, ist leer. Eine Zuschnittfläche ohne Fläche ergibt ein leeres Ergebnis. Ein Rechteck ist `Envelope2D.ToPolygon`.
- **Die Zellen haben keine Lücken und überlappen sich nicht.** Ihre Flächen ergeben zusammen die Fläche der Zuschnittfläche, und jede Zelle ist ein gültiges Polygon (ein Eigenschaftstest prüft beides, für zufällige Punkte, ein regelmäßiges Gitter und UTM-Koordinaten). So wird das erreicht:
  - Die Ecken einer Zelle sind die **Umkreismittelpunkte** der Dreiecke um ihren Punkt, einmal je Dreieck berechnet, zwei Nachbarzellen teilen sie also Bit für Bit. Die Topologie kommt aus der Triangulierung, die mit exakten Prädikaten entschieden wird (ADR 0029, ADR 0005).
  - Eine Zelle an der Hülle wird weit außerhalb der Zuschnittfläche geschlossen, entlang der Mittelsenkrechten der beiden Hüllenkanten und einem Streckenzug als Bogen dazwischen, und dann per Sutherland–Hodgman mit dem Rechteck zugeschnitten. Der Schnitt einer Zellkante mit einer Seite des Rechtecks wird aus den beiden Endpunkten **in fester Reihenfolge** berechnet, die beiden Zellen, die die Kante teilen, bekommen also denselben Punkt.
  - Die Ecken werden danach **auf das Gitter gerundet** (`gridSize`; Kaskade wie bei den Overlays: ausdrücklich, der globale Standard, sonst automatisch aus der Größenordnung der Koordinaten, ADR 0005). Fast auf einem Kreis liegende Punkte geben Umkreismittelpunkte, die sich um Rundungsfehler unterscheiden, und eine Zelle würde in dieser Größe falten; das Runden auf das Gitter lässt sie zusammenfallen. Eine gerundete Zelle wird angenommen, wenn sie noch konvex ist und eine Fläche hat; sonst macht der Overlay-Kern sie gültig (`MakeValid`, ADR 0020).
  - Mit einem Zuschnittpolygon wird die auf die Envelope des Polygons zugeschnittene Zelle per Overlay (`Intersection`) auf demselben ausdrücklichen Gitter mit dem Polygon geschnitten, benachbarte Zellen treffen sich also auch entlang des Zuschnittpolygons.
  - Eine Zelle, die in der Größe des Gitters zusammenfällt (Punkte, die näher beieinanderliegen), ist **leer**, kein Fehler.
- **Entartete Punktmengen.** Ein Punkt bekommt die ganze Zuschnittfläche, zwei Punkte werden durch ihre Mittelsenkrechte geteilt, und kollineare Punkte bekommen Streifen; ihre Nachbarn sind der vorige und der nächste Punkt entlang der Linie, und die Mittelsenkrechte wird für beide gleich berechnet. Ohne Punkt ist das Ergebnis leer.
- **`VoronoiEdges`** liefert die **Kanten zwischen benachbarten Zellen**, ein Pfad je Kante, ohne die Kanten auf dem Rechteck der Zuschnittfläche (sie gehören nicht zum Diagramm). Die Kanten stammen aus den ungerundeten Zellen, jede einmal, und werden auf dasselbe Gitter gerundet; mit einem Zuschnittpolygon werden sie damit geschnitten. Es braucht mindestens zwei Punkte.
- **`DelaunayEdges`** liefert die Kanten der Triangulierung von `DelaunayTriangles`, **jede Kante einmal**, als Pfad aus zwei Punkten, mit dem Z der Stützpunkte. Bei weniger als drei verschiedenen oder kollinearen Stützpunkten ist es leer, wie die Dreiecke. Der Nachbarschaftsgraph von Punkten ist diese Liste.
- **Ausnahmen (ADR 0012):** `ArgumentNullException` bei `null`, `NotSupportedException` bei einer unbekannten Geometrieart, `ArgumentOutOfRangeException` bei einer `gridSize`, die nicht endlich oder nicht größer als 0 ist, `InvalidOperationException` ohne Factory.
- **Geschwindigkeit.** Einmalmessungen (`--quick-voronoi`, Release-Build), die Zellen von `VoronoiDiagram` gegen den NTS-`VoronoiDiagramBuilder`, mit denselben Punkten wie in ADR 0029:

  | Punkte | Verteilung | `VoronoiDiagram` (ms) | NTS (ms) | `VoronoiEdges` (ms) |
  |---|---|---|---|---|
  | 10.000 | gleichmäßig | 45 | 165 | 52 |
  | 10.000 | Gitter | 65 | 123 | 53 |
  | 10.000 | gehäuft | 42 | 102 | 47 |
  | 100.000 | gleichmäßig | 381 | 1.463 | 544 |
  | 100.000 | Gitter | 465 | 1.561 | 532 |
  | 100.000 | gehäuft | 394 | 1.272 | 519 |

  Die Zeit enthält das Erzeugen der Zell-Polygone. Sie wächst etwa linear. `VoronoiEdges` ist bei gleichmäßigen Punkten langsamer als die Zellen, weil es erst die Zellen und dann die Kanten baut.
- **Tests (ADR 0008):** `[Covers]`-Tests, keine Fixtures. Handfälle (zwei Punkte, die Ecken eines Quadrats, ein einzelner Punkt, kollineare Punkte, Duplikate, die Reihenfolge, Z, ein Zuschnittpolygon, Fehler); Eigenschaften über zufällige Punkte (die Zellen sind gültig, füllen die Zuschnittfläche lückenlos, ein Punkt liegt in der Zelle seines nächsten Punktes, ein Punkt liegt in seiner eigenen Zelle); ein regelmäßiges Gitter, bei dem je vier Nachbarpunkte auf einem Kreis liegen; Koordinaten in UTM-Größe; **das NTS-Diagramm als Orakel** (die Fläche jeder Zelle, für zufällige Punkte in allgemeiner Lage und dasselbe Zuschnittrechteck); die Kanten liegen zwischen zwei gleich nahen Punkten; `DelaunayEdges` ist gleich den Kanten von `DelaunayTriangles` (jede Kante einmal).
- **Playground und Dokumentation:** Deskriptoren für alle drei mit Beispielpunkten (ADR 0011); die Zuschnittfläche ist im Playground eine **optionale zweite Eingabe**, was neu ist (ein `optional`-Kennzeichen des Eingabe-Deskriptors). Die Konzeptseite „Triangulierung, Voronoi und Gelände“ und ein Cookbook-Rezept zu Einzugsgebieten; Glossareinträge.
- **Vorerst nicht dabei:** Power- und gewichtete Diagramme, das Voronoi-Diagramm von Linien oder Polygonen (die Punkte sind Punkte), eine Toleranz, die fast gleiche Punkte zusammenfasst (wie NTS sie hat; ein Aufrufer kann die Punkte mit `ReducePrecision` runden), und das Diagramm auf der Kugel.

## Considered Options

- Jede Zelle berechnen, indem das Zuschnittrechteck mit den Mittelsenkrechten zu ihren Delaunay-Nachbarn geschnitten wird (verworfen: Die Ecken zweier Nachbarzellen entstehen aus verschiedenen Linienpaaren und unterscheiden sich um Rundungsfehler, sie würden sie also nicht teilen; und ein Punkt mit vielen Nachbarn ist quadratisch)
- Jede Zelle mit dem Overlay schneiden, ohne sie vorher auf das Rechteck zuzuschneiden (verworfen: Die geschlossenen fernen Ecken und die Umkreismittelpunkte von Splittern können sehr weit weg liegen, was das Gitter des Overlays nicht verträgt)
- Das NTS-Ergebnis: eine Sammlung von Polygonen mit dem Punkt als Benutzerdaten (verworfen: Die Interfaces haben keine Benutzerdaten, und ein typisiertes `VoronoiCell` ist die ehrliche Form der Zuordnung)
- Eine Pflicht-Zuschnittfläche (verworfen: Die Standard-Envelope mit Rand macht NTS und JTS auch und taugt zum Zeichnen; das Zuschnittpolygon ist die Wahl für Einzugsgebiete)
- Ein Toleranzparameter, der fast gleiche Punkte zusammenfasst (verworfen: Punkte sind in der Bibliothek überall durch X und Y verschieden; `ReducePrecision` (ADR 0027) macht es für den Aufrufer, der es will, und dann mit einem Gitter)
- Exakte rationale Umkreismittelpunkte (verworfen: Die Zellen werden ohnehin aufs Gitter gerundet, und die geteilten Ecken stimmen schon Bit für Bit überein)
- Die Zellen von Punkten, die die Zuschnittfläche nicht erreichen, als `null` liefern oder weglassen (verworfen: Die Liste hat je Punkt einen Eintrag, damit sie sich nach Index und Punkt zuordnen lässt; ein leeres Polygon sagt, was es ist)
