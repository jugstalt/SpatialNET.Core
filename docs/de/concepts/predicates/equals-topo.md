# EqualsTopo

`a.EqualsTopo(b)` ist `true`, wenn die beiden Geometrien dieselbe Punktmenge belegen, gleichgültig, wie sie dargestellt sind.

## DE-9IM

`T*F**FFF*`: Die Inneren treffen sich, und nichts von beiden liegt im Äußeren der anderen.

In Worten: Die Inneren teilen etwas (`T` bei II), nichts von `a` liegt außerhalb von `b` (`F` bei IE und BE), und nichts von `b` liegt außerhalb von `a` (`F` bei EI und EB).

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.EqualsTopo(b)` |
|---|---|
| Ein Punkt in einer Fläche | false |
| Ein Punkt auf dem Rand einer Fläche | false |
| Ein Punkt außerhalb einer Fläche | false |
| Ein Punkt im Loch einer Fläche | false |
| Ein Punkt auf einer Linie (nicht an ihrem Ende) | false |
| Ein Punkt am Ende einer Linie | false |
| Ein Punkt an einem Knick (Stützpunkt) mitten in einer Linie | false |
| Ein Punkt neben einer Linie | false |
| Zwei gleiche Punkte | **true** |
| Zwei Linien, die sich kreuzen | false |
| Zwei Linien, die Ende an Ende liegen | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false |
| Zwei Linien, die ein Stück zusammen laufen | false |
| Zwei gleiche Linien (eine umgekehrt) | **true** |
| Eine Linie in einer Fläche | false |
| Eine Linie, die den Rand einer Fläche quert | false |
| Eine Linie entlang des Rands einer Fläche | false |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false |
| Eine Linie außerhalb einer Fläche | false |
| Eine kleine Fläche in einer großen | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false |
| Zwei Flächen mit gemeinsamer Kante | false |
| Zwei Flächen, die sich in einer Ecke berühren | false |
| Zwei Flächen, die sich überlappen | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | **true** |
| Zwei getrennte Flächen | false |
| Eine Fläche im Loch einer anderen | false |

## Einzelheiten

- **Die Darstellung spielt keine Rolle:** der Startpunkt eines Rings, seine Richtung, die Richtung einer Linie, die Reihenfolge der Teile und zusätzliche Stützpunkte auf einer Geraden („Zwei gleiche Linien (eine umgekehrt)“, „Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn)“).
- **`EqualsExact` ist die andere Frage:** Es vergleicht die Koordinaten eins zu eins, wie angegeben ([Maße und Konstruktionen](../measures-and-constructions.md)). `EqualsTopo` für „dieselbe Form“, `EqualsExact` für „dieselben Daten“.
- **`false`, wenn eine leer ist.** Auch zwei leere Geometrien sind nicht `EqualsTopo`: Die Prädikate sind für leere Eingaben falsch (`Disjoint` ist die einzige Ausnahme).
- **Symmetrisch.** Exakt bis zum letzten Bit: Eine Kopie, die einen Rundungsfehler entfernt ist, ist nicht gleich ([Exaktheit](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz)).

## Mit Toleranz

Das Prädikat ist exakt bis zum letzten Bit. Um „nah genug“ zu fragen, mit einer Toleranz `t` in der Einheit Ihrer Koordinaten (bei Vermessungsdaten 0,01 für 1 cm):

```csharp
bool result = a.HausdorffDistance(b) <= t;
```

`HausdorffDistance` ist diskret (Stützpunkte und, mit `densifyFraction` unter 1, Punkte dazwischen) und eine untere Schranke; geben Sie bei langen Segmenten `densifyFraction` an (etwa 0,1).

Die Bibliothek hat keine globale Toleranz; siehe [Exaktheit in der Praxis](../predicates.md#exaktheit-in-der-praxis-und-die-alternativen-mit-toleranz).

## Siehe auch

[`EqualsExact`](../measures-and-constructions.md), [`Covers`](covers.md), [Prädikate und DE-9IM](../predicates.md).
