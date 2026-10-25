# Triangulierung, Voronoi und Gelände

Punkte, eine Delaunay-Triangulierung und das Voronoi-Diagramm sind drei Sichten auf eine Idee, und ein Geländemodell ist das, was man mit der Triangulierung macht. Diese Seite zeigt, was die Bibliothek bietet, welcher Aufruf welche Frage beantwortet und was man beachten muss. Die Entscheidungen stehen in [ADR 0036](../../adr/0036-voronoi-diagramm.de.md) (Voronoi), [ADR 0037](../../adr/0037-triangulation-netz-hoehe-hoehenlinien.de.md) (die Triangulierung als Fläche) und [ADR 0038](../../adr/0038-constrained-delaunay-und-polygon-triangulierung.de.md) (Bruchkanten und Polygone); die einfache Triangulierung ist [ADR 0029](../../adr/0029-delaunay-und-konkave-huelle.de.md).

| Sie wollen … | Aufruf | Ergebnis |
|---|---|---|
| die Dreiecke einiger Punkte zeichnen | `DelaunayTriangles(points)` | eine flache Liste von Dreiecken |
| die Nachbarschaft von Punkten zeichnen oder zählen | `DelaunayEdges(points)` | die Kanten, jede einmal |
| wissen, welcher Punkt der nächste ist, als Flächen | `VoronoiDiagram(points, clip)` | eine Zelle je Punkt, auf die Zuschnittfläche zugeschnitten |
| die Grenzen zwischen diesen Flächen zeichnen | `VoronoiEdges(points, clip)` | die Linien zwischen benachbarten Zellen |
| eine Fläche nach Höhe, Dreieck und Höhenlinie fragen | `Triangulation.FromPoints(points)` | eine `Triangulation` |
| Rücken, Gräben oder Grenzen als Kanten erhalten | `Triangulation.FromPointsAndLines(points, lines)` | eine `Triangulation`, die sie enthält |
| ein Polygon in Dreiecke zerlegen | `polygon.Triangulate()` oder `Triangulation.FromPolygon(polygon)` | die Dreiecke innerhalb des Polygons |

Überall sind die Punkte die **Stützpunkte einer beliebigen Geometrie**: eines Multipoints, einer Polylinie oder eines Polygons. Stützpunkte mit gleichem X und Y zählen einmal, nicht endliche werden ignoriert, und Z bleibt erhalten, wo das Ergebnis es tragen kann.

## Voronoi: welcher Punkt ist der nächste?

Die Zelle eines Punktes ist die Fläche, die ihm näher liegt als jedem anderen Punkt (ein Thiessen-Polygon). Sie beantwortet „welcher Hydrant, welche Messstelle, welche Schule versorgt diesen Ort“, und mit den Flächen „wie viel vom Bezirk versorgt jeder“.

```csharp
var stations = Wkt.Read<IMultiPoint2D>("MULTIPOINT ((10 20), (35 80), (60 30), (85 70), (50 55))");
var boundary = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 100 0, 100 100, 0 100, 0 0))");

IReadOnlyList<VoronoiCell> cells = stations.VoronoiDiagram(boundary);   // five cells that tile the boundary
double total = cells.Sum(c => c.Cell.Area());                            // 10000
IPolyline2D edges = stations.VoronoiEdges(boundary);                     // the lines between the cells
```

- **Die Zelle behält ihren Punkt.** `VoronoiCell` hat `Site` (mit seinem Z) und `Cell`, und die Liste hat **einen Eintrag je verschiedenem Punkt, in der Reihenfolge seines ersten Vorkommens**.
- **Die Zuschnittfläche entscheidet, was eine Randzelle ist.** Die Zellen am Rand sind unbegrenzt und müssen zugeschnitten werden. Ohne Zuschnittfläche werden sie auf die Envelope der Punkte zugeschnitten, auf jeder Seite um die Hälfte ihrer größeren Ausdehnung erweitert; mit einem Polygon auf dieses, was ein Einzugsgebiet innerhalb eines Bezirks braucht. Ein Punkt außerhalb des Polygons kann trotzdem eine Zelle darin haben; eine Zelle, die das Polygon nicht erreicht, ist leer (der Eintrag bleibt, mit einem leeren Polygon). Für ein Rechteck `Envelope2D.ToPolygon` verwenden.
- **Die Zellen füllen die Zuschnittfläche lückenlos.** Sie haben keine Lücken, überlappen sich nicht und sind gültige Polygone; ihre Ecken werden auf das Gitter der Overlays gerundet (`gridSize`, [Overlays und Gitter](overlay-and-grid.md)). Zwei Zellen, die eine Kante teilen, teilen deren Ecken exakt. Eine Zelle, die in der Größe des Gitters zusammenfiele (Punkte, die näher beieinanderliegen), ist leer.
- **Wenige Punkte sind in Ordnung.** Ein Punkt bekommt die ganze Zuschnittfläche, zwei werden durch ihre Mittelsenkrechte geteilt, kollineare Punkte bekommen Streifen.
- `VoronoiEdges` liefert die Linien **zwischen** den Zellen (nicht den Rand der Zuschnittfläche), ein Pfad je Kante, und braucht mindestens zwei Punkte.

## Die Triangulierung als Fläche

`Triangulation` hält die Dreiecke mit ihren Nachbarn und beantwortet Fragen an die Fläche. Sie ist **unveränderlich und thread-sicher**: einmal bauen, oft fragen, aus vielen Threads.

```csharp
var terrain = Triangulation.FromPoints(Wkt.Read("MULTIPOINT Z ((0 0 10), (100 0 20), (100 100 40), (0 100 30), (50 50 25))"));

double height = terrain.InterpolateZ(new Coordinate(25, 10));   // 14.5
int triangle = terrain.Locate(new Coordinate(25, 10));          // the triangle that contains the point
IPolyline2D contour = terrain.Contour(25);                      // the line at height 25, from (0 75) to (100 25)
```

- **Struktur.** `Vertices`, `TriangleCount`, `Triangle(t)` (drei Stützpunkt-Ids, gegen den Uhrzeigersinn), `Neighbour(t, side)` (das Dreieck jenseits der Kante von Stützpunkt `side` zu `side + 1`, oder −1 am Rand) und `IsConstrained(t, side)`. `ToPolygons`, `Edges` und `ConstrainedEdges` liefern Geometrien zum Zeichnen.
- **`Locate(point)`** ist das Dreieck, das den Punkt auf seinem Rand oder innen enthält, oder −1. Der Test ist exakt; auf einer geteilten Kante oder einem Stützpunkt gewinnt der kleinste Index. Es nutzt einen Index der Dreiecke, eine Frage kostet also ein bis zwei Mikrosekunden, egal wie groß das Modell ist.
- **`InterpolateZ(point)`** ist die Höhe in der Ebene des Dreiecks: linear, an den Stützpunkten exakt, auf einer Fläche, die eine Ebene ist, exakt. Es ist **`NaN`** außerhalb der Triangulierung und wo ein Stützpunkt kein Z hat.
- **`Contour(z)`** ist die Linie einer Höhe: wo die Fläche, in jedem Dreieck linear, diese Höhe hat. Ihre Stützpunkte haben Z = `z`; sie ist geschlossen, wo sie zu ihrem Anfang zurückkehrt, und endet am Rand des Modells; ein Stützpunkt genau auf `z` zählt als über der Linie. Für Höhenlinien bei vielen Höhen einmal je Höhe aufrufen.

## Bruchkanten und Polygone

Eine einfache Triangulierung legt ihre Kanten dorthin, wo die Leerkreis-Regel es sagt. Eine **Bruchkante** (ein Rücken, ein Graben, eine Böschung) muss eine Kante sein, was die Regel auch sagt. `FromPointsAndLines` behält die Segmente von Linien (oder die Ringe von Polygonen) als Ketten von Kanten und ist überall sonst Delaunay (die *Constrained*-Delaunay-Triangulierung).

```csharp
var spots = Wkt.Read("MULTIPOINT Z ((0 0 5), (100 0 5), (100 40 15), (0 40 15), (50 20 9))");
var ditch = Wkt.Read("LINESTRING Z (0 20 8, 100 20 8)");   // passes through the vertex (50 20) and is split there

var surface = Triangulation.FromPointsAndLines(spots, ditch);
IPolyline2D kept = surface.ConstrainedEdges();             // two edges that together are the ditch
double onTheDitch = surface.InterpolateZ(new Coordinate(25, 20));   // 8.5: interpolated along the break line
```

- Die **Stützpunkte der Linien sind Stützpunkte der Triangulierung**, mit ihrem Z, eine Bruchkante mit Z legt also die Höhe entlang ihrer fest.
- Eine Linie, die genau durch einen Stützpunkt geht, wird dort geteilt. Linien dürfen sich berühren, Endpunkte teilen und überlappen.
- Wo sich zwei Linien **kreuzen**, werden sie zuerst wie in einem Overlay genodet: Der Kreuzungspunkt wird ein Stützpunkt, und die Linien werden aufs Gitter gerundet (`gridSize`). Ohne Kreuzung wird nichts gerundet.
- Die Dreiecke füllen die **konvexe Hülle** aller Stützpunkte. Mit `IsConstrained` oder `ConstrainedEdges` erkennt man die Bruchkanten unter den übrigen.

Ein Polygon wird in die Dreiecke innerhalb zerlegt:

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 0, 10 10, 6 10, 6 4, 4 4, 4 10, 0 10, 0 0))");

IReadOnlyList<IPolygon2D> triangles = parcel.Triangulate();   // 8 vertices: 6 triangles inside the U
```

- Ein Polygon ohne Löcher mit `n` Stützpunkten hat `n − 2` Dreiecke, jedes Loch fügt zwei hinzu. Es wird kein Stützpunkt ergänzt. Jede Kante eines Rings ist eine Kante eines Dreiecks.
- Das Innere wird nach der Even-Odd-Regel entschieden, Löcher bleiben also draußen, und ein ungültiges Polygon wird so gelesen, wie `MakeValid` es liest.
- `Triangulation.FromPolygon(polygon)` liefert dieselben Dreiecke mit ihren Nachbarn; am Rand des Polygons ist der Nachbar −1.

## Was man beachten sollte

- **Exakt, wo entschieden wird, gerundet, wo konstruiert wird.** Welche Punkte Nachbarn sind, welches Dreieck einen Punkt enthält und ob eine Zwangskante eine Kante ist, wird mit den exakten Prädikaten der Overlays entschieden; die Ecken der Voronoi-Zellen (Umkreismittelpunkte) und die Punkte der Höhenlinien werden in Gleitkomma berechnet. Die Voronoi-Zellen werden auf ein Gitter gerundet, damit sie gültig bleiben.
- **Punkte auf einem gemeinsamen Kreis** (die Ecken eines Rechtecks, ein regelmäßiges Gitter) erlauben mehrere gleich gute Delaunay-Triangulierungen. Welche herauskommt, ist deterministisch, aber nicht festgelegt; alles andere ist der Vertrag.
- **Eine lange Grenze zu triangulieren ist langsamer, als dieselbe Zahl verstreuter Punkte zu triangulieren**, weil die Delaunay-Triangulierung von Punkten auf einer Kurve aus langen dünnen Dreiecken besteht. Ein Ring aus 10.000 Stützpunkten braucht etwa eine zehntel Sekunde; Polygone ab 100.000 Stützpunkten brauchen Sekunden ([ADR 0038](../../adr/0038-constrained-delaunay-und-polygon-triangulierung.de.md)).
- **Z ist die Höhe**, und nur Z: Eine Triangulierung von Punkten ohne Z hat keine Höhen (`InterpolateZ` ist `NaN`, `Contour` ist leer), und M bleibt nicht erhalten.
- **Nicht dabei:** Neigung, Exposition und Volumen, andere Interpolationen als linear im Dreieck, das Verändern einer Triangulierung nach dem Bauen, und Triangulierungen, die Punkte ergänzen (konforme oder Qualitätsnetze).
