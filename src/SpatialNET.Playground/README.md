# SpatialNET Playground

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
