# Seitlicher Versatz und Projektion mit Abstand und Seite

Zwei Dinge, die ein WebGIS mit einer Route tut und die zusammengehören. Erstens wird ein Symbol, eine Beschriftung oder ein Ereignis **neben** der Straße gezeichnet, nicht auf ihrer Achse: „Kilometer 12,4, 3 m rechts“. Zweitens soll ein Klick nicht nur „Kilometer 12,437“ sagen, sondern auch **„3 m links der Achse“**, damit der Host entscheiden kann, ob der Klick die Straße überhaupt gemeint hat. Beides ist dieselbe Größe, der vorzeichenbehaftete Abstand zur Linie, und es sind die beiden Richtungen einer Umrechnung: `Interpolate(position, offset)` geht von einer Position und einem Versatz zu einem Punkt, und **`ProjectDetailed`** geht von einem Punkt zurück zu Position und Versatz.

*English version: [0035-seitlicher-versatz-und-projektion-mit-seite.md](0035-seitlicher-versatz-und-projektion-mit-seite.md)*

## Consequences

- **Ort:** zwei neue Überladungen, `Interpolate(distance, offset)` und `InterpolateByM(m, offset)`, die Methode `ProjectDetailed` (vier Überladungen) und der Werttyp `LineProjection` in `LinearReferencing` (ADR 0017) in `SpatialNET.Extensions`, Namespace `SpatialNET`. Die vorhandenen Methoden bleiben unverändert, für die Paketvalidierung und die API-Verfolgung ist das also eine reine Ergänzung (ADR 0028).
- **Das Vorzeichen:** ein **positiver Versatz liegt links der Richtung der Linie, ein negativer rechts**, im üblichen mathematischen Sinn (gegen den Uhrzeigersinn): Fährt man nach Osten, ist links Norden. Das ist die Konvention von `BufferOptions.SingleSided` (positiv links, ADR 0006) und von NTS `LengthIndexedLine.ExtractPoint(index, offset)`, womit die Längenvariante in den Tests innerhalb der Segmente geprüft wird.
- **`Interpolate(distance, offset)`** liefert den Punkt bei `distance` entlang der Linie, um `offset` senkrecht zur Linie verschoben. Mit 0 ist es `Interpolate(distance)`. `InterpolateByM(m, offset)` macht dasselbe für den ersten Treffer eines M-Wertes. Beide liefern eine `Coordinate?`, einen Wert ohne Factory (ADR 0012).
  - **Die Richtung** ist die des Segments, das die Position enthält. An einem **Stützpunkt** ist es das Segment, das **dort endet**, das frühere, da die Grenze zweier Pfade zum Ende des früheren Pfades gehört (ADR 0017), **wo NTS das Segment nimmt, das dort beginnt** (eine dokumentierte Abweichung, getestet); am Anfang der Linie ist es das erste Segment. Eine Distanz außerhalb der Linie wird begrenzt (ADR 0017) und nimmt das erste oder letzte Segment. Der Punkt ist kein Bogen um eine Ecke: An einem Stützpunkt springt der versetzte Punkt von einer Seite der Biegung auf die andere, was eine Zeichnung an dieser Position will.
  - **Segmente der Länge 0 werden übersprungen.** Eine Linie ganz ohne Segment mit Länge hat keine Richtung, ein Versatz ungleich 0 ergibt also `null` (und eine Linie ohne Punkte `null`); ein Versatz von 0 braucht keine Richtung und liefert den Punkt.
  - Z und M sind die des Punktes **auf der Linie**; der Versatz verschiebt nur X und Y (ADR 0015).
- **`ProjectDetailed(point)`** liefert eine `LineProjection?` (`null` für eine Linie ohne Punkte):

  | Feld | Bedeutung |
  |---|---|
  | `Position` | die Länge des nächsten Punktes, wie `Project` |
  | `Point` | der nächste Punkt auf der Linie, mit auf dem Segment interpoliertem Z und M, wie `ProjectToM` ihn liest |
  | `Distance` | der Abstand des Punktes dazu, nicht negativ |
  | `Offset` | `Distance` mit der Seite als Vorzeichen: positiv links, negativ rechts |

  `Position` und `Point` sind die von `Project` und `ProjectToM`, mit derselben Gleichstandsregel (die kleinste Länge unter gleich nahen Punkten). Überladungen für `IPoint2D` und mit einem **Längenfenster** `(from, to)` wie in ADR 0031, damit ein Punkt nahe einer Schleife seinen Durchgang wählen kann und trotzdem seine Seite bekommt.
- **Rundreise.** Für einen Punkt, dessen nächster Punkt im Inneren eines Segments liegt, ergibt `Interpolate(Position, Offset)` den Punkt zurück. Wo der nächste Punkt ein **Stützpunkt oder ein Ende** ist, ist die Senkrechte nicht eindeutig, und `Interpolate` nimmt im Allgemeinen ein anderes Segment als das nächste des Punktes, die Rundreise wird dort also nicht versprochen.
- **Die Seite des Segments des nächsten Punktes.** An einer konvexen Ecke sind beide Segmente für einen Punkt im äußeren Keil gleich nah; das erste gewinnt wie bei `Project`, die Seite ist also die des **früheren** Segments, wie auch die Richtungsregel von `Interpolate`. Ein Punkt jenseits des Linienendes hat seinen nächsten Punkt am Ende und die Seite der **Verlängerung** des Endsegments. Ein Punkt genau **auf dieser Verlängerung** (oder auf der Linie, mit Abstand 0) liegt auf keiner Seite: Sein `Offset` zählt als links, also `+Distance`, auf der Linie also 0. Ein Segment der Länge 0 am nächsten Stützpunkt weicht einem echten, sodass eine Linie, die mit einem doppelten Stützpunkt beginnt, trotzdem eine Seite hat.
- **Ausnahmen (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei nicht endlichem Punkt, `offset`, `distance`, `m`, `from` oder `to`. Eine leere Linie antwortet `null` und wirft nie.
- **Kosten:** O(n) wie `Project`; die Richtungen der Linie werden einmal je Aufruf aufgebaut, nur wenn ein Versatz verlangt ist.
- **Tests (ADR 0008):** NTS hat `ExtractPoint(index, offset)`, die Versatzvariante wird also für zufällige Linien innerhalb der Segmente damit verglichen (die Stützpunktregel ist die eigene dieser Bibliothek und wird von Hand getestet). Die Seite von `ProjectDetailed` ist von Hand getestet (links, rechts, auf der Linie, jenseits des Endes, auf der Verlängerung, eine Schleife mit Fenster, doppelte Stützpunkte), und eine **Rundreise-Eigenschaft** prüft `Interpolate(Position, Offset)` gegen den Eingabepunkt für zufällige Linien und Punkte. Außerdem `ProjectDetailed` gegen `Project` und `ProjectToM`, Null-Ergebnisse und ungültige Eingaben.
- **Playground und Dokumentation:** `Interpolate` behält seinen Deskriptor unter demselben Namen; `ProjectDetailed` hat keinen (ADR 0011), da sein Ergebnis eine Struktur ist, die die Oberfläche nicht zeigt; es ist mit dieser Begründung als ausgenommen geführt. Die Konzeptseite „Lineare Referenzierung“ hat einen Abschnitt mit Beispiel, und das Glossar hat „seitlicher Versatz“.
- **Vorerst nicht dabei:** ein Bogen oder eine Gehrung um eine Ecke für eine versetzte Linie (das ist `OffsetCurve`, ADR 0006), der Versatz eines ganzen `Substring`, und eine `LineProjection` für Ereignisse.

## Considered Options

- `offset` als optionalen Parameter zu `Interpolate(distance)` ergänzen, verworfen: Es würde eine ausgelieferte Signatur ändern und die Binärkompatibilität brechen; eine neue Überladung lässt den alten Aufruf, wie er ist
- Ein Ergebnis von `Project` mit dem Versatz als `out`-Parametern, verworfen: Ein Record-Struct ist leichter zu lesen und zu erweitern, und `Project` bleibt eine schlichte Zahl
- ADR 0017 hatte „eine Struktur mit Länge, Punkt und Abstand“ verworfen, weil `Interpolate(Project(p))` und `Distance` den Rest liefern; das gilt für den Punkt, aber nicht für die **Seite**, die man aus diesen Aufrufen nicht bekommt, und die Seite macht die Umrechnung umkehrbar. Dieses ADR ergänzt die Struktur aus diesem Grund
- Positiven Versatz nach rechts, verworfen: Die mathematische und NTS-Konvention ist links, und `SingleSided` nutzt sie
- Ein eigenes `Side`-Enum neben dem Abstand ohne Vorzeichen, verworfen: Eine vorzeichenbehaftete Zahl nimmt `Interpolate` zurück, und eine Seite „auf der Linie“ bräuchte einen dritten Wert, den ein Vorzeichen 0 schon ist
- Die Winkelhalbierende der Biegung an einem Stützpunkt für die Richtung, verworfen: Sie liefert einen Punkt, der von keinem Segment den Versatzabstand hat, und weicht von JTS ab; das frühere Segment ist einfach und vorhersagbar
- Den Versatz entlang der begrenzten Distanz als Bogen jenseits der Enden, verworfen: Eine Position jenseits der Linie wird in dieser API überall auf ihr Ende begrenzt (ADR 0017), und die Richtung des Endsegments ist die natürliche Fortsetzung
- Bei einem Stützpunkt NTS folgen (das Segment, das dort beginnt), verworfen: Diese API löst jede Grenze zum Ende des früheren Teils auf (ADR 0017), und eine Regel für Positionen auf einem Stützpunkt ist leichter zu merken als eine andere für Versätze; die Abweichung ist dokumentiert und getestet
- `null` statt 0 für den Versatz eines Punktes auf der Verlängerung, verworfen: Er hat einen Abstand und einen nächsten Punkt, und „links“ per Konvention hält `Offset` eine Zahl, die `Interpolate` annimmt
