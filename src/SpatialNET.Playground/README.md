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
  distance of its inputs and, below it, the call that asks with a tolerance (the library has no global tolerance; the predicate itself stays exact).
- **WKT and M:** every input has a WKT field (paste WKT, also with Z and M, or copy what you drew), and every result shows its WKT
  with a copy button. GeoJSON cannot carry M, so the M algorithms (`InterpolateByM`, `ProjectToM`, `SubstringByM`) get their
  data through WKT. Editing a geometry on the map drops its M.

## Network view

`http://localhost:5003/network.html` (link "Network view" in the main page; [ADR 0050](../../docs/adr/0050-netzwerk-bauen-verknoten-teilen-shards.md)) is a page of its own, not a descriptor, because its input is a set of lines and points.

- **Input:** draw points (type Trafo 25, Kasten 15, Schalter 17, Kunde 16) and lines (Kabel 11, Freileitung 12, Kastenverbinder 13), paste WKT (one geometry per line), or load a GeoJSON file. The type of every element can be changed in the list. "Example" loads the cable of the concept page (a transformer, a cable with a switch in the middle, a connector ending on it as a T junction, a customer) in local planar mode.
- **Build:** the options are the tolerance (in metres of the auto-UTM zone, in units in local mode) and `SplitAtNodes`, `SplitAtLineEnds`, `NodeAtCrossings`. `POST /api/network/build` projects like the other algorithms, runs `NetworkBuilder` into an `InMemoryGraph` and returns the nodes, the edges (each with its piece of the source line) and `BuildResult.Diagnostics`. Real nodes are filled circles in the colour of their type, helper nodes are small hollow circles, edges are coloured by type. Diagnostics and, when the build throws a `NetworkBuildException`, the conflicts are listed; a click zooms to their place. Any change of the input drops the result.
- **Shortest path:** click two nodes; `POST /api/network/path` rebuilds the network (the server keeps no state), runs `ShortestPath` and draws the way with `NetworkPathGeometry.ToPolyline` and its total length. Shift-click or right-click blocks a node for the search (`NetworkState.Block`: it is reached, but not passed).
- **Supply (ADR 0051):** tick "Click nodes to mark sources" and click nodes (transformers) to mark them as sources; `POST /api/network/supply` rebuilds the network, runs `TraceEdges` and `Supply` with the customers (type 16) as sinks and draws each supply area in its own colour. A customer that two sources reach is orange with a black ring (multiple supply), one that nobody reaches is red; both are listed and a click zooms to them. Blocking a node (Shift-click or right-click) opens a separation point and updates the picture.
