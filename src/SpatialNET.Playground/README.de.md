# SpatialNET Playground

*English version: [README.md](README.md)*

Werkzeug zum Ausprobieren der Algorithmen auf einer Karte (ADR 0010, ADR 0011).

```bash
dotnet run --project src/SpatialNET.Playground
```

Dann `http://localhost:5003` öffnen.

- **Backend:** Minimal API in `Program.cs`. `Algorithms/` enthält die Deskriptoren (eine Klasse pro Algorithmus,
  per Assembly-Scan gefunden); `Services/` die Projektion (Proj4Net, Auto-UTM), die Fixtures aus `samples/` und `RunService`.
- **Frontend:** `wwwroot/` in reinem JavaScript (ES-Module, kein Build). Die UI baut sich aus `GET /api/algorithms`;
  ein neuer Algorithmus braucht nur einen Deskriptor, keine Frontend-Änderung.
- **Bedienung:** Strg+K wählt den Algorithmus; Eingaben zeichnen (Ziel A/B), als GeoJSON hochladen oder ein Beispiel
  aus der Leiste wählen. Gerechnet wird 300 ms nach jeder Änderung. Beispiele ohne `crs` laufen lokal planar (`L.CRS.Simple`).
- **Einrasten und Exaktheit:** Beim Zeichnen rasten Punkte an Stützpunkten und Segmenten ein (Leaflet-Geoman). Ein Stützpunkt ist exakt;
  ein auf ein Segment gerasteter Punkt liegt nur nahe daran, im Rahmen der Rundung. Prädikate rechnen exakt, ein solcher Punkt kann also
  `false` ergeben; jedes Prädikat zeigt den Abstand seiner Eingaben und darunter den Aufruf, der mit Toleranz fragt (die Bibliothek hat keine globale Toleranz; das Prädikat selbst bleibt exakt).
- **WKT und M:** Jede Eingabe hat ein WKT-Feld (WKT einfügen, auch mit Z und M, oder das Gezeichnete kopieren), und jedes
  Ergebnis zeigt sein WKT mit einer Kopieren-Schaltfläche. GeoJSON kann kein M tragen, deshalb bekommen die M-Algorithmen
  (`InterpolateByM`, `ProjectToM`, `SubstringByM`) ihre Daten über WKT. Eine Geometrie auf der Karte zu bearbeiten verwirft ihr M.

## Netzwerk-Ansicht

`http://localhost:5003/network.html` (Link „Network view“ auf der Hauptseite; [ADR 0050](../../docs/adr/0050-netzwerk-bauen-verknoten-teilen-shards.md)) ist eine eigene Seite, kein Deskriptor, weil ihre Eingabe eine Menge von Linien und Punkten ist.

- **Eingabe:** Punkte (Typ Trafo 25, Kasten 15, Schalter 17, Kunde 16) und Linien (Kabel 11, Freileitung 12, Kastenverbinder 13) zeichnen, WKT einfügen (eine Geometrie pro Zeile) oder eine GeoJSON-Datei laden. Den Typ jedes Elements ändert man in der Liste. „Example“ lädt das Beispiel der Konzeptseite (ein Trafo, ein Kabel mit einem Schalter in der Mitte, ein Verbinder, der als T-Kreuzung darauf endet, ein Kunde) im lokal planaren Modus.
- **Bauen:** Optionen sind die Toleranz (in Metern der Auto-UTM-Zone, im lokalen Modus in Einheiten) sowie `SplitAtNodes`, `SplitAtLineEnds`, `NodeAtCrossings`. `POST /api/network/build` projiziert wie die anderen Algorithmen, lässt `NetworkBuilder` in einen `InMemoryGraph` laufen und liefert die Knoten, die Kanten (jede mit ihrem Stück der Quelllinie) und `BuildResult.Diagnostics`. Echte Knoten sind gefüllte Kreise in der Farbe ihres Typs, Hilfsknoten kleine hohle Kreise, Kanten sind nach Typ gefärbt. Diagnosen und, wenn der Bau eine `NetworkBuildException` wirft, die Konflikte werden aufgelistet; ein Klick zoomt an ihre Stelle. Jede Änderung der Eingabe verwirft das Ergebnis.
- **Kürzester Weg:** zwei Knoten anklicken; `POST /api/network/path` baut das Netz neu (der Server hält keinen Zustand), führt `ShortestPath` aus und zeichnet den Weg mit `NetworkPathGeometry.ToPolyline` samt Gesamtlänge. Shift-Klick oder Rechtsklick sperrt einen Knoten für die Suche (`NetworkState.Block`: er wird erreicht, aber nicht durchquert).
- **Versorgung (ADR 0051):** "Click nodes to mark sources" ankreuzen und Knoten (Trafos) anklicken, um sie als Quellen zu markieren; `POST /api/network/supply` baut das Netz neu, führt `TraceEdges` und `Supply` mit den Kunden (Typ 16) als Senken aus und zeichnet jedes Versorgungsgebiet in einer eigenen Farbe. Ein Kunde, den zwei Quellen erreichen, ist orange mit schwarzem Ring (Mehrfachversorgung), einer, den niemand erreicht, ist rot; beide werden aufgelistet, ein Klick zoomt hin. Einen Knoten sperren (Shift-Klick oder Rechtsklick) öffnet eine Trennstelle und aktualisiert das Bild.
