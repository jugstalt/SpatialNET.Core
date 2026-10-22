# Das Geometriemodell

SpatialNET rechnet auf einer kleinen Menge lesender Interfaces. Diese Seite beschreibt, was diese Geometrien bedeuten: wie Polygone definiert sind, was „gültig“ heißt und was bei leeren oder ungültigen Eingaben passiert. Die Interfaces selbst stehen unter [Host-Anbindung](../host-integration.md).

## Geometrie-Arten

| Interface | Ist | Entspricht |
|---|---|---|
| `IPoint2D` | ein Punkt | Point |
| `IMultiPoint2D` | eine Menge von Punkten | MultiPoint |
| `IPolyline2D` | ein oder mehrere Pfade | LineString / MultiLineString |
| `IPolygon2D` | eine flache Liste von Ringen | Polygon / MultiPolygon |

Pfade und Ringe sind Teile einer Geometrie und selbst keine Geometrien. Eine Geometry-Collection gibt es nicht: Ergebnisse sind dimensionshomogen. 3D gibt es nur am Punkt (`IPoint3D`); Algorithmen interpolieren Z an neuen Vertices, wenn die beteiligten Punkte es tragen. Ein Punkt kann zusätzlich einen Messwert tragen (`IPointM`, M): Er wird wie Z mitgeführt und interpoliert, beeinflusst aber nie eine Berechnung ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)).

## Polygone: eine flache Ringliste

Ein `IPolygon2D` markiert nicht, welcher Ring Außenring und welcher Loch ist. Seine Fläche ist über alle Ringe nach der **Even-Odd-Regel** definiert ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)):

- Ein Punkt liegt im Polygon, wenn er in einer **ungeraden** Zahl von Ringen liegt.
- Bei gültigen Polygonen entspricht das der Verschachtelungstiefe: gerade Tiefe = Außenring, ungerade = Loch des nächst umgebenden Außenrings.
- Eine Insel in einem Loch liegt wieder innen, ist also ein eigener Außenring.
- **Die Ring-Orientierung wird bei der Eingabe ignoriert**, und der Schlusspunkt eines Rings (gleich dem Startpunkt) darf wiederholt werden oder fehlen.

SpatialNET nennt einen Außenring zusammen mit den Löchern, die direkt in ihm liegen, eine **Teilfläche**. Ein Polygon mit mehreren Teilflächen ist ein Multipolygon. Ergebnisse gehen als Teilflächen an die Factory, damit der Host nicht nachrechnen muss, welcher Ring Loch welches Außenrings ist. In Ergebnissen laufen Außenringe gegen den Uhrzeigersinn und Löcher im Uhrzeigersinn.

## Was „gültig“ heißt

Ein Polygon ist gültig, wenn sich die Ringe nicht kreuzen. Nach der OGC-Regel, wie NetTopologySuite sie prüft:

- Ein Loch darf den Außenring oder ein anderes Loch in **genau einem Punkt** berühren; ein Ring darf sich selbst nicht berühren.
- Ringe, die sich kreuzen oder entlang einer Kante überlagern, sind ungültig. Insbesondere sind teilweise überlappende Außenringe ungültig: Even-Odd macht die Überlappung zum Loch, während viele Hosts die Fläche doppelt zählen würden.
- Degenerierte Ringe (weniger als 3 verschiedene Punkte, Fläche 0) werden von den Algorithmen ignoriert und von `IsValid()` gemeldet.

Ein fehlender Schlusspunkt und die Ring-Orientierung sind keine Fehler. Wie man die Gültigkeit prüft, steht unter [Vereinfachen, konvexe Hülle und Validierung](simplify-and-validation.md).

## Gültige und ungültige Eingaben

| Operation | Gültige Eingabe | Ungültige Eingabe |
|---|---|---|
| Overlays, Buffer, Simplify, Cut | gültige, geschlossene Ergebnisse | definiertes Ergebnis nach der Even-Odd-Regel bzw. der gewählten Füllregel; keine Exception |
| Prädikate (`Contains`, `Relate`, …) | exaktes DE-9IM-Ergebnis | deterministisch, keine Exception, aber ohne Zusage, dass die Prädikate untereinander konsistent sind |
| Maße (`Area`, `Centroid`, …) | exakt | deterministisch, aber nicht zugesichert; leere oder ungültige Geometrien werfen nie |
| `IsValid` / `Validate` | – | meldet die Befunde |

Die Algorithmen werfen also nie bei geometrisch ungültigen oder leeren Eingaben. Sie werfen nur bei `null`-Argumenten, ungültigen Zahlparametern (`NaN`, negative Toleranz, Gitterweite ≤ 0, …) und Geometrie-Typen, die sie nicht kennen.

## Leere Geometrien

Eine leere Geometrie hat 0 Punkte, Pfade bzw. Ringe. Sie ist überall ein gültiges Argument:

- Die Factory erzeugt für leere Eingaben leere Ergebnisse, nie `null`.
- `Envelope()` einer leeren Geometrie ist `Envelope2D.Empty` (alle Werte `NaN`, `IsEmpty` ist wahr).
- `Centroid()` ist `null`; `Distance` zu einer leeren Geometrie ist `NaN` (eine bewusste Abweichung von NetTopologySuite, das 0 liefert und damit „berührt sich“ und „nichts da“ verwechselbar macht).
- Bei Prädikaten ist nur `Disjoint` wahr, wenn eine der Geometrien leer ist.

## Wo liegt ein Punkt? Locate

`Locate` sagt, wo ein Punkt relativ zu einer Geometrie liegt: `Location.Inside`, `Location.Boundary` oder `Location.Outside` ([ADR 0022](../../adr/0022-locate-envelope-helfer-und-clip.de.md)). Es ist exakt wie die Prädikate und stimmt mit ihnen überein: Bei einem Polygon ist `Inside` gleich `Contains`, `Outside` gleich `Disjoint`, und `Boundary` ist der Rest von `Intersects`.

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))");

Location a = parcel.Locate(new Coordinate(2, 2));    // Inside
Location b = parcel.Locate(new Coordinate(0, 10));   // Boundary
Location c = parcel.Locate(new Coordinate(10, 10));  // Outside: im Loch
```

- **Polygon:** Even-Odd über alle Ringe; ein Loch ist außen, jeder Ring (auch der eines Lochs) ist Rand. **Polylinie:** Rand an einem Endpunkt nach der Mod-2-Regel, sonst innen auf der Linie. **Punkte:** innen, wenn gleich, sie haben keinen Rand. Eine leere Geometrie ist außen.
- Für viele Punkte gegen dieselbe Geometrie nutzt `geometry.Prepare().Locate(point)` einen Segmentindex.
- Ein Punkt mit `NaN` oder unendlicher Koordinate wirft `ArgumentOutOfRangeException`.

## Envelopes

`Envelope2D` hat Helfer für die Arithmetik, die jeder Host selbst schreibt: `Intersects`, `Contains` (eine Envelope oder ein Punkt; der Rand zählt), `Union`, `Intersection`, `Expand` (negativ schrumpft), `Center`. Eine leere Envelope trifft nichts und enthält nichts. `ToPolygon(factory)` macht aus einer Envelope ein Rechteck-Polygon.

## Maße

`Area`, `Length`, `Envelope`, `Centroid` und `Distance` sind Extension-Methoden ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.de.md)). `Area` gibt es für Polygone, `Length` für Polylinien und Polygone (der Umfang); die allgemeinen Overloads auf `IGeometry2D` liefern 0, wenn das Maß nicht zur Dimension passt. `Centroid` kommt als Wert `Coordinate?` ohne Z zurück, nicht als Geometrie, braucht also keine Factory.
