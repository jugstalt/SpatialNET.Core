# Touches

`a.Touches(b)` ist `true`, wenn sich die beiden Geometrien treffen, aber nur in Randpunkten: Ihre Inneren schneiden sich nicht.

## DE-9IM

Eines von `FT*******`, `F**T*****` oder `F***T****`: Die Inneren treffen sich nicht (`F` in II), aber die Ränder oder ein Inneres und ein Rand.

In Worten: Die Inneren teilen nichts (`F` bei II), aber etwas anderes wird geteilt: Das Innere von `a` trifft den Rand von `b` (`T` bei IB), oder der Rand von `a` trifft das Innere von `b` (`T` bei BI), oder die Ränder treffen sich (`T` bei BB).

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Touches(b)` |
|---|---|
| Ein Punkt in einer Fläche | false |
| Ein Punkt auf dem Rand einer Fläche | **true** |
| Ein Punkt außerhalb einer Fläche | false |
| Ein Punkt im Loch einer Fläche | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false |
| Ein Punkt am Ende einer Linie | **true** |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | false |
| Ein Punkt neben einer Linie | false |
| Zwei gleiche Punkte | false |
| Zwei Linien, die sich kreuzen | false |
| Zwei Linien, die Ende an Ende liegen | **true** |
| Eine Linie, die auf einer anderen Linie endet (ein T) | **true** |
| Zwei Linien, die ein Stück zusammen laufen | false |
| Zwei gleiche Linien (eine umgekehrt) | false |
| Eine Linie in einer Fläche | false |
| Eine Linie, die den Rand einer Fläche quert | false |
| Eine Linie entlang des Rands einer Fläche | **true** |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | **true** |
| Eine Linie außerhalb einer Fläche | false |
| Eine kleine Fläche in einer großen | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false |
| Zwei Flächen mit gemeinsamer Kante | **true** |
| Zwei Flächen, die sich in einer Ecke berühren | **true** |
| Zwei Flächen, die sich überlappen | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | false |
| Zwei getrennte Flächen | false |
| Eine Fläche im Loch einer anderen | false |

## Einzelheiten

- **Was „Inneres“ und „Rand“ sind:** Ein Punkt hat keinen Rand (der Punkt ist sein Inneres); eine Linie hat ihre beiden Endpunkte als Rand und den Rest als Inneres; eine Fläche hat ihre Kante als Rand.
- **Ein Punkt am Ende einer Linie berührt die Linie, ein Punkt in der Mitte nicht.** Das Ende ist der Rand, die Mitte das Innere („Ein Punkt am Ende einer Linie“, „Ein Punkt auf einer Linie“, „Ein Punkt an einem Knick“). Ein Knick (ein Stützpunkt) mitten in einer Linie ist nichts Besonderes: Er gehört zum Inneren.
- **Eine geschlossene Linie hat keinen Rand.** Ein Punkt an ihrem Startpunkt liegt in ihrem Inneren und berührt sie deshalb nicht. Bei einer Polylinie aus mehreren Pfaden zählt ein Endpunkt nur dann als Rand, wenn dort eine ungerade Zahl von Pfadenden liegt ([Prädikate](../predicates.md)).
- **Zwei Punkte berühren sich nie:** Ein Punkt hat keinen Rand.
- **Flächen berühren sich** an einer gemeinsamen Kante oder in einer Ecke. Überlappen sie in der Fläche, berühren sie sich nicht („Zwei Flächen, die sich überlappen“).
- **Linien berühren sich** Ende an Ende oder dort, wo eine auf der anderen endet (ein T) („Zwei Linien, die Ende an Ende liegen“, „Eine Linie, die auf einer anderen Linie endet“). Sich kreuzende Linien berühren sich nicht; Linien, die ein Stück zusammen laufen, auch nicht.
- **Eine Linie entlang des Rands einer Fläche berührt sie:** Die Linie liegt auf dem Rand und betritt das Innere nicht.
- **Symmetrisch.** Leere Geometrien berühren nichts.

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b);
```

Flächen: Sie treffen sich innerhalb von `t` und überlappen um weniger als `t` (`Buffer(-t)` schrumpft die Fläche). Für eine Linie oder einen Punkt nehmen Sie den Abstand: Sie berühren sich, wenn `IsWithinDistance(b, t)`. Für „am Ende einer Linie“ fragen Sie nach dem Rand: `line.Boundary().IsWithinDistance(point, t)`.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Intersects`](intersects.md), [`Overlaps`](overlaps.md), [`Crosses`](crosses.md), [Prädikate und DE-9IM](../predicates.md).
