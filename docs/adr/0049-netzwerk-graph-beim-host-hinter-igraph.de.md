# Netzwerke: ein Graph beim Host hinter IGraph, typisierte Elemente, die ersten Suchen

SpatialNET bekommt **Netzwerke**: Linien und Punkte eines Leitungsnetzes (Kabel, Verbinder, Kästen, Trafos, Kunden) oder eines Straßennetzes werden zu einem Graphen aus **Knoten** und **Kanten**, der sich durchsuchen und prüfen lässt. Die Bibliothek **hält den Graphen nicht**. Der Host speichert ihn, typischerweise in einer Datenbank, aufgeteilt in **Shards** (meist einer pro Ortsnetz), und gibt Knoten und Kanten über ein kleines Interface, **`IGraph<TNodeId, TEdgeId>`**, nach ID heraus, immer für viele IDs auf einmal. Die Algorithmen sind Extension-Methoden auf diesem Interface, so wie die Geometrie-Algorithmen Extension-Methoden auf den Geometrie-Interfaces sind (ADR 0001, ADR 0002), und lesen nur den Teil des Netzwerks, den sie tatsächlich besuchen. Alles Fachliche bleibt beim Host: Elemente tragen einen ganzzahligen **Typ**, den eine kleine Registry aus **Facetten** beschreibt, Kanten tragen **Gewichte**, deren Art die Bibliothek in Kosten umrechnen kann, und der **Schaltzustand** ist bei den Elementen gespeichert und kann pro Aufruf abweichen. Ein Zielnetz von etwa 10.000 Trafos und 500.000 Kunden (2 bis 3 Mio. Knoten) hat diese Entscheidungen geprägt. Dieses ADR legt das Modell und die ersten beiden Suchen fest; Bauen, Tracing und Regelprüfungen folgen in eigenen ADRs.

*English version: [0049-netzwerk-graph-beim-host-hinter-igraph.md](0049-netzwerk-graph-beim-host-hinter-igraph.md)*

## Consequences

- **Pakete:**
  - Ein neues Paket **`SpatialNET.Network`**, Namespace `SpatialNET.Network`, das nur von `SpatialNET.Core` abhängt. Es enthält das Modell, `IGraph`, Typen, Gewichte, den Schaltzustand und alle Suchen und Prüfungen. Es braucht keine Geometrie-Algorithmen.
  - Ein zweites Paket **`SpatialNET.Network.Building`** (Core, Extensions, Network) für den Builder und die Weg-Geometrie; entschieden in ADR 0050.
  - `SpatialNET.Imp` bekommt **`InMemoryGraph`** und **`ShardedGraphBase`** und referenziert dafür `SpatialNET.Network`.
  - Die neuen Projekte nehmen von Anfang an an der Verfolgung der öffentlichen API teil (ADR 0028); die Package Validation hat bis zum ersten Release, das sie enthält, keine Baseline.
- **Modell** (unveränderliche `readonly record struct`s):
  - `GraphNode<TNodeId>(Id, TypeId, Location, Blocked)`. `Location` ist eine `Coordinate` und ist für jeden Knoten gesetzt, auch für Hilfsknoten aus dem Builder, damit sich Ergebnisse immer auf einer Karte zeigen lassen.
  - `GraphEdge<TNodeId, TEdgeId>(Id, From, To, TypeId, Length, Direction, Disabled, Weights)`. `Direction` ist `Both`, `Forward` oder `Backward` bezogen auf `From → To`; `Weights` ist ein `ReadOnlyMemory<EdgeWeight>` aus Paaren `(WeightId, Value)`.
  - Der Bezug zum Quellobjekt (Tabelle, Objekt-ID, Bereich auf der Linie) ist **nicht** Teil des Modells. Die Algorithmen brauchen ihn nicht; der Host speichert ihn neben der Kante.
  - Parallele Kanten und Schleifen sind erlaubt.
- **IDs:** `TNodeId` und `TEdgeId` sind Typparameter mit `notnull, IEquatable<T>` (ein `long`, eine `Guid`, ein Struct des Hosts). Die Bibliothek ordnet sie nie. Sie müssen über alle Feature-Klassen des Hosts eindeutig sein.
- **`IGraph<TNodeId, TEdgeId>`**, vom Host implementiert:
  - `IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids)`: unbekannte IDs werden weggelassen.
  - `IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds)`: jede Kante, die mindestens einen der Knoten berührt, jede einmal, auch Kanten, deren anderes Ende in einem anderen Shard liegt.
  - Synchron und gestapelt: Die Bibliothek fragt viele IDs auf einmal (eine Front einer Suche, einen Block eines Stroms), damit der Host mit einer Abfrage antworten kann. Die Stapelgrößen sind ein Implementierungsdetail.
  - Für dieselben Daten liefert der Host dieselbe Reihenfolge; unter dieser Bedingung sind die Ergebnisse der Bibliothek deterministisch.
  - Das Interface kann nicht alle Knoten aufzählen. Jede Suche und Prüfung bekommt ihre **Startmenge** vom Host (alle Trafos eines Ortsnetzes, geänderte Objekte), damit „das ganze Netzwerk“ eine ausdrückliche Wahl des Aufrufers bleibt.
- **`ShardedGraphBase<TNodeId, TEdgeId>`** in `SpatialNET.Imp`: ein `IGraph` mit einem begrenzten Cache ganzer Shards (zuletzt benutzte bleiben). Der Host implementiert zwei Methoden, `FindShards(nodeIds)` (eine Abfrage, die IDs auf Shards abbildet) und `LoadShard(shardId)` (alle Knoten des Shards und alle Kanten, die sie berühren). Wie ein Host Shards bildet, ist seine Sache; ADR 0050 lässt den Builder sie aus Polygonen zuordnen.
- **`InMemoryGraph<TNodeId, TEdgeId, TSource>`** in `SpatialNET.Imp`: ein `IGraph` für Tests, kleine Netzwerke und das temporäre Netzwerk eines Ortsnetzes, von Hand gefüllt oder als Ziel des Builders. Er kennt die Quelle jeder Kante und darf seine Knoten aufzählen.
- **Typen:**
  - Ein Typ ist ein Vektor aus **Facetten** (z. B. Ebene, Objektart, Bauart) mit je eigenen Werten; `TypeRegistry(facets, types)` entsteht aus der Typentabelle des Hosts, die klein ist und im Speicher liegt. Datensätze tragen nur die ganzzahlige `TypeId`; `0` ist für untypisierte Hilfsknoten reserviert. Jeder Typ ist entweder ein Knoten- oder ein Kantentyp.
  - **Typmuster** wie `"[*, Kastenverbinder, *]"` oder `"Niederspannung.*"` werden einmal in eine **`TypeSet`** aufgelöst (`types.Nodes(…)`, `types.Edges(…)`); die Suchen vergleichen danach nur noch Zahlen. `*` trifft jeden Wert seiner Facette einschließlich „nicht belegt“, `A.*` enthält also `A` selbst. Mehrere Muster bedeuten ODER. `*` trifft keine untypisierten Hilfsknoten; `TypeSet.Untyped` schon. Ein Muster ohne Treffer ist ein Fehler, weil es meist ein Tippfehler ist.
  - Facetten, kein Baum: Der Wert einer Facette bedeutet unter jeder anderen Facette dasselbe, deshalb ist eine Regel „jeder Kastenverbinder auf jeder Spannungsebene“ das Muster `[*, Kastenverbinder, *]`. In einem Baum hätte der Kastenverbinder unter Niederspannung eine andere Nummer als unter Mittelspannung.
- **Gewichte:**
  - `WeightDefinition(Id, Name, Kind, Fallback)` mit vier Arten: `Length` (Kosten = Länge; eingebaut als `WeightDefinition.Length`), `Absolute` (Kosten = Wert), `PerLength` (Wert × Länge, z. B. Ohm pro km) und `LengthOverValue` (Länge ÷ Wert, z. B. Fahrzeit aus einer Geschwindigkeit). Einheiten rechnet der Host um.
  - Die Kosten eines Wegs sind die Summe seiner Kanten. Kombinationen von Gewichten, Engpasskosten, zeitabhängige Gewichte und Abbiegeverbote gibt es nicht.
  - Die Werte stecken in der Kante und kommen mit dem Shard, eine Suche fragt also nie ein einzelnes Gewicht ab. Ein fehlender Wert nimmt den `Fallback`; ohne ihn bricht die Suche mit einer Ausnahme ab, sobald sie eine solche Kante erreicht, statt still mit 0 zu rechnen.
- **Schaltzustand:**
  - Der **Normalschaltzustand** ist Teil der Datensätze (`Blocked`, `Disabled`, `Direction`) und kommt mit dem Shard; Standard ist „alles passierbar“.
  - **Abweichungen** für einen Aufruf laufen über `INetworkState<TNodeId, TEdgeId>` (`IsBlocked`, `IsDisabled`, `GetDirection`, jeweils mit dem Datensatz, damit ein Host nach Typ entscheiden kann). `NetworkState<TNodeId, TEdgeId>` implementiert es mit kleinen Mengen: `Block`, `Unblock`, `Disable`, `Enable`, `SetDirection`.
  - Ein **gesperrter Knoten wird erreicht, aber nicht durchlaufen**; er erscheint im Ergebnis, nichts dahinter. Eine deaktivierte Kante gibt es für den Aufruf nicht.
  - Die Namen `Blocked` und `Disabled` sind absichtlich neutral: Für einen Elektriker leitet ein „geschlossener“ Schalter, bei einem Tor ist es umgekehrt.
- **Suchen in diesem Schritt** (statische Klasse `NetworkAnalysis`, Extension-Methoden auf `IGraph`):
  - `TraversalOptions<TNodeId, TEdgeId>` mit `Weight` (Standard Länge), `State` (null = Normalschaltzustand), `EdgeTypes` und `NodeTypes` (null = alle; Hilfsknoten sind immer passierbar) und `MaxCost`.
  - `Reach(starts, options, cancellationToken)` liefert `IEnumerable<ReachedNode>` (Knoten, Typ, Start, Kosten, die Kante, über die er erreicht wurde, gesperrt) in der Reihenfolge der Kosten, **verzögert**: Der Aufrufer kann früh aufhören oder in eine Tabelle streamen.
  - `ShortestPath(from, to, options, cancellationToken)` liefert `NetworkPath?` (von, nach, Kosten und Schritte aus Kante, von, nach, umgekehrt, Kosten); `null`, wenn es keinen Weg gibt. Dijkstra mit binärem Heap; gleiche Kosten entscheidet die Reihenfolge, in der der Host die Kanten geliefert hat.
  - Eine Suche merkt sich einen Eintrag pro besuchtem Knoten. Bei etwa 3 Mio. Knoten und `long`-IDs sind das grob 100 bis 300 MB; Suchen über das ganze Netzwerk sollten deshalb pro Ortsnetz laufen.
- **Ausnahmen** (die Regeln aus ADR 0012, dazu ein Typ):
  - `InvalidGraphException : InvalidOperationException`, wenn die Daten kein Netzwerk ergeben: Eine Kante zeigt auf einen Knoten, den `GetNodes` nicht liefert, ein Gewicht fehlt ohne Ersatzwert, oder ein Gewicht ist negativ, `NaN` oder unendlich. Sie nennt die beteiligten IDs.
  - `ArgumentNullException`, `ArgumentOutOfRangeException` und `ArgumentException` für ungültige Argumente (ein unbekannter Startknoten, ein Muster ohne Treffer, negative `MaxCost`).
  - Ausnahmen des Hosts laufen unverändert durch; `OperationCanceledException`, wenn das Token auslöst.
- **Thread-Sicherheit:** Die Suchen haben keinen gemeinsamen Zustand. Ob ein `IGraph` von mehreren Suchen gleichzeitig benutzt werden darf, entscheidet der Host; `ShardedGraphBase` ist dafür sicher, `InMemoryGraph`, sobald er gefüllt ist.
- **Tests (ADR 0008):** Für Netzwerke gibt es kein NTS-Orakel. Die Referenz sind ein einfacher Bellman-Ford und eine Breitensuche im Testprojekt, geprüft mit CsCheck auf Zufallsgraphen (Optimalität jedes Wegs, Symmetrie bei ungerichteten Graphen, die Wirkung gesperrter Knoten, deaktivierter Kanten und Richtungen, Determinismus derselben Eingabe), dazu handgeschriebene Fälle für die Musterregeln, fehlende Gewichte, parallele Kanten und Schleifen. `ShardedGraphBase` wird mit einem Fake-Speicher getestet, der Abfragen zählt.
- **Benchmark:** `NetworkBenchmarks` mit einem synthetischen Netzwerk von etwa 3 Mio. Knoten in radialen Ortsnetzen, gelesen über `ShardedGraphBase` mit simulierter Abfrage-Latenz. Er misst Zeit und Speicher einer Suche in einem Ortsnetz und über das ganze Netzwerk und legt die Standardgröße des Caches und der Stapel fest.
- **Playground:** noch nicht; ein Netzwerk aus gezeichneten Geometrien braucht den Builder (ADR 0050).
- **Folgeentscheidungen:**
  - ADR 0050 Bauen: Linienenden innerhalb einer Toleranz zu einem Knoten zusammenführen, Linien an Punkten und Linienenden teilen, IDs vom Host, Shards aus Polygonen, das Ergebnis an den Host streamen.
  - ADR 0051 Tracing: Quellen und Senken, Versorgungsgebiete, nicht versorgte Kunden, Knoten, die mehrere Quellen erreichen.
  - ADR 0052 Regeln: lokale Regeln, die die Nachbarn eines Elements nach Typ zählen, geprüft über Ströme von Knoten oder Kanten.

## Considered Options

- Die Bibliothek hält den Graphen in einer eigenen Struktur, wie `SpatialIndex<T>` (verworfen: Ein Netzwerk aus Millionen Elementen kann nicht immer im Speicher liegen, und der Host hat schon eine Datenbank; die Bibliothek würde einen Speicher doppeln, den sie nicht besitzen kann)
- Dichte `int`-IDs, von der Bibliothek vergeben (verworfen: Hosts haben eigene IDs, oft `Guid`s; jedes Ergebnis zurückzuübersetzen kostet eine Abfrage pro Element)
- Ein asynchrones Interface von Anfang an (verworfen: Es änderte jede Signatur und jeden Test; ein zweites Interface `IAsyncGraph` kann folgen, wenn ein Host es braucht)
- Ein Aufruf pro Knoten (verworfen: eine Datenbankabfrage pro Knoten macht eine Suche über Tausende Knoten langsam; Stapel kosten den Host nichts)
- Gewichte und Schaltzustand nur als Funktionen pro Aufruf (verworfen: Eine Funktion, die pro Kante die Datenbank des Hosts liest, macht die Shards wertlos; Normalzustand und Werte gehören in den Datensatz, die Abweichungen pro Aufruf bleiben klein)
- Feste Knotenrollen in der Bibliothek (Quelle, Senke, Schalter) (verworfen: Sie zwingen jedem Host ein Fachgebiet auf; Typen, Muster und Schaltzustand drücken dasselbe aus, ohne dass die Bibliothek die Begriffe kennt)
- Typen als Baum, oder die Facettenwerte als Spalten jedes Knotens und jeder Kante (verworfen: Ein Baum kann „jeder Kastenverbinder auf jeder Ebene“ nicht ausdrücken; Spalten pro Facette wiederholten in Millionen Datensätzen, was eine kleine Tabelle schon sagt)
- Eine Graph-Bibliothek wie QuikGraph (verworfen: ADR 0001, und sie bräuchte den ganzen Graphen im Speicher)
- Bauen, Tracing und Regeln in diesem ADR (verworfen: Jedes hat eigene Abwägungen; dieses ADR legt fest, was sie gemeinsam haben)
