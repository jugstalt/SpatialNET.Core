# Overlay: OverlayNG-artige Pipeline mit Windungszahlen und Snap-Rounding

Alle Overlay-Operationen laufen über **einen** Kern nach dem Muster von JTS OverlayNG: Snap-Rounding-Noding aller Kanten, planarer Graph, Labelling jeder Masche, Auswahl nach Operation, Aufbau von Ringen/Teilflächen bzw. Linien. Anders als OverlayNG bekommt jede Masche **je Operand eine Windungszahl**, und eine wählbare **Füllregel** (EvenOdd, NonZero, Positive, Negative) entscheidet daraus über innen/außen. Nur so lassen sich sich kreuzende Eingaben (ADR 0003, Even-Odd) und der Buffer als `Union(FillRule.Positive)` über Roh-Offsets (#12) mit demselben Kern bedienen. Konstruierte Schnittpunkte werden immer per **Snap-Rounding auf ein Gitter** gerundet, Prädikate rechnen exakt (Shewchuk `orient2d`). Ausgabekoordinaten liegen damit auf dem Gitter, dafür ist die Topologie beweisbar konsistent. Das ist die Voraussetzung für die Zusage „Ergebnisse sind gültig“ aus ADR 0003.

## Consequences

- **Gitterweite:** `double? gridSize = null` → `SpatialNET.Precision.DefaultGridSize` (statisch, anfangs `null`) → automatisch aus max|Koordinate| beider Eingaben · 1e-12, auf eine Zehnerpotenz gerundet. Das ist dasselbe Kaskadenmuster wie bei der Factory (ADR 0004).
- **Strict-Semantik wie OverlayNG Strict-Mode:** Ergebnisse sind dimensionshomogen. Berührungs- und Kollapsreste niedrigerer Dimension fallen weg (z. B. Polygon ∩ Polygon an einer Kante berührend = leeres Polygon).
- **Prädikate** (Intersects, Contains …) rechnen exakt auf den Eingabekoordinaten, ohne Gitter. Bei Beinahe-Berührungen unterhalb der Gitterweite kann `Intersects` wahr und `Intersection` leer sein. Das ist dokumentiert und bei NTS genauso.
- **FillRule** ist öffentlich als optionaler Parameter an allen Overlay-Operationen, Standard EvenOdd. `Union(fillRule: NonZero)` verschmilzt z. B. überlappende Außenringe aus Host-Daten.
- **Z** an neuen Knoten wird über den Parameter t auf dem ungerundeten Originalsegment interpoliert. Bei Kreuzungen gilt das Z des ersten Operanden, oder das des einzigen 3D-Operanden.
- **Bau-Reihenfolge:**
  1. Präzisionskern (`orient2d`-Port, Snap-Rounding-Noder, **interner** Segment-Index)
  2. Linie/Polygon, Linie/Linie, Cut
  3. Polygon/Polygon mit Winding-Labelling (danach kann Buffer starten)
  4. Optimierungen (Envelope-Vorfilter, ggf. Floating-Fast-Path ohne API-Änderung)
- **Tests:** Das NTS-Orakel nutzt OverlayNG mit gleichem `PrecisionModel` im Strict-Mode. Verglichen wird über Validität, Fläche, symmetrische Differenz, Hausdorff-Distanz und Topologie-Kennzahlen, nie über Koordinatengleichheit.

## Considered Options

- Martinez-Rueda-Feito 2013 plus separater Linien-Clip (verworfen: zwei Codepfade, Füllregeln und Buffer schwer anzubinden)
- Vatti/Clipper-Nachbau (verworfen: kein Linie/Linie, hoher Aufwand)
- Floating mit Fallback-Kaskade wie `OverlayNGRobust` oder mit Epsilon (verworfen als Standard: keine beweisbare Gültigkeit, zweistufig schwer testbar)
- OGC-Vollsemantik mit Berührungslinien/-punkten (verworfen: Mischergebnisse, widerspricht ADR 0004)

Recherche: #11 (`research/overlay-algorithmen`), #12 (`research/buffer-offset`).
