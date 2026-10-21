# Lineare Referenzierung: nach Länge und nach M, erster Treffer entlang der Linie, Werte statt Geometrien

SpatialNET bekommt **lineare Referenzierung** auf Polylinien: Eine Position auf einer Linie wird entweder über ihre **Länge entlang der Linie** oder über ihren **M-Wert** (ADR 0015) angesprochen. Die Operationen sind `Interpolate` (der Punkt an einer Position), `Project` (die Position des nächsten Punktes zu einem gegebenen Punkt) und `Substring` (das Stück zwischen zwei Positionen), jeweils als Längen-Variante und als `...ByM`-Variante. Sie sind Extension-Methoden in `SpatialNET.Extensions`. Wie sonst auch (ADR 0012) sind punktförmige und skalare Ergebnisse **Werte** (`Coordinate?`, `double`), die keine Factory brauchen; nur die Stücke von `Substring` sind Geometrien und kommen aus der Factory. M spricht eine Position nur dort an, wo die Linie M trägt, und verändert die Geometrie nie; für nicht monotones M, das echte Daten haben, gilt die Regel **erster Treffer entlang der Linie**, und nichts wirft.

*English version: [0017-lineare-referenzierung-laenge-und-m-erster-treffer.md](0017-lineare-referenzierung-laenge-und-m-erster-treffer.md)*

## Consequences

- **Ort:** statische Klasse `LinearReferencing` in `SpatialNET.Extensions`, Namespace `SpatialNET`. Kein neues Paket, `SpatialNET.Core` bleibt reine Verträge. Für die Package Validation ist das nur eine Ergänzung.
- **API** (Extension-Methoden auf `IPolyline2D`; Parameterreihenfolge wie in ADR 0012: Pflichtparameter, dann `factory`):

  | Methode | Liefert | Bedeutung |
  |---|---|---|
  | `Interpolate(double distance)` | `Coordinate?` | der Punkt bei einer Länge; `null` bei einer Linie ohne Punkte |
  | `Project(Coordinate point)` | `double` | die Länge des Punktes auf der Linie, der `point` am nächsten liegt; `NaN` bei einer Linie ohne Punkte |
  | `Substring(double from, double to, factory)` | `IPolyline2D` | das Stück zwischen zwei Längen |
  | `InterpolateByM(double m)` | `Coordinate?` | der Punkt, an dem die Linie `m` zuerst erreicht; `null`, wenn es keinen gibt |
  | `ProjectToM(Coordinate point)` | `double` | der M-Wert am nächsten Punkt; `NaN`, wenn die Linie dort kein M hat |
  | `SubstringByM(double fromM, double toM, factory)` | `IPolyline2D` | das Stück zwischen den ersten Treffern zweier M-Werte |

  `Project` und `ProjectToM` gibt es auch für `IPoint2D`, das nur umwandelt. `IGeometry2D`-Overloads gibt es nicht: Die Operationen sind auf Linien definiert, und der Rand eines Polygons ist hier keine Linie.
- **Position entlang der Linie:** die planare Länge, dieselbe wie `Length()`. Z nimmt nicht teil. Die Pfade einer Polylinie werden **in ihrer Reihenfolge aneinandergelegt** (kumulierte Länge), ohne dass eine Verbindung zwischen dem Ende eines Pfades und dem Anfang des nächsten angenommen wird. Segmente der Länge 0 tragen nichts bei.
- **`Interpolate`:**
  - `distance` wird auf `[0, Länge]` **begrenzt**: negativ ergibt den Anfang, zu groß das Ende. „Negativ zählt vom Ende“ wie in JTS gibt es nicht (der Aufrufer rechnet `Länge - d`).
  - Eine Linie, deren Punkte alle gleich sind, hat Länge 0 und liefert diesen Punkt.
  - Das Ergebnis trägt **interpoliertes Z und M** auf dem Segment nach den Regeln aus ADR 0015: nur wenn beide Enden es haben, und an einem ursprünglichen Stützpunkt genau dessen eigene Werte.
- **`Project`:** nächster Punkt in der Ebene, planarer Abstand. Unter gleich nahen Punkten (eine Linie, die sich kreuzt oder berührt) wird die **kleinste Länge** geliefert, damit die Antwort deterministisch ist. Die Projektion auf ein Segment wird auf das Segment begrenzt.
- **`Substring`:**
  - Beide Längen werden begrenzt. `from < to` läuft vorwärts, `from > to` liefert das Stück **umgekehrt** (die Richtung folgt von → nach), wie in JTS. Sind die Längen nach der Begrenzung gleich, ist das Ergebnis eine **leere Polylinie**: Ein Stück der Länge 0 ist nichts, im Einklang mit den Kollaps-Regeln aus ADR 0006 und ADR 0007.
  - Das Stück ist eine Polylinie mit **einem Pfad je berührtem Pfad**; die Lücke zwischen zwei Pfaden wird nicht überbrückt.
  - Neue Endpunkte tragen interpoliertes Z und M, ursprüngliche Stützpunkte dazwischen behalten ihre. Das Ergebnis wird auf `SupportsZ`/`SupportsM` der Factory beschränkt (ADR 0015).
- **Die `...ByM`-Varianten:**
  - Nur Segmente, deren **beide Enden M tragen**, nehmen teil. Eine Strecke ohne M wird weder überinterpoliert noch ist sie ein Fehler: Sie kann einfach nicht antworten.
  - **Erster Treffer:** Die Linie wird in ihrer Reihenfolge durchlaufen (Pfade der Reihe nach, Segmente der Reihe nach). Das erste Segment, dessen M-Bereich den Wert enthält (geschlossenes Intervall), antwortet; die Position darin ist linear in M. Ein Segment mit konstantem M gleich dem Wert antwortet mit seinem Anfang. Nicht monotones M und mehrfach vorkommende Werte (eine Schleife) sind damit erlaubt, und das erste Vorkommen gewinnt.
  - `InterpolateByM` liefert `null`, wenn kein Segment antwortet. `SubstringByM` bestimmt die Länge beider ersten Treffer und verhält sich dann wie `Substring`; hat ein Wert keinen Treffer, ist das Ergebnis eine leere Polylinie. `ProjectToM` liefert das M am nächsten Punkt (demselben wie bei `Project`): interpoliert, wenn beide Enden seines Segments M tragen, das eigene M des Stützpunkts, wenn der Punkt ein Stützpunkt ist, sonst `NaN`.
  - Eine Liste aller Treffer gehört nicht zu diesem Schritt (siehe Considered Options).
- **Exceptions (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei `NaN` oder unendlichem `distance`, `from`, `to`, `m`, `fromM`, `toM` oder einem nicht endlichen Punkt, `InvalidOperationException` ohne Factory (nur `Substring` und `SubstringByM`). Leere oder entartete Linien werfen **nie**; sie antworten wie oben mit `null`, `NaN` oder einer leeren Polylinie.
- **Kein Zustand, kein Schnappschuss:** Jeder Aufruf rechnet aus der Geometrie in O(n). Ein wiederverwendbarer Index kumulierter Längen für viele Abfragen auf einer langen Linie lässt sich später ohne Bruch ergänzen, wenn ein Benchmark ihn verlangt.
- **Tests (ADR 0008):**
  - Orakel für die Längen-Varianten ist die NTS-`LengthIndexedLine` (`ExtractPoint`, `IndexOf`, `ExtractLine`), mit den dokumentierten Unterschieden (negative Indizes, Begrenzung, die Gleichstand-Regel von `Project`) als `divergence`. Fixtures unter `samples/interpolate/`, `samples/project/` und `samples/substring/` für die Fälle, die NTS beurteilen kann; der Tag-Katalog wird erfüllt oder seine Ausnahmen sind in `_catalog.json` begründet (Loch-, Verschachtelungs- und ähnliche Tags gelten für Linien nicht).
  - NTS hat kein M-basiertes Gegenstück, daher sind die `...ByM`-Varianten durch handgeschriebene Fälle (`[Covers]`) und Property-Tests abgedeckt: die Erster-Treffer-Regel gegen einen Brute-Force-Durchlauf, Schleifen, Lücken ohne M, konstantes M, M gleich 0.
  - Property-Tests (CsCheck) für die Längen-Varianten: `Project(Interpolate(d))` ist `d` auf einfachen Linien; die Länge von `Substring(a, b)` ist `|b − a|` nach der Begrenzung; `Substring(0, Länge)` hat dasselbe X/Y wie die Linie; bei monotonem M liefert `InterpolateByM(M von Interpolate(d))` denselben Punkt; M ändert nie ein X/Y.
- **Playground und Dokumentation:** Jede öffentliche Methode braucht einen Deskriptor (ADR 0011) oder eine begründete Ausnahme. `Interpolate`, `Project` und `Substring` folgen dem Muster von `Centroid`, `Area` und `Cut`. Die drei `...ByM`-Methoden sind ausgenommen: Der Playground liest GeoJSON, und das hat kein M (ADR 0015), sie könnten dort nie antworten. Eine neue Konzeptseite „Lineare Referenzierung“ auf Englisch und Deutsch und ein Glossareintrag.
- **Vorerst nicht dabei:** Polygonränder, die Liste aller Treffer eines Wertes, 3D-Längen, M-basierte Maße wie eine Länge entlang M und ein vorberechneter Längenindex.

## Considered Options

- Nur die Längen-Varianten (verworfen: Der Hauptgrund für M im Modell ist, eine Linie darüber anzusprechen)
- Negative Längen zählen vom Ende wie in JTS (verworfen: Begrenzen ist einfacher und vermeidet stille Überraschungen; das Ende ist `Länge - d`)
- Werfen, wenn eine Länge außerhalb der Linie liegt (verworfen: ADR 0012 lässt Algorithmen bei Daten nicht werfen; Begrenzen ist vorhersehbar)
- Für die M-Varianten monotones M verlangen und sonst werfen (verworfen: Echte Netze haben Schleifen und Rücksprünge; die Erster-Treffer-Regel antwortet ohne Exception)
- Lücken ohne M durch Interpolation überbrücken (verworfen: Das würde Messwerte erfinden)
- Eine Liste aller Treffer als einzige M-API (verworfen für jetzt: Die meisten Aufrufer wollen einen Punkt; eine Listenvariante lässt sich ohne Bruch ergänzen)
- `Interpolate` legt einen Punkt über die Factory an (verworfen: Ein punktförmiges Ergebnis ist ein Wert, wie `Centroid` in ADR 0012)
- `Project` liefert eine Struktur aus Länge, Punkt und Abstand (verworfen: `Interpolate(Project(p))` und `Distance` liefern den Rest)
- `IGeometry2D`-Overloads, die Polygonränder annehmen (verworfen: Die Position entlang der Ringe eines Polygons hat keinen natürlichen Anfang)
- Ein `LengthIndex`-Objekt von Anfang an (verworfen: Kein Benchmark verlangt es; es kann ohne Bruch folgen)
