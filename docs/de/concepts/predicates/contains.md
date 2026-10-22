# Contains

`a.Contains(b)` ist `true`, wenn `b` in `a` liegt und die beiden mindestens einen inneren Punkt teilen. Es ist das Spiegelbild von `Within`: `a.Contains(b)` ist gleich `b.Within(a)`.

## DE-9IM

`T*****FF*`: Die Inneren treffen sich, und kein Teil von `b` liegt im Äußeren von `a`.

In Worten: Die Inneren teilen etwas (`T` bei II), und das Äußere von `a` teilt nichts mit dem Inneren oder dem Rand von `b` (`F` bei EI und EB): Nichts von `b` liegt außerhalb von `a`.

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Contains(b)` | `b.Contains(a)` |
|---|---|---|
| Ein Punkt in einer Fläche | false | **true** |
| Ein Punkt auf dem Rand einer Fläche | false | false |
| Ein Punkt außerhalb einer Fläche | false | false |
| Ein Punkt im Loch einer Fläche | false | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false | **true** |
| Ein Punkt am Ende einer Linie | false | false |
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
| Eine Linie entlang des Rands einer Fläche | false | false |
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

- **Ein Punkt oder eine Linie auf dem Rand ist nicht enthalten.** Ein Polygon *enthält* einen Punkt auf seinem Rand oder eine Linie entlang seines Rands nicht, weil sie keinen Punkt in seinem Inneren haben. `Covers` sagt „innen oder auf dem Rand“ („Ein Punkt auf dem Rand einer Fläche“ und „Eine Linie entlang des Rands einer Fläche“ in der Tabelle, mit vertauschten Rollen gelesen).
- **Gleiche Geometrien enthalten einander.** `a.Contains(a)` ist `true` (bei einer Geometrie, die nicht leer ist).
- **Eine Fläche enthält einen Teil von sich,** der ihren Rand von innen berührt.
- **Leere Geometrien** enthalten nichts und sind in nichts enthalten.
- **Auf die Rollen achten.** In der Tabelle ist die erste Geometrie in der ersten Spalte der Behälter und in der anderen die zweite.

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.Buffer(t).Contains(b);
```

`Buffer(t)` vergrößert den Behälter ringsum um `t`. Er ist der teure Teil; für viele Tests gegen dieselbe Geometrie berechnen Sie den Buffer einmal und behalten ihn.

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`Within`](within.md), [`Covers`](covers.md), [Prädikate und DE-9IM](../predicates.md).
