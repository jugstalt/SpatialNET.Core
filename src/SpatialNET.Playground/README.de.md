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
  `false` ergeben; jedes Prädikat zeigt den Abstand seiner Eingaben, und die optionale Toleranz sagt, ob sie darunter liegen (das Prädikat selbst bleibt exakt).
- **WKT und M:** Jede Eingabe hat ein WKT-Feld (WKT einfügen, auch mit Z und M, oder das Gezeichnete kopieren), und jedes
  Ergebnis zeigt sein WKT mit einer Kopieren-Schaltfläche. GeoJSON kann kein M tragen, deshalb bekommen die M-Algorithmen
  (`InterpolateByM`, `ProjectToM`, `SubstringByM`) ihre Daten über WKT. Eine Geometrie auf der Karte zu bearbeiten verwirft ihr M.
