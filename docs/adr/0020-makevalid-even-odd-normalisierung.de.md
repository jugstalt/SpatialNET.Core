# MakeValid: Even-Odd-Normalisierung über den Overlay-Kern, keine gemischten Dimensionen

SpatialNET bekommt **`MakeValid`**, das eine ungültige Geometrie in eine gültige verwandelt. Das kehrt den Ausschluss von „MakeValid/Clean“ aus der Charta (Karte #1) um, weil schmutzige Daten aus Hosts die Regel sind und nicht die Ausnahme, und die Bausteine schon da sind. Die Bedeutung ist **eng und im Einklang mit dem Rest der Bibliothek**: Ein Polygon wird durch **Even-Odd-Normalisierung** über den Overlay-Kern repariert, dieselbe Selbst-Union, die `Simplify` schon ausführt, sodass die Fläche, die die Eingabe nach der Even-Odd-Regel hat (ADR 0003), genau die Fläche des Ergebnisses ist. Es ist **nicht** das OGC/GEOS-`ST_MakeValid`, das Linien- und Punktreste behält und gemischte Collections liefert: Ergebnisse von SpatialNET sind dimensionshomogen (ADR 0002, ADR 0005), und eine Geometry-Collection gibt es nicht.

*English version: [0020-makevalid-even-odd-normalisierung.md](0020-makevalid-even-odd-normalisierung.md)*

## Consequences

- **Ort und API:** `MakeValid` in der statischen Klasse `Overlays` (es führt den Overlay-Kern aus), `SpatialNET.Extensions`, Namespace `SpatialNET`. Typisierte Überladungen liefern den Eingabetyp: `MakeValid(this IPolygon2D, FillRule = EvenOdd, factory, gridSize)` liefert `IPolygon2D`, `MakeValid(this IPolyline2D, factory)` liefert `IPolyline2D`, `MakeValid(this IMultiPoint2D, factory)` liefert `IMultiPoint2D`, und `MakeValid(this IGeometry2D, FillRule, factory, gridSize)` verzweigt zur Laufzeit (ein nicht endlicher Punkt wird zu einem leeren Multipunkt, ein endlicher zu einer Kopie). Für die Package Validation ist das nur eine Ergänzung.
- **Polygone:**
  - Jeder Ring wird unter der `FillRule` (Standard EvenOdd) gelesen, und das Polygon wird mit dem Overlay-Kern mit sich selbst vereinigt, genau wie `Simplify` sein Ergebnis normalisiert. Sich selbst schneidende und überlappende Ringe, sich berührende Ringe, umgekehrte Löcher und doppelte Ringe lösen sich alle auf; Löcher, Inseln und Teilflächen entstehen aus dem Overlay.
  - Ringe ohne Fläche entfallen, das Ergebnis ist gültig, Ringe sind geschlossen, Außenringe gegen den Uhrzeigersinn und Löcher im Uhrzeigersinn (ADR 0003). Ein Polygon ohne Fläche wird zu einem leeren Polygon.
  - Die Reparatur läuft auf dem Gitter (ADR 0005): `gridSize`, sonst `Precision.DefaultGridSize`, sonst automatisch. Koordinaten des Ergebnisses liegen also auf dem Gitter, **auch die von Teilen, die vorher gültig waren**. Wer gültige Eingaben unberührt lassen will, prüft vorher `IsValid()`.
  - Z und M werden wie bei `Union` mitgeführt und interpoliert (ADR 0005, ADR 0015).
- **Polylinien:** Eine Polylinie ist gültig, wenn jeder Pfad mindestens zwei verschiedene Punkte hat und alle Koordinaten endlich sind (sich kreuzende Linien sind gültig). `MakeValid` entfernt nicht endliche Punkte und aufeinanderfolgende doppelte Punkte und lässt Pfade mit weniger als zwei verschiedenen Punkten weg. Andere Koordinaten werden **nicht gerundet**: Für Linien läuft kein Overlay.
- **Punkte:** Nicht endliche Punkte entfallen. Duplikate bleiben, weil `MakeValid` repariert und aus einer Liste keine Menge macht (`Union` hat Mengensemantik).
- **Nicht endliche Koordinaten** (NaN oder unendlich) werden bei jeder Art zuerst entfernt, weil sie sich nicht auf ein Gitter legen lassen. Ein Ring, der dadurch Punkte verliert, wird wie jeder andere repariert.
- **Gültige Eingabe wird kopiert:** Das Ergebnis ist immer eine neue Geometrie aus der Factory (nie die Eingabe-Instanz), egal ob die Eingabe gültig war. So bleiben Rückgabetyp und Eigenschaften für jeden Host vorhersehbar.
- **Garantien:** Das Ergebnis ist gültig (`IsValid()`), es ist **bis auf das Gitter** idempotent (`MakeValid(MakeValid(g))` hat die Fläche von `MakeValid(g)` innerhalb der Gitter-Toleranz; meist dieselben Knoten, aber der Startpunkt eines Rings und die Reihenfolge der Teilflächen können abweichen, und ein erneutes Snap-Rounding kann eine Kante durch den Hot Pixel eines nahen Stützpunkts biegen, was ein Stresstest bei dünnen Splittern gezeigt hat), und bei einem Polygon gleicht seine Fläche der Fläche der Eingabe unter der Füllregel, bis auf das Gitter.
- **Exceptions (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei ungültigem `gridSize`, `NotSupportedException` bei unbekannter Geometrieart, `InvalidOperationException` ohne Factory. Ungültige oder leere Geometrien werfen nie.
- **Tests (ADR 0008):**
  - NTS `GeometryFixer` hat die OGC-Bedeutung und ist deshalb **nicht** das Orakel. Das Orakel ist eine unabhängige Even-Odd-Fläche: Die Eingaberinge werden mit NTS genodet und polygonisiert, und eine Fläche zählt, wenn eine ungerade Zahl der Eingaberinge sie enthält. Der Test vergleicht die Fläche des Ergebnisses damit und verlangt Gültigkeit. Die Fixtures sind aus denen von `validate` abgeleitet, der Pflicht-Tag-Katalog gilt also mit den begründeten Ausnahmen von `validate`.
  - Property-Tests (CsCheck): Das Ergebnis ist immer gültig, für zufällige Ringe mit Selbstschnitten und Duplikaten; gültige Eingaben behalten ihre Fläche bis auf das Gitter; Idempotenz; die Fläche gleicht der unabhängig berechneten Even-Odd-Fläche einer zufälligen Menge von Rechtecken (Ein- und Ausschluss über die symmetrische Differenz); `FillRule.NonZero` verschmilzt überlappende Außenringe.
  - Handgeschriebene Fälle: Linien und Punkte, nicht endliche Koordinaten, leere Geometrien, `null`, gemischtes Z und M.
- **Playground und Dokumentation:** ein Deskriptor (ADR 0011), ein Abschnitt in der Konzeptseite „Vereinfachen, konvexe Hülle und Validierung“, ein Glossareintrag.

## Considered Options

- Das OGC/GEOS-`MakeValid` (verworfen: Es liefert gemischte Dimensionen und Geometry-Collections, die das Modell nicht hat, und seine Regeln sind weit größer als das, was Hosts brauchen)
- `Buffer(0)` als Umsetzung (verworfen: Es ist für Linien und Punkte nicht definiert, rundet über Offset-Kurven und verliert dünne Teile)
- Es `Normalize` nennen (verworfen: Bei JTS bedeutet das die Reihenfolge der Koordinaten)
- Eine gültige Eingabe unverändert als dieselbe Instanz zurückgeben (verworfen: Typ und Eigenschaften des zurückgegebenen Objekts hingen vom Host ab; wer es will, prüft vorher `IsValid()`)
- Nur die ungültigen Bereiche aufs Gitter runden (verworfen: kompliziert und brüchig; eine Regel für das ganze Polygon ist leicht zu sagen)
- Auch Linien aufs Gitter runden (verworfen: Linien haben nichts zu reparieren, das es bräuchte, und Runden würde gültige Daten verändern)
- Doppelte Punkte eines Multipunkts entfernen (verworfen: Das ist Mengensemantik, die `Union` schon bietet)
- Die kollabierten Reste eines Polygons als Linien behalten (verworfen: Ergebnisse sind dimensionshomogen)
