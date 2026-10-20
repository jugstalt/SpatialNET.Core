# API-Signaturen: typisierte Overloads, Werte statt Geometrien für punktförmige Maße, feste Exception-Regeln

Algorithmen haben **typisierte Overloads**, deren Rückgabetyp sich aus der Strict-Semantik (ADR 0005) ergibt, plus je eine allgemeine `IGeometry2D`-Overload, die zur Laufzeit verzweigt. Der Compiler kennt so den Ergebnistyp ohne Cast. Punktförmige Maße sind **Werte statt Geometrien**: `Centroid()` liefert `Coordinate?` (`null` bei leerer Eingabe) und braucht keine Factory. `Distance` mit einer leeren Geometrie ist `NaN`. Das ist eine bewusste Abweichung von NTS, das 0 liefert und damit „berührt sich“ und „nichts da“ verwechselbar macht.

## Consequences

- **Binäre Operationen:**
  - **Intersection:** Polygon ∩ Polygon → `IPolygon2D`. Polygon ∩ Polylinie und Polylinie ∩ Polylinie → `IPolyline2D`, nur Überlappungen, reine Kreuzungspunkte fallen nach Strict weg. Multipunkt bzw. Punkt ∩ beliebig → `IMultiPoint2D`.
  - **Union und SymDifference:** nur für gleiche Dimension.
  - **Difference:** liefert den Typ des ersten Operanden.
- **Maße:**
  - `Area` auf `IPolygon2D`, `Length` auf `IPolyline2D` und `IPolygon2D` (Umfang).
  - Allgemeine Overloads auf `IGeometry2D` liefern 0, wenn das Maß nicht zur Dimension passt.
  - `Envelope()` liefert `Envelope2D`, bei leerer Eingabe `Envelope2D.Empty` (NaN, `IsEmpty`).
- **ConvexHull** liefert `IGeometry2D`. Je nach Entartung ist das ein Polygon, eine Polylinie oder ein Multipunkt, wie JTS.
- **Simplify** liefert den Eingabetyp.
  - **Standard:** Douglas-Peucker und danach Normalisierung über das Overlay (Union mit EvenOdd). Das Ergebnis ist immer gültig (ADR 0003), kollabierte Ringe fallen weg.
  - **`preserveTopology: true`:** ein topologieerhaltender Simplifier nach dem Muster von JTS `TopologyPreservingSimplifier`. Das ist ein **zusätzlicher Algorithmus im v1-Umfang**.
- **IsValid:** `IsValid()` liefert `bool`, `Validate()` liefert `ValidationResult` mit Befunden aus Art und `Coordinate`. Die Arten sind `SelfIntersection`, `RingSelfTouch`, `DegenerateRing`, `InvalidCoordinate`, `TooFewPoints` und `OverlappingShells`, nach ADR 0003 und nicht 1:1 aus NTS (ein fehlender Schlusspunkt ist z. B. gültig).
- **Exceptions:**
  - `ArgumentNullException` für `null`-Argumente
  - `ArgumentOutOfRangeException` für ungültige Zahlparameter (`NaN`/`Infinity`, negative Toleranz, `gridSize ≤ 0`, `QuadrantSegments < 1`)
  - `InvalidOperationException` bei fehlender Factory (ADR 0004)
  - `NotSupportedException` für unbekannte `IGeometry2D`-Typen oder nicht abbildbare Kombinationen in allgemeinen Overloads
  - Ungültige oder leere Geometrien werfen **nie**.
- **Parameter:** Ein Optionsobjekt gibt es nur ab mehr als zwei fachlichen Stellschrauben (`BufferOptions`, `OffsetOptions`, ADR 0006), sonst einfache optionale Parameter. Die Reihenfolge ist immer: Pflichtparameter → fachliche Optionen → `factory` → `gridSize`.

## Considered Options

- Centroid als `IPoint2D` über die Factory mit Exception bzw. `TryCentroid` (verworfen: Allokation für einen Wert)
- Distance mit leerer Geometrie = 0 wie NTS (verworfen: mehrdeutig) oder Exception
- ConvexHull immer `IPolygon2D` (verworfen: verliert die Entartungsinformation)
- Simplify ohne Gültigkeitsgarantie (verworfen: widerspricht ADR 0003)
