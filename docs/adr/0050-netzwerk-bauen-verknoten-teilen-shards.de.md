# Ein Netzwerk bauen: Linienenden innerhalb einer Toleranz verknoten, Linien teilen, IDs und Shards vom Host

Ein Netzwerk (ADR 0049) entsteht aus den Linien und Punkten des Hosts: Kabel, Freileitungen und Verbinder aus mehreren Feature-Klassen, dazu Kästen, Trafos, Schalter und Kunden als Punkte. SpatialNET bekommt einen **Builder**, der daraus Knoten und Kanten macht. Er **verknotet** alle Linienenden und Punkte, die innerhalb einer **Toleranz** liegen, zu einem Knoten, **teilt** eine Linie dort, wo ein Punkt oder das Ende einer anderen Linie auf ihr liegt (ein T-Anschluss), ordnet jeden Knoten einem **Shard** zu, aus Polygonen, die der Host übergibt (meist seine Ortsnetze), und **streamt** Knoten und Kanten an einen Writer des Hosts. Die Geometrie bleibt unverändert: Anders als bei `SnapTo` (ADR 0046) bewegt sich kein Stützpunkt; ein Knoten sagt nur, welche Enden zusammengehören. IDs für alles, was der Builder erzeugt, kommen vom Host, damit sie von einem nächtlichen Bau zum nächsten gleich bleiben. Jede Suche im Builder läuft über ein Raster oder einen räumlichen Index, der Aufwand wächst also mit *n* log *n*, nicht mit *n*², bei etwa 3 Mio. Linien.

*English version: [0050-netzwerk-bauen-verknoten-teilen-shards.md](0050-netzwerk-bauen-verknoten-teilen-shards.md)*

## Consequences

- **Ort:** das Paket `SpatialNET.Network.Building` (ADR 0049), Namespace `SpatialNET.Network.Building`; es hängt von Core, Extensions und Network ab, weil es den räumlichen Index, vorbereitete Geometrien und die lineare Referenzierung benutzt.
- **API:**

  ```csharp
  public sealed class NetworkBuilder<TNodeId, TEdgeId, TSource>
  {
      public NetworkBuilder(NetworkBuildOptions options, INetworkIdFactory<TNodeId, TEdgeId, TSource> ids);
      public void AddNode(TSource source, TNodeId id, IPoint2D point, int typeId, bool blocked = false);
      public void AddNode(TSource source, TNodeId id, Coordinate location, int typeId, bool blocked = false);
      public void AddLine(TSource source, IPolyline2D line, int typeId, LineAttributes? attributes = null);
      public BuildResult Build(INetworkWriter<TNodeId, TEdgeId, TSource> writer, IProgress<BuildProgress>? progress = null,
                               CancellationToken cancellationToken = default);
  }
  public sealed record LineAttributes(EdgeDirection Direction = Both, bool Disabled = false,
                                      double? Length = null, IReadOnlyList<EdgeWeight>? Weights = null);
  ```

  - `TSource` ist der Verweis des Hosts auf das Quellobjekt (z. B. ein Struct aus Tabelle und Objekt-ID); der Builder reicht ihn nur durch.
  - Ein Builder wird einmal benutzt: Die `Add…`-Aufrufe dürfen in beliebiger Reihenfolge kommen, `Build` verarbeitet die Punkte vor den Linien, und jeder Aufruf nach `Build` wirft `InvalidOperationException`. Er ist nicht thread-sicher.
  - `AddLine` kopiert die Koordinaten in kompakte Arrays, der Host kann seine Features also aus der Datenbank streamen und wieder freigeben.
- **Optionen:** `NetworkBuildOptions` mit `Tolerance` (Pflicht, endlich, größer 0, in den Einheiten der Koordinaten, wie bei `SnapTo`), `SplitAtNodes` und `SplitAtLineEnds` (beide an), `NodeAtCrossings` (aus), `ShardRegions`, `DefaultShard`, `FallbackShardCellSize` und `Weights` (die Gewichtsdefinitionen, nötig, wenn Linien Gewichte tragen). Die Toleranz ist nicht das Gitter der Overlays (ADR 0005): Der Builder rundet nichts.
- **Verknoten:**
  - Echte Knoten und Linienenden kommen in ein Hash-Raster mit Zellen in der Größe der Toleranz; jeder schaut nur in die 3 × 3 Zellen um sich herum. Alles, was innerhalb der Toleranz von etwas anderem liegt, wird vereinigt (Union-Find). Die **zusammenhängenden Gruppen werden die Knoten**, das Ergebnis hängt also nicht von der Reihenfolge der Eingabe ab.
  - Eine Gruppe mit **einem echten Knoten** wird dieser Knoten, mit seiner ID, seinem Typ und seiner Lage. Eine Gruppe nur aus Linienenden wird ein **Hilfsknoten** (Typ 0) im Mittel ihrer Enden, berechnet in einer festen Reihenfolge.
  - Eine Gruppe mit **zwei oder mehr echten Knoten** ist ein Konflikt: Zwei Kästen an derselben Stelle sind ein Fehler in den Daten.
  - Eine Kette von Enden, die weiter als die Toleranz reicht (A nahe B, B nahe C, A weit von C), bleibt ein Knoten; der Builder meldet sie, weil sie meist auf eine zu große Toleranz oder dichte Daten hinweist.
- **Teilen:**
  - `SplitAtNodes`: Ein echter Knoten innerhalb der Toleranz vom Inneren einer Linie teilt die Linie am nächsten Punkt und wird der Knoten zwischen den beiden Teilen (ein Schalter mitten in einem Kabel).
  - `SplitAtLineEnds`: Ein Knoten aus Linienenden innerhalb der Toleranz vom Inneren einer anderen Linie teilt diese dort (ein Verbinder, der auf einem Kabel endet).
  - Linien, die sich ohne gemeinsamen Punkt kreuzen, bleiben getrennt (eine Freileitung über einem Kabel). `NodeAtCrossings` macht jede Kreuzung zu einem Hilfsknoten, der beide Linien teilt; gedacht für Straßennetze.
  - Die Kandidaten kommen aus einem statischen STR-Index über die um die Toleranz vergrößerten Envelopes der Linien (`SpatialIndex<T>`, ADR 0014); nur die Segmente der Kandidaten werden exakt geprüft. Ein Index über Linien statt über Segmente bleibt bei 3 Mio. Einträgen statt etwa 15 Mio.
  - Eine mehrteilige Linie ergibt Kanten pro Teil; der Teilindex zählt über alle Teile der Reihe nach. Eine geschlossene Linie, deren Enden sich treffen, ergibt eine Schleife.
- **Kanten:**
  - Jeder Teil einer Linie wird eine Kante vom Knoten an seinem Anfang zum Knoten an seinem Ende, in Richtung der Linie; `Direction`, `Disabled` und `TypeId` kommen von der Linie.
  - `FromMeasure` und `ToMeasure` geben den Bereich des Teils auf der Quelllinie als geometrische Länge ab ihrem Anfang an, damit `Substring` (ADR 0017) genau diesen Teil ausschneidet.
  - `Length` ist die geometrische Länge des Teils, oder, wenn `LineAttributes.Length` gesetzt ist (eine Aufmaßlänge), diese Länge, aufgeteilt im Verhältnis der geometrischen Längen der Teile.
  - Gewichte werden auf jeden Teil kopiert, außer Gewichten der Art `Absolute`, die wie die Aufmaßlänge anteilig nach Länge geteilt werden; eine Linie mit einem Gewicht, dessen ID nicht in `options.Weights` steht, ist ein Fehler.
  - Ein Teil kürzer als die Toleranz, dessen Enden in denselben Knoten fallen, wird weggelassen und gemeldet.
- **IDs:** Der Host implementiert `INetworkIdFactory<TNodeId, TEdgeId, TSource>`:
  - `TNodeId NewHelperNode(Coordinate location)` für jeden Hilfsknoten. Die Lage ist das Mittel der Enden; eine daraus abgeleitete ID (ein Hash der gerundeten Koordinate) bleibt zwischen den Bauläufen gleich, wovon quittierte Fehler und Vergleiche zwischen zwei Nächten abhängen.
  - `TEdgeId NewEdge(TSource source, int part, int partCount)` für jede Kante, auch für eine ungeteilte Linie (Teil 0 von 1).
  - Echte Knoten behalten die ID aus `AddNode`. Eine doppelte ID aus `AddNode` wirft sofort `ArgumentException`; eine doppelte ID aus der Fabrik ist ein Konflikt.
- **Shards:**
  - `ShardRegion(IPolygon2D Area, long ShardId, TypeSet? AppliesTo)`. Ein Knoten bekommt den Shard der **ersten Region in der Liste**, deren Fläche seine Lage überdeckt (der Rand zählt) und die für ihn gilt. Regionen werden über einen STR-Index über ihre Envelopes gefunden und mit vorbereiteten Geometrien geprüft (ADR 0013).
  - `AppliesTo` darf Knoten- und Kantentypen enthalten. Ein echter Knoten passt über seinen Typ; ein Hilfsknoten, der keinen Typ hat, passt, wenn eine seiner Kanten einen Typ aus der Menge hat. So nehmen Ortsnetze die Niederspannung und gröbere Polygone die Mittelspannung.
  - Ohne passende Region bekommt ein Knoten `DefaultShard`, oder, wenn das nicht gesetzt ist, den Schlüssel einer quadratischen Rasterzelle der Größe `FallbackShardCellSize`; beides wird gemeldet.
  - Überlappende Regionen werden einmal pro Paar von Regionen gemeldet, mit der ersten Stelle.
  - Eine Kante trägt die Shards beider Enden (`FromShard`, `ToShard`); eine Kante zwischen zwei Shards gehört zu beiden, und der Host speichert sie bei beiden (ADR 0049).
  - Die Shard-ID ist ein `long`. Ein generischer Typ machte jede Signatur länger bei wenig Gewinn; ein Host mit Texten als Schlüssel nummeriert seine Regionen.
- **Schreiben:** Der Host implementiert `INetworkWriter<TNodeId, TEdgeId, TSource>` (in `SpatialNET.Network`, mit `BuiltNode` und `BuiltEdge`: Sie brauchen keine Geometrie, und so kann `InMemoryGraph` in `SpatialNET.Imp` es implementieren, ohne das Builder-Paket zu kennen) mit `WriteNode(in BuiltNode)` (Knoten, Shard, Hilfsknoten oder nicht, Quelle) und `WriteEdge(in BuiltEdge)` (Kante, Quelle, Maße, beide Shards). `InMemoryGraph` (ADR 0049) implementiert es schon. Alle Knoten werden vor der ersten Kante geschrieben, damit eine Datenbank mit Fremdschlüsseln sie annehmen kann: echte Knoten in der Reihenfolge von `AddNode`, dann Hilfsknoten in einer festen Reihenfolge ihrer Lage; Kanten in der Reihenfolge von `AddLine`, Teile aufsteigend.
- **Fehler vor dem ersten Schreiben:** Alle Prüfungen, die scheitern können, laufen, bevor etwas geschrieben wird. Konflikte (echte Knoten in einer Gruppe, doppelte IDs aus der Fabrik, eine Hilfsknoten-ID gleich der ID eines echten Knotens) werden gesammelt und zusammen als `NetworkBuildException : InvalidOperationException` mit einer Liste von `NodeConflict` (Stelle, Art, beteiligte Quellen) geworfen. Der Writer sieht also nie ein halbes Netzwerk wegen schlechter Daten; eine Ausnahme des Hosts oder ein Abbruch können ihn dennoch mittendrin stoppen, deshalb schreibt ein Host in neue Tabellen und tauscht sie erst nach dem Erfolg.
- **Fortschritt:** Der Bau eines ganzen Netzwerks dauert Minuten, deshalb nimmt `Build` optional ein `IProgress<BuildProgress>`, das Muster von .NET: `BuildProgress(Phase, Done, Total)` mit den Phasen `JoiningEnds`, `Splitting`, `AssigningShards`, `Checking`, `WritingNodes` und `WritingEdges`. Ein Anteil pro Phase ist ehrlich; ein Anteil über alle Phasen wäre geraten, weil die Phasen sehr unterschiedlich lange dauern. Der Builder meldet höchstens alle 250 ms und am Ende jeder Phase, damit Millionen Aufrufe keine Zeit kosten, und ruft den Fortschritt im bauenden Thread auf: Eine Oberfläche übergibt `Progress<T>`, das den Aufruf in ihren Thread bringt, ein nächtlicher Job schreibt in sein Log. Eine Ausnahme aus dem Fortschritt bricht den Bau ab wie jede Ausnahme des Hosts. Abbrechen bleibt das `CancellationToken`.
- **Bericht:** `BuildResult(NodeCount, EdgeCount, Diagnostics)`; ein `BuildDiagnostic` hat eine Art (`OutsideShardRegions`, `OverlappingShardRegions`, `ClusterWiderThanTolerance`, `IsolatedNode`, `ShortPartDropped`), eine Stelle und eine Meldung. Hinweise halten den Bau nicht an.
- **Argumentprüfungen** (ADR 0012): `null` wirft `ArgumentNullException`; Typ 0 für einen Knoten, eine negative Aufmaßlänge oder eine ungültige Toleranz werfen `ArgumentOutOfRangeException`; eine leere Geometrie oder Koordinaten mit `NaN` werfen `ArgumentException`.
- **Determinismus:** Dieselbe Eingabe in derselben Reihenfolge ergibt dieselben Knoten, Kanten, IDs und dieselbe Ausgabereihenfolge. Eine andere Reihenfolge ergibt dieselben Knoten und Kanten, möglicherweise in anderer Reihenfolge geschrieben.
- **Größe:** Bei etwa 3 Mio. Linien mit im Schnitt 5 Stützpunkten brauchen die kopierten Koordinaten etwa 250 MB, Raster, Index und Gruppen noch einmal etwa so viel; die Rechenzeit liegt im Bereich von Sekunden bis wenigen Minuten. Lesen aus dem GIS und Schreiben in die Datenbank dauern meist länger.
- **Weg-Geometrie:** `NetworkPathGeometry.ToPolyline(this NetworkPath, Func<TEdgeId, EdgeShape> shapeOf, IGeometryFactory? factory = null)` mit `EdgeShape(IPolyline2D Line, double FromMeasure, double ToMeasure)`. Der Host liefert zu jeder Kante die Quelllinie und den Bereich; die Methode schneidet jeden Teil mit `Substring` aus, dreht Teile um, die gegen ihre Richtung durchlaufen wurden, und verbindet sie zu einem Pfad.
- **Tests (ADR 0008):** NTS hat keinen Netzwerk-Builder, es gibt also kein Orakel. Ein Builder mit roher Gewalt im Testprojekt (jedes Ende gegen jedes Ende und jede Linie, *n*²) ist die Referenz für kleine Zufallseingaben (CsCheck): dieselben Gruppen und Teilungen, dasselbe Ergebnis für eine gemischte Eingabe bis auf IDs, jedes Linienende in genau einem Knoten, die Teile einer Linie decken ihre Länge lückenlos ab, Aufmaßlängen und absolute Gewichte summieren sich zur Eingabe, Shards gleich einem Punkt-in-Polygon mit roher Gewalt. Handgeschriebene Fälle decken den T-Anschluss ab, den Schalter mitten im Kabel, zwei zu nahe Kästen, Ketten, Kreuzungen mit und ohne `NodeAtCrossings`, mehrteilige und geschlossene Linien und die Fehler.
- **Benchmark:** `NetworkBuildBenchmarks` baut ein synthetisches Netzwerk aus etwa 3 Mio. Linien in radialen Ortsnetzen und misst Zeit und Speicher; zusammen mit `NetworkBenchmarks` (ADR 0049) entscheidet er, ob parallele Schritte oder ein Bau in Kacheln nötig sind.
- **Playground:** eine eigene Netzwerk-Ansicht, kein Deskriptor (ADR 0011), weil die Eingabe eine Menge von Linien und Punkten ist: Linien und Punkte zeichnen oder laden, bauen, echte Knoten, Hilfsknoten und Kanten anzeigen und den kürzesten Weg zwischen zwei angeklickten Knoten.
- **Nicht Teil dieses Schritts:**
  - **Inkrementelle Änderungen.** Das Netzwerk wird jede Nacht neu gebaut. Um ein Ortsnetz beim Speichern zu prüfen, baut der Host aus allen Objekten, die sein Polygon berühren, ein temporäres Netzwerk in einen `InMemoryGraph` und prüft nur die Knoten innerhalb (ADR 0052).
  - **Bau in Kacheln** und **parallele Schritte**, bis der Benchmark sie verlangt.
  - Stützpunkte verschieben: Daten, die sich treffen sollten, es aber nicht tun, lassen sich vor dem Bau mit `SnapTo` vorbereiten.

## Considered Options

- Alle Enden auf das Gitter der Overlays runden und gleiche Gitterpunkte als einen Knoten behandeln (verworfen: Zwei Enden 1 mm voneinander auf beiden Seiten einer Zellgrenze blieben getrennt, eine falsche Unterbrechung im Netzwerk an zufälliger Stelle; Gruppen innerhalb einer Toleranz haben diese Kante nicht)
- Ein Ende mit dem nächsten schon gesetzten Knoten verbinden, in Eingabereihenfolge (verworfen: Das Ergebnis hinge von der Reihenfolge der Eingabe ab)
- Die Stützpunkte aufeinander verschieben, wie `SnapTo` es tut (verworfen: Das Netzwerk änderte die Geometrie, die dem Host gehört; ein Knoten hält nur fest, welche Enden zusammengehören)
- Knoten an jeder Kreuzung als Standard (verworfen: In einem Leitungsnetz sind eine Freileitung über einem Kabel oder zwei Kabel in einem Graben nicht verbunden; für Straßennetze ist es eine Option)
- Von zwei echten Knoten an derselben Stelle den ersten nehmen und den anderen weglassen (verworfen: Zwei Kästen an einer Stelle sind ein Datenfehler, der sonst still verschwände)
- Von der Bibliothek abgeleitete Kanten-IDs, z. B. Quell-ID und Teil (verworfen: `TEdgeId` ist der Typ des Hosts; die Fabrik bekommt Quelle und Teil und kann genau das tun)
- Shards aus einem Schlüssel pro Eingabeobjekt (verworfen: Hilfsknoten haben kein Objekt, und an der Grenze zweier Ortsnetze wären sich die Linien uneinig; ein Polygon entscheidet nach der eigenen Lage des Knotens)
- Eine generische Shard-ID (vorerst verworfen: ein weiterer Typparameter an jeder Signatur)
- Knoten und Kanten gemischt schreiben, so wie sie gefunden werden (verworfen: Eine Datenbank mit Fremdschlüsseln braucht die Knoten zuerst, und die Konflikte müssen vor dem ersten Schreiben bekannt sein)
- Ein Index über Segmente statt über Linien (verworfen: etwa fünfmal so viele Einträge für dieselben Kandidaten)
