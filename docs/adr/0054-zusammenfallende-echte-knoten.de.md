# Zusammenfallende echte Knoten: Der Host darf einen Gewinner wählen

ADR 0050 hat zwei echte Knoten innerhalb der Toleranz zum Datenfehler gemacht: Der Build bricht vor dem ersten Schreiben ab und listet jede solche Gruppe. Echte Leitungsdaten brechen diese Regel absichtlich. Ein Kunde wird oft auf dem Hausanschlusskasten gezeichnet, aus dem er versorgt wird (ein Netz hatte 11.362 solche Paare), und ein Endpunkt sitzt auf dem Knoten, an dem die Leitung endet. Das ist ein Ort im Netz, zweimal gezeichnet. Welcher Knoten für den Ort stehen soll, weiß der Host, nicht der Builder.

*English version: [0054-zusammenfallende-echte-knoten.md](0054-zusammenfallende-echte-knoten.md)*

## Konsequenzen

- **Die Regel aus ADR 0050 bleibt der Standard.** Ohne Resolver ist eine Gruppe echter Knoten innerhalb der Toleranz wie bisher ein Konflikt `RealNodesTooClose`.
- **`NetworkBuilder.ResolveCoincidentNodes`** (ein `CoincidentNodesResolver<TNodeId, TSource>`) wird **einmal pro Gruppe** vor dem ersten Schreiben aufgerufen, mit allen echten Knoten der Gruppe (`CoincidentNode`: Id, Typ, Lage, Quelle) in der Reihenfolge, in der sie hinzugefügt wurden. Er liefert den Index des Knotens, der bleibt, oder -1 für einen Konflikt. Jeder andere Wert wirft `InvalidOperationException`.
- **Einmal pro Gruppe, nicht pro Paar:** Drei Knoten an einem Ort bekommen eine Entscheidung, das Ergebnis kann also nicht von der Reihenfolge abhängen, in der Paare gefunden werden. Ein Resolver, der nach den Knoten entscheidet (Typ, Quelle) und nicht nach ihrer Stelle in der Liste, hält den Build unabhängig von der Eingabereihenfolge.
- **Die Verlierer werden zusammengelegt, nicht verworfen.** Sie werden nicht als Knoten geschrieben, teilen keine Linien und gelten nicht als isoliert; der Gewinner wird mit `BuiltNode.Merged` geschrieben, der Liste der zusammengelegten Knoten, damit der Host sie als Aliase behalten kann (ein Kunde muss seinen Knoten weiter finden). Eine Diagnose `NodesMerged` pro Gruppe.
- **Die Regel gehört dem Host** (zum Beispiel: Der Layer weiter oben im Inhaltsverzeichnis der Karte gewinnt). Der Builder kennt keine Prioritätslisten, keine erlaubten Paare und keine Marker-Typen.

## Erwogene Optionen

- Eine Prioritätsliste von Typmustern in den Build-Optionen (verworfen: Der Builder hielte fachliche Regeln, und die natürliche Ordnung des Hosts, etwa das Inhaltsverzeichnis, ist kein Typmuster)
- Eine Liste von Typpaaren, die zusammenfallen dürfen (vorerst verworfen: Ein Resolver kann das, und jede Zusammenlegung wird gemeldet)
- Marker-Typen, die nie einen eigenen Knoten bilden und sich an den Knoten an ihrer Stelle hängen (verworfen: eine zweite Art von Knoten im Builder für das, was ein Resolver mit einem Callback ausdrückt)
- Den ersten Knoten in Eingabereihenfolge behalten (schon in ADR 0050 verworfen: Das Ergebnis hinge von der Reihenfolge der Daten ab)
- Ein Callback pro Paar (verworfen: Drei Knoten an einem Ort gäben reihenfolgeabhängige Ergebnisse)
