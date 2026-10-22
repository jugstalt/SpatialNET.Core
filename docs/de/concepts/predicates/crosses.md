# Crosses

`a.Crosses(b)` ist `true`, wenn sich die Geometrien kreuzen. Was das heißt, hängt von den Dimensionen ab (Punkt 0, Linie 1, Fläche 2).

## DE-9IM

Nach den Dimensionen: `T*T******`, wenn `a` die kleinere Dimension hat (Punkt/Linie, Punkt/Fläche, Linie/Fläche), `T*****T**`, wenn `a` die größere hat, und `0********` bei zwei Linien.

In Worten, für `a` mit der kleineren Dimension: Die Inneren teilen etwas (`T` bei II), und ein Teil von `a` liegt außerhalb von `b` (`T` bei IE). Für `a` mit der größeren Dimension ist es das Spiegelbild: `T` bei II und EI. Bei zwei Linien teilen die Inneren nur einzelne Punkte (`0` bei II).

Wie man ein Muster liest: siehe [Ein Muster lesen](../predicates.md#ein-muster-lesen). Die neun Positionen sind `II IB IE BI BB BE EI EB EE` (Inneres, Rand und Äußeres von `a` gegen die von `b`); `T` = teilt etwas, `F` = teilt nichts, `*` = egal, `0`/`1`/`2` = teilt genau Punkte, Linien oder Flächen.

## Situationen

`a` ist die erste und `b` die zweite Geometrie. Jede Zeile ist eine Situation; die genauen Geometrien stehen in [Prädikate](../predicates.md#die-situationen).

| Situation (a, b) | `a.Crosses(b)` |
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
| Zwei Linien, die sich kreuzen | **true** |
| Zwei Linien, die Ende an Ende liegen | false |
| Eine Linie, die auf einer anderen Linie endet (ein T) | false |
| Zwei Linien, die ein Stück zusammen laufen | false |
| Zwei gleiche Linien (eine umgekehrt) | false |
| Eine Linie in einer Fläche | false |
| Eine Linie, die den Rand einer Fläche quert | **true** |
| Eine Linie entlang des Rands einer Fläche | false |
| Eine Linie außerhalb einer Fläche, die an ihrem Rand endet | false |
| Eine Linie außerhalb einer Fläche | false |
| Eine kleine Fläche in einer großen | false |
| Eine Fläche in einer anderen, die deren Rand von innen berührt | false |
| Zwei Flächen mit gemeinsamer Kante | false |
| Zwei Flächen, die sich in einer Ecke berühren | false |
| Zwei Flächen, die sich überlappen | false |
| Zwei gleiche Flächen (anderer Startpunkt und Umlaufsinn) | false |
| Zwei getrennte Flächen | false |
| Eine Fläche im Loch einer anderen | false |

## Einzelheiten

- **Zwei Linien kreuzen sich,** wenn sie sich in einzelnen Punkten treffen, in denen sie einander durchqueren („Zwei Linien, die sich kreuzen“). Linien, die Ende an Ende liegen, ein T bilden oder ein Stück zusammen laufen, kreuzen sich nicht.
- **Eine Linie kreuzt eine Fläche,** wenn ein Teil der Linie innen und ein Teil außen liegt („Eine Linie, die den Rand einer Fläche quert“). Eine Linie innen, außen oder entlang des Rands kreuzt sie nicht.
- **Punkte:** Ein Multipunkt kreuzt eine Linie oder eine Fläche, wenn einige seiner Punkte innen und einige außen liegen. Ein einzelner Punkt kann nichts kreuzen.
- **Zwei Flächen kreuzen sich nie** (dafür gibt es `Overlaps`).
- **Symmetrisch.** Leere Geometrien kreuzen sich nicht.

## Mit Toleranz

Es gibt kein einfaches Gegenstück. Eine Kreuzung ist eine Aussage über die Inneren, und „fast kreuzen“ hat keine einheitliche Bedeutung. Fragen Sie nach dem, was Sie meinen: `IsWithinDistance` für „sie treffen sich“ und `NearestPoints` für „wo“.

## Siehe auch

[`Touches`](touches.md), [`Overlaps`](overlaps.md), [`Intersects`](intersects.md), [Prädikate und DE-9IM](../predicates.md).
