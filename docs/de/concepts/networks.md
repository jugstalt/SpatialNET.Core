# Netzwerke

Ein Netzwerk ist ein Graph aus **Knoten** und **Kanten**, gebildet aus den Linien und Punkten eines Leitungsnetzes (Kabel, Verbinder, Kästen, Trafos, Kunden) oder eines Straßennetzes. SpatialNET durchsucht es; **gespeichert wird es beim Host**. Das Paket `SpatialNET.Network` enthält das Modell, das Interface `IGraph` und die Suchen; `SpatialNET.Imp` ergänzt einen Graphen im Speicher und einen Cache für Hosts, die ihr Netzwerk in einer Datenbank halten ([ADR 0049](../../adr/0049-netzwerk-graph-beim-host-hinter-igraph.de.md)).

Diese Seite beschreibt, was es heute gibt: das Modell, Typen, Gewichte, den Schaltzustand, `Reach` und `ShortestPath` und das Bauen eines Netzwerks aus Linien und Punkten mit `SpatialNET.Network.Building`. Das Tracing der Versorgung von Kunden und Regelprüfungen folgen ([ADR 0051](../../adr/0051-tracing-pro-quelle-versorgung.de.md), [ADR 0052](../../adr/0052-netzwerkregeln-nachbarn-zaehlen.de.md)).

## Der Host hält den Graphen

Das Netzwerk einer Stadt hat Millionen Knoten, und der Host hat ohnehin eine Datenbank. Deshalb hält SpatialNET den Graphen nicht selbst, sondern fragt den Host nach Knoten und Kanten nach ID, immer für viele IDs auf einmal, über ein kleines Interface:

```csharp
public interface IGraph<TNodeId, TEdgeId>
{
    IEnumerable<GraphNode<TNodeId>> GetNodes(IReadOnlyCollection<TNodeId> ids);              // unknown ids are left out
    IEnumerable<GraphEdge<TNodeId, TEdgeId>> GetEdges(IReadOnlyCollection<TNodeId> nodeIds); // every edge at these nodes, once
}
```

- **Die IDs gehören dem Host**: `long`, `Guid`, ein eigenes Struct. Sie müssen über alle Feature-Klassen eindeutig sein.
- **Stapel**: Eine Suche fragt eine ganze Front auf einmal, damit der Host mit einer Abfrage antworten kann. Eine Suche liest nur den Teil des Netzwerks, den sie besucht.
- **Dieselbe Reihenfolge für dieselben Daten**: Dann sind die Ergebnisse deterministisch.
- **Keine Aufzählung aller Knoten**: Jede Suche bekommt ihre Startknoten vom Host.

`GraphNode` trägt die ID, einen Typ, die Lage und den Normalschaltzustand (`Blocked`). `GraphEdge` trägt die ID, `From` und `To`, einen Typ, die Länge, die Richtung (`Both`, `Forward`, `Backward`), `Disabled` und die Werte seiner Gewichte. Der Bezug zum Quellobjekt im GIS (Tabelle, Objekt-ID, Bereich auf der Linie) steht nicht in der Kante; der Host speichert ihn daneben.

`SpatialNET.Imp` bringt zwei Implementierungen mit:

- **`InMemoryGraph<TNodeId, TEdgeId, TSource>`** für Tests, kleine Netzwerke und das temporäre Netzwerk eines Gebiets; mit `Add` gefüllt, kann er auch die Quelle jeder Kante behalten.
- **`ShardedGraphBase<TNodeId, TEdgeId>`** für einen Host, der sein Netzwerk in **Shards** speichert, meist einer pro Ortsnetz. Sie hält eine Anzahl ganzer Shards im Speicher; der Host schreibt zwei Methoden, `FindShards` (welcher Shard enthält diese Knoten, in einer Abfrage) und `LoadShard` (die Knoten eines Shards und jede Kante, die sie berührt, auch die Kanten zu anderen Shards).

## Ein Netzwerk bauen

`NetworkBuilder` im Paket `SpatialNET.Network.Building` macht aus den Punkten und Linien des Hosts die Knoten und Kanten ([ADR 0050](../../adr/0050-netzwerk-bauen-verknoten-teilen-shards.de.md)):

- **Verknoten.** Linienenden und Punkte, die näher als die `Tolerance` beieinanderliegen, werden ein Knoten. Eine Gruppe mit einem Punkt des Hosts wird dieser **echte Knoten** mit seiner ID und seinem Typ; eine Gruppe nur aus Linienenden wird ein **Hilfsknoten** (Typ 0) im Mittel der Enden. Kein Stützpunkt bewegt sich: Das Netzwerk sagt, welche Enden zusammengehören, die Geometrie bleibt die des Hosts. Zwei Punkte des Hosts innerhalb der Toleranz sind ein Fehler in den Daten.
- **Teilen.** Ein echter Knoten im Inneren einer Linie teilt sie (ein Schalter in einem Kabel), ebenso ein Linienende im Inneren einer anderen Linie (ein T-Anschluss). Linien, die sich ohne gemeinsamen Punkt kreuzen, bleiben getrennt, außer `NodeAtCrossings` ist an (für Straßennetze).
- **Kanten.** Jeder Teil wird eine Kante mit seinem Bereich auf der Quelllinie (`FromMeasure`, `ToMeasure`, für `Substring`), der geometrischen Länge oder einem Anteil einer Aufmaßlänge und den Gewichten der Linie; Werte der Art `Absolute` werden anteilig nach Länge geteilt.
- **IDs** kommen aus der `INetworkIdFactory` des Hosts: Aus Lage, Linie und Teil abgeleitet, bleiben sie von einem nächtlichen Bau zum nächsten gleich. **Shards** kommen aus den `ShardRegions`, Polygonen wie den Ortsnetzen; die erste Region, die einen Knoten überdeckt, gewinnt.
- **Schreiben.** Der Builder prüft zuerst alles, was scheitern kann, und wirft eine `NetworkBuildException` mit allen Konflikten, bevor etwas geschrieben ist. Dann schreibt er alle Knoten und danach alle Kanten an einen `INetworkWriter`: die Datenbank des Hosts oder einen `InMemoryGraph`.

```csharp
// The host's ids: a helper node from its location (stable between builds), an edge from its line and part.
sealed class GisIds : INetworkIdFactory<string, string, string>
{
    public string NewHelperNode(Coordinate at) => FormattableString.Invariant($"H({at.X},{at.Y})");

    public string NewEdge(string source, int part, int partCount) => $"{source}#{part}";
}
```

```csharp
IPolyline2D Line(string wkt) => Wkt.Read<IPolyline2D>(wkt, ImpGeometryFactory.Instance);

var builder = new NetworkBuilder<string, string, string>(new NetworkBuildOptions { Tolerance = 0.01 }, new GisIds());
builder.AddNode("Trafo.1", "T", new Coordinate(0, 0), typeId: 25);     // the points of the GIS are the real nodes
builder.AddNode("Switch.1", "S", new Coordinate(30, 0), typeId: 17);
builder.AddLine("Cable.210", Line("LINESTRING(0 0, 100 0)"), typeId: 11);         // one cable ...
builder.AddLine("Connector.1", Line("LINESTRING(50 30, 50 0.004)"), typeId: 13);  // ... and a connector 4 mm beside it

var graph = new InMemoryGraph<string, string, string>();   // or the host's writer into its database
BuildResult result = builder.Build(graph);
Console.WriteLine($"{result.NodeCount} nodes, {result.EdgeCount} edges");        // 5 nodes, 4 edges

// The cable was split at the switch and at the T junction: three edges with their range on the cable.
foreach (var edge in graph.Edges.Where(e => e.Id.StartsWith("Cable.210")))
{
    graph.TryGetSource(edge.Id, out _, out double from, out double to);
    Console.WriteLine($"{edge.Id}: {edge.From} -> {edge.To}, {from} to {to} m");
}
// Cable.210#0: T -> S, 0 to 30 m
// Cable.210#1: S -> H(50,0.004), 30 to 50 m
// Cable.210#2: H(50,0.004) -> H(100,0), 50 to 100 m
```

`Build` nimmt optional ein `IProgress<BuildProgress>` (die Phase, erledigte und gesamte Einträge), gemeldet höchstens alle 250 ms, und ein `CancellationToken`. Das `BuildResult` listet Hinweise, die den Bau nicht angehalten haben: Knoten außerhalb jeder Region, überlappende Regionen, Ketten von Enden breiter als die Toleranz, Punkte ohne Linie, weggelassene winzige Teile. Das Verknoten nutzt ein Hash-Raster, das Teilen einen räumlichen Index über die Linien; an einem synthetischen Netzwerk aus radialen Ortsnetzen braucht der Builder für 3 Mio. Linien etwa 25 Sekunden (`--quick-build` in den Benchmarks).

Um einen Weg zu zeichnen, schneidet `path.ToPolyline(edge => new EdgeShape(line, from, to))` den Bereich jeder Kante aus ihrer Quelllinie, dreht Stücke um, die der Weg gegen ihre Richtung durchläuft, und fügt sie zusammen.

## Suchen

`Reach` liefert jeden Knoten, der von den Startknoten aus erreichbar ist, in der Reihenfolge der Kosten und verzögert, ein Aufrufer kann also früh aufhören oder in eine Tabelle streamen. `ShortestPath` liefert den günstigsten Weg als Folge von Kanten, oder `null`.

```csharp
// A small network in memory: a transformer, a box with a switch, two customers.
var graph = new InMemoryGraph<int, int, string>();
graph.Add(new GraphNode<int>(1, 25, new Coordinate(0, 0)));    // transformer
graph.Add(new GraphNode<int>(2, 15, new Coordinate(40, 0)));   // box
graph.Add(new GraphNode<int>(3, 16, new Coordinate(40, 30)));  // customer
graph.Add(new GraphNode<int>(4, 16, new Coordinate(80, 0)));   // customer
graph.Add(new GraphEdge<int, int>(101, 1, 2, 11, 40));         // cable, 40 m
graph.Add(new GraphEdge<int, int>(102, 2, 3, 13, 30));         // connector, 30 m
graph.Add(new GraphEdge<int, int>(103, 2, 4, 13, 40));         // connector, 40 m

// Everything within 75 m of cable from the transformer, in order of cost.
foreach (var hit in graph.Reach([1], new() { MaxCost = 75 }))
    Console.WriteLine($"{hit.Node} after {hit.Cost} m");       // 1 after 0 m, 2 after 40 m, 3 after 70 m

var path = graph.ShortestPath(1, 4)!;
Console.WriteLine(string.Join(" > ", path.Steps.Select(s => s.Edge)) + $" = {path.Cost} m"); // 101 > 103 = 80 m

// The box is switched off today: it is still reached, but not passed.
var today = new NetworkState<int, int>().Block(2);
Console.WriteLine(graph.ShortestPath(1, 4, new() { State = today }) is null);              // True
```

Die Optionen (`TraversalOptions`) sind für jede Suche dieselben:

| Option | Wirkung |
|---|---|
| `Weight` | wie die Kosten einer Kante berechnet werden; standardmäßig die Länge |
| `State` | Abweichungen vom Normalschaltzustand; `null` für den Normalzustand |
| `EdgeTypes` | nur Kanten dieser Typen werden begangen, z. B. nur Niederspannung |
| `NodeTypes` | nur Knoten dieser Typen werden betreten; Hilfsknoten und Startknoten immer |
| `MaxCost` | die Suche endet bei diesen Kosten |

Gleiche Kosten entscheidet die Reihenfolge der Startknoten und der Kanten vom Host. Schleifen werden nie begangen; von parallelen Kanten gewinnt die günstigere.

## Typen und Muster

Ein Typ ist ein Vektor aus **Facetten**, z. B. Spannungsebene, Objektart und Bauart. Knoten und Kanten speichern nur die Nummer ihres Typs; die kleine Typentabelle des Hosts sagt, was sie bedeutet. Muster beschreiben Mengen von Typen und werden einmal in eine `TypeSet` aufgelöst, eine Suche vergleicht also nur Zahlen.

```csharp
var types = new TypeRegistry(
    [
        new Facet("Level", new Dictionary<int, string> { [1] = "Low", [2] = "Medium" }),
        new Facet("Object", new Dictionary<int, string> { [1] = "Line", [2] = "BoxConnector", [4] = "Box", [5] = "Transformer" }),
        new Facet("Build", new Dictionary<int, string> { [1] = "Cable", [2] = "Overhead" }),
    ],
    [
        new NetworkType(11, ElementKind.Edge, [1, 1, 1]),   // Low.Line.Cable
        new NetworkType(12, ElementKind.Edge, [1, 1, 2]),   // Low.Line.Overhead
        new NetworkType(13, ElementKind.Edge, [1, 2]),      // Low.BoxConnector
        new NetworkType(15, ElementKind.Node, [1, 4]),      // Low.Box
        new NetworkType(21, ElementKind.Edge, [2, 1, 1]),   // Medium.Line.Cable
        new NetworkType(23, ElementKind.Edge, [2, 2]),      // Medium.BoxConnector
        new NetworkType(25, ElementKind.Node, [2, 5]),      // Medium.Transformer
    ]);

TypeSet connectors = types.Edges("[*, BoxConnector, *]");  // {13, 23}: every connector, on every level
TypeSet low = types.Edges("Low.*");                        // {11, 12, 13}
TypeSet cables = types.Edges("[*, Line, Cable]");          // {11, 21}
TypeSet stations = types.Nodes("[*, Box, *] | [*, Transformer, *]"); // {15, 25}

// Keep a search on the low voltage side of a transformer.
var options = new TraversalOptions<int, int> { EdgeTypes = low };
```

| Muster | trifft |
|---|---|
| `[Low, *, Cable]` | ein Eintrag pro Facette: ein Wertname, eine Zahl oder `*` |
| `Low.Line` | die angegebenen Facetten; die Facetten danach müssen „nicht belegt“ sein |
| `Low.*` | `Low` und alles darunter; ein abschließendes `*` heißt beliebiger Wert für alle restlichen Facetten |
| `*` / `untyped` | jeden Typ der Art, aber keine Hilfsknoten / nur die Hilfsknoten (Typ 0) |
| `a \| b` | eines von beiden |

Die Facetten sind ein Vektor, kein Baum: Der Wert einer Facette bedeutet unter jeder anderen Facette dasselbe, deshalb findet `[*, BoxConnector, *]` die Kastenverbinder auf jeder Ebene. Ein Muster ohne Treffer ist ein Fehler, weil es meist ein Tippfehler ist.

## Gewichte

Eine `WeightDefinition` benennt ein Gewicht und seine Art; die Werte stehen an den Kanten (`EdgeWeight(WeightId, Value)`) und kommen mit ihnen, eine Suche fragt also nie ein einzelnes Gewicht ab.

| Art | Kosten einer Kante | Beispiel |
|---|---|---|
| `Length` | Länge | der Standard, `WeightDefinition.Length` |
| `Absolute` | Wert | eine feste Zeit für eine Fähre |
| `PerLength` | Wert × Länge | Ohm pro Kilometer |
| `LengthOverValue` | Länge ÷ Wert | Fahrzeit aus einer Geschwindigkeit |

Die Kosten eines Wegs sind die Summe seiner Kanten. Eine Kante ohne Wert nimmt den `Fallback` der Definition; ohne ihn bricht die Suche mit einer `InvalidGraphException` ab, sobald sie die Kante erreicht, statt mit 0 zu rechnen.

## Schaltzustand

Der Normalschaltzustand ist Teil der Datensätze: gesperrte Knoten (`Blocked`), deaktivierte Kanten (`Disabled`) und die Richtung (`Direction`). Für eine Suche hält `NetworkState` die Abweichungen (`Block`, `Unblock`, `Disable`, `Enable`, `SetDirection`); das Netzwerk ändert sich nicht. Ein **gesperrter Knoten wird erreicht, aber nicht durchlaufen**: Er erscheint im Ergebnis, nichts dahinter. Eine deaktivierte Kante gibt es für die Suche nicht. Ein Host mit eigener Logik, z. B. „jede Trennstelle gesperrt“, implementiert `INetworkState`; dessen Methoden bekommen den ganzen Datensatz.

Die Namen sind absichtlich neutral: Für einen Elektriker leitet ein „geschlossener“ Schalter, bei einem Tor ist es umgekehrt.

## Große Netzwerke

- Eine Suche merkt sich einen Eintrag pro besuchtem Knoten. In einem Ortsnetz sind das einige hundert Einträge.
- **Ein Gebiet nach dem anderen durchsuchen.** Eine Suche von allen Trafos einer Stadt läuft als eine Welle durch alle Ortsnetze zugleich: Über einen Shard-Cache lädt sie jeden Shard wieder und wieder, im Speicher merkt sie sich jeden Knoten. Stattdessen von einem Trafo (oder einem Gebiet) nach dem anderen suchen, in der Reihenfolge der Shards, dann wird jeder Shard etwa einmal geladen.
- Gemessen an einem synthetischen Netzwerk aus radialen Ortsnetzen mit je 300 Knoten (`--quick-network` in den Benchmarks): Eine Suche in einem Ortsnetz braucht im Speicher unter 1 ms; über `ShardedGraphBase` etwa 1,5 ms mit leerem und 0,7 ms mit warmem Cache (etwa 5 ms mit leerem Cache, wenn jede Abfrage an den Speicher 0,5 ms dauert). Jedes Ortsnetz eines Netzwerks mit 3 Mio. Knoten nacheinander zu durchsuchen dauert etwa 10 s und lädt jeden Shard einmal; eine einzige Suche von allen 3.333 Trafos eines Netzwerks mit 1 Mio. Knoten über einen Cache von 64 Shards lud Shards über 500.000-mal.
- Fehler in den Daten (eine Kante zu einem Knoten, den der Host nicht liefert, ein fehlendes Gewicht, negative oder unendliche Kosten) sind eine `InvalidGraphException` mit den beteiligten IDs; Fehler des Hosts laufen unverändert durch. Jede Suche nimmt ein `CancellationToken`.
