# Intersects

`a.Intersects(b)` ist `true`, wenn die beiden Geometrien mindestens einen Punkt gemeinsam haben, auch nur einen einzelnen Punkt auf einem Rand.

## DE-9IM

Es gibt kein einzelnes Muster: `Intersects` ist die Verneinung von `Disjoint` (`FF*FF****`), ist also wahr, wenn eine der Zellen II, IB, BI oder BB nicht leer ist (`T********`, `*T*******`, `***T*****` oder `****T****`).

In Worten: Der gemeinsame Punkt darf überall liegen, in jedem der vier Paare Inneres/Rand von `a` mit Inneres/Rand von `b`; es genügt, dass eines davon etwas teilt.

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Intersects(b)` |
|---|---|
| Ein Punkt in einer Fläche | **true** |
| Ein Punkt auf dem Rand einer Fläche | **true** |
| Ein Punkt außerhalb einer Fläche | false |
| Ein Punkt im Loch einer Fläche | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | **true** |
| Ein Punkt am Ende einer Linie | **true** |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | **true** |
| Ein Punkt neben einer Linie | false |
| Zwei gleiche Punkte | **true** |
| Zwei Linien, die sich kreuzen | **true** |
| Zwei Linien, die Ende an Ende liegen | **true** |
| Eine Linie, die auf einer anderen Linie endet (ein T) | **true** |
| Zwei Linien, die ein Stück zusammen laufen | **true** |
| Zwei gleiche Linien (eine umgekehrt) | **true** |
| Eine Linie in einer Fläche | **true** |
| Eine Linie, die den Rand einer Fläche quert | **true** |
| Eine Linie entlang des Rands einer Fläche | **true** |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | **true** |
| Eine Linie außerhalb einer Fläche | false |
| Eine kleine Fläche in einer großen | **true** |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | **true** |
| Zwei Flächen mit gemeinsamer Kante | **true** |
| Zwei Flächen, die sich in einer Ecke berühren | **true** |
| Zwei Flächen, die sich überlappen | **true** |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | **true** |
| Zwei getrennte Flächen | false |
| Eine Fläche im Loch einer anderen | false |

## Einzelheiten

- **Berührung zählt.** Zwei Flächen, die nur eine Kante oder eine Ecke teilen, schneiden sich. Wenn Sie „sie überlappen in der Fläche“ meinen, nehmen Sie `Overlaps`, oder fragen Sie `Intersects` und nicht `Touches`.
- **Symmetrisch.** `a.Intersects(b)` ist gleich `b.Intersects(a)`.
- **Leere Geometrien** schneiden nichts: Das Ergebnis ist `false`.
- **Schnell.** `Intersects` berechnet nicht die ganze Matrix, sondern nimmt einen schnellen exakten Weg (erst Segmentschnitt, dann Enthaltensein). Für viele Tests gegen eine Geometrie nutzen Sie `Prepare()` ([Prädikate](../predicates.md)).
- **Exakt bis zum letzten Bit.** Ein Punkt, den Sie nur auf eine Linie setzen, durch Klicken oder Rechnen, schneidet sie fast nie ([Exaktheit](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz)).

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.IsWithinDistance(b, t);
```

Billig: Es prüft zuerst die Envelopes und hört beim ersten Segmentpaar innerhalb der Distanz auf. Für viele Abfragen nutzen Sie `Prepare()` und `prepared.IsWithinDistance(b, t)`.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Disjoint`](disjoint.md), [`Touches`](touches.md), [`Overlaps`](overlaps.md), [Prädikate und DE-9IM](../predicates.md).
