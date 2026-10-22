# Disjoint

`a.Disjoint(b)` ist `true`, wenn die beiden Geometrien keinen Punkt gemeinsam haben.

## DE-9IM

`FF*FF****`: Die Inneren und die Ränder treffen sich nicht.

In Worten: Das Innere von `a` teilt nichts mit dem Inneren oder dem Rand von `b` (`F` bei II und IB), und der Rand von `a` auch nicht (`F` bei BI und BB). Was im Äußeren liegt, wird nicht gefragt (`*`).

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Disjoint(b)` |
|---|---|
| Ein Punkt in einer Fläche | false |
| Ein Punkt auf dem Rand einer Fläche | false |
| Ein Punkt außerhalb einer Fläche | **true** |
| Ein Punkt im Loch einer Fläche | **true** |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false |
| Ein Punkt am Ende einer Linie | false |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | false |
| Ein Punkt neben einer Linie | **true** |
| Zwei gleiche Punkte | false |
| Zwei Linien, die sich kreuzen | false |
| Zwei Linien, die Ende an Ende liegen | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false |
| Zwei Linien, die ein Stück zusammen laufen | false |
| Zwei gleiche Linien (eine umgekehrt) | false |
| Eine Linie in einer Fläche | false |
| Eine Linie, die den Rand einer Fläche quert | false |
| Eine Linie entlang des Rands einer Fläche | false |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false |
| Eine Linie außerhalb einer Fläche | **true** |
| Eine kleine Fläche in einer großen | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false |
| Zwei Flächen mit gemeinsamer Kante | false |
| Zwei Flächen, die sich in einer Ecke berühren | false |
| Zwei Flächen, die sich überlappen | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | false |
| Zwei getrennte Flächen | **true** |
| Eine Fläche im Loch einer anderen | **true** |

## Einzelheiten

- **Die Verneinung von `Intersects`:** `a.Disjoint(b)` ist gleich `!a.Intersects(b)`.
- **Das einzige Prädikat, das für eine leere Geometrie `true` ist.** Alle anderen sind `false`, weil eine leere Geometrie keinen Punkt hat, der in Beziehung stehen könnte.
- **Symmetrisch.**
- **Getrennt heißt getrennt.** Eine Fläche im Loch einer anderen ist zu ihr disjunkt, so nah sie auch liegt („Eine Fläche im Loch einer anderen“).
- **Exakt bis zum letzten Bit:** „disjunkt“ gilt auch, wenn die Geometrien nur einen Rundungsfehler auseinanderliegen ([Exaktheit](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz)).

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = !a.IsWithinDistance(b, t);
```

Derselbe Aufruf, verneint.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Intersects`](intersects.md), [Prädikate und DE-9IM](../predicates.md).
