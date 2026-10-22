# SpatialNET Playground

*German version: [README.de.md](README.de.md)*

A tool for trying the algorithms on a map ([ADR 0010](../../docs/adr/0010-playground-projektion-serverseitig-auto-utm.md), [ADR 0011](../../docs/adr/0011-playground-karte-zuerst-deskriptor-registry.md)).

```bash
dotnet run --project src/SpatialNET.Playground
```

Then open `http://localhost:5003`.

- **Backend:** minimal API in `Program.cs`. `Algorithms/` contains the descriptors (one class per algorithm,
  found by assembly scan); `Services/` contains the projection (Proj4Net, auto-UTM), the fixtures from `samples/` and `RunService`.
- **Frontend:** `wwwroot/` in plain JavaScript (ES modules, no build). The UI builds itself from `GET /api/algorithms`;
  a new algorithm needs only a descriptor, no frontend change.
- **Usage:** Ctrl+K chooses the algorithm; draw inputs (target A/B), upload them as GeoJSON or pick an example
  from the bar. Computation runs 300 ms after every change. Examples without `crs` run local planar (`L.CRS.Simple`).
- **Snapping and exactness:** drawing snaps to vertices and to segments (Leaflet-Geoman). A vertex is exact; a point snapped onto a
  segment is only close to it, within rounding. Predicates compute exactly, so such a point can give `false`; every predicate shows the
  distance of its inputs, and the optional tolerance tells whether they are within it (the predicate itself stays exact).
- **WKT and M:** every input has a WKT field (paste WKT, also with Z and M, or copy what you drew), and every result shows its WKT
  with a copy button. GeoJSON cannot carry M, so the M algorithms (`InterpolateByM`, `ProjectToM`, `SubstringByM`) get their
  data through WKT. Editing a geometry on the map drops its M.
