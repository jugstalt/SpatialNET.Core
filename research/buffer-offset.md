# Buffer- und Offset-Algorithmen: Optionen

Research zu [#12](https://github.com/jugstalt/SpatialNET.Core/issues/12). Rahmen: [ADR 0001](../docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.md) – alle Algorithmen werden selbst gegen die read-only **Geometrie-Interfaces** implementiert, keine NetTopologySuite/Clipper in der Lib (NTS nur als Test-Orakel), Ziel `net10.0`, planar 2D. Ergebnisgeometrien entstehen über die **Geometrie-Factory** des **Hosts**.

Eng verwandt: [#11 Overlay-Algorithmen und Präzisionsmodelle](https://github.com/jugstalt/SpatialNET.Core/issues/11) und [#5 Overlay-Algorithmus festlegen](https://github.com/jugstalt/SpatialNET.Core/issues/5) – praktisch jeder ernsthafte Buffer-Ansatz braucht einen Teil der Overlay-Infrastruktur.

---

## 1. Begriffe und Semantik

| Begriff | Bedeutung |
|---|---|
| **Buffer(G, d)**, d > 0 | Menge aller Punkte mit Abstand ≤ d zu G = Minkowski-Summe von G mit einer Kreisscheibe vom Radius d. Ergebnis immer (Multi-)Polygon. |
| **Buffer(P, d)**, d < 0 | Erosion: alle Punkte von P, die mindestens \|d\| vom Rand entfernt sind (Minkowski-Differenz). Nur für flächige Eingaben sinnvoll; Linien/Punkte → leer. Kann Polygone aufspalten oder ganz verschwinden lassen. |
| **Offset-Kurve** (Parallel-Linie) | Einseitige Linie im Abstand d links (d > 0) oder rechts (d < 0) einer Polylinie. Ergebnis ist eine *Linie*, kein Polygon. Keine harte mathematische Definition bei Schleifen/Selbstschnitten (siehe §4). |
| **Single-sided Buffer** | Polygon zwischen Linie und ihrer Offset-Kurve (JTS `BufferParameters.setSingleSided`). |
| **Join-Style** | Behandlung *konvexer* Ecken (außen): **round** (Kreisbogen), **mitre** (Schnitt der verlängerten Offset-Segmente, begrenzt durch **mitre limit**, sonst Abschrägung), **bevel** (direkte Verbindung der Segmentenden). Konkave Ecken (innen) werden bei allen Stilen gleich behandelt – dort entstehen Überlappungen, die bereinigt werden müssen. |
| **Cap-Style** | Enden offener Linien: **round** (Halbkreis), **flat/butt** (senkrecht am Endpunkt abgeschnitten), **square** (um d verlängert, rechteckig). |
| **Quadrant segments** | Anzahl Sehnen pro Viertelkreis bei der Bogenapproximation (JTS-Default 8). Alternative Parametrisierung: **arc tolerance** (max. Sehnenabweichung, Clipper2). Aus Toleranz t folgt Segmentwinkel θ = 2·acos(1 − t/d). |

Planare Konsequenz: Buffer in geografischen Koordinaten (Grad) ist verzerrt – das ist Sache des Hosts bzw. der Projektion (#8), nicht des Algorithmus.

---

## 2. Grundbausteine, die alle Ansätze brauchen

1. **Offset-Segment-Generator**: für jedes Segment die um d parallel verschobene Strecke, an Ecken je nach Drehrichtung (Kreuzprodukt) Join-Geometrie, an Linienenden Cap-Geometrie. Reine lokale Geometrie, ~300–600 LOC, gut testbar.
2. **Bogenapproximation** (Kreis, Halbkreis, Eckbogen) mit Quadrant-Segments bzw. Arc-Tolerance – gleiche Stützpunkte wie NTS wählen, sonst sind Orakel-Vergleiche nur über Flächen-/Hausdorff-Toleranz möglich.
3. **Bereinigung**: der rohe Offset überlappt sich selbst (an konkaven Ecken, engen Engstellen, bei d > lokaler Krümmungsradius). Diese Bereinigung ist *der eigentliche Aufwand* – und genau sie unterscheidet die Ansätze.

---

## 3. Ansätze

### A. Raw-Offset-Kurve + Noding + Tiefen-/Winding-Polygonisierung (JTS `BufferBuilder`)

**Prinzip** ([JTS buffer package](https://github.com/locationtech/jts/tree/master/modules/core/src/main/java/org/locationtech/jts/operation/buffer)):

1. `BufferCurveSetBuilder` erzeugt pro Komponente geschlossene **Raw-Offset-Ringe** (`OffsetCurveBuilder` / `OffsetSegmentGenerator`): Punkt → Kreis; Linie → ein Ring, der links hin, Cap, rechts zurück, Cap läuft; Polygon → Schale und Löcher jeweils einseitig, Orientierung so gewählt, dass bei negativem d nach innen erodiert wird. Jede Kante trägt eine *Tiefen-Markierung* (links/rechts: innen/außen).
2. **Konkave Ecken**: Statt die zwei Offset-Segmente exakt zu schneiden, wird die Kurve über den Originalvertex geführt („closing segment“). Das erzeugt bewusst kleine Schleifen, die später herausfallen, vermeidet aber Lücken bei Rundungsfehlern.
3. **Vorfilter**: `BufferInputLineSimplifier` entfernt Konkavitäten, die kleiner als ein Bruchteil von d sind (Default `simplifyFactor` = 0.01·d) – drastisch weniger Schnittpunkte bei verrauschten Linien. Komplett erodierte Ringe (z.B. Hüllrechteck schmaler als 2·|d|) werden vorab verworfen.
4. **Noding** aller Raw-Kurven (`MCIndexNoder`, Monotone Chains), anschließend Aufbau eines planaren Graphen.
5. **Tiefenberechnung**: `BufferSubgraph`/`RightmostEdgeFinder`/`SubgraphDepthLocater` bestimmen für jeden zusammenhängenden Teilgraphen die Tiefe ausgehend von der äußersten Kante; Kanten mit Tiefenübergang 0↔1 bilden den Rand. Das ist äquivalent zu „Winding-Number ≥ 1 ist innen“.
6. `PolygonBuilder` setzt Maximal-/Minimal-Edge-Rings zu Polygonen mit Löchern zusammen.
7. **Robustheits-Fallback** (`BufferOp`): Wirft das Floating-Noding eine `TopologyException`, wird mit schrittweise reduzierter Präzision und **Snap-Rounding**-Noder wiederholt.

**Bewertung**: Industriestandard (JTS, GEOS, NTS, damit PostGIS/Shapely/QGIS). Alle Join-/Cap-Styles, negative Distanzen, Single-sided Buffer. Die Schritte 4–6 sind *exakt* die Overlay-Infrastruktur (Noding, planarer Graph, Ring-Aufbau, Loch-Zuordnung). Hoher Aufwand, aber fast komplett geteilt mit dem Overlay.

### B. Clipper-Offset-Prinzip (`ClipperOffset`)

**Prinzip** ([Clipper2 ClipperOffset](https://angusj.com/clipper2/Docs/Units/Clipper.Offset/Classes/ClipperOffset/_Body.htm), Quelle [`clipper.offset.cpp`](https://github.com/AngusJohnson/Clipper2/blob/main/CPP/Clipper2Lib/src/clipper.offset.cpp)):

1. Pro Pfad wird eine Offset-Kontur gebaut (`DoRound`, `DoMiter`, `DoSquare`, `DoBevel`). Konkave Ecken werden ebenfalls über den Originalvertex geführt; laut Quellkommentar entstehen dabei Artefakte, die „later by the finishing union operation“ entfernt werden.
2. Offene Pfade (`EndType.Butt/Square/Round/Joined`) werden zu einer geschlossenen Umrisskontur (beide Seiten + Caps) gemacht.
3. **Ein einziger Union-Schritt** mit Füllregel **Positive** (bzw. Negative bei invertierter Orientierung) über alle Roh-Konturen. Konturen und Schleifen mit Winding ≤ 0 fallen weg.
4. Koordinaten sind Integer (`Path64`); Gleitkomma-API skaliert intern. Mitre-Limit und Arc-Tolerance sind Parameter; Default-Bogenfeinheit ergibt sich aus der Arc-Tolerance relativ zu d.

Das ist eine direkte Umsetzung von **Chen & McMains (2005)** „Polygon Offsetting by Computing Winding Numbers“ ([PDF](https://mcmains.me.berkeley.edu/pubs/DAC05OffsetPolygon.pdf)), das in den Clipper1-Quellen auch zitiert wird: Raw-Offset-Kurve so bauen, dass alle ungültigen Schleifen Winding-Number ≤ 0 haben, dann positive Winding-Regel anwenden. Komplexität O((n+k) log n), k = Selbstschnitte.

**Bewertung**: Konzeptionell einfacher als A, weil keine Tiefen-Markierung pro Kante nötig ist – nur „Union mit Füllregel Positive“. Voraussetzung: das eigene Overlay kann **Selbstüberlagerungen mit Füllregel** auswerten (Non-Zero/Positive), nicht nur Bool-Ops zweier gültiger Polygone. Clipper-Hinweise: Eingabepolygone sollten vorher per Union bereinigt sein; sehr kurze Segmente vorher entfernen (`SimplifyPaths`), sonst „Blemishes“.

### C. Minkowski-Summe (Konvolution / konvexe Zerlegung)

**Prinzip**: Buffer = P ⊕ D, D = regelmäßiges n-Eck als Kreisapproximation.
- *Konvexe Zerlegung*: P in konvexe Teile zerlegen, je Teil konvexe Minkowski-Summe in O(n+m) (Kantensortierung nach Winkel), danach Union aller Teile.
- *Konvolution* ([Wein 2006, „Exact and Efficient Construction of Planar Minkowski Sums using the Convolution Method“](https://doi.org/10.1007/11841036_73); in [CGAL Minkowski_sum_2](https://doc.cgal.org/latest/Minkowski_sum_2/index.html)): Konvolutionszyklus der Kanten von P und D bilden, dann Bereiche mit Winding-Number > 0 behalten – strukturell dasselbe wie B.

**Bewertung**: Eleganter Formalismus, liefert aber nativ nur **round** (bzw. die Form von D). Mitre/Bevel, Caps flat/square und negative Distanz (Minkowski-Differenz = Komplement-Trick) passen schlecht. Für Buffer mit Kreis degeneriert die Konvolution praktisch zu Ansatz B. CGAL ist GPL/kommerziell – nur als Literatur nutzbar. Eigener Mehrwert gering; Minkowski lohnt sich nur, wenn SpatialNET allgemeine Minkowski-Summen (z.B. Kollision, Konfigurationsraum) anbieten will.

### D. Segmentweise Kapsel-Union („Stadium-Union“)

**Prinzip**: Jedes Segment → Kapsel (Rechteck + zwei Halbkreise, Minkowski-Summe Segment ⊕ Kreis), Punkt → Kreis. Buffer(Linie) = Union aller Kapseln. Buffer(Polygon, d > 0) = P ∪ Kapseln aller Randsegmente. Buffer(Polygon, d < 0) = P \ (Union der Kapseln aller Randsegmente mit |d|).

Varianten: Kapseln baumartig vereinigen (Cascaded Union, wie JTS `CascadedPolygonUnion`/`UnaryUnionOp`), damit nicht n-mal gegen ein wachsendes Resultat vereinigt wird.

**Bewertung**:
- + Mathematisch trivial korrekt für **round/round**; jede Teiloperation ist ein gewöhnliches Overlay – 100 % Wiederverwendung, kein eigener Bereinigungscode.
- + Sehr guter **Referenz-/Fallback-Pfad** und Testorakel *innerhalb* der Lib (z.B. zum Vergleich mit Ansatz A/B).
- − Nur Round-Join natürlich. Mitre/Bevel brauchen zusätzliche Keile/Dreiecke an konvexen Ecken (machbar, aber fummelig); flat/square Caps durch Kapsel-Variation an den Endsegmenten.
- − Benachbarte Kapseln teilen nahezu identische Bögen → viele fast-kollineare, fast-koinzidente Kanten. Das ist ein **Stresstest für die Overlay-Robustheit** (genau die Fälle, an denen Floating-Overlays scheitern) und erzeugt viele Stützpunkte. Performance deutlich schlechter als A/B (Faktor 5–50 je nach Overlay).
- − Ergebnis-Vertices weichen von NTS ab (andere Bogenstützpunkte an Ecken), Orakel-Vergleich nur über Toleranz.

### E. Straight Skeleton / Voronoi-basierte Offsets

**Prinzip**:
- **Straight Skeleton** ([Aichholzer, Aurenhammer et al. 1995, „A Novel Type of Skeleton for Polygons“](https://www.jucs.org/jucs_1_12/a_novel_type_of.html)): Wellenfront, deren Kanten mit konstanter Geschwindigkeit parallel nach innen laufen; Edge- und Split-Events ändern die Topologie. Der Offset bei Distanz d ist der Schnitt der Wellenfront zum Zeitpunkt d – entspricht einem **mitre-Offset ohne Mitre-Limit**. Implementierungen: Felkel & Obdržálek 1998 (bekannt fehleranfällig), [Palfrader/Held/Huber 2012 „On Computing Straight Skeletons by Means of Kinetic Triangulations“](https://doi.org/10.1007/978-3-642-33090-2_66), [CGAL Straight_skeleton_2 inkl. Polygon Offsetting](https://doc.cgal.org/latest/Straight_skeleton_2/index.html) (GPL/kommerziell, Autor F. Cacciola).
- **Voronoi-Diagramm von Segmenten / Medial Axis** (Held 1998, „Voronoi diagrams and offset curves of curvilinear polygons“, CAD 30(4); Held: [VRONI / Voronoi-Projekt](https://www.cosy.sbg.ac.at/~held/projects/voronoi_2d/voronoi.html)): liefert exakte **round**-Offsets; Offsets für viele d ohne Neuberechnung (typisch für CNC-Taschenfräsen).

**Bewertung**: Algorithmisch die schwerste Option. Robustheit (Split-Events, gleichzeitige Events, parallele Kanten, Entartungen) ist ein Forschungsthema für sich; brauchbare Implementierungen sind fast nur in CGAL/VRONI zu finden, beide nicht übernehmbar. Natürlicher Einsatz: viele *innere* Offsets desselben Polygons, Dachformen, Mitte-Linien. Für GIS-Buffer (positiv, Linien mit Caps, Multi-Geometrien, round als Default) überdimensioniert. Kein Overlay nötig, aber auch keine Wiederverwendung des Overlays – eine zweite große Robustheitsbaustelle. **Nicht empfohlen** für SpatialNET-Buffer; allenfalls später als eigener Algorithmus „StraightSkeleton/MedialAxis“.

### Weitere Referenzen mit demselben Grundmuster

- **Boost.Geometry `buffer`** ([Doku](https://www.boost.org/doc/libs/latest/libs/geometry/doc/html/geometry/reference/algorithms/buffer/buffer_7_with_strategies.html), BSL-1.0, Barend Gehrels): baut pro Segment/Ecke/Ende „Pieces“ (Strategien für Distanz, Side, Join, End, Point), berechnet Turns (Schnittpunkte) zwischen Pieces, klassifiziert Turns per `turn_in_piece_visitor`/`turn_in_input` als innen/außen und traversiert wie im Overlay. Ein Hybrid aus A und D.
- **georust `geo` `Buffer`** ([buffer.rs](https://github.com/georust/geo/blob/main/geo/src/algorithm/buffer.rs), MIT/Apache-2.0): delegiert an [iOverlay](https://github.com/iShape-Rust/iOverlay) (Apache-2.0) `OutlineOffset`/`StrokeOffset` + `unary_union` – also Prinzip B mit einem Integer-Overlay.

---

## 4. Offset-Kurven für Polylinien

### Naiver Ansatz
Offset-Segmente einer Seite bilden, Joins nur auf der Außenseite, auf der Innenseite Nachbarsegmente schneiden. Scheitert, sobald d größer als die Länge kurzer Segmente oder der lokale Krümmungsradius ist (Schleifen, „Swallowtails“). Selbstschnitt-Trimming (Tiller & Hanson 1984, „Offsets of Two-Dimensional Profiles“, IEEE CG&A; Choi & Park 1999, „A pair-wise offset algorithm for 2D point-sequence curve“, CAD 31) entfernt lokale Schleifen, globale Interferenzen (Engstellen der Linie, Selbstschnitte der Eingabe) bleiben ungelöst.

### Buffer-abgeleitet (alt: GEOS `OffsetCurve`/PostGIS `ST_OffsetCurve` vor 2022)
Single-sided Buffer bzw. Buffer berechnen, Raw-Offset-Kurve berechnen, beide **schneiden** und die passenden Teile behalten. Problem: Buffer-Heuristiken (Simplifier, Snap-Rounding) lassen Buffer-Rand und Raw-Kurve minimal auseinanderlaufen → kaputte Schnitte, zerrissene oder zusätzliche Teilstücke. Martin Davis 2018: „nothing ‚simple‘ about parallel offset curves“ ([jts-dev](https://www.eclipse.org/lists/jts-dev/msg00225.html)).

### JTS `OffsetCurve` (neu, Ende 2021/2022)
Eingeführt mit [JTS PR #810](https://github.com/locationtech/jts/pull/810) (Dez. 2021), beschrieben im Blog [„JTS Offset Curves“ (Jan. 2022)](http://lin-ear-th-inking.blogspot.com/2022/01/jts-offset-curves.html), erweitert in [PR #956](https://github.com/locationtech/jts/pull/956) (Feb. 2023, Multi-Ergebnis + „Joined mode“), Mitre-Joins für Polygone in [PR #1109](https://github.com/locationtech/jts/pull/1109) (2025). GEOS-Port in 3.11; Quelle [`OffsetCurve.java`](https://github.com/locationtech/jts/blob/master/modules/core/src/main/java/org/locationtech/jts/operation/buffer/OffsetCurve.java).

Algorithmus:
1. Buffer der Linie mit Distanz |d| berechnen (normaler, robuster Buffer).
2. Raw-Offset-Kurve der gewünschten Seite berechnen.
3. Raw-Kurven-Segmente über einen räumlichen Index und **Distanztoleranz** auf Segmente des Buffer-Randes **matchen** (kein exakter Schnitt!).
4. Ausgegeben werden die Abschnitte des Buffer-Randes, die zur Raw-Kurve passen – geordnet entlang der Raw-Kurve, Hauptkurve zuerst; Richtung wie die Eingabe.

Unterschiede zum buffer-abgeleiteten Schneiden:
- Ergebnis liegt **garantiert auf dem Buffer-Rand** → konsistent mit Buffer, exakter Abstand d (bis auf Quantisierung), nie selbstschneidend.
- Robust, weil Toleranz-Matching statt fragiler Schnittpunktberechnung.
- Bei Selbstschnitten oder Engstellen < 2d: seit #956 **MultiLineString** mit disjunkten Abschnitten (ggf. Ringe); im „Joined mode“ eine durchgehende Linie, die dann selbstschneiden darf und bei großem d „Flat-Line-Artefakte“ haben kann.
- Konsequenz: Offset ist *kein eigener Geometriekern*, sondern Buffer + Raw-Offset-Generator + Matching (~300 LOC zusätzlich). Wer Buffer hat, bekommt robusten Offset fast geschenkt.

Clipper2 hat **keine** einseitige Offset-Kurve (offene Pfade nur als geschlossener Umriss, negative Distanz für offene Pfade unzulässig).

---

## 5. Robustheit – typische Fallen

| Problem | Ursache | Gegenmittel |
|---|---|---|
| Enge Konkavitäten / Engstellen < 2·\|d\| | Raw-Kurve faltet sich, viele Selbstschnitte | Bereinigung über Winding/Tiefe (A/B); Vorab-Simplifizierung kleiner Konkavitäten (JTS `BufferInputLineSimplifier`) |
| Kollaps bei negativem Buffer | Ring oder Teil verschwindet; Raw-Ring kehrt Orientierung um | Ringe mit Hüllrechteck < 2·\|d\| vorab verwerfen; invertierte Raw-Ringe erkennen (JTS `isRingCurveInverted`); leeres Ergebnis sauber über die Factory erzeugen |
| Aufspaltung bei negativem Buffer | Polygon zerfällt in mehrere | Ergebnis immer als Multi-Polygon behandeln; Loch-Zuordnung durch Polygonisierung |
| Winzige Segmente, Duplikat-Punkte | Offset-Normalen numerisch instabil, Mikro-Schleifen | Duplikate entfernen; Segmente < ε·d überspringen/zusammenfassen (Clipper `SimplifyPaths`, JTS-Fix in 1.20 für Repeated Points) |
| Fast-kollineare Nachbarsegmente | Mitre-Schnittpunkt im Unendlichen | Kollinearitätstest mit Toleranz, dann ohne Join fortsetzen; Mitre-Limit erzwingen |
| Spitze Winkel bei Mitre | Mitre-Spitze sehr lang | Mitre-Limit (JTS Default 5.0); darüber Bevel bzw. abgeschnittene Spitze |
| Noding-Fehler (Floating Point) | Schnittpunkte nicht konsistent | Snap-Rounding, Retry mit reduzierter Präzision (JTS `BufferOp`) oder Integer-Koordinaten (Clipper) – Entscheidung gehört zu #11/#5 |
| Große d relativ zur Geometrie | Bögen dominieren, viele Vertices | Quadrant-Segments/Arc-Tolerance konfigurierbar; Konvexhülle als Shortcut für sehr große d möglich |
| Ungültige Eingabe (selbstschneidende Polygone) | Undefinierter Buffer | Vorher validieren/„make valid“ oder dokumentiertes Verhalten (JTS: `buffer(0)` als Reparatur-Trick) |

---

## 6. Geteilte Bausteine mit dem Overlay

| Baustein | Overlay | A JTS | B Clipper | C Minkowski | D Kapseln | E Skeleton |
|---|---|---|---|---|---|---|
| Segment-Schnitt, Orientierungsprädikat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (andere Prädikate) |
| Noding (Monotone Chains / Sweep, Snap-Rounding) | ✓ | ✓ | ✓ | ✓ | ✓ (über Overlay) | – |
| Planarer Graph / Kantenring-Aufbau | ✓ | ✓ | ✓ | ✓ | ✓ | eigener Graph |
| Winding-Number / Tiefen-Labeling | ✓ (Bool-Ops = Labeling) | ✓ Tiefe | ✓ Füllregel Positive | ✓ | ✓ | – |
| Union mehrerer / selbstüberlappender Ringe | Cascaded Union | implizit | ✓ zentral | ✓ | ✓ zentral | – |
| Loch-Zuordnung, Polygon-Assembly | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Räumlicher Index (STR-Tree, Monotone Chains) | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Offset-Segment-Generator, Bögen, Joins, Caps | – | ✓ | ✓ | (Kreispolygon) | (Kapselform) | – |

Kernaussage: Wenn das Overlay (#5/#11) so gebaut wird, dass es **(1)** beliebige Kantenmengen nodet und **(2)** Flächen per **Winding-Number mit wählbarer Füllregel** labelt, dann sind Buffer nach Ansatz B (und auch A) im Wesentlichen „Offset-Segment-Generator + `Overlay.Union(rawRings, FillRule.Positive)`“. Das ist die wichtigste Anforderung, die aus diesem Ticket an das Overlay-Design zurückfließt.

---

## 7. Aufwand (grob, ohne Overlay-Kern)

Annahme: ein robustes Overlay mit Noding und Winding-Labeling existiert (eigener Aufwand in #5/#11).

| Komponente | Aufwand |
|---|---|
| Offset-Segment-Generator inkl. round/mitre/bevel, Caps round/flat/square, Quadrant-Segments, Mitre-Limit | 3–5 PT |
| Buffer-Fassade für Punkt/MultiPoint/Linie/Polygon/Multi*, Orientierung, negative Distanz, Kollaps-Vorfilter, leere Ergebnisse | 2–4 PT |
| Bereinigung Ansatz B (Union mit Füllregel) – wenn Overlay sie kann | 1–2 PT |
| Bereinigung Ansatz A (Tiefen-Labeling, BufferSubgraph-Logik) zusätzlich zum Overlay | 5–10 PT |
| Input-Simplifier (kleine Konkavitäten), Duplikat-/Kurzsegment-Filter | 1–2 PT |
| Offset-Kurve à la JTS `OffsetCurve` (Raw-Kurve + Matching gegen Buffer-Rand, Multi-Ergebnis, optional Joined mode) | 3–5 PT |
| Kapsel-Union (D) als Referenz-/Fallback-Pfad | 1–2 PT |
| Tests gegen NTS-Orakel (Flächendifferenz, Hausdorff-Distanz, Gültigkeit), Fuzzing, Randfälle | 5–8 PT |
| **Summe B (+ Offset + D als Referenz + Tests)** | **~ 17–30 PT** |
| Straight Skeleton (E) robust, zusätzlich | 20–40+ PT, hohes Risiko |

Ohne vorhandenes Overlay kommen für Noding + Labeling + Polygon-Assembly erfahrungsgemäß mehrere Wochen hinzu – die Buffer-Arbeit sollte deshalb **nach** dem Overlay-Kern eingeplant werden.

---

## 8. Open-Source-Referenzen (nur als Studienmaterial, nicht als Abhängigkeit)

| Projekt | Ansatz | Lizenz | Hinweis |
|---|---|---|---|
| [JTS](https://github.com/locationtech/jts) `operation.buffer` | A + OffsetCurve | EPL-2.0 / EDL-1.0 (BSD-artig) | Referenz-Design, sehr gut kommentiert |
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | Port von JTS | BSD-3-Clause | Test-Orakel gemäß ADR 0001 |
| [GEOS](https://github.com/libgeos/geos) | C++-Port von JTS | LGPL-2.1 | `OffsetCurve` ab 3.11 |
| [Clipper2](https://github.com/AngusJohnson/Clipper2) | B | BSL-1.0 | C#-Quelle vorhanden, Integer-Koordinaten |
| [Boost.Geometry](https://github.com/boostorg/geometry) | Pieces + Turns (A/D-Hybrid) | BSL-1.0 | Strategien-Design für Join/End/Side |
| [georust geo](https://github.com/georust/geo) + [iOverlay](https://github.com/iShape-Rust/iOverlay) | B mit Integer-Overlay | MIT/Apache-2.0, Apache-2.0 | Moderne, kompakte Umsetzung |
| [CGAL Straight_skeleton_2](https://doc.cgal.org/latest/Straight_skeleton_2/index.html), [Minkowski_sum_2](https://doc.cgal.org/latest/Minkowski_sum_2/index.html) | E, C | GPL-3+ / kommerziell | Nur Literatur, Code nicht übernehmen |

Lizenzhinweis: Auch bei permissiven Lizenzen gilt ADR 0001 – kein Einbinden. Wird Code *portiert* (z.B. Struktur von JTS `OffsetSegmentGenerator`), Lizenz-/Copyright-Hinweise beachten; EPL-2.0 ist für abgeleiteten Quelltext nicht trivial, EDL-1.0 (BSD) ist die unkompliziertere Wahl bei JTS.

## 9. Papers

- X. Chen, S. McMains: *Polygon Offsetting by Computing Winding Numbers*, ASME IDETC/CIE 2005, DETC2005-85513. [PDF](https://mcmains.me.berkeley.edu/pubs/DAC05OffsetPolygon.pdf)
- W. Tiller, E. Hanson: *Offsets of Two-Dimensional Profiles*, IEEE CG&A 4(9), 1984.
- B. K. Choi, S. C. Park: *A pair-wise offset algorithm for 2D point-sequence curve*, Computer-Aided Design 31(12), 1999.
- R. Wein: *Exact and Efficient Construction of Planar Minkowski Sums using the Convolution Method*, ESA 2006, S. 829–840. [DOI](https://doi.org/10.1007/11841036_73)
- O. Aichholzer, F. Aurenhammer, D. Alberts, B. Gärtner: *A Novel Type of Skeleton for Polygons*, J.UCS 1(12), 1995. [J.UCS](https://www.jucs.org/jucs_1_12/a_novel_type_of.html)
- P. Felkel, Š. Obdržálek: *Straight Skeleton Implementation*, SCCG 1998.
- P. Palfrader, M. Held, S. Huber: *On Computing Straight Skeletons by Means of Kinetic Triangulations*, ESA 2012. [DOI](https://doi.org/10.1007/978-3-642-33090-2_66)
- M. Held: *Voronoi diagrams and offset curves of curvilinear polygons*, Computer-Aided Design 30(4):287–300, 1998. [Projektseite](https://www.cosy.sbg.ac.at/~held/projects/voronoi_2d/voronoi.html)
- J. Hobby: *Practical segment intersection with finite precision output*, Computational Geometry 13(4), 1999 (Snap-Rounding, relevant für Noding).
- M. Davis: [JTS Offset Curves](http://lin-ear-th-inking.blogspot.com/2022/01/jts-offset-curves.html) (Blog, 2022) und [jts-dev-Diskussion 2018](https://www.eclipse.org/lists/jts-dev/msg00225.html).

---

## 10. Vergleich

| Kriterium | A Raw-Offset + Tiefe (JTS) | B Raw-Offset + Union Positive (Clipper) | C Minkowski | D Kapsel-Union | E Straight Skeleton / Voronoi |
|---|---|---|---|---|---|
| Punkt / Linie / Polygon | ✓ / ✓ / ✓ | ✓ / ✓ / ✓ | ✓ / (✓) / ✓ | ✓ / ✓ / ✓ | – / – / ✓ (innen) |
| Negative Distanz | ✓ | ✓ | umständlich | ✓ (Differenz) | ✓ (Kernkompetenz) |
| Join round / mitre / bevel | ✓ ✓ ✓ | ✓ ✓ ✓ | nur Form von D | ✓ / Zusatz / Zusatz | mitre (Skel.) bzw. round (Voronoi) |
| Cap round / flat / square | ✓ ✓ ✓ | ✓ ✓ ✓ | nur round | ✓ / Zusatz / Zusatz | n/a |
| Einseitiger Offset ableitbar | ✓ (OffsetCurve) | ✓ (mit Matching à la JTS) | – | ✓ (Matching) | teilweise |
| Robustheit | hoch (mit Snap-Rounding-Fallback) | hoch (wenn Overlay robust) | wie Overlay | wie Overlay, aber Stressfälle | heikel |
| Performance | sehr gut | sehr gut | gut | mäßig bis schlecht | gut bei vielen d |
| Wiederverwendung Overlay | hoch | sehr hoch | hoch | vollständig | gering |
| Zusatzaufwand ggü. Overlay | mittel–hoch | gering–mittel | mittel | gering | sehr hoch |
| Nähe zum NTS-Orakel | identisches Verfahren | sehr ähnlich | abweichend | abweichende Vertices | abweichend |

---

## 11. Empfehlung (unverbindlich)

1. **Buffer nach Ansatz B** (Chen–McMains/Clipper-Prinzip): eigener Offset-Segment-Generator mit allen Join-/Cap-Styles und Quadrant-Segments, Bereinigung über **eine Union mit Füllregel Positive** im eigenen Overlay. Bogenstützpunkte, Mitre-Limit und Defaults (8 Quadrant-Segments, Mitre-Limit 5, closing segment an konkaven Ecken, Input-Simplifier 0.01·d) **wie JTS** wählen, damit Vergleiche mit dem NTS-Orakel eng bleiben.
2. **Overlay-Anforderung festhalten** (an #5/#11): Das Overlay muss beliebige, selbstüberlappende Ringmengen noden und per Winding-Number mit wählbarer Füllregel (mind. Non-Zero/Positive) labeln. Damit ist Ansatz A nur noch eine Optimierung, kein eigenes Subsystem.
3. **Kapsel-Union (D)** als einfacher Referenzpfad in den Tests bzw. als interner Fallback, wenn B auf einem Randfall eine Topologie-Ausnahme liefert.
4. **Offset-Kurve nach JTS-`OffsetCurve`-Muster**: Buffer + Raw-Offset der Seite + Toleranz-Matching gegen den Buffer-Rand; Ergebnis Multi-Linie, optional „Joined mode“. Keine eigene Selbstschnitt-Trimmlogik bauen.
5. **Straight Skeleton nicht für Buffer**; höchstens später als eigenständiger Algorithmus, falls Medial-Axis-/Dachflächen-Use-Cases auftauchen.
6. Reihenfolge: Overlay-Kern → Offset-Segment-Generator → Buffer (B) → OffsetCurve → Feintuning (Simplifier, Kollaps-Shortcuts, Performance).

Offene Fragen für die Spezifikation: Sollen Buffer-Parameter als eigenes Parameterobjekt (analog `BufferParameters`) oder als optionale Argumente der Extension-Methode erscheinen? Soll Arc-Tolerance zusätzlich zu Quadrant-Segments angeboten werden? Welches Verhalten bei ungültigen Eingaben (Fehler vs. „make valid“)?
