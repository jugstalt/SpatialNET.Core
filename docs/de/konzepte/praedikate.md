# Prädikate und DE-9IM

Räumliche Prädikate beantworten Ja/Nein-Fragen zur Beziehung zweier Geometrien: Schneiden sie sich, enthält die eine die andere, berühren sie sich nur. Alle beruhen auf dem vollen **DE-9IM**-Modell von OGC und NetTopologySuite ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.de.md)) und rechnen **exakt auf den Eingabekoordinaten**, ohne Gitter.

## Die Schnittmatrix

Für zwei Geometrien a und b liefert `a.Relate(b)` eine `IntersectionMatrix`: für jede Kombination aus Inneres (I), Rand (B) und Äußerem (E) von a und b die Dimension ihres Schnitts (`F` = leer, 0 = Punkte, 1 = Linien, 2 = Flächen).

```csharp
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var edge = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)"); // lies on the boundary of the square

Console.WriteLine(square.Contains(edge)); // False - DE-9IM: no point of the edge lies in the interior
Console.WriteLine(square.Covers(edge));   // True  - boundary points count
Console.WriteLine(square.Touches(edge));  // True
Console.WriteLine(square.Relate(edge));   // FF2101FF2
```

Die Matrix wird zeilenweise in der Reihenfolge I, B, E von a gegen I, B, E von b geschrieben (`II IB IE BI BB BE EI EB EE`); die neun Werte gibt es auch als Properties (`II`, `IB`, …, `EE`). `a.Relate(b, pattern)` prüft die Matrix gegen ein Muster aus neun Zeichen: `T` (nicht leer), `F` (leer), `*` (beliebig), `0`, `1`, `2` (genau diese Dimension). `T*F**F***` ist zum Beispiel „within“.

## Die Prädikate

Alle Prädikate sind Extension-Methoden auf `IGeometry2D`:

| Prädikat | Bedeutung |
|---|---|
| `Intersects` | die Geometrien haben mindestens einen Punkt gemeinsam |
| `Disjoint` | die Geometrien haben keinen Punkt gemeinsam (`FF*FF****`) |
| `Contains(a, b)` | b liegt in a, und sie haben einen inneren Punkt gemeinsam (`T*****FF*`) |
| `Within(a, b)` | a liegt in b (`T*F**F***`) |
| `Covers(a, b)` | kein Punkt von b liegt außerhalb von a; Randpunkte zählen mit |
| `CoveredBy(a, b)` | kein Punkt von a liegt außerhalb von b; Randpunkte zählen mit |
| `Touches` | die Geometrien treffen sich nur in Randpunkten |
| `Crosses` | die Geometrien kreuzen sich (OGC; abhängig von den Dimensionen) |
| `Overlaps` | gleichdimensionale Geometrien überlappen sich teilweise |
| `EqualsTopo` | die Geometrien sind topologisch gleich (`T*F**FFF*`); falsch, wenn eine leer ist |

`Contains` gegenüber `Covers` ist der Unterschied, der die meisten überrascht: Ein Polygon *enthält* eine Linie auf seinem Rand nicht (die Linie hat keinen Punkt im Inneren), es *überdeckt* sie aber. Wer „liegt drin, der Rand zählt mit“ meint, nimmt `Covers`/`CoveredBy`.

Regeln, die für alle Prädikate gelten:

- **Leere Geometrien.** Ist eine Geometrie leer, ist nur `Disjoint` wahr; alle anderen Prädikate sind falsch, und `Relate` liefert eine Matrix nur aus `F`.
- **Flächen nach Even-Odd.** Flächen folgen der Even-Odd-Regel ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)).
- **Linienränder nach Mod-2.** Die Endpunkte einer Linie sind ihr Rand, geschlossene Pfade haben keinen Rand, und bei einer Polylinie mit mehreren Pfaden zählt in einem Punkt nur eine ungerade Zahl von Pfadenden.
- **Ungültige Eingaben** werden deterministisch und ohne Exception behandelt, aber ohne Zusage, dass die Prädikate untereinander konsistent sind.

## Exaktheit gegenüber Overlays

Prädikate rechnen exakt auf den übergebenen Koordinaten. Overlays runden auf ein Gitter ([Overlays, Füllregeln und das Gitter](overlay-und-gitter.md)). Bei Beinahe-Berührungen unterhalb der Gitterweite kann `a.Intersects(b)` deshalb wahr und `a.Intersection(b)` leer sein.

## Wiederholte Prädikate: `Prepare()`

Wer viele Geometrien gegen dieselbe testet (etwa ein Polygon gegen tausende Punkte), bereitet sie einmal vor:

```csharp
// a and b are the two polygons from the quick start.
// Several expensive predicates against the same geometry: prepare it once.
PreparedGeometry prepared = a.Prepare();
bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

// Full DE-9IM: matrix and pattern matching.
string matrix = a.Relate(b).ToString(); // "212101212"
```

Eine `PreparedGeometry` kopiert einen **Schnappschuss** der Koordinaten und baut daraus einen Segment-Index (und eine Punkt-in-Polygon-Struktur). Spätere Änderungen am Host-Objekt wirken nicht auf sie, was wichtig ist, weil Host-Geometrien veränderbar sind. Sie bietet `Intersects`, `Disjoint`, `Contains`, `Covers`, `Within`, `CoveredBy` und `Touches`, immer mit denselben Ergebnissen wie die nicht vorbereiteten Prädikate; schnelle Wege gibt es für `Intersects`/`Disjoint` und Punkt-in-Fläche, alles andere läuft über das exakte `Relate`. Eine `PreparedGeometry` ist **nicht thread-sicher**; je Thread eine eigene Instanz vorbereiten.
