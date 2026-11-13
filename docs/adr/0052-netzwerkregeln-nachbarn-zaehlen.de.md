# Netzwerkregeln: die Nachbarn eines Elements zählen, Regeln als Zeilen, Prüfungen über Ströme

Ein Netzwerk ist nur so gut wie seine Daten, und ein Leitungsnetz hat viele Regeln, die das GIS beim Zeichnen nicht erzwingen kann: Jeder Kasten hat mindestens einen Kastenverbinder, am freien Ende eines Kastenverbinders hängt genau ein Kabel oder eine Freileitung, Nieder- und Mittelspannung treffen sich nur am Trafo, eine Leitung endet nicht im Leeren. SpatialNET prüft solche Regeln auf `IGraph` (ADR 0049). Eine **Regel zählt die direkten Nachbarn** eines Elements (die Kanten an einem Knoten oder die anderen Kanten an einem Ende einer Kante), die auf ein Typmuster passen, und vergleicht die Anzahl mit einer erlaubten Spanne. Jede Regel, die der Host bisher genannt hat, passt in diese eine Form, es gibt also keine Pfade, Variablen oder Ausdrücke. Eine Regel ist **eine Zeile einfacher Werte** (Muster als Text, Zahlen, eine Schwere), der Host kann seine Regeln also in einer Tabelle halten, die das GIS-Team pflegt; es gibt keine Regelsprache zu parsen. Die Prüfungen **streamen**: Der Host übergibt die zu prüfenden Knoten oder Kanten, nach Shard sortiert, und bekommt die Verletzungen einzeln zurück, eine Prüfung über 3 Mio. Kanten braucht also nicht mehr Speicher als eine über ein Ortsnetz.

*English version: [0052-netzwerkregeln-nachbarn-zaehlen.md](0052-netzwerkregeln-nachbarn-zaehlen.md)*

## Consequences

- **Ort:** `SpatialNET.Network` (ADR 0049): `NetworkRule`, `NetworkRuleDefinition`, `RuleViolation` und die statische Klasse `NetworkValidation` mit Extension-Methoden auf `IGraph`. Die Geometrie-Prüfung `Validate` auf `IGeometry2D` ist eine andere Methode auf einem anderen Typ; die beiden treffen sich nie in der Überladungsauflösung.
- **Die Form einer Regel:**
  - **Subjekt:** Knoten oder Kanten, deren Typ in einer `TypeSet` liegt; `TypeSet.Untyped` meint die Hilfsknoten.
  - **Bei einer Kante die betrachteten Enden:** alle Enden, Enden, deren Knoten in einer `TypeSet` liegt, oder Enden, deren Knoten nicht darin liegt. Ein ausgeschlossenes Ende wird nicht geprüft; eine Kante ohne verbleibendes Ende gar nicht.
  - **Was gezählt wird:** bei einem Knoten seine Kanten, deren Typ in einer `TypeSet` liegt; bei einer Kante die **anderen** Kanten am gewählten Ende (das Subjekt selbst zählt nie).
  - **Die Spanne:** `Min` und `Max` (ohne `Max` keine obere Grenze). Eine Anzahl außerhalb ist eine Verletzung.
  - **Schwere:** `Error` oder `Warning`, damit der Host sortieren und anzeigen kann.
  - Eine Kantenregel wird **an jedem gewählten Ende für sich** geprüft: Ein Kastenverbinder, an dessen freiem Ende nichts hängt, ergibt eine Verletzung an diesem Ende, keine Summe über beide Enden.
- **Regeln im Code bauen:**

  ```csharp
  NetworkRule.ForNodes("KASTEN-ABGANG", types.Nodes("[*, Kasten, *]"))
      .CountEdges(types.Edges("[*, Kastenverbinder, *]")).AtLeast(1);

  NetworkRule.ForEdges("KV-FREIES-ENDE", types.Edges("[*, Kastenverbinder, *]"))
      .AtEndsWhereNodeIsNot(types.Nodes("[*, Kasten, *]"))
      .CountOtherEdges(types.Edges("[*, Leitung, *]")).Exactly(1);
  ```

  `ForNodes` und `ForEdges` beginnen eine Regel, `AtEndsWhereNode` und `AtEndsWhereNodeIsNot` wählen die Enden, `CountEdges` und `CountOtherEdges` legen fest, was gezählt wird, und `Between`, `AtLeast`, `Exactly` und `None` schließen sie ab; `AsWarning()` senkt die Schwere. Eine fertige `NetworkRule` ist unveränderlich und für beliebig viele Prüfungen und Threads verwendbar.
- **Regeln als Zeilen:** `NetworkRuleDefinition(Id, Severity, Subject, SubjectTypes, EndFilter, EndTypes, CountTypes, Min, Max)` enthält dasselbe als einfache Werte: `Subject` ist `Node` oder `Edge`, `EndFilter` ist `All`, `NodeIs` oder `NodeIsNot`, und die drei `…Types` sind **Mustertext** wie für die Typ-Registry (ADR 0049). `NetworkRule.FromDefinition(definition, types)` löst sie auf, `rule.ToDefinition()` gibt sie zurück.
  - Mehrere Muster in einem Text werden durch `|` getrennt und bedeuten ODER (`"[*, Kasten, *] | [*, Trafo, *]"`); dasselbe gilt für `types.Nodes(…)` und `types.Edges(…)`.
  - `rule.ToString()` liefert eine kurze lesbare Form für Berichte, z. B. `KV-FREIES-ENDE: edges [*, Kastenverbinder, *], ends not [*, Kasten, *], other edges [*, Leitung, *] = 1`. Sie ist für Menschen, nicht zum Parsen.
  - Eine Definition, die keine Regel sein kann, wirft beim Auflösen, mit der Regel-ID in der Meldung: ein Muster ohne Treffer oder mit einem Typ der falschen Art (`ArgumentException`), ein Ende-Filter an einer Knotenregel (`ArgumentException`), `Min` unter 0 oder über `Max` (`ArgumentOutOfRangeException`).
- **Prüfen:**
  - `ValidateNodes(nodes, rules, cancellationToken)`: die Knotenregeln für jede übergebene Knoten-ID. Gedacht für Ströme über das ganze Netzwerk.
  - `ValidateEdges(edges, rules, cancellationToken)`: die Kantenregeln für jeden übergebenen **Kantendatensatz**. Der Host liest die Kanten ohnehin aus seiner Datenbank, `IGraph` braucht also keine Abfrage von Kanten nach ID.
  - `Validate(nodes, rules, cancellationToken)`: die Knotenregeln für die übergebenen Knoten und die Kantenregeln für jede Kante, die an ihnen hängt, jede einmal. Es merkt sich die geprüften Kanten und ist für ein begrenztes Gebiet wie ein Ortsnetz gedacht.
  - Jede Methode wendet nur die Regeln ihrer Art an und übergeht die anderen, eine Liste von Regeln dient also allen dreien. Regel-IDs müssen in der Liste eindeutig sein (`ArgumentException`).
  - `ValidateNodes` und `ValidateEdges` prüfen, was sie bekommen; ein doppelt übergebenes Element wird doppelt geprüft. Sie merken sich **keine Menge geprüfter Elemente**, ihr Speicher wächst also nicht mit dem Netzwerk.
- **Lesen und Cache:** Die Methoden lesen ihre Eingabe in Blöcken (etwa tausend Elemente), fragen `IGraph` in je einem Stapel nach den Knoten an den Enden und den Kanten an diesen Knoten, prüfen den Block und geben seine Verletzungen aus. **Nach Shard sortiert** übergeben, kommen die Nachbarn eines Blocks aus den Shards, die schon im Cache liegen. In zufälliger Reihenfolge lüde fast jeder Block andere Shards; das ist für das ganze Netzwerk der Unterschied zwischen Minuten und Stunden, und der Benchmark zeigt ihn.
- **`RuleViolation<TNodeId, TEdgeId>`** mit `RuleId`, `Severity`, `Kind` (Knoten oder Kante), `Node` (der Knoten, oder bei einer Kantenregel das Ende, an dem gezählt wurde), `Edge` (nur bei einer Kantenregel), `TypeId` des geprüften Elements, `Count`, `Min`, `Max` und `Location` (des Knotens, die jeder Knoten hat, ADR 0049). Verletzungen kommen in der Reihenfolge der Eingabe, für ein Element in der Reihenfolge der Regeln, bei einer Kante erst an `From`, dann an `To`.
- **Zählen:**
  - Parallele Kanten zählen einzeln: Zwei Kabel zwischen demselben Kasten und Knoten sind zwei.
  - Eine Schleife am Knoten zählt einmal; sie ist eine Kante.
  - Der **Schaltzustand spielt keine Rolle**: Regeln prüfen, was gebaut ist, nicht wie geschaltet; eine deaktivierte Kante zählt wie jede andere.
  - Mehrere Muster im Zählen addieren sich, da sie ODER bedeuten.
- **Fehler:** die Ausnahmen aus ADR 0049; ein Kantendatensatz, dessen Ende `GetNodes` nicht liefert, ist eine `InvalidGraphException`.
- **Quittierte Verletzungen** sind Sache des Hosts: Das Paar aus Regel-ID und Element bleibt zwischen den Bauläufen gleich, weil Hilfsknoten-IDs aus der Lage und Kanten-IDs aus Quelle und Teil abgeleitet werden (ADR 0050).
- **Ein Ortsnetz beim Speichern prüfen:** Der Host baut aus allen Objekten, die das Gebiet berühren, ein temporäres Netzwerk in einen `InMemoryGraph` (ADR 0050) und ruft `Validate` nur mit den Knoten innerhalb des Gebiets auf. Die Objekte außerhalb sind Kontext: Ihre Kanten werden am Rand mitgezählt, sie selbst aber nicht geprüft, eine Leitung, die das Gebiet verlässt, wird also nicht als offenes Ende gemeldet.
- **Nicht Teil der Regeln:**
  - ob ein Kunde versorgt ist: Das ist ein Trace (`Supply`, ADR 0051), keine lokale Zählung;
  - Attribute, die das Netzwerk nicht trägt (Querschnitt gegen Sicherung): Die prüft der Host;
  - zwei echte Knoten an einer Stelle: Die meldet der Builder (ADR 0050).
- **Tests (ADR 0008):** Eine Referenz im Testprojekt, die mit roher Gewalt über einem `InMemoryGraph` zählt, verglichen mit CsCheck auf Zufallsgraphen und Zufallsregeln: dieselben Verletzungen; `ValidateNodes` über alle Knoten zusammen mit `ValidateEdges` über alle Kanten gleich `Validate` über alle Knoten; dieselbe Menge an Verletzungen für eine gemischte Eingabe; `FromDefinition` und `ToDefinition` hin und zurück. Die vier Regeln aus dem Konzept sind handgeschriebene Fälle (Kasten mit und ohne Kastenverbinder, das freie Ende eines Kastenverbinders mit Kabel, Freileitung und nichts, Nieder- und Mittelspannung am Trafo und an einem Hilfsknoten, ein offenes Leitungsende), ebenso parallele Kanten, Schleifen, Regeln mit `|` und die Fehler von Definitionen.
- **Benchmark:** `NetworkBenchmarks` (ADR 0049) bekommt `ValidateEdges` über die Kanten des synthetischen Netzwerks, nach Shard sortiert und gemischt, und misst Zeit, Speicher und Shard-Ladevorgänge.
- **Playground:** Die Netzwerk-Ansicht (ADR 0050) bekommt eine kleine Tabelle mit Regelzeilen und markiert die Verletzungen auf der Karte.

## Considered Options

- Eine eigene Regelsprache mit Parser, als Text gespeichert (verworfen: eine Grammatik zu entwerfen, zu dokumentieren und stabil zu halten, für eine Form, die in ein paar Spalten passt; die Muster sind der einzige Text, der geparst werden muss, und den parst die Registry schon)
- Regeln über Pfade, als Muster entlang einer Kette von Elementen (verworfen: Jede Regel, die der Host genannt hat, ist eine Zählung direkter Nachbarn, und eine Kette ist viel schwerer schnell zu prüfen und in einem Bericht zu erklären)
- Regeln mit Variablen, z. B. „auf beiden Seiten dieselbe Ebene“ (verworfen: Die aufgetretenen Fälle sind verbotene Paare, die eine Zählung mit `None()` ausdrückt)
- Nur ein `Validate`, das Knoten nimmt und deren Kanten mitprüft (verworfen: Über das ganze Netzwerk müsste es sich Millionen geprüfter Kanten merken, und nur Kanten zu prüfen ginge nicht)
- Kanten-IDs statt Kantendatensätzen für `ValidateEdges` (verworfen: `IGraph` bräuchte eine Abfrage von Kanten nach ID, und jede Kante würde zweimal gelesen)
- Eine Kantenregel über beide Enden summieren (verworfen: Ein Kastenverbinder mit einem Kabel an einem Ende und nichts am anderen bestünde eine Regel „eine Leitung am freien Ende“; der Ende-Filter ergibt nur pro Ende Sinn)
- Regeln, die den Schaltzustand beachten (verworfen: Eine Prüfung der Daten darf sich nicht mit einem Schalter ändern; Versorgung unter einem Schaltzustand ist ein Trace)
- Reparieren, was die Regeln finden (verworfen: Die richtige Reparatur hängt von den Daten und dem Fachgebiet ab, wie bei `Validate` auf Geometrien)
