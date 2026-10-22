# Overlaps

`a.Overlaps(b)` ist `true`, wenn zwei Geometrien **gleicher Dimension** sich teilweise überlappen: Sie teilen einen Teil dieser Dimension, und keine liegt in der anderen.

## DE-9IM

`T*T***T**` für zwei Flächen oder zwei Multipunkte, `1*T***T**` für zwei Linien.

In Worten: Die Inneren teilen etwas (`T` bei II), ein Teil von `a` liegt außerhalb von `b` (`T` bei IE), und ein Teil von `b` liegt außerhalb von `a` (`T` bei EI). Bei zwei Linien muss das Geteilte selbst eine Linie sein (`1` bei II): Linien, die sich nur kreuzen, teilen einen Punkt (`0`), das zählt nicht.

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Overlaps(b)` |
|---|---|
| Ein Punkt in einer Fläche | false |
| Ein Punkt auf dem Rand einer Fläche | false |
| Ein Punkt außerhalb einer Fläche | false |
| Ein Punkt im Loch einer Fläche | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false |
| Ein Punkt am Ende einer Linie | false |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | false |
| Ein Punkt neben einer Linie | false |
| Zwei gleiche Punkte | false |
| Zwei Linien, die sich kreuzen | false |
| Zwei Linien, die Ende an Ende liegen | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false |
| Zwei Linien, die ein Stück zusammen laufen | **true** |
| Zwei gleiche Linien (eine umgekehrt) | false |
| Eine Linie in einer Fläche | false |
| Eine Linie, die den Rand einer Fläche quert | false |
| Eine Linie entlang des Rands einer Fläche | false |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false |
| Eine Linie außerhalb einer Fläche | false |
| Eine kleine Fläche in einer großen | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false |
| Zwei Flächen mit gemeinsamer Kante | false |
| Zwei Flächen, die sich in einer Ecke berühren | false |
| Zwei Flächen, die sich überlappen | **true** |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | false |
| Zwei getrennte Flächen | false |
| Eine Fläche im Loch einer anderen | false |

## Einzelheiten

- **Nur bei gleicher Dimension.** Eine Linie und eine Fläche überlappen sich nie, in welcher Beziehung sie auch stehen („Eine Linie in einer Fläche“ ist `Within`, kein Überlappen).
- **Flächen** überlappen sich, wenn sie Fläche teilen und jede auch Fläche für sich hat („Zwei Flächen, die sich überlappen“). Liegt eine in der anderen, überlappen sie sich nicht; teilen sie nur eine Kante oder eine Ecke, berühren sie sich.
- **Linien** überlappen sich, wenn sie ein Stück zusammen laufen und jede auch einen Teil für sich hat („Zwei Linien, die ein Stück zusammen laufen“). Linien, die sich in einem Punkt kreuzen, teilen nur einen Punkt, was nicht die Dimension einer Linie ist: Sie kreuzen sich, sie überlappen sich nicht.
- **Gleiche Geometrien überlappen sich nicht** (jede liegt in der anderen).
- **Symmetrisch.** Leere Geometrien überlappen sich nicht.

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.Buffer(-t).Intersects(b.Buffer(-t)) && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a);
```

Nur für Flächen: Die Formel nutzt `Buffer(-t)`, das für Polygone geht.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Intersects`](intersects.md), [`Touches`](touches.md), [`Contains`](contains.md), [Prädikate und DE-9IM](../predicates.md).
