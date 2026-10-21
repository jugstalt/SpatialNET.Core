# Playground: "map first", minimal API without a frontend build, UI from explicit algorithm descriptors

The playground is an ASP.NET Core minimal API (net10.0) with a single static page in plain JavaScript (ES modules, no npm/build). Leaflet 1.9 and Leaflet-Geoman free live locally in `wwwroot/lib`. The UI follows the prototype variant **"map first"**: a full-screen map, a command palette (Ctrl+K) with the parameters of the selected algorithm below it, chips for computation CRS, layers and figures, and at the bottom a bar with preview cards of the sample geometries. The UI knows no algorithm by heart but builds itself from `GET /api/algorithms`. For this, every algorithm has an **explicit descriptor in the playground** (`IPlaygroundAlgorithm`: id, name, group, inputs with allowed types, parameters with type, default, unit `crs` and German label, `Run(...)`), found by assembly scan. Reflection over the extension methods with attributes in SpatialNET.Extensions was rejected because it carries playground concerns into the library.

*German version: [0011-playground-karte-zuerst-deskriptor-registry.de.md](0011-playground-karte-zuerst-deskriptor-registry.de.md)*

## Consequences

- **Completeness test:** every public method of SpatialNET.Extensions must have a playground descriptor (analogous to the reflection test from ADR 0008).
- **`GET /api/fixtures?algorithm=`** reads `samples/` (copied to the output directory) and converts WKT to GeoJSON with SpatialNET.IO. Fixtures with `crs` are projected to WGS84, local planar ones arrive raw with the flag `local` (ADR 0010). By default only fixtures with the tag `playground` appear; a switch shows all.
- **`POST /api/run`** computes automatically on every change (300 ms debounce). The response contains the result as GeoJSON (WGS84 or local), the computation CRS, figures (area, length, vertices, parts) and warnings. List results such as Cut are colored and numbered per piece. Errors appear as a chip; the last result stays in place.
- Inputs come from drawing with Geoman (target A/B selectable), GeoJSON upload (`GeoJson.ReadAll`, ADR 0009) or sample geometries.
- The `node_modules/` entry in `.gitignore` is dropped.

## Considered Options

- Variants "workbench" (3 columns) and "catalog" (fixture table with text editor) – prototype on branch `prototype/playground-ux`
- Frontend with an npm/Vite build, Blazor (rejected: tooling ballast for one page)
- Reflection plus attributes in SpatialNET.Extensions (rejected: playground concerns in the library)
