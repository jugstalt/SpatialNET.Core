# UnionAll von Flächen in einem Durchlauf

`UnionAll` (ADR 0018) vereinigte Polygone in einer Kaskade: Gruppen zu vier, Ebene für Ebene, jeder Schritt ein Overlay mit zwei Operanden. ADR 0018 ließ einen Durchlauf in einem Zug offen, „falls ein Benchmark ihn verlangt“. Ein Benchmark eines Hosts hat das getan (gView GIS: 1.000 sich überlappende Blobs mit 32 Stützpunkten, 871 ms in der Kaskade gegen 99 ms für Clipper in einem Lauf): Jede Ebene nodet fast jeden Vertex erneut. **Polygone werden jetzt in einem Overlay aller Eingaben vereinigt.** Die Zwei-Operanden-Maschine wird dafür nicht verallgemeinert: Die Vereinigung braucht keine Windungszahl je Eingabe, nur ob ein Punkt in irgendeiner liegt, und das sagt die Summe der Windungen orientierter Ringe, wie beim Buffer (ADR 0006). Polylinien und Multipunkte behalten die Kaskade.

*English version: [0047-unionall-von-flaechen-in-einem-durchlauf.md](0047-unionall-von-flaechen-in-einem-durchlauf.md)*

## Consequences

- **Keine API-Änderung.** `UnionAll(IEnumerable<IPolygon2D>, …)` und die allgemeine Überladung mit flächigen Eingaben nehmen den neuen Weg; der Vertrag von ADR 0018 (Eingaben unter der Füllregel gelesen, leere übersprungen, ein Gitter über alle Eingaben, eine einzelne Eingabe normalisiert) bleibt.
- **Algorithmus:**
  - Jede Eingabe wird zu **Ringen, die nach ihrer Verschachtelung orientiert sind** (`PolygonStructure`, ADR 0003): Außenringe gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn, eine Insel in einem Loch wieder ein Außenring. Für Ringe, die sich nicht treffen, ist die Windungszahl der Eingabe dann 1 in ihrer Fläche und 0 außerhalb, genau ihre Even-Odd-Lesart.
  - Ob sich die Ringe einer Eingabe treffen, wird exakt geprüft (ein Sweep über ihre Segmente; nur Nachbarn in einem Ring dürfen ihren gemeinsamen Vertex teilen). Eine Eingabe, deren Ringe sich kreuzen, berühren, überlappen oder keine Fläche haben, wird **zuerst vom Overlay normalisiert**, unter EvenOdd, und dann orientiert; ebenso alle Eingaben bei einer anderen Füllregel als EvenOdd (wie bisher, ADR 0018). Eine solche Eingabe kostet ein kleines Overlay mehr; gültige Daten, der übliche Fall, kosten keines.
  - Alle Ringe gehen in **ein Overlay mit `FillRule.Positive`**: Die Summe der Windungen ist genau dort positiv, wo ein Punkt in mindestens einer Eingabe liegt. Das Ergebnis ist gültig und normalisiert wie jedes Overlay-Ergebnis.
- **Ergebnisse:** dieselbe Fläche und dieselben Teile wie die Kaskade, bis auf das Gitter. Die Vertices können sich dort, wo sich Kanten kreuzen, um etwa eine Gitterzelle unterscheiden, weil die Kreuzungen jetzt einmal aus den ursprünglichen Kanten gerundet werden statt aus Zwischenergebnissen; das ist die Toleranz, die ADR 0018 schon für eine andere Gruppierung zuließ. Die Reihenfolge der Teile und der Startvertex eines Rings folgen der Reihenfolge der Eingaben. Dieselbe Folge liefert weiterhin das identische Ergebnis; Z und M stammen an einem Knoten von der ersten Eingabe in der Eingabereihenfolge, die sie dort hat.
- **Geschwindigkeit** (BenchmarkDotNet-Kurzläufe, Blobs mit 32 Stützpunkten in UTM-Metern, teils überlappend; `HostOverlayBenchmarks`):

  | Blobs | Kaskade 0.8.0 (ms) | Kaskade mit den Engine-Verbesserungen (ms) | ein Durchlauf (ms) |
  |---|---|---|---|
  | 100 | 45 | 21 | 7 |
  | 1.000 | 871 | 346 | 118 |

  Die Zahlen der Kaskade enthalten schon den schnelleren Overlay-Kern desselben Releases (Hash für deckungsgleiche Ringe, Gleitkomma-Filter für Kreuzungen, Sweep in Arrays).
  Mit den Quadraten von `UnionAllBenchmarks` (`--quick-unionall`) brauchen 1.000 getrennte Quadrate 12 ms statt der 103 ms aus ADR 0018, 1.000 überlappende 27 ms statt 36 ms. Der Speicher sinkt mit der Zeit: 58 MB statt 762 MB für die 1.000 Blobs.
- **Grenzen:** Ein Overlay hält alle Vertices zugleich, der Speicher ist also der aller Eingaben zusammen statt der einer Gruppe; für sehr viele Eingaben bleibt das linear. Für Polylinien und Multipunkte bleibt die Kaskade, weil das Noding dort die Enden jeder Eingabelinie erhalten muss (ADR 0018), was eine Summe von Windungen nicht trägt.
- **Tests (ADR 0008):** Die Tests von ADR 0018 laufen unverändert (Fixtures gegen die NTS-Faltung auf demselben Gitter, Property-Tests gegen die Faltung von `Union` in Fläche und Gültigkeit, jede Reihenfolge dieselbe Fläche, dieselbe Folge bitgleich, Füllregeln, Z und M, parallele Aufrufe), dazu der Stresstest `UnionAll_equals_the_fold_of_Union`.

## Considered Options

- Die Kaskade behalten und nur die Engine beschleunigen (verworfen: auch mit dem schnelleren Kern nodet die Kaskade jeden Vertex einmal je Ebene und bleibt etwa dreimal langsamer als ein Durchlauf)
- In jedem Kaskadenschritt Teile überspringen, deren Envelope fern vom anderen Operanden liegt (die „Overlap Union“ von NTS; verworfen: behält die Ebenen, braucht eine Hülle über die Envelopes, um gültig zu bleiben, und ändert das Ergebnis in Grenzfällen ebenfalls)
- Eine allgemeine n-äre Engine mit einer Windungszahl je Eingabe (verworfen: der heikelste Teil des Projekts verallgemeinert, für eine Vereinigung, die nur die Summe braucht)
- Jede Eingabe vor dem Durchlauf vom Overlay normalisieren lassen (verworfen: ein Overlay je Eingabe; die exakte Prüfung auf Ringe, die sich nicht treffen, macht es für gültige Daten unnötig)
