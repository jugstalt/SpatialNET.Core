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
| Neigung, Volumen über einer Höhe, Höhenprofil einer Straße wissen | `Slope`, `Aspect`, `Volume`, `Drape` an einer `Triangulation` | Zahlen, oder die Straße mit Z und M |
| die Flächen zwischen zwei Höhen, oder wo es steil ist | `ContourBands`, `SlopeZones` | ein Polygon je Stufe oder Klasse |
| eine glatte Höhe, eine glattere Fläche, ein feineres Netz | `InterpolateNaturalNeighbour`, `Resample`, `Refine` | eine Zahl, oder eine `Triangulation` |
| die Erde zwischen zwei Flächen (Abtrag und Auftrag) | `Volume(other)` | das Volumen darüber und darunter |
| was von einem Punkt aus zu sehen ist | `LineOfSight`, `Viewshed` | ja oder nein, oder die sichtbare Fläche als Polygon |

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
- **`Contour(z)`** ist die Linie einer Höhe: wo die Fläche, in jedem Dreieck linear, diese Höhe hat. Ihre Stützpunkte haben Z = `z`; sie ist geschlossen, wo sie zu ihrem Anfang zurückkehrt, und endet am Rand des Modells; ein Stützpunkt genau auf `z` zählt als über der Linie. Für Höhenlinien bei vielen Höhen einmal je Höhe aufrufen. Die Linien haben an jeder Dreieckskante einen kleinen Knick; für glatte siehe `Resample` und `Smooth` weiter unten.

## Neigung, Exposition, Fläche und Volumen

Weil die Fläche in jedem Dreieck linear ist, haben die Fragen nach ihrer Form exakte Antworten, je Dreieck oder für die ganze Fläche ([ADR 0039](../../adr/0039-gelaendeanalyse-neigung-flaeche-volumen-drape.de.md)). X, Y und Z müssen **in derselben Einheit** sein, geografische Koordinaten also zuerst projizieren.

```csharp
// A plane that rises to the north: 0 at y = 0, 50 at y = 100 (the unit of X, Y and Z is the same).
var terrain = Triangulation.FromPoints(Wkt.Read("MULTIPOINT Z ((0 0 0), (100 0 0), (100 100 50), (0 100 50), (50 50 25))"));
int triangle = terrain.Locate(new Coordinate(20, 80));

double slope = terrain.Slope(triangle);             // 26.565 degrees: atan(0.5)
double aspect = terrain.Aspect(triangle);           // 180: it faces south, downhill
double area = terrain.SurfaceArea();                // 11180.34: the map area 10000 times sqrt(1 + 0.5²)
var (above, below) = terrain.Volume(25);            // 62500 above the height 25 (to cut away), 62500 below it (to fill)
var (fill, cut) = after.Volume(before);             // the same between two surfaces, say the ground after and before an excavation
```

- **`Slope(t)`** ist der Winkel der Ebene des Dreiecks zur Waagerechten in Grad (0 eben, 90 senkrecht). **`Aspect(t)`** ist die Himmelsrichtung, in die es schaut, also die Richtung des steilsten **Gefälles**, im Uhrzeigersinn von Norden (0 Nord, 90 Ost, 180 Süd, 270 West); ein ebenes Dreieck schaut nirgendwohin und ergibt `NaN`. `Gradient(t)` liefert die Höhenänderung je Einheit in X und Y, woraus sich ein Prozentwert oder ein Verhältnis ergibt. Ein Punkt hat keine eigene Neigung: Fragen Sie sein Dreieck, `Slope(Locate(point))`.
- **`SurfaceArea(t)`** und **`SurfaceArea()`** sind die Flächen auf der Oberfläche, größer als die Kartenfläche, wo das Gelände geneigt ist.
- **`Volume(z)`** ist das Volumen zwischen der Fläche und der waagerechten Ebene auf der Höhe `z`, für die lineare Fläche **exakt**: `Above` ist, was darüber liegt (abzutragende Erde), `Below`, was bis dahin fehlt (aufzufüllen), und `Above − Below` das Nettovolumen. Abtrag und Auftrag zwischen zwei Flächen sind nicht enthalten.
- **`Volume(other)`** ist das Volumen zwischen zwei Flächen über ihrer gemeinsamen Fläche, **exakt**: `Above` ist dort, wo diese Fläche über der anderen liegt, `Below` wo darunter. Mit dem Gelände vor und nach einem Aushub sind das die Erde zum Auffüllen und die Erde zum Abtragen.
- Dreiecke mit einem Stützpunkt ohne Z ergeben `NaN` (Neigung, Exposition, Fläche) oder zählen nicht (Gesamtfläche, Volumen).

## Eine Linie über dem Gelände

`Drape` legt eine Polylinie über die Oberfläche: dieselbe Linie in der Karte, mit **Z als Höhe** und **M als Abstand entlang der Linie**, und einem Stützpunkt überall, wo die Linie eine Kante der Triangulierung kreuzt, damit das Ergebnis der Fläche exakt folgt.

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING (10 10, 90 90)");

IPolyline2D onTerrain = terrain.Drape(road);        // Z the height, M the distance along the road, a vertex on every edge it crosses
bool visible = terrain.LineOfSight(new Coordinate(10, 50), new Coordinate(90, 50), observerHeight: 1.7);   // can the second point be seen from the first?
```

- Das Höhenprofil einer Straße ist das Z des Ergebnisses gegen sein M ([Cookbook](../cookbook.md)); die 3D-Länge folgt aus den Stützpunkten. M ist der Abstand, wie `LinearReferencing` ihn misst, `Interpolate`, `Project` und der Rest gelten also für das Ergebnis.
- Teile der Linie außerhalb der Fläche oder über einem Dreieck ohne Z fallen weg; das Ergebnis hat dann einen Pfad für jeden Teil innen.
- Z braucht eine Factory mit `SupportsZ`, M eine mit `SupportsM`.
- **`LineOfSight`** fragt, ob die gerade Linie vom Auge zum Ziel unter dem Gelände verläuft. Ein Punkt außerhalb der Fläche ist nicht sichtbar.
- **`Viewshed(observer, maxDistance, observerHeight, targetHeight, rays)`** ist dieselbe Frage für jeden Punkt: der Teil der Fläche, der vom Standort aus zu sehen ist, als Polygon ([ADR 0044](../../adr/0044-sichtbarkeitsflaeche.de.md)). Ein Fächer von Strahlen (standardmäßig 360) geht vom Standort aus; entlang jedes Strahls sind die sichtbaren Abschnitte exakt (der Horizont wird am Profil entlang verfolgt), und die Abschnitte werden zu Winkelscheiben zusammengesetzt. Zwischen zwei Strahlen ist der Rand angenähert, um etwa `r · π / rays` in der Entfernung `r`, für eine weite Sicht also mehr Strahlen nehmen. Es kostet so viele Linien über die Fläche, wie es Strahlen gibt, bei einer größeren Fläche nicht mehr.

## Zonen und Stufen

`SlopeZones` und `ContourBands` machen aus der Fläche **Flächen**: wo es steil ist, und was zwischen zwei Höhen liegt ([ADR 0041](../../adr/0041-natuerliche-nachbarn-qualitaetsnetze-und-flaechenfragen.de.md), [ADR 0042](../../adr/0042-hoehenstufen-als-flaechen.de.md)).

```csharp
// the areas by slope class: below 10, 10 to 30, 30 and more degrees
IReadOnlyList<IPolygon2D> zones = terrain.SlopeZones([10, 30]);

// the areas between contour lines: below 100, 100 to 120, 120 to 140, 140 and up
IReadOnlyList<IPolygon2D> bands = terrain.ContourBands([100, 120, 140]);
```

- **`SlopeZones(limits)`** gibt für jede Neigungsklasse ein Polygon, eines mehr als Grenzen (ein leeres Polygon für eine Klasse ohne Fläche).
- **`ContourBands(heights)`** gibt die Flächen zwischen Höhenlinien: ein Polygon für jede Stufe (unter der ersten Höhe, zwischen zwei Höhen, über der letzten), exakt an den Linien geschnitten; die Stufen überdecken die Fläche. `terrain.ContourBands([100, 120, 140])` hat vier Polygone: unter 100, 100 bis 120, 120 bis 140 und 140 und mehr.
- Beide geben **ein Polygon mehr, als es Grenzen gibt** (ein leeres Polygon für eine Klasse ohne Fläche), und die Polygone überdecken die Fläche. Ein Dreieck, das genau auf einer Grenze eben ist, gehört zur Klasse darüber; Dreiecke ohne Z gehören zu keiner.

## Eine glattere oder feinere Fläche

Drei Aufrufe verändern die Fläche selbst, jeder für einen anderen Zweck ([ADR 0041](../../adr/0041-natuerliche-nachbarn-qualitaetsnetze-und-flaechenfragen.de.md), [ADR 0043](../../adr/0043-glaetten-von-linien-und-flaechen.de.md)).

```csharp
// a smooth height at one point, without the creases of the linear one (natural neighbour, Sibson)
double smooth = terrain.InterpolateNaturalNeighbour(new Coordinate(45, 55));

// a smoother surface: a grid of 10 by natural neighbour instead of the measurements; contour lines of it have no creases
Triangulation smoothed = terrain.Resample(10);

// a finer mesh without sharp triangles, for a calculation
Triangulation fine = terrain.Refine(minAngle: 25, maxArea: 50);
double worst = fine.MinAngle();                      // at least 25 degrees
```

- **`InterpolateNaturalNeighbour`** gewichtet die Höhen der Nachbarn, die ein neuer Punkt bekäme, mit der Fläche ihrer Voronoi-Zellen, die er übernähme. Sie ist stetig und glatt, gibt eine Ebene exakt wieder, geht nie über die Höhen um den Punkt hinaus und ist so einfach aufzurufen wie `InterpolateZ`. Mit Bruchkanten (Zwangskanten) in der Triangulierung gibt sie die lineare Höhe, da die Fläche dort einen Knick haben soll.
- **`Resample(cellSize)`** glättet die Fläche: Die Höhen eines Rasters durch natürliche Nachbarn treten an die Stelle der inneren Messpunkte, sodass Höhenlinien und Stufen davon keine Knicke an den alten Dreieckskanten haben. Linien verschiedener Höhen einer beliebigen Triangulierung können sich nicht kreuzen, also ist das der Weg, Höhenlinien zu glätten; `Smooth` rundet den Rest ([Kochbuch](../cookbook.md)).
- **`Refine`** fügt Punkte hinzu (die Delaunay-Verfeinerung nach Ruppert), bis kein Winkel unter `minAngle` und kein Dreieck größer als `maxArea` ist. Der Rand und die Bruchkanten bleiben, in kürzere Kanten zerlegt, und die neuen Punkte liegen auf der Fläche. `maxSteinerPoints` begrenzt die Arbeit; Winkel über 34 Grad werden abgelehnt, und ein spitzer Winkel zwischen zwei Bruchkanten der Eingabe lässt sich nicht verbessern.
- **Welcher?** `InterpolateNaturalNeighbour` für die Höhe an einem Punkt, `Resample` für Höhenlinien und Karten, die glatt aussehen sollen, `Refine` für ein Netz, das eine Berechnung braucht (Finite Elemente, Strömung). Sie lassen sich verbinden; `Resample` auf einer mit `Refine` verfeinerten Fläche ist nicht nötig, denn `Refine` behält die Fläche und fügt nur Punkte auf ihr hinzu.

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

## Delaunay oder Constrained?

Beide bauen dieselbe Art von Triangulierung. Die Frage ist, ob es eine **Linie gibt, die die Fläche nicht überqueren darf**.

| | Einfaches Delaunay | Constrained Delaunay |
|---|---|---|
| Aufrufe | `DelaunayTriangles`, `DelaunayEdges`, `Triangulation.FromPoints` | `Triangulation.FromPointsAndLines`, `Triangulation.FromPolygon`, `polygon.Triangulate()` |
| Eingabe | Punkte | Punkte **und** Linien (oder ein Polygon) |
| Wo die Kanten liegen | wohin die Leerkreis-Regel sie legt | die Linien sind Kanten; sonst dieselbe Regel |
| Verwenden, wenn | Messwerte verstreut sind und nichts im Gelände als Bruch bekannt ist | die Wirklichkeit einen Rücken, einen Graben, eine Böschung, einen Straßendamm, ein Ufer oder eine Grenze hat, über die Höhen nicht interpoliert werden dürfen |
| Ergebnis | Dreiecke, Kanten oder eine `Triangulation` | eine `Triangulation`, die ihre Bruchkanten kennt (`IsConstrained`, `ConstrainedEdges`) |

Ohne die Linie kann die Triangulierung eine Kante **quer über** einen Graben oder Rücken legen, und eine Höhe, die dort gefragt wird, wird zwischen den beiden Ufern interpoliert, als gäbe es den Graben nicht:

```csharp
// A ditch (height 5) between two banks (height 20); the banks have points right next to the ditch.
var points = Wkt.Read("MULTIPOINT Z ((0 0 20), (100 0 20), (0 40 20), (100 40 20), (0 20 5), (100 20 5), (50 15 20), (50 25 20))");
var ditch = Wkt.Read("LINESTRING Z (0 20 5, 100 20 5)");
var middle = new Coordinate(50, 20);

// Delaunay alone does not know about the ditch: the short edge from bank to bank (50 15)-(50 25) crosses it.
double plain = Triangulation.FromPoints(points).InterpolateZ(middle);                   // 20

// With the ditch as a break line it is an edge, and the surface follows it.
double forced = Triangulation.FromPointsAndLines(points, ditch).InterpolateZ(middle);   // 5
```

- **`DelaunayEdges` ist eine fertige Liniengeometrie** der einfachen Triangulierung, zum Zeichnen oder als Nachbarschaftsgraph von Punkten. Für die Kanten einer *Constrained*-Triangulierung nimmt man `Triangulation.FromPointsAndLines(...).Edges()` (alle Kanten) oder `.ConstrainedEdges()` (nur die Bruchkanten); `Triangulation.FromPoints(points).Edges()` ist dasselbe wie `DelaunayEdges(points)`.
- Beide kosten etwa gleich viel. Eine Bruchkante kostet die Flips, die sie einfügen (wenige je gekreuzter Kante), wenige Linien sind also billig; Punkte ohne Linien brauchen nichts davon.
- Im Playground sind das die Einträge `DelaunayEdges` (Punkte) und `ConstrainedDelaunay` (Punkte und Bruchkanten).
- Wer nur ein Polygon in Dreiecke zerlegen will, nimmt `Triangulate`: Es braucht keine zusätzlichen Punkte und lässt weg, was außerhalb der Ringe liegt.

## Was man beachten sollte

- **Exakt, wo entschieden wird, gerundet, wo konstruiert wird.** Welche Punkte Nachbarn sind, welches Dreieck einen Punkt enthält und ob eine Zwangskante eine Kante ist, wird mit den exakten Prädikaten der Overlays entschieden; die Ecken der Voronoi-Zellen (Umkreismittelpunkte) und die Punkte der Höhenlinien werden in Gleitkomma berechnet. Die Voronoi-Zellen werden auf ein Gitter gerundet, damit sie gültig bleiben.
- **Punkte auf einem gemeinsamen Kreis** (die Ecken eines Rechtecks, ein regelmäßiges Gitter) erlauben mehrere gleich gute Delaunay-Triangulierungen. Welche herauskommt, ist deterministisch, aber nicht festgelegt; alles andere ist der Vertrag.
- **Eine lange Grenze zu triangulieren ist langsamer, als dieselbe Zahl verstreuter Punkte zu triangulieren**, weil die Delaunay-Triangulierung von Punkten auf einer Kurve aus langen dünnen Dreiecken besteht. Ein Ring aus 10.000 Stützpunkten braucht etwa eine zehntel Sekunde; Polygone ab 100.000 Stützpunkten brauchen Sekunden ([ADR 0038](../../adr/0038-constrained-delaunay-und-polygon-triangulierung.de.md)).
- **Z ist die Höhe**, und nur Z: Eine Triangulierung von Punkten ohne Z hat keine Höhen (`InterpolateZ` ist `NaN`, `Contour` ist leer), und M bleibt nicht erhalten.
- **Nicht dabei:** andere Interpolationen als linear und natürliche Nachbarn (inverse Distanz, Laplace), das Verändern einer Triangulierung nach dem Bauen und konforme Netze mit garantierter Mindestgröße.
