# Punktereignisse und Ereignisse auf der Karte zeichnen

`LineEvents` (ADR 0033) rechnet mit Tabellen von Ereignissen mit einem Bereich. Ein Routensystem hat auch Ereignisse bei **einem einzigen M-Wert**: eine Brücke, einen Durchlass, eine Schadensmeldung, einen Hydranten. Ihre Fragen sind andere: *Welches Tempolimit gilt an dieser Brücke?*, *welche Brücken liegen auf dieser Strecke?*, *welche haben gar kein Tempolimit?* Und jede Tabelle, aus Bereichen oder aus Punkten, muss **auf der Karte gezeichnet** werden. Das ergänzt den Typ **`PointEvent<T>`**, die Operationen **`PointEvents.Between`**, **`Within`** und **`Outside`** und **`LocateEvents`**, das eine ganze Tabelle auf die Route zeichnet.

*English version: [0040-punktereignisse-und-ereignisse-auf-der-karte.md](0040-punktereignisse-und-ereignisse-auf-der-karte.md)*

## Consequences

- **Ort:** `PointEvent<T>` und die statische Klasse `PointEvents` in `SpatialNET.Extensions` (eigene Datei) und die beiden Erweiterungsmethoden `LocateEvents` in `LinearReferencing` (ADR 0017), Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **Der Datentyp:** `readonly record struct PointEvent<T>(double M, T Value)`, das Gegenstück zu `LineEvent<T>` (ADR 0033): ein einfacher Wert ohne Prüfung im Konstruktor, mit den Attributen nach Wahl des Hosts. Wie dort liest die Bibliothek nie eine Tabelle und kennt keine Routen-Id (der Host gruppiert nach Route), und die M-Werte sind M-Werte, keine Längen.
- **Operationen** (statisch, keine Erweiterungsmethoden, aus dem Grund von ADR 0033; Ergebnisse sind Listen, nach M sortiert, und Ereignisse mit gleichem M behalten die Reihenfolge der Eingabe):

  | Methode | Bedeutung |
  |---|---|
  | `PointEvents.Between(points, from, to)` | die Punktereignisse, deren M im geschlossenen Bereich liegt; vertauschte Grenzen sind derselbe Bereich |
  | `PointEvents.Within(points, lines, combine)` | für jedes Punktereignis die Linienereignisse, die sein M enthalten: ein Ergebnis je Paar, beim M des Punktes, mit dem Wert, den `combine` aus beiden macht |
  | `PointEvents.Outside(points, lines)` | die Punktereignisse, die in keinem Linienereignis liegen |

  Die Bereiche der Linienereignisse sind **geschlossen**, ein Punkt genau auf der Grenze zweier benachbarter Ereignisse liegt also in **beiden**, und `Within` liefert zwei Ergebnisse (wie `LineEvents.At`, ADR 0033); Linienereignisse dürfen sich überlappen, und jedes überlappende ergibt ein Ergebnis. Ein Linienereignis **ohne Länge** (`From == To`) wird ignoriert, und `From` größer als `To` wird vertauscht gelesen, wie dort. `NaN` oder ein unendliches M, `From` oder `To` ist eine `ArgumentOutOfRangeException`, die das Ereignis nennt; eine `null`-Tabelle oder -Funktion ist eine `ArgumentNullException`.
- **`Outside` ist die Qualitätsprüfung**: eine Brücke ohne Tempolimit, eine Meldung außerhalb der erfassten Strecke, eine Lücke in einer Tabelle. Zusammen mit `Within` teilt es die Punktereignisse in die mit und die ohne Linienereignis, und `LineEvents.Difference` aus einem Ereignis, das die ganze Route bedeckt, und einer Linientabelle liefert die Lücken dieser Tabelle.
- **`LocateEvents`** zeichnet eine Tabelle auf die Route, **ein Eintrag je Ereignis, in der Reihenfolge der Eingabe**:

  ```csharp
  IReadOnlyList<(LineEvent<T> Event, IPolyline2D Shape)> LocateEvents<T>(this IPolyline2D route, IEnumerable<LineEvent<T>> events, IGeometryFactory? factory = null)
  IReadOnlyList<(PointEvent<T> Event, IMultiPoint2D Points)> LocateEvents<T>(this IPolyline2D route, IEnumerable<PointEvent<T>> events, IGeometryFactory? factory = null)
  ```

  Ein Linienereignis wird zur Strecke von `LocateBetween` (ADR 0032), ein Punktereignis zu den Punkten von `LocateAlong` (mehrere, wo die Route den Wert mehr als einmal passiert). Ein Ereignis, dessen M die Route nicht erreicht, behält seinen Eintrag, mit einer leeren Polylinie bzw. einem leeren Multipoint, damit sich das Ergebnis nach Position und nach Ereignis der Eingabe zuordnen lässt. Der Index der Route wird **einmal** für die ganze Tabelle gebaut, eine Tabelle vieler Ereignisse kostet also kaum mehr als eines, wo eine Schleife von `LocateBetween` ihn jedes Mal bauen würde. Die Route braucht M (wie bei diesen Methoden); ein Ereignis mit `NaN` oder unendlicher Grenze ist eine `ArgumentOutOfRangeException`.
- **Das Ergebnis ist eine Tupelliste, kein neuer Typ**: Das Ereignis und seine Geometrie ist alles, und ein Tupel lässt sich in einer `foreach`-Schleife zerlegen. Die Geometrien entstehen über die Factory; die Ereignisse sind die eigenen Werte des Hosts.
- **Playground (ADR 0011):** ein neuer Parametertyp **`text`** (mehrere Zeilen) für die Tabellen von Ereignissen und die Einträge `LocateEvents` (eine Tabelle auf der Route gezeichnet) und `IntersectEvents` (zwei Tabellen geschnitten, auf der Route gezeichnet). Die Tabelle ist schlichter Text, ein Ereignis je Zeile: `from..to value` für ein Linienereignis, `m value` für ein Punktereignis, `#` beginnt einen Kommentar. Die Beispiele sind eine Straße mit Tempolimits, Belägen und Brücken und eine Spitzkehre, in der ein M-Bereich und ein Wert zweimal vorkommen.
- **Tests (ADR 0008):** `[Covers]`-Tests. `Between` (geschlossener Bereich, vertauschte Grenzen, sortiert, gleiches M behält die Eingabereihenfolge); `Within` (ein Ergebnis je Paar, zwei auf einer Grenze, eines je überlappendem Ereignis, Linienereignisse ohne Länge ignoriert, vertauschte Grenzen, unsortierte Eingabe); `Outside`; ein **Modelltest**, der `Within` und `Outside` über zufällige Tabellen mit einer Brute-Force-Prüfung vergleicht; Fehler; `LocateEvents` (die Reihenfolge und ein Eintrag je Ereignis, leere Geometrien, mehrere Punkte, wo die Route einen Wert zweimal passiert, Übereinstimmung mit `LocateBetween` und `LocateAlong`, Fehler).
- **Vorerst nicht dabei:** ein Punktereignis der nächsten Route zuordnen (ein Punkt auf der Karte ist `ProjectToM`, ADR 0017, und `LocateFeature`, ADR 0034, liefert den Bereich einer Linie oder Fläche), mehrere Routen in einem Aufruf (der Host gruppiert) und eine Toleranz für „nahe“ an einem Linienereignis (ein Punktereignis liegt genau in einem Bereich oder nicht).

## Considered Options

- Ein Punktereignis als Linienereignis mit `From == To` (verworfen: `LineEvents` ignoriert Ereignisse ohne Länge, weil ein Stück ohne Länge nichts ist, ein Punktereignis aber genau das ist; und die Typen sagen einem Leser, was was ist)
- Erweiterungsmethoden auf `IEnumerable<PointEvent<T>>` (verworfen aus dem Grund von ADR 0033: `Enumerable` hat eigene `Where`, `Except` und ähnliche, und die Operationen sind keine LINQ-Pipeline)
- `Within`, das nur den Wert des Linienereignisses oder einen Paartyp liefert (verworfen: `combine` überlässt dem Aufrufer die Wahl, und ein Tupel oder ein Record ist ein Lambda; ADR 0033 macht dasselbe bei `Intersect`)
- `LocateEvents`, das nur die Geometrien liefert (verworfen: Die Zuordnung zum Ereignis braucht der Aufrufer, um es zu gestalten oder zu beschriften, und das Zuordnen über den Index ist fragil, wenn die Eingabe eine Abfrage ist)
- Die Ereignisse weglassen, die die Route nicht erreicht (verworfen: Ein Eintrag je Ereignis hält das Ergebnis mit der Tabelle ausgerichtet, und eine leere Geometrie sagt, was sie ist; Voronoi macht dasselbe, ADR 0036)
- Ein `LocateEvents`, das eine Tabelle beliebiger Ereignisse über eine Funktion nimmt (verworfen: Die beiden Arten unterscheiden sich schon darin, was sie liefern, eine Polylinie oder ein Multipoint, und zwei Überladungen sind klarer als ein generischer Selektor)
- Ein Textformat für Tabellen in der Bibliothek (verworfen: Die Bibliothek liest nie Tabellen, ADR 0033; das Format gehört in den Playground, der es braucht, um eine Tabelle in ein Textfeld einzugeben)
