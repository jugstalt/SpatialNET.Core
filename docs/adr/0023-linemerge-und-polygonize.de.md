# LineMerge und Polygonize

Zwei Operationen schließen die Lücke zwischen Linien und Flächen. **`LineMerge`** verbindet die Pfade einer Polylinie zu längeren Pfaden, wo genau zwei Pfadenden zusammentreffen; das tut `Union` von Linien bewusst nicht (ADR 0005, ADR 0018: Ein Pfad wird getrennt, wo verschiedene Eingabelinien sich treffen oder enden). **`Polygonize`** macht aus einer Polylinie die **Flächen, die sie umschließt**: Jede beschränkte Fläche der Anordnung der Linien wird zu einem Polygon, was zusammen mit `Cut` eine vollständige Zerlegung einer Fläche durch Linien ergibt. Ein öffentliches `Node` gibt es nicht: `Union` einer Polylinie mit sich selbst (zum Beispiel `new[] { line }.UnionAll()`) nodet die Linien schon an ihren Kreuzungen und entfernt Doppelungen, und `Polygonize` nodet intern.

*English version: [0023-linemerge-und-polygonize.md](0023-linemerge-und-polygonize.md)*

## Consequences

- **Ort:** `LineMerge` in der statischen Klasse `Constructions` (ein geometrisches Ergebnis über die Factory, ADR 0019), `Polygonize` in `Overlays` (es führt den Overlay-Kern aus), beide in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Package Validation ist das nur eine Ergänzung.
- **`LineMerge(this IPolyline2D line, bool directed = false, IGeometryFactory? factory = null)`** liefert `IPolyline2D`:
  - Ein Knoten verbindet zwei Pfade nur, wenn dort **genau zwei Pfadenden** zusammentreffen (exakte Gleichheit von X und Y, keine Toleranz; wer sie braucht, rastet die Daten vorher ein). Ein Knoten, an dem drei oder mehr Enden zusammentreffen oder an dem ein einzelnes Ende liegt, hält seine Pfade getrennt.
  - Ein Zyklus von Pfaden wird zu **einem geschlossenen** Pfad. Ein Pfad, der sich schon selbst schließt, bleibt, wie er ist, es sei denn, ein anderer Pfad endet an seinem Startpunkt (dann treffen drei Enden zusammen, und nichts wird verbunden).
  - **Richtung:** Standardmäßig wird ein Pfad umgekehrt, wo nötig, und die Richtung des ersten Pfades in der Eingabereihenfolge setzt sich für den verbundenen Pfad durch. Mit `directed: true` werden zwei Pfade nur verbunden, wenn das Ende des einen der Anfang des anderen ist und am Knoten genau ein Pfad endet und genau einer beginnt.
  - `LineMerge` **nodet nicht**. Sich kreuzende Linien bleiben, wie sie sind; dafür vorher `Union`.
  - Pfade mit weniger als zwei verschiedenen Punkten entfallen (sie sind keine Linien, wie bei `MakeValid`, ADR 0020). Z und M der Koordinaten bleiben erhalten, an einem verbundenen Stützpunkt die des früheren Pfades. Das Ergebnis ist deterministisch: Die Pfade kommen in der Reihenfolge, in der ihr erster Pfad in der Eingabe steht.
- **`Polygonize(this IPolyline2D line, IGeometryFactory? factory = null, double? gridSize = null)`** liefert `IReadOnlyList<IPolygon2D>`:
  - Die Linien werden zuerst genodet, per Snap-Rounding auf dem Gitter wie bei `Union` (`gridSize`, sonst `Precision.DefaultGridSize`, sonst automatisch, ADR 0005); die Stützpunkte des Ergebnisses liegen also auf dem Gitter. Sich überlappende Doppelungen fallen zu einer Kante zusammen.
  - **Jede beschränkte Fläche** wird zu einem eigenen Polygon, in einer **flachen Liste** wie die Stücke von `Cut` (ADR 0007): eine Fläche mit den Komponenten von Linien in ihrem Inneren als Löchern (Außenring gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn, ADR 0003), eine Insel in einem Loch eine eigene Fläche. Benachbarte Flächen sind getrennte Polygone, die ihre Kante teilen. Eine Fläche, die sich an einem Stützpunkt selbst berührt, ergibt ein Polygon je Lappen, jedes Polygon ist also gültig.
  - **Dangles** (Kanten mit freiem Ende) und **Cut Edges** (Kanten, die auf beiden Seiten dieselbe Fläche haben, etwa eine Brücke zwischen zwei Teilen) umschließen keine Fläche und entfallen; eine Fläche wird von ihnen nicht zerschnitten. Die unbeschränkte äußere Fläche wird nie geliefert.
  - Eine Polylinie, die nichts umschließt, ergibt eine leere Liste. Die Reihenfolge der Liste ist deterministisch.
  - Z und M der Stützpunkte folgen den Regeln der Overlays (ADR 0005, ADR 0015).
  - Der Ergebnistyp ist die schlichte Liste. Ein Ergebnis mit den Dangles und Cut Edges (der JTS-`Polygonizer` hat sie) kann ohne Bruch folgen.
- **Exceptions (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei ungültigem `gridSize`, `InvalidOperationException` ohne Factory. Leere und entartete Linien werfen nie.
- **Umsetzung:** `Polygonize` baut aus den Linien den planaren Graphen des Overlay-Kerns und setzt die Polygone aus allen seinen Flächen zusammen, mit dem Zusammenbau, den die Overlays für ihre Ergebnisse nutzen. Ein geschlossener Ring aus Linien braucht keinen Sonderfall.
- **Tests (ADR 0008):**
  - `lineMerge`- und `polygonize`-Fixtures mit dem Pflicht-Tag-Katalog (Ausnahmen in `_catalog.json` begründet: Linien haben keine Ringe, Löcher und Verschachtelung im Sinne der Tags). `LineMerge` wird mit dem NTS-`LineMerger` verglichen (als Mengen von Linien, jede in kanonischer Richtung und, bei Ringen, mit kanonischem Start), `Polygonize` mit dem NTS-`Polygonizer` auf denselben, von NTS genodeten Linien (als Mengen von Polygonen nach Fläche und symmetrischer Differenz, weil sich die Knoten um das Gitter unterscheiden).
  - Property- und Stresstests: Jedes Polygon ist gültig; die Flächen überlappen sich nicht und ihre Flächeninhalte addieren sich; jede Kante einer Fläche gehört zu höchstens zwei Flächen; `LineMerge` erhält die Gesamtlänge und die Menge der Stützpunkte, verbindet nie an einem Knoten mit drei Enden, und zweimaliges Verbinden ändert nichts.
- **Playground und Dokumentation:** Deskriptoren für beide (ADR 0011; `Polygonize` zeigt die Liste der Flächen wie `Cut`), ein Abschnitt in der Konzeptseite zu Overlays und Konstruktionen, Glossareinträge.

## Considered Options

- Ein öffentliches `Node` (verworfen: `Union` der Linie mit sich selbst leistet es, und `Polygonize` nodet intern; ein Name mehr für denselben Schritt)
- `LineMerge`, das zuerst nodet (verworfen: Verbinden und Noden sind verschiedene Schritte, und Noden rundet aufs Gitter, was `LineMerge` nicht tut)
- `LineMerge` mit Snap-Toleranz (verworfen: Exakte Gleichheit ist vorhersehbar; Einrasten ist ein eigener, datenabhängiger Schritt)
- Nur gerichtetes Verbinden (verworfen: Die meisten Liniendaten haben keine sinnvolle Richtung, und der ungerichtete Fall ist, was JTS standardmäßig bietet)
- `Polygonize` liefert ein mehrteiliges Polygon aller Flächen (verworfen: Benachbarte Flächen verlören ihre Identität; die flache Liste von `Cut` ist die etablierte Form)
- Ein Ergebnistyp mit Dangles, Cut Edges und ungültigen Ringen (verworfen für jetzt: Noch verlangt es niemand, und es lässt sich ohne Bruch ergänzen)
- `Polygonize`, das genodete Eingabe erwartet und sonst wirft oder Unsinn liefert (verworfen: Noden auf dem Gitter ist der eigene Schritt der Bibliothek, und das Ergebnis darf nicht davon abhängen, dass die Eingabe vorbereitet ist)
