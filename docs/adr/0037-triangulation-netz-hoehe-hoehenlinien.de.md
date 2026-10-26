# Die Triangulierung als Fläche: Nachbarn, Höhe an einem Punkt, Höhenlinien

`DelaunayTriangles` (ADR 0029) liefert eine flache Liste von Dreiecken, was zum Zeichnen reicht, aber nicht zum Arbeiten damit: Die Dreiecke kennen ihre Nachbarn nicht, und das Dreieck zu finden, das einen Punkt enthält, braucht eine Suche. Ein Geländemodell braucht genau das. Die Klasse **`Triangulation`** behält die Triangulierung als Struktur: Stützpunkte, Dreiecke, die ihre Nachbarn kennen, **`Locate`** (welches Dreieck enthält einen Punkt), **`InterpolateZ`** (die Höhe an einem Punkt) und **`Contour`** (die Linie einer Höhe). Eine Triangulierung von Punkten ist einer von drei Wegen, sie zu erzeugen; die anderen beiden, mit Bruchkanten und von einem Polygon, sind ADR 0038.

*English version: [0037-triangulation-netz-hoehe-hoehenlinien.md](0037-triangulation-netz-hoehe-hoehenlinien.md)*

## Consequences

- **Ort:** die Klasse `Triangulation` in `SpatialNET.Extensions`, Namespace `SpatialNET`. Das Halbkanten-Netz aus ADR 0029 bleibt intern. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **Eine Klasse und keine Erweiterungsmethoden**, weil eine Triangulierung **Zustand ist, der einmal gebaut und oft abgefragt wird**: Ein Geländemodell wird aus tausenden Punkten gebaut und dann nach der Höhe an noch viel mehr Punkten gefragt. Sie ist **unveränderlich und thread-sicher**; der Index für `Locate` wird bei der ersten Frage gebaut und lässt sich gefahrlos teilen.
- **Struktur.** Stützpunkte sind `Coordinate`s in einer schreibgeschützten Liste, und ein Dreieck wird durch seinen Index von 0 bis `TriangleCount` − 1 angesprochen:

  | Mitglied | Bedeutung |
  |---|---|
  | `Vertices` | die Stützpunkte; Z bleibt erhalten, M nicht |
  | `TriangleCount` | die Zahl der Dreiecke; 0 bei weniger als drei verschiedenen oder kollinearen Stützpunkten |
  | `Triangle(t)` | die drei Stützpunkt-Ids, gegen den Uhrzeigersinn |
  | `Neighbour(t, side)` | das Dreieck jenseits der Kante von Stützpunkt `side` zu Stützpunkt `side` + 1 (modulo 3), oder −1 am Rand |
  | `IsConstrained(t, side)` | ob diese Kante eine Zwangskante ist (ADR 0038) |

  Ids sind schlichte `int`, wie in jeder Netz-Bibliothek, und eine Id außerhalb des Bereichs ist eine `ArgumentOutOfRangeException`. Die Nachbarn eines Dreiecks am Rand sind −1, ein Gang über die Fläche braucht also keinen Sonderfall.
- **`FromPoints(geometry)`** ist die Delaunay-Triangulierung der Stützpunkte einer Geometrie, mit den Regeln aus ADR 0029 (X und Y, gleiche Stützpunkte einmal, nicht endliche ignoriert). Der erste Stützpunkt eines (X, Y), der ein Z hat, gibt es vor. Bei zu wenigen oder kollinearen Punkten wirft es nie: Die Triangulierung ist leer.
- **`Locate(point)`** liefert das Dreieck, das den Punkt **auf seinem Rand oder in seinem Inneren** enthält, oder −1. Die Entscheidung ist exakt (der Orientierungstest der Overlays, ADR 0005). Auf einer Kante oder einem Stützpunkt, den Dreiecke teilen, ist es **das mit dem kleinsten Index**, die Antwort ist also deterministisch. Die Suche ist kein Gang von einem Startdreieck, sondern eine Abfrage eines unveränderlichen `SpatialIndex` der Dreiecks-Envelopes (ADR 0014) und dann der exakte Test an den wenigen Kandidaten, was O(log n) ist und keinen Zustand des Aufrufers braucht; viele Threads dürfen gleichzeitig fragen.
- **`InterpolateZ(point)`** ist die Höhe an einem Punkt: Z linear im Dreieck interpoliert, das ihn enthält (die Ebene durch seine drei Stützpunkte). An einem Stützpunkt ist es genau dessen Z (die Schwerpunktgewichte sind ohne Rundung 1 und 0), auf einer Fläche, die eine Ebene ist, ist es bis auf Rundung exakt (ein Eigenschaftstest), und auf einer Kante ist es die lineare Interpolation entlang der Kante, die für beide Dreiecke gleich ist. Es ist **`NaN`** außerhalb jedes Dreiecks oder wenn ein Stützpunkt des Dreiecks kein Z hat; deshalb wirft es nie. Ein nicht endlicher Punkt ist eine `ArgumentOutOfRangeException`.
- **`Contour(z, factory)`** liefert die **Linie einer Höhe** als Polylinie, durch Marschieren durch die Dreiecke: Ein Dreieck, dessen Stützpunkte auf beiden Seiten der Höhe liegen, hat ein Stück der Linie, und die Stücke werden verbunden, wo genau zwei zusammentreffen. Die Regeln:
  - Ein Stützpunkt, dessen Z genau `z` ist, zählt als **darüber**, die Höhenlinie geht also durch ihn und teilt sich dort nicht. Eine ebene Fläche genau auf `z` hat wie üblich keine Höhenlinie.
  - Der Schnitt auf einer Kante wird aus ihren Endpunkten **in der Reihenfolge der Stützpunkt-Ids** berechnet, die beiden Dreiecke, die die Kante teilen, bekommen also denselben Punkt Bit für Bit, und die Stücke fügen sich genau (das Verbinden geschieht über gleiche Koordinaten).
  - Die Stützpunkte der Linie haben **Z = `z`**. Eine Höhenlinie, die zu ihrem Anfang zurückkehrt, ist geschlossen (erster = letzter); eine, die den Rand der Triangulierung erreicht, endet dort.
  - Dreiecke mit einem Stützpunkt ohne Z werden übersprungen. Eine Höhe, die die Fläche nicht erreicht, ergibt eine leere Polylinie.
  - Höhenlinien bei vielen Höhen sind ein Aufruf je Höhe; der Playground macht es für ein Intervall. Eine Glättung der Linien gehört nicht dazu.
- **Als Geometrien:** `ToPolygons` (die Dreiecke als Polygone, mit Z, in der Reihenfolge ihrer Ids, wie `DelaunayTriangles`, aber geordnet), `Edges` (jede Kante einmal als ein Pfad, wie `DelaunayEdges`) und `ConstrainedEdges` (nur die Zwangskanten, ADR 0038).
- **Ausnahmen (ADR 0012):** `ArgumentNullException` bei `null`, `NotSupportedException` bei einer unbekannten Geometrieart, `ArgumentOutOfRangeException` bei einer Id außerhalb des Bereichs oder einem nicht endlichen Punkt oder einer Höhe, `InvalidOperationException` ohne Factory (nur bei den Methoden, die Geometrien erzeugen).
- **Geschwindigkeit.** Einmalmessungen (`--quick-voronoi`, Release-Build) an Punkten mit glatter Höhe und Rauschen und je 100.000 zufälligen Fragen:

  | Punkte | Verteilung | `FromPoints` (ms) | 100.000 × `Locate` (ms) | 100.000 × `InterpolateZ` (ms) | 11 Höhenlinien (ms) |
  |---|---|---|---|---|---|
  | 10.000 | gleichmäßig | 8 | 117 | 132 | 10 |
  | 10.000 | Gitter | 24 | 78 | 77 | 4 |
  | 10.000 | gehäuft | 6 | 99 | 101 | 6 |
  | 100.000 | gleichmäßig | 79 | 208 | 185 | 89 |
  | 100.000 | Gitter | 200 | 142 | 140 | 65 |
  | 100.000 | gehäuft | 79 | 155 | 165 | 63 |

  Das Bauen enthält die Stützpunkte und die Nachbarn; eine Frage dauert etwa 1 bis 2 Mikrosekunden, fast unabhängig von der Größe, wofür der Index da ist.
- **Tests (ADR 0008):** `[Covers]`-Tests. Die Struktur an zufälligen Punkten (Dreiecke gegen den Uhrzeigersinn, Nachbarn, die übereinstimmen und die Kante des anderen nennen, die Fläche ist die der Hülle, Anzahl und Kanten passen zu `DelaunayTriangles` und `DelaunayEdges`); `Locate` gegen eine Brute-Force-Suche für zufällige Punkte innerhalb und außerhalb, auf Stützpunkten und Kanten, und von vielen Threads zugleich; `InterpolateZ` auf einer Ebene (bis auf Rundung exakt), an Stützpunkten, außerhalb und ohne Z; Höhenlinien einer Ebene (eine Gerade, jeder Stützpunkt auf der Höhe), eines Kegels (ein geschlossener Ring, dessen Punkte zur Höhe interpolieren) und für Höhen, die die Fläche nicht erreicht; Index- und Argumentfehler.
- **Playground und Dokumentation:** Deskriptoren für `InterpolateZ` (die Höhe an einem Punkt) und `Contour` (eine Höhe, oder alle Höhen in einem Intervall) mit den Beispielgeländen (ADR 0011); die Konzeptseite „Triangulierung, Voronoi und Gelände“ und ein Cookbook-Rezept.
- **Vorerst nicht dabei:** andere Interpolationen (natürliche Nachbarn, inverse Distanz), Glättung von Höhenlinien, das **Verändern einer Triangulierung** (einen Punkt einfügen oder entfernen; sie wird einmal gebaut), und Qualitätstriangulierungen mit zusätzlichen Punkten. Neigung, Exposition, Fläche, Volumen und das Drapieren einer Linie kamen später, in [ADR 0039](0039-gelaendeanalyse-neigung-flaeche-volumen-drape.de.md).

## Considered Options

- Nur Erweiterungsmethoden (`geometry.InterpolateZ(point)`), die bei jedem Aufruf triangulieren (verworfen: Ein Geländemodell wird oft abgefragt, und es jedes Mal zu bauen würde eine Frage so teuer machen wie ein Modell)
- Eine veränderliche Triangulierung, der Punkte hinzugefügt werden können (verworfen: Sie bräuchte Sperren oder einen einzigen Besitzer, und die Delaunay-Flips würden Teil des Vertrags; eine unveränderliche ist thread-sicher und lässt sich neu bauen)
- `Locate` als Gang von einem Startdreieck (verworfen: Er ist O(√n) und will einen Hinweis des Aufrufers, um schnell zu sein; der Index ist O(log n) und zustandslos)
- Eine Ausnahme, wenn eine Höhe außerhalb der Fläche gefragt wird (verworfen: `NaN` ist die übliche Antwort einer Interpolation, und ein Aufrufer bildet viele Punkte ab; auch die anderen Methoden der Bibliothek antworten „geht nicht“ mit einem leeren Ergebnis oder `NaN`, ADR 0017)
- Die Dreiecke nur als `int[]` von Ids liefern, ohne `Neighbour` (verworfen: Die Nachbarn braucht ein Gang, ein Flutfüllen oder eine Neigungsberechnung, und sie neu zu bauen kostet ein Dictionary)
- Eine Höhenlinie als Liste von Linienstücken ohne Verbinden (verworfen: Eine Höhenlinie ist eine Linie zum Zeichnen und Messen, und die Stücke teilen ihre Endpunkte ohnehin Bit für Bit)
- `Contour`, das Höhen zwischen mehreren Triangulierungen interpoliert (verworfen: nicht Teil; die Fläche ist eine Triangulierung)
- Einen Stützpunkt genau auf der Höhe als darunter zählen (verworfen: Jede Wahl geht, wenn sie durchgehend gilt; es wurde „darüber“ gewählt, und damit teilt sich eine Höhenlinie durch einen Stützpunkt nicht)
