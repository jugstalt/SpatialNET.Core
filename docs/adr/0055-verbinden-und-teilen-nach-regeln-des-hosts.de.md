# Verbinden und Teilen nach Regeln des Hosts

ADR 0050 fasst jedes Linienende und jeden Punkt innerhalb der Toleranz zu einem Knoten zusammen und teilt jede Linie an jedem Knoten, der innerhalb der Toleranz an ihrem Inneren liegt. In einem Versorgungsnetz werden mehrere Ebenen (Hoch-, Mittel- und Niederspannung) oft von verschiedenen Abteilungen gepflegt, und ihre Elemente liegen zufällig aufeinander: Eine Niederspannungsmuffe auf einem Stützpunkt eines Mittelspannungskabels teilte das Kabel, und ein Trace auf der Mittelspannung lief in die Niederspannung. Der Graph braucht keine Geometrie, zwei Knoten an einer Stelle sind also kein Problem; der Builder muss nur wissen, welche Linie an welchen Knoten anschließt. Das ist Fachlogik, also entscheidet der Host, nach Typen.

*English version: [0055-verbinden-und-teilen-nach-regeln-des-hosts.md](0055-verbinden-und-teilen-nach-regeln-des-hosts.md)*

## Konsequenzen

- **Vier optionale Callbacks in `NetworkBuildOptions`, alle auf Typ-IDs.** Ohne sie baut der Builder wie in ADR 0050.
  - `HelperNodeType(edgeType) → nodeType`: der Typ des Hilfsknotens am Ende einer Linie. Linienenden bilden nur mit Enden desselben Hilfstyps einen Hilfsknoten, Enden zweier Ebenen an einer Stelle ergeben also zwei Hilfsknoten. Eine Verbindung zwischen Ebenen braucht immer einen echten Knoten (einen Trafo); es gibt keine Kette von Enden über Ebenen hinweg.
  - `Connect(nodeType, edgeType)`: ob ein Linienende innerhalb der Toleranz eines echten Knotens an ihn anschließt. Jedes Ende einer Gruppe von Enden schließt an den ersten echten Knoten seiner Stelle an, den sein Typ erlaubt; die übrigen bilden den Hilfsknoten der Gruppe.
  - `Split(nodeType, edgeType)`: ob ein Knoten innerhalb der Toleranz am Inneren einer Linie sie teilt; gefragt mit dem Typ des echten Knotens oder mit dem Hilfstyp eines T-Stoßes. Hilfsknoten an Kreuzungen (`NodeAtCrossings`) werden nicht gefragt.
  - `Partition(nodeType) → int`: Echte Knoten verschiedener Partitionen innerhalb der Toleranz sind kein Konflikt und bleiben zwei Knoten. Darf ein Linienende an echte Knoten mehrerer Gruppen an einer Stelle anschließen, werden sie eine Gruppe für `ResolveCoincidentNodes` (ADR 0054), denn ein Ende endet nur an einem Knoten.
- **`ConnectionDecision`**: `Connect`, `Ignore` (keine Verbindung, kein Hinweis) oder `Report` (keine Verbindung und eine Diagnose `ConnectionRefused` oder `SplitRefused`, die in `BuildDiagnostic.Sources` die Quelle von Knoten und Linie nennt): Eine erwartete Ablehnung bleibt still, eine, die auf einen Datenfehler deutet, wird gemeldet.
- **Einmal pro Typpaar gefragt** und gemerkt; die Kosten wachsen nicht mit den Daten, und die Antwort hängt nicht von der Reihenfolge ab.
- **Hilfstypen.** `NetworkType.Helper` kennzeichnet Knotentypen von Hilfsknoten. Wie Typ 0 trifft sie `untyped`, nicht aber `*` oder ein Muster ohne `untyped`; `untyped & [Niederspannung, *, *]` ist der Hilfstyp der Ebene Niederspannung.
- **`INetworkIdFactory.NewHelperNode(Coordinate, int typeId)`**: Hilfsknoten verschiedener Typen können am selben Ort liegen und brauchen verschiedene, über Bauläufe stabile IDs. Bricht die API.
- `SplitAtNodes`, `SplitAtLineEnds` und `NodeAtCrossings` bleiben als grobe Schalter vor den Callbacks.

## Betrachtete Optionen

- Eine Liste von Teilungsregeln (Typmuster) in den Build-Optionen (verworfen: Der Builder hielte Fachlogik; der Host löst seine Muster einmal auf und antwortet mit IDs)
- Ein Callback am generischen Builder mit den Quellen von Knoten und Linie (verworfen: Die Entscheidungen hängen am Typ, und ein Callback pro Objekt ließe sich nicht merken)
- Ein Callback pro Paar von Elementen beim Zusammenfassen von Enden (verworfen: "darf verbinden" ist nicht transitiv, Union-Find über Paare verbände A und C über B; Enden fassen sich nach Hilfstyp zusammen und schließen einzeln an echte Knoten an)
- Eine Antwort als bool (verworfen: Eine Ablehnung, die ein Datenfehler ist, braucht einen Hinweis, eine erwartete muss still bleiben)
- Verbindungen von Linienenden über Ebenen ohne echten Knoten (verworfen: Ohne Betriebsmittel gibt es keine physikalische Verbindung zwischen Spannungsebenen)
