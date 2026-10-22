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

### Exaktheit in der Praxis, und die Alternativen mit Toleranz

Die Prädikate verlangen **numerische Exaktheit**: `Intersects` ist nur `true`, wenn die beiden Geometrien einen Punkt *bis zum letzten Bit* ihrer Koordinaten teilen. Das macht die Ergebnisse verlässlich und untereinander stimmig, und es ist, was NetTopologySuite und JTS tun. Bei gemessenen oder berechneten Daten ist es fast nie erfüllt: Eine Koordinate ist ein gerundeter `double`, ein Punkt, den Sie durch Klicken, Projizieren oder Rechnen auf eine Linie setzen, liegt etwa 4e-16 daneben, und eine gemeinsame Grenze zweier Flurstücke ist nur so genau wie die beiden Vermessungen. Ein Punkt auf einem **Stützpunkt** ist exakt, weil er genau dieselben Zahlen hat; ein Punkt auf einem **Segment** ist praktisch nie exakt.

Wenn „nah genug“ gemeint ist, und bei Vermessungsdaten ist es das, sagen Sie es mit einer Toleranz. Die Bibliothek hat keine globale Toleranz für die Prädikate; sie hat die Werkzeuge, um nach einer zu fragen:

```csharp
double t = 0.01;                                    // die Toleranz, in der Einheit Ihrer Koordinaten (hier 1 cm)

bool intersects = a.IsWithinDistance(b, t);         // Intersects: näher als t
bool disjoint = !a.IsWithinDistance(b, t);          // Disjoint: weiter als t
bool contains = a.Buffer(t).Contains(b);            // Contains: b liegt in a, höchstens t über den Rand
bool within = b.Buffer(t).Contains(a);              // Within
bool covers = a.Buffer(t).Covers(b);                // Covers
bool coveredBy = b.Buffer(t).Covers(a);             // CoveredBy
bool touches = a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b);          // Touches (areas): sie treffen sich und überlappen um weniger als t
bool overlaps = a.Buffer(-t).Intersects(b.Buffer(-t))                             // Overlaps (areas): sie überlappen um mehr als t,
    && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a);                          // und keines liegt im anderen
bool equal = a.HausdorffDistance(b) <= t;           // EqualsTopo: no vertex of one is weiter als t from the other
```

| Prädikat | Mit Toleranz `t` | Bedeutet |
|---|---|---|
| `Intersects` | `a.IsWithinDistance(b, t)` | der Abstand ist höchstens `t` |
| `Disjoint` | `!a.IsWithinDistance(b, t)` | der Abstand ist größer als `t` |
| `Contains(a, b)` | `a.Buffer(t).Contains(b)` | b liegt in a oder ragt höchstens um `t` heraus |
| `Within(a, b)` | `b.Buffer(t).Contains(a)` | a liegt in b oder ragt höchstens um `t` heraus |
| `Covers(a, b)` | `a.Buffer(t).Covers(b)` | wie `Contains`, Randpunkte zählen |
| `CoveredBy(a, b)` | `b.Buffer(t).Covers(a)` | wie `Within`, Randpunkte zählen |
| `Touches` (Flächen) | `a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b)` | sie treffen sich und überlappen um weniger als `t` |
| `Overlaps` (Flächen) | `a.Buffer(-t).Intersects(b.Buffer(-t)) && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a)` | sie überlappen um mehr als `t`, keine liegt in der anderen |
| `EqualsTopo` | `a.HausdorffDistance(b) <= t` | kein Stützpunkt ist weiter als `t` von der anderen Geometrie entfernt (siehe unten) |
| `Crosses`, `Relate` | keine | siehe unten |

Anmerkungen zur Tabelle:

- `IsWithinDistance` hört auf, sobald es die Antwort kennt, und prüft zuerst die Envelopes; es ist also billig. Für viele Abfragen gegen eine Geometrie nutzen Sie `Prepare()` und `prepared.IsWithinDistance(b, t)`. Keiner dieser Aufrufe verändert `a` oder `b`; die Buffer sind neue Geometrien.
- `Buffer` ist der teure Teil der Alternativen. Für viele Tests gegen dasselbe `a` berechnen Sie `a.Buffer(t)` (und `a.Buffer(-t)`) einmal und behalten es.
- `Buffer(-t)` schrumpft eine Fläche; das geht für Polygone, nicht für Linien oder Punkte, `Touches` und `Overlaps` stehen also für Flächen. Für eine Linie oder einen Punkt nehmen Sie den Abstand: Sie berühren sich, wenn `IsWithinDistance(b, t)`.
- `HausdorffDistance` ist diskret (sie sieht Stützpunkte und, mit einem `densifyFraction` unter 1, Punkte dazwischen) und eine untere Schranke; geben Sie `densifyFraction` an (etwa 0,1), wenn die Geometrien lange Segmente haben. Ein Stützpunkt auf einem Stützpunkt ist der Fall, den sie exakt entscheidet.
- `Crosses` und `Relate` haben kein einfaches Gegenstück: Eine Kreuzung oder eine DE-9IM-Matrix ist eine Aussage über die Inneren, und „fast kreuzen“ hat keine einheitliche Bedeutung. Fragen Sie nach dem, was Sie meinen: `IsWithinDistance` für „sie treffen sich“ und `NearestPoints` für „wo“.
- Um die *Daten* exakt zu machen statt die Frage tolerant, runden Sie sie einmal beim Einlesen auf ein Gitter (`ReducePrecision(0.01)`). Das macht Stützpunkte, die in einer Zelle liegen, identisch, was hilft, wo Geometrien an Stützpunkten zusammenstoßen, aber es hilft nicht bei einem Punkt mitten auf einem Segment ([Maße und Konstruktionen](masse-und-konstruktionen.md)).

Der [Playground](../erste-schritte.md#der-playground) zeigt deshalb neben jedem Prädikat den Abstand, nimmt eine Toleranz als Hinweis und sagt es, wenn ein `false` nur eine Rundungsdifferenz ist.

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

Eine `PreparedGeometry` kopiert einen **Schnappschuss** der Koordinaten und baut daraus einen Segment-Index (und eine Punkt-in-Polygon-Struktur). Spätere Änderungen am Host-Objekt wirken nicht auf sie, was wichtig ist, weil Host-Geometrien veränderbar sind. Sie bietet `Intersects`, `Disjoint`, `Contains`, `Covers`, `Within`, `CoveredBy` und `Touches`, immer mit denselben Ergebnissen wie die nicht vorbereiteten Prädikate; schnelle Wege gibt es für `Intersects`/`Disjoint` und Punkt-in-Fläche, alles andere läuft über das exakte `Relate`. Eine `PreparedGeometry` ist nach dem Bauen **thread-sicher** (ADR 0024): einmal vorbereiten und zwischen Threads teilen. Sie beantwortet außerdem `Distance`, `IsWithinDistance` und `NearestPoints` gegen andere Geometrien, mit denselben Ergebnissen wie die unvorbereiteten Methoden (nur bei gleich nahen Paaren können die nächsten Punkte abweichen).
