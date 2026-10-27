# Maße und Konstruktionen

Neben `Area`, `Length`, `Centroid` und `Distance` decken ein paar kleine Helfer ab, was Hosts täglich brauchen ([ADR 0019](../../adr/0019-mess-und-punkt-helfer-werte-statt-geometrien.de.md)). Punktförmige und skalare Ergebnisse sind **Werte**, die keine Factory brauchen; geometrische Ergebnisse entstehen über die Factory und werden auf deren Fähigkeiten beschränkt.

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var well = Wkt.Read<IPoint2D>("POINT(14 5)");

var (onParcel, onWell) = parcel.NearestPoints(well)!.Value;  // (10 5) und (14 5)
bool near = parcel.IsWithinDistance(well, 5);                // true: 4 entfernt, und er hört auf, sobald er es weiß
Coordinate? label = parcel.PointOnSurface();                 // ein Punkt im Inneren, für eine Beschriftung
IPolyline2D outline = parcel.Boundary();                     // der Ring als Linie
IPolyline2D dense = outline.Densify(2.5);                    // kein Segment länger als 2,5
IPolyline2D back = outline.Reverse();                        // dieselbe Linie rückwärts
```

## Maße

- **`NearestPoints(a, b)`** liefert die zwei Punkte, die `Distance` bilden, als `(OnA, OnB)?`, `null`, wenn eine Geometrie leer ist. Schneiden sich die Geometrien oder liegt eine in der Fläche der anderen, sind beide Punkte gleich. Unter gleich nahen Paaren gewinnt das erste in Eingabereihenfolge. Z und M werden auf ihrem Segment interpoliert. Eine Linie zwischen den Punkten ist ein Factory-Aufruf entfernt.
- **`IsWithinDistance(a, b, d)`** ist `Distance(a, b) <= d`, beginnt aber mit den Envelopes und hört beim ersten Segmentpaar innerhalb des Abstands auf; bei großen Eingaben ist er viel schneller als der volle Abstand. `d = 0` heißt, die Geometrien berühren oder überlappen sich; leere Geometrien ergeben `false`; ein negatives oder nicht endliches `d` wirft.
- **`PointOnSurface(g)`** liefert einen Punkt, der auf der Geometrie liegt, ohne Z und M. Bei einem Polygon ist es ein innerer Punkt aus dem breitesten Abschnitt einer waagerechten Linie durch die Envelope, er funktioniert also auch bei einem C-förmigen Flurstück, dessen Schwerpunkt außerhalb liegt. Bei einer Linie ist es der innere Stützpunkt, der dem Schwerpunkt am nächsten liegt, sonst die Mitte der Linie.

## Konstruktionen

- **`Boundary`** eines Polygons sind seine Ringe als Polylinie (normalisiert: Außenringe gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn). Der Rand einer Polylinie sind ihre Endpunkte nach der Mod-2-Regel ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.de.md)): ein Endpunkt, der ungerade oft vorkommt; geschlossene Pfade haben keinen. Punkte haben einen leeren Rand.
- **`Densify(maxSegmentLength)`** teilt jedes längere Segment in gleich lange Stücke, die nicht länger als die Grenze sind. Die Form und alle ursprünglichen Stützpunkte bleiben; Z und M der neuen Stützpunkte werden interpoliert. Polygone bleiben gültig. Eine Grenze, bei der das Ergebnis mehr als 50 Millionen Stützpunkte hätte, wirft.
- **`Smooth(iterations, method)`** rundet die Ecken einer Polylinie oder die Ringe eines Polygons ([ADR 0043](../../adr/0043-glaetten-von-linien-und-flaechen.de.md)): `Chaikin` schneidet die Ecken ab (die Kurve bleibt in der Hülle der Stützpunkte), `CatmullRom` ist ein Spline durch jeden Stützpunkt. Die Enden eines offenen Pfads bleiben, ein geschlossener Pfad wird ganz herum geglättet, und Z und M werden entlang der Segmente interpoliert, sodass eine Höhenlinie ihre Höhe behält. Jeder Durchlauf verdoppelt die Zahl der Segmente; zwei oder drei sehen glatt aus. `SmoothToTolerance(tolerance)` hört von selbst auf, wenn kein Stützpunkt weiter als die Toleranz (in den Einheiten der Koordinaten) von der Linie zwischen seinen Nachbarn liegt. Zwei Linien, die dichter nebeneinander laufen, als sich die Kurve bewegt, können sich nach dem Glätten kreuzen; um Höhenlinien zu glätten, siehe `Triangulation.Resample` ([Triangulierung und Gelände](triangulation-and-voronoi.md)).
- **`LineMerge`** verbindet die Pfade einer Polylinie, wo genau zwei Pfadenden zusammentreffen ([ADR 0023](../../adr/0023-linemerge-und-polygonize.de.md)); das ist, was `Union` von Linien nicht tut. Die Endpunkte müssen exakt gleich sein (keine Toleranz), ein Knoten, an dem drei oder mehr Enden zusammentreffen, hält seine Pfade getrennt, ein Zyklus wird zu einem geschlossenen Pfad, und mit `directed: true` wird nur Ende an Anfang verbunden. Es nodet nicht: Sich kreuzende Linien bleiben, wie sie sind (dafür vorher `Union`).
- **`Reverse`** kehrt jeden Pfad und die Reihenfolge der Pfade einer Polylinie um. Für Polygone gibt es keines, weil ihre Orientierung normalisiert ist ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)).

## Transformationen und Prüfungen

Verschieben, Skalieren und Drehen, einige Ja/Nein-Fragen zur Form einer Geometrie und die Hausdorff-Distanz ([ADR 0025](../../adr/0025-transformationen-pruefungen-und-hausdorff.de.md)):

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var track = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 4 4, 8 0)");

IPolygon2D moved = parcel.Translate(100, 50);                          // dx, dy
IPolygon2D turned = parcel.Rotate(Math.PI / 4, parcel.Centroid());     // Bogenmaß, gegen den Uhrzeigersinn
IPolygon2D mirrored = parcel.Scale(-1, 1);                             // weiter gültig, Ringe wieder ausgerichtet

bool simple = track.IsSimple();                                        // true: keine Selbstüberschneidung
double apart = track.HausdorffDistance(Wkt.Read("LINESTRING(0 0, 8 0)")); // 4: die Spitze ist 4 entfernt
```

- **`Translate(dx, dy)`, `Scale(sx, sy, origin?)`, `Rotate(angle, origin?)`** ändern X und Y und behalten Z und M. Der Ursprung ist (0, 0), wenn nicht angegeben; Winkel sind Bogenmaß, gegen den Uhrzeigersinn; Vierteldrehungen sind exakt. Ein negativer Faktor spiegelt, und die Ringe eines Polygons werden wieder ausgerichtet, es bleibt also gültig. **`Transform(Func<Coordinate, Coordinate>)`** bildet jeden Stützpunkt durch Ihre Funktion ab (sie kann ein Polygon ungültig machen, wenn sie nicht eindeutig umkehrbar ist). Es gibt Überladungen für jede Geometrieart.
- **`IsSimple`** ist die OGC-Definition: Eine Linie ist einfach, wenn sie sich außer an den Endpunkten weder berührt noch kreuzt; offene Pfade dürfen sich an ihren Enden treffen, der Schlusspunkt eines Rings darf nicht berührt werden. Punkte und Polygone sind einfach (für Flächen `IsValid`). **`IsClosed`** und **`IsRing`** gelten für Polylinien: geschlossene Pfade, und ein geschlossener, einfacher Pfad mit mindestens vier Punkten.
- **`EqualsExact(a, b, tolerance = 0)`** vergleicht Koordinaten **wie angegeben**, Stützpunkt für Stützpunkt, innerhalb der Toleranz; ein Ring, der woanders beginnt, ist verschieden. Für dieselbe Punktmenge dient `EqualsTopo`.
- **`HausdorffDistance(a, b, densifyFraction = 1)`** ist die diskrete Hausdorff-Distanz: wie weit die Formen als Ganzes auseinanderliegen (der größte Abstand eines Stützpunkts der einen zur Linienführung der anderen, in beiden Richtungen). Ein kleinerer Anteil fügt Stützpunkte entlang der Segmente hinzu und nähert sich dem exakten Wert von unten. Polygone zählen mit ihren Ringen.

## Hüllformen und ein Beschriftungspunkt

Drei Konstruktionen für Karten und Layouts ([ADR 0026](../../adr/0026-huellformen-und-beschriftungspunkt.de.md)):

```csharp
var lot = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 10, 0 10, 0 0), (6 2, 14 2, 14 8, 6 8, 6 2))");

IGeometry2D box = lot.MinimumRotatedRectangle();   // das engste Rechteck, in beliebigem Winkel
Circle? around = lot.MinimumBoundingCircle();      // Mitte und Radius des kleinsten Kreises
IPolygon2D disc = around!.Value.ToPolygon();       // derselbe Kreis als Polygon
Coordinate? label = lot.Polylabel();               // innen und möglichst weit vom Rand: nicht im Loch
```

- **`MinimumRotatedRectangle`** ist das Rechteck mit der kleinsten Fläche (nicht der kleinsten Breite), gefunden mit Rotating Calipers über die konvexe Hülle. Eine entartete Eingabe liefert einen Punkt oder die Linie zwischen den äußersten Punkten, wie `ConvexHull`.
- **`MinimumBoundingCircle`** liefert einen `Circle` (Mitte und Radius), `null` bei leerer Geometrie. `ToPolygon(factory, quadrantSegments)` macht daraus ein einbeschriebenes Polygon.
- **`Polylabel(polygon, tolerance?)`** ist der Pol der Unzugänglichkeit: der Punkt im Inneren mit dem größten Abstand zum Rand, Löcher eingeschlossen, innerhalb der Toleranz (Standard ein Tausendstel der größeren Seite der Envelope). Bei einem Polygon aus mehreren Teilen ist es der Pol des Teils mit dem größten Kreis.

## Triangulierung und konkave Hülle

Die Delaunay-Triangulierung der Stützpunkte einer Geometrie und eine Hülle, die sie enger umschließt als die konvexe Hülle ([ADR 0029](../../adr/0029-delaunay-und-konkave-huelle.de.md)):

```csharp
var wells = Wkt.Read("MULTIPOINT ((0 0), (4 0), (8 0), (0 4), (4 4), (8 4), (0 8), (4 8), (8 8), (12 2), (12 6), (13 4))");

IReadOnlyList<IPolygon2D> triangles = wells.DelaunayTriangles();      // kein Stützpunkt im Umkreis eines Dreiecks
IGeometry2D around = wells.ConcaveHull(5.5);                          // lässt jede Randkante bis 5,5 Länge stehen
IGeometry2D byRatio = wells.ConcaveHullByRatio(0.3);                  // dasselbe, maßstabsfrei: 0 = am engsten, 1 = konvexe Hülle
```

- **`DelaunayTriangles`** liefert eine flache Liste von Dreiecken (Polygone gegen den Uhrzeigersinn), die die konvexe Hülle füllen, sodass kein Stützpunkt im Umkreis eines Dreiecks liegt. Punkte auf einem gemeinsamen Kreis erlauben mehrere Triangulierungen; das Ergebnis ist eine davon. Die Triangulierung als Struktur (Nachbarn, Höhe an einem Punkt, Höhenlinien), Bruchkanten, Polygone und das Voronoi-Diagramm stehen auf der Seite [Triangulierung, Voronoi und Gelände](triangulation-and-voronoi.md).
- **`ConcaveHull(maxEdgeLength, holesAllowed)`** nimmt vom Rand her Dreiecke weg, längste Randkante zuerst, solange diese Kante länger als die Grenze ist; das Polygon bleibt zusammenhängend und enthält jeden Stützpunkt. `ConcaveHullByRatio` gibt die Grenze als Verhältnis der Kantenlängen der Triangulierung an (0 bis 1; 1 ist die konvexe Hülle). Mit `holesAllowed` kann sie auch im Inneren Löcher öffnen. Eine Geometrie mit weniger als drei verschiedenen oder nur kollinearen Stützpunkten liefert, was `ConvexHull` liefert.
- Die Grenze wählen: Eine Grenze etwas über dem üblichen Abstand benachbarter Punkte hält den Umriss getreu; eine größere füllt Lücken, eine kleinere öffnet Buchten.
