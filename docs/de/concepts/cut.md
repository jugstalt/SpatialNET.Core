# Cut

`Cut` zerlegt ein Polygon oder eine Polylinie an einer Schnittlinie. Das Ergebnis ist eine flache Liste der Teile ohne links/rechts-Zuordnung, wie bei den Schnitt-Funktionen von Editierwerkzeugen ([ADR 0007](../../adr/0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.de.md)).

```csharp
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var cutter = Wkt.Read<IPolyline2D>("LINESTRING(5 -1, 5 11)");

IReadOnlyList<IPolygon2D> pieces = square.Cut(cutter);

Console.WriteLine(pieces.Count);      // 2
Console.WriteLine(pieces[0].Area());  // 50
```

Die Signaturen sind `Cut(this IPolygon2D, IPolyline2D cutter, IGeometryFactory? factory = null, double? gridSize = null)` mit Rückgabe `IReadOnlyList<IPolygon2D>` und `Cut(this IPolyline2D, IPolyline2D cutter, …)` mit Rückgabe `IReadOnlyList<IPolyline2D>`.

## Was als Teil zählt

- Jedes **zerschnittene Stück** ist ein eigenes Element.
- Alle **unberührten** Teilflächen (bzw. Pfade) bleiben **zusammen ein Element** und behalten ihre Originalkoordinaten, ohne Snap-Rounding. Ein Multipart-Feature zerfällt also nicht nebenbei, und was der Schnitt nicht trifft, bleibt unverändert.
- Wird nichts geschnitten, enthält die Liste genau ein Element.
- Die Reihenfolge der Elemente ist nicht festgelegt, das Ergebnis aber deterministisch.

## Polygone

Die Teile sind die Maschen aus dem genodeten Polygonrand plus den Teilen der Schnittlinie im Inneren. Nur vollständige Durchquerungen schneiden: lose Enden der Schnittlinie, Verläufe entlang des Randes und Berührungen an Vertices schneiden nicht. Teile der Schnittlinie, die in einem Loch liegen, zählen nicht, und die Löcher werden passend auf die Teile verteilt.

## Polylinien

Eine Polylinie wird an jedem Kontakt mit der Schnittlinie zerlegt: bei einer Kreuzung, einer Berührung in einem Punkt sowie am Anfang und Ende einer kollinearen Überlappung. Jedes Teil ist eine Polylinie mit genau einem Pfad.

## Gut zu wissen

- **Alle Pfade der Schnittlinie schneiden**, mehrere Schnitte in einem Aufruf sind also möglich. Es gibt keine Optionen-Klasse.
- **Die Schnittlinie wird nicht verlängert.** Eine Fangtoleranz, die die Linie bis zum Rand verlängert, wie Editierwerkzeuge sie umsetzen, ist Sache des Werkzeugs, nicht der Bibliothek.
- Z und M an Schnittpunkten werden auf dem Originalsegment interpoliert.
