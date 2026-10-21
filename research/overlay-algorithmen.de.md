# Overlay-Algorithmen und Präzisionsmodelle: Optionen

*English version: [overlay-algorithmen.md](overlay-algorithmen.md)*

> Research zu Ticket #11. Die Entscheidung fällt später gemeinsam mit dem Menschen in Ticket #5. Diese Empfehlung ist nicht bindend.

## Rahmenbedingungen aus dem Repo

- **ADR 0001**: Jeder **Algorithmus** wird selbst implementiert und arbeitet direkt gegen die **Geometrie-Interfaces**. Intern gibt es weder NetTopologySuite noch Clipper2. NTS darf nur im Testprojekt als Vergleichs-Orakel vorkommen.
- Zielplattform ist net10.0. Gerechnet wird planar in 2D, Z wird an neuen Knoten interpoliert.
- Die Eingaben kommen read-only über Interfaces vom **Host**. Ergebnisse entstehen über die **Geometrie-Factory**. Ein Algorithmus darf also intern beliebige eigene Arbeitsstrukturen aufbauen (Kantenlisten, Sweep-Events, Knotengraph), solange er die Host-Geometrien nicht verändert.
- Gebraucht werden Intersection, Union, Difference, SymDifference und Clip, für Polygone/Multipolygone mit Löchern und zusätzlich für Polylinie gegen Polygon.

Wir übernehmen also keinen fremden Code. Fremde Implementierungen dienen nur als Lesestoff und als Testfall-Quelle, deshalb ist ihre Lizenz relevant (z.B. wenn wir Testfälle übernehmen oder Code eng nachbauen).

---

## 1. Kandidaten für Overlay-Algorithmen

### 1.1 Weiler-Atherton (1977)

- **Idee**: Man läuft die Ränder ab und springt an Schnittpunkten von der Subjekt- auf die Clip-Kontur. Dazu braucht man Ein- und Austrittsmarker.
- **Robustheit**: Schwach. Berührende Ecken, kollineare Kanten und Ecken genau auf einer Kante erzeugen mehrdeutige Ein/Aus-Klassifikationen. Die Originalarbeit behandelt diese Fälle kaum.
- **Löcher/Multipolygone**: Grundsätzlich möglich, aber man muss die Orientierungen sauber führen und Löcher separat zuordnen.
- **Aufwand**: Gering bis mittel für den Normalfall. Für die Randfälle ist der Aufwand hoch und kaum abschätzbar.
- **Referenz**: Weiler, K., Atherton, P.: *Hidden surface removal using polygon area sorting*, SIGGRAPH 1977. Eine moderne, gepflegte Referenzimplementierung ist nicht bekannt.
- **Bewertung**: Historisch wichtig, für eine robuste GIS-Bibliothek nicht zu empfehlen.

### 1.2 Greiner-Hormann (1998) mit den Erweiterungen von Foster/Hormann/Popa (2019)

- **Idee**: Zwei doppelt verkettete Vertexlisten, Schnittpunkte in beide einfügen, Entry/Exit-Flags setzen, dann traversieren. Das Verfahren ist sehr kompakt.
- **Robustheit**: Das Original scheitert an Degenerationen (Vertex auf Kante, überlappende Kanten) und umgeht sie, indem es Vertices perturbiert. Das verfälscht das Ergebnis. Foster, Hormann und Popa (2019) erweitern das Verfahren um eine saubere Klassifikation degenerierter Schnittpunkte ohne Perturbation. Das gilt allerdings nur für **nicht selbstschneidende** Polygone (mehrere und verschachtelte Komponenten sind erlaubt). Ein älterer Ansatz (Foster/Overfelt 2012, arXiv) wurde laut arXiv-Vermerk zurückgezogen, weil er nicht alle Degenerationen abdeckt.
- **Löcher/Multipolygone**: Mit der Erweiterung ja. Für die Zuordnung der Ausgabe (welcher Ring ist Loch von welcher Schale) braucht man aber eine eigene Nachbearbeitung.
- **Schnittberechnung**: Naiv O(n·m). Für große Geometrien braucht man einen zusätzlichen räumlichen Index.
- **Aufwand**: Gering (Original) bis mittel (Erweiterung 2019, viele Fallunterscheidungen).
- **Referenzen**:
  - Greiner, G., Hormann, K.: *Efficient clipping of arbitrary polygons*, ACM TOG 17(2), 1998.
  - Foster, E. L., Hormann, K., Popa, R. T.: *Clipping simple polygons with degenerate intersections*, Computers & Graphics: X, 2019. [TU Delft](https://research.tudelft.nl/en/publications/clipping-simple-polygons-with-degenerate-intersections-2)
  - C++-Implementierung: [Fribur/FosterHormannPopaClipping](https://github.com/Fribur/FosterHormannPopaClipping)
  - Zurückgezogener Vorläufer: [arXiv 1211.3376](https://arxiv.org/abs/1211.3376)
  - Eine GPU-Parallelisierung zeigt, dass das Verfahren aktiv weiterentwickelt wird: [GH GPU-Paper](https://www.cs.mu.edu/~satish/papers/GH_GPU_Camera_ready.pdf)
- **Bewertung**: Attraktiv, weil es klein ist. Die Einschränkung auf selbstschnittfreie Eingaben und die fehlende Mengen-Semantik für Linien sind aber Nachteile.

### 1.3 Vatti (1992), Basis von Clipper/Clipper2

- **Idee**: Ein Scanline-Verfahren über Scanbeams (horizontale Streifen zwischen Vertex-y-Werten). Es arbeitet mit lokalen Minima/Maxima, einer Active Edge Table und Fill-Regeln (EvenOdd, NonZero, Positive, Negative).
- **Robustheit**: In Clipper2 sehr gut, aber nur, weil intern **Int64-Koordinaten** verwendet werden. Auch `ClipperD` skaliert intern auf Integer. Der Clipper-Autor schreibt selbst, dass winzige Selbstschnitt-Artefakte und fast flächenlose Polygone durch Rundung „sehr gelegentlich" vorkommen können ([Clipper2 Robustness](https://angusj.com/clipper2/Docs/Robustness.htm)).
- **Löcher/Multipolygone/Selbstschnitte**: Ja, durch die Fill-Regeln auch für beliebig selbstschneidende Eingaben. Optional entsteht ein PolyTree mit der Hierarchie aus Schalen und Löchern.
- **Polylinien**: Clipper2 kann offene Pfade gegen Polygone clippen.
- **Aufwand**: Hoch. Clipper2 ist nach über 10 Jahren Entwicklung voller Spezialfälle (horizontale Kanten, Join-Logik, Horizontal-Processing).
- **Referenzen**:
  - Vatti, B. R.: *A generic solution to polygon clipping*, CACM 35(7), 1992.
  - [Clipper2](https://github.com/AngusJohnson/Clipper2), Boost Software License 1.0, C++/C#/Delphi. [Clipper64-Doku](https://angusj.com/clipper2/Docs/Units/Clipper.Engine/Classes/Clipper64/_Body.htm)
- **Bewertung**: Industriell bewährt. Ein eigener Nachbau ist aber aufwändig, und das Integer-Gitter verändert die Koordinaten (siehe Abschnitt 2).

### 1.4 Martinez-Rueda-Feito (2009 und 2013)

- **Idee**: Ein Bentley-Ottmann-artiger Plane-Sweep über Segment-Endpunkte (Event-Queue plus Sweep-Line-Status als balancierter Baum). Jedes Segment trägt die Flags `inOut`, `otherInOut` und `inResult`. Die Auswahl der Ergebnissegmente richtet sich nach der Operation, danach werden die Ergebniskonturen verkettet. Die Version von 2013 vereinfacht den Algorithmus und berechnet zusätzlich die **Loch-/Schalen-Hierarchie** der Ausgabe.
- **Robustheit**: Überlappende (kollineare) Kanten werden explizit behandelt: Sie werden zerlegt und als `SAME_TRANSITION` oder `DIFFERENT_TRANSITION` markiert. Berührende Vertices sind abgedeckt, Selbstschnitte innerhalb eines Operanden nur eingeschränkt. Die Schwachstelle sind Gleitkomma-Schnittpunkte. Ein berechneter Schnittpunkt liegt nicht exakt auf beiden Segmenten, und das kann die Sortierordnung im Sweep zerstören. In der Folge treten Endlosschleifen oder falsche Ergebnisse auf. mfogels `polygon-clipping` führt dafür eigens ein Iterationslimit ein ([README](https://arches.lcc.ufmg.br/static/polygon-clipping/README.md)).
- **Löcher/Multipolygone**: Ja. Die Ausgabehierarchie gibt es ab der Version 2013.
- **Laufzeit**: O((n+k) log n), skaliert also gut.
- **Aufwand**: Mittel. Er ist überschaubar, wenn man eine bestehende Implementierung als Lesestoff nutzt. Der Kern umfasst etwa 1.000 bis 2.000 Zeilen C#.
- **Referenzen**:
  - Martínez, F., Rueda, A. J., Feito, F. R.: *A new algorithm for computing Boolean operations on polygons*, Computers & Geosciences 35(6), 2009.
  - Martínez, F., Ogayar, C., Jiménez, J. R., Rueda, A. J.: *A simple algorithm for Boolean operations on polygons*, Advances in Engineering Software 64, 2013, S. 11–19.
  - [w8r/martinez](https://github.com/w8r/martinez) (JS, MIT). Laut README werden Multipolygone, Löcher, selbstschneidende und degenerierte Polygone mit überlappenden Kanten unterstützt. ([README](https://cdn.jsdelivr.net/npm/martinez-polygon-clipping@0.9.0/README.md))
  - [mfogel/polygon-clipping](https://github.com/mfogel/polygon-clipping) (JS, MIT): eine robustere Weiterentwicklung mit Fokus auf Korrektheit und einer großen Testfall-Sammlung.
  - [21re/rust-geo-booleanop](https://github.com/21re/rust-geo-booleanop) (Rust, MIT)
  - [SixLabors/PolygonClipper](https://github.com/SixLabors/PolygonClipper) (**C#**, Six Labors Split License, also keine Standardlizenz). Für die Boolean-Operationen nutzt es Martínez-Rueda, für die Normalisierung einen Vatti/Clipper2-artigen Ansatz, und es rechnet mit double. Für uns ist das der nächstliegende Lesestoff in derselben Sprache. Wegen der Lizenz sollte man sich aber nicht eng daran orientieren.
- **Bewertung**: Unter den „Polygon-only"-Verfahren der beste Kompromiss aus Aufwand, Leistung und Funktionsumfang. Robust wird er aber nur mit sauberen Prädikaten und kontrollierter Schnittpunkt-Rundung.

### 1.5 polybooljs-artiger Segment-Sweep (velipso)

- **Idee**: Ebenfalls „somewhat based on F. Martinez (2008)". Jedes Segment trägt Fill-Annotationen (`myFill.above/below`, `otherFill.above/below`). Zuerst werden beide Eingaben in einem gemeinsamen Sweep zerlegt („combine"). Die Boolesche Operation ist danach nur noch eine Auswahl über eine 16er-Tabelle der Fill-Zustände. Daraus folgt ein wichtiger Vorteil: Einmal zerlegte Eingaben lassen sich für mehrere Operationen wiederverwenden.
- **Robustheit**: Der Algorithmus arbeitet durchgängig mit **Epsilon-Vergleichen** (Standard 1e-10, global einstellbar). Koinzidente Segmente werden behandelt (volle Überlappung, gemeinsame Vertices, Segment in Segment), und überflüssige Vertices werden entfernt. Ist das Epsilon falsch gewählt, entstehen Null-Längen-Segmente, und es wird ein Fehler geworfen. Ein absolutes Epsilon ist allerdings maßstabsabhängig. Für GIS-Koordinaten (Grad gegenüber Metern im Bereich 10^6) muss es pro Koordinatenbereich gewählt werden.
- **Löcher/Multipolygone/Selbstschnitte**: Ja. Die Region-Semantik ist EvenOdd-artig.
- **Aufwand**: Mittel. Das Datenmodell ist sehr aufgeräumt und didaktisch gut.
- **Referenzen**: [velipso/polybooljs](https://github.com/velipso/polybooljs) (MIT, nicht mehr gepflegt, Nachfolger `@velipso/polybool` in TypeScript), Dart-Port [polybool](https://pub.dev/packages/polybool) (BSD-3).
- **Bewertung**: Didaktisch das klarste Sweep-Modell. Das Epsilon-Modell muss man aber durch etwas Besseres ersetzen.

### 1.6 JTS/NTS OverlayNG (Noding + Topologiegraph)

- **Idee**: Kein reiner Clipping-Algorithmus, sondern eine Pipeline:
  1. Alle Kanten beider Eingaben werden gemeinsam **genodet**, also an allen Schnittpunkten zerlegt. Das geschieht mit einem austauschbaren *Noder*, z.B. Snap-Rounding, MCIndex oder Snapping.
  2. Aus den Kanten entsteht ein planarer **Topologiegraph**. Kollineare Duplikate werden dabei zusammengeführt, und jede Kante bekommt ein Label mit der Lage (innen/außen/auf dem Rand) bezüglich beider Eingaben.
  3. Die Labels werden propagiert, und für Kanten ohne Label gibt es einen Point-in-Polygon-Test.
  4. Ergebniskanten werden nach Operation ausgewählt, Ringe gebaut, Löcher zugeordnet und Linien bzw. Punkte extrahiert.
- **Robustheit**: Am höchsten von allen Kandidaten. Mit einem **Fixed-PrecisionModel** und dem Snap-Rounding-Noder rechnet OverlayNG laut Doku robust (bis etwa 13 signifikante Dezimalstellen). Mit Floating-Precision (MCIndexNoder) ist es *nicht* voll robust und kann eine `TopologyException` werfen. `OverlayNGRobust` fängt das heuristisch ab: zuerst Floating, dann Snapping mit automatisch gewählter, wachsender Toleranz, zuletzt Snap-Rounding. ([OverlayNG Javadoc](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/operation/overlayng/OverlayNG.html), [OverlayNGRobust.h in GEOS](https://gemfury.com/flet/python:flet-libgeos/-/content/include/geos/operation/overlayng/OverlayNGRobust.h))
- **Löcher, Multipolygone, Selbstberührung, gemischte Dimensionen**: Ja. Linie gegen Polygon und Linie gegen Linie sind Teil desselben Frameworks. Ein Strict-Mode sorgt für homogene Ergebnisdimensionen und verwirft Kollaps-Linien und -Punkte.
- **Aufwand**: Hoch. Es gibt viele Komponenten: Noder, Kantenmerge, Graph, Labelling, RingBuilder, LineBuilder, Hole-Assignment und Spatial-Index. Dafür sind die Teile wiederverwendbar, z.B. für Validierung, Buffer, Polygonize und Linien-Overlay.
- **Referenzen**:
  - [JTS](https://github.com/locationtech/jts) (EPL 2.0 / EDL 1.0) und [NTS](https://github.com/NetTopologySuite/NetTopologySuite) (BSD-3)
  - Martin Davis' Ankündigung auf jts-dev, 2019: [msg00261](https://www.eclipse.org/lists/jts-dev/msg00261.html)
  - GEOS 3.9 / PostGIS 3.1: [Crunchy Data Blog](https://www.crunchydata.com/blog/waiting-for-postgis-3.1-geos-3.9)
  - Bekannte Randfall-Bugs: [GEOS #942](https://github.com/libgeos/geos/issues/942), [GEOS-Trac #1051](https://trac.osgeo.org/geos/ticket/1051)
- **Bewertung**: Robustheit und Funktionsumfang sind „Goldstandard". Wichtig für uns: Es ist genau das Verhalten unseres **Test-Orakels**. Je ähnlicher unsere Semantik der von OverlayNG ist, desto einfacher werden die Vergleichstests.

---

## 2. Präzisionsmodelle

| Modell | Prinzip | Vorteile | Nachteile / Folgen |
|---|---|---|---|
| **Floating ohne Toleranz** | Rohes double überall | Keine Koordinatenänderung | Orientierungstests können „lügen". Topologie wird inkonsistent, Sweeps geraten in Endlosschleifen, es entstehen fehlende oder doppelte Kanten |
| **Floating mit Epsilon** (polybooljs) | Vergleiche mit `abs(a-b) < eps` | Einfach, fängt viele Fast-Degenerationen ab | Epsilon ist maßstabsabhängig und nicht transitiv (a≈b, b≈c, aber a≉c). Es gibt keine Garantie, nur Verschiebung der Fehlerfälle |
| **Robuste Prädikate** (Shewchuk) | Adaptive, exakte Vorzeichen von `orient2d` (und `incircle`). Die exakte Arithmetik greift nur, wenn das schnelle Ergebnis unsicher ist | Exakte Entscheidungen auf den Eingabekoordinaten, praktisch so schnell wie double, Code ist Public Domain | Löst nur das *Prädikat*-Problem. **Konstruierte** Schnittpunkte sind weiterhin gerundet und liegen nicht exakt auf den Segmenten. Folgeprädikate mit diesen Punkten sind wieder unsicher |
| **Fixed-Precision-Grid** (Clipper: Int64) | Alle Koordinaten auf ein ganzzahliges Gitter skalieren, Schnittpunkte auf das Gitter runden | Prädikate sind auf Integern exakt (mit 128-Bit-Zwischenwerten), deterministisch und schnell | Die Ausgabe ändert sich in jeder Koordinate. Durch Rundung kann es Mini-Selbstschnitte und „Splitter" geben (Clipper rät, keine Fast-Null-Flächen zu erwarten). Die Skalierung muss pro Datensatz gewählt werden |
| **Snap-Rounding** (Hobby 1999, Goodrich et al. 1997, Hershberger 2013) | Gitter mit „Hot Pixels". Jeder Vertex und Schnittpunkt wird auf seine Pixelmitte gezogen, und jedes Segment, das ein Hot Pixel durchquert, wird durch dessen Mitte umgeleitet | **Beweisbar** konsistente Topologie: Die Ausgabe ist ein korrekt genodetes Arrangement, und der Abstand jedes Segments zum Original ist ≤ eine halbe Pixeldiagonale. Diesen Weg nimmt JTS/GEOS. Hershbergers *Stable Snap Rounding* verhindert, dass nachträgliches Re-Snapping weitere Drift erzeugt. *Iterated Snap Rounding* (Halperin/Packer) hält Vertices weiter von fremden Kanten weg | Koordinaten werden gerundet. Zusätzliche Knoten auf durchquerten Kanten. Mittel bis hoch im Aufwand (Hot-Pixel-Index). Kleine Strukturen können kollabieren, die Dimension sinkt (Polygon wird zur Linie), was der Strict-Mode behandeln muss |
| **Snapping-Noder mit Toleranz** (JTS SnappingNoder) | Vertices innerhalb einer Toleranz werden aufeinander gezogen, ohne festes Gitter | Behält die Originalkoordinaten weitgehend bei | Heuristisch, nicht garantiert. In JTS nur ein Fallback-Schritt |
| **Exakte Arithmetik** (rational/BigInteger, CGAL-Stil) | Schnittpunkte als exakte Rationalzahlen | Mathematisch exakt | Langsam, Speicherwachstum. Die Ausgabe muss am Ende ohnehin auf double gerundet werden, und dann steht man wieder vor dem Rundungsproblem |

Quellen: Shewchuk, J. R.: *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, Discrete & Computational Geometry 18, 1997 ([Abstract und Code](https://people.eecs.berkeley.edu/~jrs/papers/robust-predicates.abstract), [DOI](https://www.doi.org/10.1007/PL00009321)). Ports: [robust-predicates (JS)](https://npmjs.com/package/robust-predicates), [robust (Rust)](https://docs.rs/robust). Eine gepflegte C#-Version ist nicht bekannt, ein eigener Port von `predicates.c` ist aber klein und lizenzfrei. Hobby, J. D.: *Practical segment intersection with finite precision output*, CGTA 13(4), 1999. Hershberger, J.: *Stable snap rounding*, CGTA 46(4), 2013. Goodrich, Guibas, Hershberger, Tanenbaum: *Snap rounding line segments efficiently in two and three dimensions*, SoCG 1997 ([Folien](https://www.uni-trier.de/fileadmin/fb4/prof/INF/DEA/Seminar0708/Snap_goodrich.pdf)). [Iterated Snap Rounding](https://www.cgl.cs.tau.ac.il/projects/iterated-snap-rounding/). [CGAL 2D Snap Rounding](https://doc.cgal.org/5.3/Snap_rounding_2/citelist.html). [GEOS SnapRoundingNoder.h](https://gemfury.com/flet/python:flet-libgeos/-/content/include/geos/noding/snapround/SnapRoundingNoder.h).

### 2.1 Kernaussage zur Präzision

Robuste Prädikate sind notwendig, aber nicht hinreichend. Das eigentliche Problem eines Overlays sind die **konstruierten Schnittpunkte**. Es gibt nur zwei saubere Auswege:

1. Ein **Rundungsmodell mit Topologie-Garantie** (Snap-Rounding auf ein Gitter). Das ist der JTS-Weg mit Fixed-PrecisionModel.
2. Exakte Arithmetik. Für eine Bibliothek, die double-Interfaces bedient, ist das unverhältnismäßig.

Floating mit Toleranz bleibt eine Heuristik, die meistens funktioniert und gelegentlich nicht.

### 2.2 Folgen für die Ergebnisstabilität

- **Gitter oder Snap-Rounding**: Die Ergebnisse sind deterministisch und topologisch gültig. Ausgabekoordinaten liegen aber auf dem Gitter und unterscheiden sich von den Eingabekoordinaten um bis zu eine halbe Gitterweite. Die Gitterweite muss als API-Parameter vom Host kommen, z.B. als „Precision/Scale" oder Toleranz in Karteneinheiten, oder sie wird aus der Koordinatengröße abgeleitet (wie JTS-`PrecisionUtil`). Für GIS-Daten ist ein Gitter von 1e-6 bis 1e-9 in Karteneinheiten fachlich meist unkritisch.
- **Floating**: Die Originalkoordinaten bleiben erhalten. Bei nahezu degenerierten Eingaben (fast kollinear, Abstände im Bereich von 1 ULP) ist aber mit Fehlschlägen oder ungültigen Ergebnissen zu rechnen. Eine Rückfall-Kaskade wie bei `OverlayNGRobust` ist dann Pflicht.
- **Z-Interpolation**: Bei Snap-Rounding verschieben sich Knoten. Z sollte deshalb auf dem *Originalsegment* interpoliert werden, also über den Parameter t des ungerundeten Schnitts, und nicht aus der gerundeten Position. Das ist unabhängig vom Algorithmus und lässt sich gut testen.

### 2.3 Folgen für Tests gegen NTS

- **Gleiches Präzisionsmodell auf beiden Seiten**: Rechnen wir mit Gitter s, sollte das Orakel `OverlayNG` mit `new PrecisionModel(s)` laufen. Dann sind auch die Koordinaten weitgehend vergleichbar. Eine exakte Gleichheit ist trotzdem nicht garantiert, weil Hot-Pixel-Reihenfolge und Knotenwahl abweichen können.
- **Nie auf Koordinatengleichheit testen**, sondern auf:
  1. `IsValid` des Ergebnisses
  2. Flächengleichheit mit relativer Toleranz
  3. Symmetrische Differenz zum Orakel-Ergebnis: Ihre Fläche muss kleiner als ε·Gesamtfläche sein. Diese Prüfung rechnet NTS selbst.
  4. Hausdorff-Distanz kleiner als Gitterweite × k
  5. Topologische Kennzahlen: Anzahl Polygone, Anzahl Löcher, Dimension
- **Normalisierung vor Vergleichen**: Ringstart, Orientierung und Reihenfolge der Teile unterscheiden sich, deshalb beide Seiten mit `Normalize()` vergleichen.
- **Semantik-Unterschiede** gezielt festlegen: Was passiert mit Berührungen? Ergibt die Intersection zweier sich berührender Polygone eine Linie oder einen Punkt, oder (Strict-Mode) leer? Wie werden kollabierende Splitter behandelt? Am einfachsten ist es, die **OverlayNG-Strict-Semantik** zu übernehmen. Dann ist das Orakel direkt nutzbar.
- **Testkorpora**: Die JTS-TestRunner-XML-Suites (`modules/tests/src/test/resources/testxml`, EPL/EDL) und die Fixtures von mfogel/polygon-clipping (MIT) enthalten viele dokumentierte Degenerationen. Man sollte sie als *Eingaben* übernehmen und das NTS-Ergebnis als Soll verwenden. Dazu kommen **Property-basierte Tests**:
  - A∪B ⊇ A
  - Fläche(A∪B) = Fläche(A) + Fläche(B) − Fläche(A∩B)
  - A \ B ∩ B = ∅
  - Invarianz unter Translation und Rotation um 90°
  - zufällig gesnappte Koordinaten, um Kollinearitäten zu erzwingen

---

## 3. Polylinie gegen Polygon und Linie gegen Linie

- **Polygon-only-Algorithmen** (Greiner-Hormann, Weiler-Atherton, Martinez, polybooljs) kennen nur Flächen. Clip einer Polylinie gegen ein Polygon lässt sich aber leicht ergänzen:
  1. Die Linie an allen Schnittpunkten mit dem Polygonrand zerlegen. Dafür genügt ein Noding von Linie und Rand, ohne den Sweep der Flächen-Booleschen-Operation.
  2. Für jedes Teilstück einen Mittelpunkt-Test machen (innen, außen oder auf dem Rand).
  3. Teilstücke nach Operation auswählen (Intersection: innen plus optional Rand; Difference: außen) und angrenzende Stücke wieder verketten.

  Martinez-Implementierungen haben oft keinen Linienmodus. Clipper2 hat einen Modus für offene Pfade.
- **Linie gegen Linie** (Intersection, Union, Difference von Linien) ist reine **Noding- und Kantenmerge-Arbeit**: alle Segmente nodieren, kollineare Überlappungen zusammenführen, nach Herkunft auswählen und Linien neu zusammensetzen. Ergebnisse können gemischte Dimensionen haben (Punkte aus Kreuzungen, Linien aus Überlappungen).
- **Folgerung**: Eine Noding-Schicht (Segmentzerlegung mit robusten Prädikaten und einem Snap-Rounding- oder Gitter-Modell) ist der gemeinsame Kern von *allen* drei Fällen: Polygon/Polygon, Linie/Polygon und Linie/Linie. Bei OverlayNG ist das von vornherein so. Bei Martinez/polybooljs steckt das Noding im Sweep. Man kann dort aber denselben Prädikat- und Rundungskern nutzen und den Linienfall separat lösen.

---

## 4. Vergleich

| Kriterium | Weiler-Atherton | Greiner-Hormann (+FHP 2019) | Vatti/Clipper2 | Martinez-Rueda-Feito 2013 | polybooljs-Sweep | JTS OverlayNG |
|---|---|---|---|---|---|---|
| Kollineare/überlappende Kanten | schwach | mit Erweiterung ja | ja (Integer) | ja (explizit) | ja (Epsilon) | ja (Kantenmerge) |
| Berührende Vertices | schwach | mit Erweiterung ja | ja | ja | ja | ja |
| Löcher | mühsam | ja | ja (PolyTree) | ja, inkl. Hierarchie | ja | ja |
| Multipolygone | mühsam | ja | ja | ja | ja | ja |
| Selbstberührende Ringe / Selbstschnitte | nein | nur selbstschnittfrei | ja (Fill-Regeln) | eingeschränkt | ja (EvenOdd) | Eingaben müssen gültig sein, Selbstberührung (invertierte Löcher) ok |
| Linie/Polygon, Linie/Linie | nein | nein | Linie/Polygon (offene Pfade) | nein (separat) | nein (separat) | ja, alle Kombinationen |
| Laufzeit | O(n·m) | O(n·m) ohne Index | O((n+k) log n) | O((n+k) log n) | O((n+k) log n) | O((n+k) log n) mit Index |
| Typisches Präzisionsmodell | float | float (+Perturbation) | Int64-Gitter | float | float + Epsilon | Fixed + Snap-Rounding oder Floating + Fallback |
| Robustheit insgesamt | gering | mittel | hoch | mittel (hoch mit Snap-Kern) | mittel | sehr hoch |
| Nähe zum NTS-Orakel (Semantik) | gering | gering | mittel | mittel | mittel | **identisch** |
| Implementierungsaufwand | gering/unklar | gering bis mittel | hoch | **mittel** | mittel | hoch |
| Wiederverwendbar für andere Algorithmen | nein | nein | teilweise | teilweise (Sweep) | teilweise | **ja** (Noder, Graph, Validierung, Polygonize, Buffer) |
| Referenz-Code (Lizenz) | – | C++ FHP-Clipping | Clipper2 (Boost 1.0) | w8r/martinez (MIT), polygon-clipping (MIT), rust-geo-booleanop (MIT), SixLabors.PolygonClipper (C#, Split License) | polybooljs (MIT) | JTS (EPL 2.0/EDL 1.0), NTS (BSD-3), GEOS (LGPL) |

---

## 5. Empfehlung (nicht bindend, Entscheidung in #5)

1. **Präzisionskern zuerst und unabhängig vom Algorithmus**:
   - eigener C#-Port von Shewchuks `orient2d` (Public Domain)
   - ein konfigurierbares **Fixed-Precision-Modell mit Snap-Rounding** als Standard. Die Gitterweite gibt der Host vor, oder sie wird automatisch aus der Koordinatengröße abgeleitet.
   - optional ein Floating-Modus mit Fallback-Kaskade nach dem Vorbild von `OverlayNGRobust`

   Dieser Kern kann für Clip, Cut und alle Overlays gemeinsam genutzt werden.
2. **Architektur nach dem Muster von OverlayNG** (Noding → Kantenmerge → Topologiegraph mit Labels → Ergebnisaufbau), aber schrittweise:
   - **Stufe 1**: Noder plus Linie/Polygon-Clip und Linie/Linie. Das ist der kleinste Graph und gleichzeitig ein früh nutzbares Feature (Clip).
   - **Stufe 2**: Polygon/Polygon mit Labelling, RingBuilder und Hole-Assignment.
   - **Stufe 3**: Optimierungen (Envelope-Clipping, Spatial Index).

   Hauptgrund: Damit decken wir alle Dimensionskombinationen ab, und die **Semantik deckt sich mit dem NTS-Orakel** (Strict-Mode). Das vereinfacht die Tests erheblich, und der Graph ist für spätere Algorithmen wiederverwendbar.
3. **Pragmatische Alternative**, falls der Aufwand zu groß wirkt: **Martinez-Rueda-Feito 2013** für Polygon/Polygon, aufgesetzt auf denselben Prädikat- und Snap-Kern, plus separater Linien-Clip über Noding und Mittelpunkt-Test. Er ist schneller fertig, liefert aber zwei getrennte Codepfade, und die Semantik in Randfällen weicht eher vom Orakel ab.
4. **Nicht empfohlen**: Weiler-Atherton und ursprüngliches Greiner-Hormann (zu fragil). Ein Vatti-Nachbau lohnt sich nur, wenn Fill-Regeln für selbstschneidende Eingaben eine Kernanforderung werden.
5. **Testen**:
   - gleiches Fixed-PrecisionModel im NTS-Orakel
   - Vergleich über Validität, Fläche, symmetrische Differenz, Hausdorff-Distanz und Topologie-Kennzahlen, nie über Koordinatengleichheit
   - Testkorpora aus der JTS-XML-Suite und den Fixtures von polygon-clipping
   - Property-basierte Tests mit erzwungenen Degenerationen

Offene Fragen für #5:

- Soll die Ausgabe gerundete Koordinaten haben dürfen (Snap-Rounding als Standard)?
- Welche Semantik gilt für Berührungsergebnisse und Kollapse (Strict-Mode ja oder nein)?
- Müssen selbstschneidende oder ungültige Eingaben unterstützt werden, oder wird Validität vorausgesetzt (wie bei JTS)?
