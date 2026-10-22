# Covers

`a.Covers(b)` ist `true`, wenn kein Punkt von `b` außerhalb von `a` liegt. Randpunkte zählen als innen. Es ist das Spiegelbild von `CoveredBy`.

## DE-9IM

Eines von `T*****FF*`, `*T****FF*`, `***T**FF*` oder `****T*FF*`: `b` teilt einen Punkt mit `a`, und nichts von `b` liegt im Äußeren von `a`.

In Worten: Der gemeinsame Punkt darf überall liegen, bei II, IB, BI oder BB (deshalb gibt es vier Muster); die beiden `F` bei EI und EB sagen, dass nichts von `b` außerhalb von `a` liegt.

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Covers(b)` | `b.Covers(a)` |
|---|---|---|
| Ein Punkt in einer Fläche | false | **true** |
| Ein Punkt auf dem Rand einer Fläche | false | **true** |
| Ein Punkt außerhalb einer Fläche | false | false |
| Ein Punkt im Loch einer Fläche | false | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false | **true** |
| Ein Punkt am Ende einer Linie | false | **true** |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | false | **true** |
| Ein Punkt neben einer Linie | false | false |
| Zwei gleiche Punkte | **true** | **true** |
| Zwei Linien, die sich kreuzen | false | false |
| Zwei Linien, die Ende an Ende liegen | false | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false | false |
| Zwei Linien, die ein Stück zusammen laufen | false | false |
| Zwei gleiche Linien (eine umgekehrt) | **true** | **true** |
| Eine Linie in einer Fläche | false | **true** |
| Eine Linie, die den Rand einer Fläche quert | false | false |
| Eine Linie entlang des Rands einer Fläche | false | **true** |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false | false |
| Eine Linie außerhalb einer Fläche | false | false |
| Eine kleine Fläche in einer großen | false | **true** |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false | **true** |
| Zwei Flächen mit gemeinsamer Kante | false | false |
| Zwei Flächen, die sich in einer Ecke berühren | false | false |
| Zwei Flächen, die sich überlappen | false | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | **true** | **true** |
| Zwei getrennte Flächen | false | false |
| Eine Fläche im Loch einer anderen | false | false |

## Einzelheiten

- **`Contains` gegenüber `Covers`** ist der Unterschied, der die meisten überrascht: Ein Polygon *enthält* eine Linie entlang seines Rands nicht (die Linie hat keinen Punkt im Inneren), es *überdeckt* sie aber. Wenn Sie „innen oder auf dem Rand“ meinen, nehmen Sie `Covers`.
- **Gleiche Geometrien überdecken einander.**
- **Leere Geometrien** überdecken nichts und werden von nichts überdeckt.
- **Auf die Rollen achten** in der Tabelle: Die erste Spalte fragt, ob die erste Geometrie die zweite überdeckt.

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.Buffer(t).Covers(b);
```

`Buffer(t)` vergrößert den Behälter ringsum um `t`. Er ist der teure Teil; für viele Tests gegen dieselbe Geometrie berechnen Sie den Buffer einmal und behalten ihn.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`CoveredBy`](covered-by.md), [`Contains`](contains.md), [Prädikate und DE-9IM](../predicates.md).
