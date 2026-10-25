# Project mit Längenfenster

Eine Linie, die sich kreuzt oder auf sich zurückläuft (eine Spitzkehre, eine Schleife, eine Straße, die an sich selbst entlang zurückführt), hat für einen Klick mehrere nächste Punkte, und `Project` (ADR 0017) antwortet mit der **kleinsten Länge**, ein Klick auf den späteren Durchgang landet also auf dem früheren. Ein Host weiß meist, wo der Punkt liegen soll: nahe beim vorigen Ergebnis oder in dem Abschnitt, an dem er gerade arbeitet. **`Project` und `ProjectToM` mit Fenster `[from, to]`** suchen nur in dieser Strecke der Linie und unterscheiden die Durchgänge.

*English version: [0031-project-mit-laengenfenster.md](0031-project-mit-laengenfenster.md)*

## Consequences

- **Ort:** je zwei weitere Überladungen von `Project` und `ProjectToM` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **API** (Erweiterungsmethoden auf `IPolyline2D`):

  | Methode | Liefert |
  |---|---|
  | `Project(Coordinate point, double from, double to)` | die Länge, auf der ganzen Linie, des Punktes, der `point` im Fenster am nächsten liegt |
  | `ProjectToM(Coordinate point, double from, double to)` | den M-Wert an diesem Punkt |

  Beide gibt es auch für `IPoint2D`, das nur umwandelt, wie ohne Fenster. Das Ergebnis ist eine Länge (oder M) auf der **ganzen Linie** und lässt sich unverändert an `Interpolate`, `Substring` und die übrigen Methoden weitergeben.
- **Das Fenster** ist der Teil der Linie zwischen den Längen `from` und `to`, mit den Pfaden aneinandergelegt wie überall (ADR 0017). Beide werden auf `[0, Länge]` begrenzt, und ist `from` größer als `to`, werden sie **vertauscht**: Anders als bei `Substring` hat eine Suche keine Richtung, es gibt also nichts umzukehren. Das Fenster ist **geschlossen**, der nächste Punkt darf an seinem Anfang oder Ende liegen, und ein Punkt, der der Linie außerhalb des Fensters näher ist, bekommt trotzdem den nächsten Punkt *innerhalb*. Ein leeres Fenster (`from == to`) antwortet mit dieser Länge.
- **Dieselben Regeln wie ohne Fenster:** Bei gleich nahen Punkten gewinnt die kleinste Länge; die Projektion auf ein Segment wird auf den Teil des Segments im Fenster begrenzt; `ProjectToM` liefert das M dieses Punktes, interpoliert, wenn beide Enden seines Segments M tragen, sonst `NaN`; eine leere Linie ergibt `NaN`. Ein Fenster über die ganze Linie ergibt genau das Ergebnis von `Project(point)`.
- **„Nach einer Position“** (JTS `IndexOfAfter`) ist das Fenster `[Position, Länge]`: `line.Length()` als `to` übergeben. Ein eigenes `ProjectAfter` kommt nicht dazu, denn JTS fällt auf die ganze Linie zurück, wenn nach der Position nichts liegt, und diese API antwortet lieber im Fenster, als zu raten.
- **Ausnahmen (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei nicht endlichem Punkt oder `NaN` bzw. unendlichem `from` oder `to`. Unendlich wird nicht als „bis zum Ende“ akzeptiert, damit das Verhalten dem aller anderen Längen in `LinearReferencing` entspricht.
- **Kosten:** O(n) wie `Project`; Segmente außerhalb des Fensters werden ohne Abstandsberechnung übersprungen.
- **`CalibrateM` (ADR 0030)** nutzt das Fenster noch nicht: Seine Anker tragen keinen Hinweis, wo sie liegen. Anker auf einem späteren Durchgang einer Schleife landen weiter auf dem früheren; ihnen ein Fenster zu geben (oder ihre Reihenfolge zu nutzen) ist ein möglicher nächster Schritt.
- **Tests (ADR 0008):** NTS hat nur `IndexOfAfter`, die Fälle sind deshalb von Hand geschrieben (`[Covers]`), dazu ein Brute-Force-Durchlauf einer dicht abgetasteten Spitzkehren-Straße gegen zufällige Punkte und Fenster. Außerdem: ein Fenster über die ganze Linie ist gleich keinem Fenster, das Ergebnis bleibt im Fenster, mehrere Pfade und Ein-Punkt-Pfade, leere Linien und ungültige Eingaben.
- **Playground und Dokumentation:** Der Playground hat je Algorithmus nur Eingaben ohne Möglichkeit, Längen für ein Fenster zu geben, und die M-Methoden sind ohnehin ausgenommen (ADR 0017), also kein Deskriptor; die Konzeptseite „Lineare Referenzierung“ hat einen Abschnitt.
- **Vorerst nicht dabei:** ein Ergebnis mit Abstand und Seite des nächsten Punktes, ein Fenster in M-Werten, ein Fenster für `CalibrateM`.

## Considered Options

- Nur `ProjectAfter(point, position)`, verworfen: Es deckt „danach“, aber nicht „in diesem Abschnitt“, und das Fenster schließt es ein
- `double.PositiveInfinity` als „bis zum Ende“ akzeptieren, verworfen: Alle anderen Längen hier müssen endlich sein, und `Length()` ist ein Aufruf
- Werfen, wenn `from` größer als `to` ist, verworfen: Für eine Suche hat die Reihenfolge der Grenzen keine Bedeutung, Vertauschen verliert nichts
- Auf die ganze Linie zurückfallen, wenn im Fenster kein näherer Punkt liegt (wie JTS `IndexOfAfter`), verworfen: Die Antwort läge dann außerhalb des Fensters, das der Aufrufer verlangt hat
- Ein `ProjectionOptions`-Objekt mit Fenster, Seite und Toleranz, vorerst verworfen: Zwei Längen sind alles, was dieser Schritt braucht, und die anderen Teile sind nicht entschieden
