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

### Ein Muster lesen

Ein Muster hat neun Zeichen, eines für jedes Paar aus den drei Teilen von `a` und den drei Teilen von `b`. Die drei Teile einer Geometrie sind ihr **Inneres** (I), ihr **Rand** (B) und ihr **Äußeres** (E): bei einer Fläche das Innere, die Kante und der Rest der Ebene; bei einer Linie alles außer den beiden Endpunkten, die Endpunkte und der Rest; bei einem Punkt der Punkt selbst, nichts und der Rest. Die neun Positionen, Zeile für Zeile:

| | Inneres von `b` | Rand von `b` | Äußeres von `b` |
|---|---|---|---|
| **Inneres von `a`** | 1. (`II`) | 2. (`IB`) | 3. (`IE`) |
| **Rand von `a`** | 4. (`BI`) | 5. (`BB`) | 6. (`BE`) |
| **Äußeres von `a`** | 7. (`EI`) | 8. (`EB`) | 9. (`EE`) |

Jedes Zeichen sagt, was die beiden Teile **teilen**:

| Zeichen | Bedeutet |
|---|---|
| `T` | sie teilen etwas (Punkte, eine Linie oder eine Fläche; die Dimension ist egal) |
| `F` | sie teilen nichts (leer) |
| `*` | egal |
| `0`, `1`, `2` | sie teilen genau Punkte (0), eine Linie (1) oder eine Fläche (2) |

`T` und `F` sagen nicht, dass das Prädikat wahr oder falsch ist; das Prädikat ist wahr, wenn alle neun Positionen zu seinem Muster passen. Als Beispiel ist `T*F**F***` das Muster von `Within`: `T` bei II, die Inneren teilen etwas; `F` bei IE, das Innere von `a` teilt nichts mit dem Äußeren von `b`; `F` bei BE, der Rand von `a` auch nicht; der Rest ist egal. In Worten: Die Inneren treffen sich, und nichts von `a` ragt aus `b` heraus. Die Matrix `FF2101FF2` des Quadrats und seiner Kante weiter oben liest sich genauso: II `F` (die Inneren teilen nichts), IE `2` (das Innere des Quadrats teilt eine Fläche mit dem Äußeren der Kante), BI `1` (der Rand des Quadrats und das Innere der Kante teilen eine Linie: Die Kante liegt darauf), BB `0` (die Endpunkte der Kante berühren den Rand) und so weiter.

## Die Prädikate

Alle Prädikate sind Extension-Methoden auf `IGeometry2D`:

| Prädikat | Bedeutung |
|---|---|
| [`Intersects`](predicates/intersects.md) | die Geometrien haben mindestens einen Punkt gemeinsam |
| [`Disjoint`](predicates/disjoint.md) | die Geometrien haben keinen Punkt gemeinsam (`FF*FF****`) |
| [`Contains(a, b)`](predicates/contains.md) | b liegt in a, und sie haben einen inneren Punkt gemeinsam (`T*****FF*`) |
| [`Within(a, b)`](predicates/within.md) | a liegt in b (`T*F**F***`) |
| [`Covers(a, b)`](predicates/covers.md) | kein Punkt von b liegt außerhalb von a; Randpunkte zählen mit |
| [`CoveredBy(a, b)`](predicates/covered-by.md) | kein Punkt von a liegt außerhalb von b; Randpunkte zählen mit |
| [`Touches`](predicates/touches.md) | die Geometrien treffen sich nur in Randpunkten |
| [`Crosses`](predicates/crosses.md) | die Geometrien kreuzen sich (OGC; abhängig von den Dimensionen) |
| [`Overlaps`](predicates/overlaps.md) | gleichdimensionale Geometrien überlappen sich teilweise |
| [`EqualsTopo`](predicates/equals-topo.md) | die Geometrien sind topologisch gleich (`T*F**FFF*`); falsch, wenn eine leer ist |

`Contains` gegenüber `Covers` ist der Unterschied, der die meisten überrascht: Ein Polygon *enthält* eine Linie auf seinem Rand nicht (die Linie hat keinen Punkt im Inneren), es *überdeckt* sie aber. Wer „liegt drin, der Rand zählt mit“ meint, nimmt `Covers`/`CoveredBy`.

Regeln, die für alle Prädikate gelten:

- **Leere Geometrien.** Ist eine Geometrie leer, ist nur `Disjoint` wahr; alle anderen Prädikate sind falsch, und `Relate` liefert eine Matrix nur aus `F`.
- **Flächen nach Even-Odd.** Flächen folgen der Even-Odd-Regel ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)).
- **Linienränder nach Mod-2.** Die Endpunkte einer Linie sind ihr Rand, geschlossene Pfade haben keinen Rand, und bei einer Polylinie mit mehreren Pfaden zählt in einem Punkt nur eine ungerade Zahl von Pfadenden.
- **Ungültige Eingaben** werden deterministisch und ohne Exception behandelt, aber ohne Zusage, dass die Prädikate untereinander konsistent sind.

## Die Situationen

Die Tabellen auf den Seiten der einzelnen Prädikate nutzen dieselben 27 Situationen. `a` ist die erste, `b` die zweite Geometrie; die Matrix ist `a.Relate(b)` ([Die Schnittmatrix](#die-schnittmatrix)). Eine Zelle zeigt `true`, wo das Prädikat gilt.

| Situation | Matrix | [Intersects](predicates/intersects.md) | [Disjoint](predicates/disjoint.md) | [Contains](predicates/contains.md) | [Within](predicates/within.md) | [Covers](predicates/covers.md) | [CoveredBy](predicates/covered-by.md) | [Touches](predicates/touches.md) | [Crosses](predicates/crosses.md) | [Overlaps](predicates/overlaps.md) | [EqualsTopo](predicates/equals-topo.md) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Ein Punkt in einer Fläche | `0FFFFF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Ein Punkt auf dem Rand einer Fläche | `F0FFFF212` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| Ein Punkt außerhalb einer Fläche | `FF0FFF212` |  | **true** |  |  |  |  |  |  |  |  |
| Ein Punkt im Loch einer Fläche | `FF0FFF212` |  | **true** |  |  |  |  |  |  |  |  |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | `0FFFFF102` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Ein Punkt am Ende einer Linie | `F0FFFF102` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | `0FFFFF102` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Ein Punkt neben einer Linie | `FF0FFF102` |  | **true** |  |  |  |  |  |  |  |  |
| Zwei gleiche Punkte | `0FFFFFFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| Zwei Linien, die sich kreuzen | `0F1FF0102` | **true** |  |  |  |  |  |  | **true** |  |  |
| Zwei Linien, die Ende an Ende liegen | `FF1F00102` | **true** |  |  |  |  |  | **true** |  |  |  |
| Eine Linie, die auf einer anderen Linie endet (ein T) | `FF10F0102` | **true** |  |  |  |  |  | **true** |  |  |  |
| Zwei Linien, die ein Stück zusammen laufen | `1010F0102` | **true** |  |  |  |  |  |  |  | **true** |  |
| Zwei gleiche Linien (eine umgekehrt) | `1FFF0FFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| Eine Linie in einer Fläche | `1FF0FF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Eine Linie, die den Rand einer Fläche quert | `1010F0212` | **true** |  |  |  |  |  |  | **true** |  |  |
| Eine Linie entlang des Rands einer Fläche | `F1FF0F212` | **true** |  |  |  |  | **true** | **true** |  |  |  |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | `FF1F00212` | **true** |  |  |  |  |  | **true** |  |  |  |
| Eine Linie außerhalb einer Fläche | `FF1FF0212` |  | **true** |  |  |  |  |  |  |  |  |
| Eine kleine Fläche in einer großen | `2FF1FF212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | `2FF11F212` | **true** |  |  | **true** |  | **true** |  |  |  |  |
| Zwei Flächen mit gemeinsamer Kante | `FF2F11212` | **true** |  |  |  |  |  | **true** |  |  |  |
| Zwei Flächen, die sich in einer Ecke berühren | `FF2F01212` | **true** |  |  |  |  |  | **true** |  |  |  |
| Zwei Flächen, die sich überlappen | `212111212` | **true** |  |  |  |  |  |  |  | **true** |  |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | `2FFF1FFF2` | **true** |  | **true** | **true** | **true** | **true** |  |  |  | **true** |
| Zwei getrennte Flächen | `FF2FF1212` |  | **true** |  |  |  |  |  |  |  |  |
| Eine Fläche im Loch einer anderen | `FF2FF1212` |  | **true** |  |  |  |  |  |  |  |  |

### Die Geometrien

| Situation | `a` | `b` |
|---|---|---|
| Ein Punkt in einer Fläche | `POINT (5 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Ein Punkt auf dem Rand einer Fläche | `POINT (10 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Ein Punkt außerhalb einer Fläche | `POINT (15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Ein Punkt im Loch einer Fläche | `POINT (5 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))` |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | `POINT (5 0)` | `LINESTRING (0 0, 10 0)` |
| Ein Punkt am Ende einer Linie | `POINT (0 0)` | `LINESTRING (0 0, 10 0)` |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | `POINT (5 0)` | `LINESTRING (0 0, 5 0, 5 5)` |
| Ein Punkt neben einer Linie | `POINT (5 1)` | `LINESTRING (0 0, 10 0)` |
| Zwei gleiche Punkte | `POINT (1 1)` | `POINT (1 1)` |
| Zwei Linien, die sich kreuzen | `LINESTRING (0 0, 10 10)` | `LINESTRING (0 10, 10 0)` |
| Zwei Linien, die Ende an Ende liegen | `LINESTRING (0 0, 5 5)` | `LINESTRING (5 5, 10 0)` |
| Eine Linie, die auf einer anderen Linie endet (ein T) | `LINESTRING (5 0, 5 5)` | `LINESTRING (0 0, 10 0)` |
| Zwei Linien, die ein Stück zusammen laufen | `LINESTRING (0 0, 10 0)` | `LINESTRING (5 0, 15 0)` |
| Zwei gleiche Linien (eine umgekehrt) | `LINESTRING (0 0, 10 0)` | `LINESTRING (10 0, 0 0)` |
| Eine Linie in einer Fläche | `LINESTRING (2 2, 8 8)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine Linie, die den Rand einer Fläche quert | `LINESTRING (5 5, 15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine Linie entlang des Rands einer Fläche | `LINESTRING (0 0, 10 0)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | `LINESTRING (10 5, 15 5)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine Linie außerhalb einer Fläche | `LINESTRING (12 0, 12 10)` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine kleine Fläche in einer großen | `POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | `POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` |
| Zwei Flächen mit gemeinsamer Kante | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))` |
| Zwei Flächen, die sich in einer Ecke berühren | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 10, 20 10, 20 20, 10 20, 10 10))` |
| Zwei Flächen, die sich überlappen | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((5 0, 15 0, 15 10, 5 10, 5 0))` |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((10 0, 10 10, 0 10, 0 0, 10 0))` |
| Zwei getrennte Flächen | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))` | `POLYGON ((20 0, 30 0, 30 10, 20 10, 20 0))` |
| Eine Fläche im Loch einer anderen | `POLYGON ((4.5 4.5, 5.5 4.5, 5.5 5.5, 4.5 5.5, 4.5 4.5))` | `POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))` |
## Exaktheit gegenüber Overlays

Prädikate rechnen exakt auf den übergebenen Koordinaten. Overlays runden auf ein Gitter ([Overlays, Füllregeln und das Gitter](overlay-and-grid.md)). Bei Beinahe-Berührungen unterhalb der Gitterweite kann `a.Intersects(b)` deshalb wahr und `a.Intersection(b)` leer sein.

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
- Um die *Daten* exakt zu machen statt die Frage tolerant, runden Sie sie einmal beim Einlesen auf ein Gitter (`ReducePrecision(0.01)`). Das macht Stützpunkte, die in einer Zelle liegen, identisch, was hilft, wo Geometrien an Stützpunkten zusammenstoßen, aber es hilft nicht bei einem Punkt mitten auf einem Segment ([Maße und Konstruktionen](measures-and-constructions.md)).

Der [Playground](../getting-started.md#der-playground) zeigt deshalb neben jedem Prädikat den Abstand, nennt darunter die Alternative mit Toleranz und sagt es, wenn ein `false` nur eine Rundungsdifferenz ist.

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
