# UnionAll: kaskadierte Vereinigung über den räumlichen Index, ein Gitter für alle Schritte

SpatialNET bekommt **`UnionAll`**, die Vereinigung vieler Geometrien (das „Dissolve“ der GIS-Software), weil `Union` nur zwei Operanden nimmt und eine Schleife darüber quadratisch ist. Es wird **aus dem vorhandenen Overlay-Kern und Interna von `SpatialNET.Extensions`** gebaut: Die Eingaben werden mit dem Sort-Tile-Recursive-Packing des räumlichen Index (ADR 0014) geordnet und dann **in kleinen Gruppen, Ebene für Ebene** vereinigt, damit sich Nachbarn früh treffen und die Zwischenergebnisse klein bleiben (die kaskadierte Vereinigung von JTS). Der Overlay-Kern selbst bleibt eine Zwei-Operanden-Maschine. Alle Schritte teilen **ein Gitter**, das einmal über alle Eingaben aufgelöst wird, damit das Ergebnis nicht davon abhängt, wie die Eingaben gruppiert wurden. Ein echter n-närer Überlauf in einem Zug bleibt für später offen, falls ein Benchmark ihn verlangt.

*English version: [0018-unionall-kaskadiert-gemeinsames-gitter.md](0018-unionall-kaskadiert-gemeinsames-gitter.md)*

## Consequences

- **Ort:** `UnionAll` in der statischen Klasse `Overlays`, `SpatialNET.Extensions`, Namespace `SpatialNET`. Kein neues Paket. Für die Package Validation ist das nur eine Ergänzung.
- **API** (Extension-Methoden; Parameter wie bei `Union`, ADR 0012: Pflichtparameter, dann `fillRule`, `factory`, `gridSize`):
  - `UnionAll(this IEnumerable<IPolygon2D>, FillRule fillRule = EvenOdd, …)` liefert `IPolygon2D`.
  - `UnionAll(this IEnumerable<IPolyline2D>, …)` liefert `IPolyline2D` (genodet wie `Union`: Ein Pfad wird dort getrennt, wo verschiedene Eingabelinien sich treffen oder enden). `UnionAll(this IEnumerable<IMultiPoint2D>, …)` liefert `IMultiPoint2D`.
  - `UnionAll(this IEnumerable<IGeometry2D>, FillRule, …)` liefert `IGeometry2D` und akzeptiert wie das binäre `Union` nur Eingaben **gleicher Dimension** (Punkte und Multipunkte zählen als eine Dimension); gemischte Dimensionen werfen `NotSupportedException`.
- **Eingaben:**
  - Jede Eingabe wird einzeln unter `fillRule` (Standard EvenOdd) gelesen und dann vereinigt, genau wie `Union` seine Operanden liest. Bei einer anderen Füllregel als EvenOdd wird zuerst jede Eingabe einmal unter dieser Regel normalisiert, und die Gruppen werden unter EvenOdd auf diesen gültigen Polygonen vereinigt; so bleibt `FillRule.Negative`, das Außenringe im Uhrzeigersinn erwartet, über die Schritte hinweg korrekt, denn Zwischenergebnisse sind nach ADR 0003 gegen den Uhrzeigersinn orientiert.
  - Leere Geometrien werden übersprungen. Eine leere Folge liefert über die Factory eine leere Geometrie (für die allgemeine Überladung ein leeres Polygon, wie `ConvexHull`). Ein `null`-Element wirft `ArgumentNullException`, eine `null`-Folge ebenso.
  - Eine **einzelne** Eingabe läuft trotzdem durch den Kern, das Ergebnis ist also gültig und normalisiert (wie bei `Simplify`).
  - Die Folge wird einmal gelesen und kopiert; spätere Änderungen an der Quelle wirken nicht (Schnappschuss, ADR 0013).
- **Algorithmus:** Die Eingaben werden per STR-Packing ihrer Envelopes geordnet (das Packing von `SpatialIndex<T>`, als internes Hilfsmittel geteilt), in Vierergruppen geschnitten, und jede Gruppe wird durch rekursive Halbierung mit dem Zwei-Operanden-Overlay vereinigt. Das Ergebnis einer Gruppe wird mit seinem eigenen Envelope zur Eingabe der nächsten Ebene; das wiederholt sich, bis eine Geometrie übrig ist. Der Aufwand sind etwa `n log n` Zwei-Operanden-Overlays wachsender Größe statt `n` Overlays gegen ein immer größeres Ergebnis.
- **Gitter und Determinismus:** Die Gitterweite wird **einmal** über alle Eingaben aufgelöst (explizites `gridSize`, sonst `Precision.DefaultGridSize`, sonst automatisch aus der größten Koordinate aller Eingaben, ADR 0005) und jedem Schritt ausdrücklich übergeben. Jedes Zwischenergebnis liegt dann auf diesem Gitter, die Gruppierung der Eingaben kann das Ergebnis also nur um etwa eine Gitterzelle verändern (eine Kreuzung schon gerundeter Kanten kann anders runden als die Kreuzung der Originalkanten), nie mehr.
  - Dieselbe Eingabefolge liefert immer das identische Ergebnis.
  - Eine andere Reihenfolge der Eingaben liefert ein gültiges Ergebnis derselben Fläche bis auf diese Gitter-Toleranz, aber Knoten, die Reihenfolge der Teilflächen und der Startpunkt eines Rings können abweichen.
- **Z und M:** mitgeführt und interpoliert wie bei `Union` (ADR 0005, ADR 0015); an einer Kreuzung ist deterministisch, aber nicht festgelegt, welcher Eingabe der Wert gehört, weil die Eingaben nach Lage neu gruppiert werden. Zwischenergebnisse entstehen über die Factory; unterstützt sie Z oder M nicht, entfallen sie schon ab dem ersten Schritt, was auch der Endzustand ist.
- **Thread-Sicherheit:** Die Methode hat keinen gemeinsamen Zustand; beliebig viele Threads dürfen sie aufrufen, auch auf denselben Eingaben. Sie selbst läuft sequenziell. Parallele Ebenen wären eine gemessene Optimierung für später und müssten den Determinismus oben erhalten.
- **Nicht Teil dieses Schritts:**
  - ein Join-Helfer auf `SpatialIndex<T>` (Paare überlappender Envelopes), den ADR 0014 als eigenes Thema zurückgestellt hat, und
  - „Dissolve nach Schlüssel“ als eigene Methode: `items.GroupBy(key).Select(g => g.UnionAll())` erledigt das in einer Zeile und steht auf der Konzeptseite.
- **Tests (ADR 0008):**
  - Fixtures unter `samples/unionAll/` tragen die Eingaben als eine `GEOMETRYCOLLECTION` in `a` (der Test liest sie mit `Wkt.ReadAll`); das Orakel ist NTS `OverlayNG`, über die Eingaben gefaltet, mit demselben Gitter. Der Pflicht-Tag-Katalog gilt wie bei `union`.
  - Property-Tests (CsCheck): Das Ergebnis gleicht dem Falten von `Union` in Fläche und Gültigkeit für zufällige Rechtecke und Sterne, jede Reihenfolge der Eingaben liefert dieselbe Fläche, dieselbe Folge liefert bitgleiche Ausgabe, eine einzelne Eingabe wird normalisiert, viele disjunkte und viele überlappende Eingaben, und parallele Aufrufe.
  - Handgeschriebene Fälle: leere Folge, leere und `null`-Elemente, gemischte Dimensionen, Linien und Punkte, Eingaben mit `FillRule.Negative` und `NonZero`.
- **Playground:** vom Deskriptor-Test (ADR 0011) ausgenommen, weil der Playground nur zwei Operanden A und B kennt.
- **Benchmarks:** `UnionAllBenchmarks` vergleicht die Kaskade mit dem Falten `Aggregate(Union)` und der kaskadierten Vereinigung von NTS; er entscheidet auch, ob je ein n-närer Kern in einem Zug nötig ist. Ein Einzellauf (`--quick-unionall`) mit 1.000 Quadraten braucht für die Kaskade 103 ms gegen 7.448 ms für das Falten, wenn die Quadrate getrennt liegen, und 36 ms gegen 289 ms, wenn sie ihre Nachbarn überlappen.

## Considered Options

- Eine Schleife über das binäre `Union` (das Falten) als Umsetzung (verworfen: quadratisch in der Zahl der Eingaben, jeder Schritt verarbeitet das ganze angesammelte Ergebnis neu)
- Ein n-närer Overlay-Kern in einem Zug (verworfen für jetzt: Er verallgemeinert den heikelsten Teil des Projekts von zwei Operanden auf n; die Kaskade braucht dort keine Änderung und lässt sich später ohne API-Änderung ersetzen)
- Nur Polygone (verworfen: Straßennetze und Punktmengen aufzulösen ist ebenso üblich, und die typisierten Überladungen kosten wenig)
- Nur eine allgemeine Methode (verworfen: Die typisierten Überladungen geben dem Compiler den Ergebnistyp, ADR 0012)
- Gemischte Dimensionen, bei denen die niedrigeren wegfallen (verworfen: Die Strict-Semantik aus ADR 0005 und das binäre `Union` verbieten es)
- Jeder Schritt löst sein eigenes automatisches Gitter auf (verworfen: Das Ergebnis hinge von der Gruppierung ab)
- Eine parallele Kaskade von Anfang an (verworfen: erst Determinismus und Einfachheit; die Ebenen sind unabhängig und lassen sich nach einem Benchmark parallelisieren)
- Den Index-Join und „Dissolve nach Schlüssel“ in diesen ADR aufnehmen (verworfen: eigene Themen; das zweite ist eine LINQ-Zeile)
