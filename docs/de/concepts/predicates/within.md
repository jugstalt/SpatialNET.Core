# Within

`a.Within(b)` ist `true`, wenn `a` in `b` liegt und die beiden mindestens einen inneren Punkt teilen. Es ist das Spiegelbild von `Contains`: `a.Within(b)` ist gleich `b.Contains(a)`.

## DE-9IM

`T*F**F***`: Die Inneren treffen sich, und kein Teil von `a` liegt im Äußeren von `b`.

In Worten: Die Inneren teilen etwas (`T` bei II), und das Innere und der Rand von `a` teilen nichts mit dem Äußeren von `b` (`F` bei IE und BE): Nichts von `a` liegt außerhalb von `b`.

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Within(b)` | `b.Within(a)` |
|---|---|---|
| Ein Punkt in einer Fläche | **true** | false |
| Ein Punkt auf dem Rand einer Fläche | false | false |
| Ein Punkt außerhalb einer Fläche | false | false |
| Ein Punkt im Loch einer Fläche | false | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | **true** | false |
| Ein Punkt am Ende einer Linie | false | false |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | **true** | false |
| Ein Punkt neben einer Linie | false | false |
| Zwei gleiche Punkte | **true** | **true** |
| Zwei Linien, die sich kreuzen | false | false |
| Zwei Linien, die Ende an Ende liegen | false | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false | false |
| Zwei Linien, die ein Stück zusammen laufen | false | false |
| Zwei gleiche Linien (eine umgekehrt) | **true** | **true** |
| Eine Linie in einer Fläche | **true** | false |
| Eine Linie, die den Rand einer Fläche quert | false | false |
| Eine Linie entlang des Rands einer Fläche | false | false |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false | false |
| Eine Linie außerhalb einer Fläche | false | false |
| Eine kleine Fläche in einer großen | **true** | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | **true** | false |
| Zwei Flächen mit gemeinsamer Kante | false | false |
| Zwei Flächen, die sich in einer Ecke berühren | false | false |
| Zwei Flächen, die sich überlappen | false | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | **true** | **true** |
| Zwei getrennte Flächen | false | false |
| Eine Fläche im Loch einer anderen | false | false |

## Einzelheiten

- **Ein Punkt auf dem Rand ist nicht „within“ einer Fläche.** Er liegt auf dem Rand, nicht im Inneren; `CoveredBy` sagt „innen oder auf dem Rand“.
- **Ein Punkt auf einer Linie ist „within“ der Linie,** wenn er nicht an einem Ende liegt: Die Enden sind der Rand der Linie („Ein Punkt auf einer Linie“, „Ein Punkt am Ende einer Linie“).
- **Gleiche Geometrien sind „within“ voneinander.**
- **Leere Geometrien** sind in nichts „within“.

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = b.Buffer(t).Contains(a);
```

`Buffer(t)` vergrößert den Behälter ringsum um `t`. Er ist der teure Teil; für viele Tests gegen dieselbe Geometrie berechnen Sie den Buffer einmal und behalten ihn.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Contains`](contains.md), [`CoveredBy`](covered-by.md), [Prädikate und DE-9IM](../predicates.md).
