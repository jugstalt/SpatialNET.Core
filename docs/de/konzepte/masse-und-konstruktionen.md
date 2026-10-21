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
- **`Reverse`** kehrt jeden Pfad und die Reihenfolge der Pfade einer Polylinie um. Für Polygone gibt es keines, weil ihre Orientierung normalisiert ist ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)).
