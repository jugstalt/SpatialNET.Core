# Fixtures

*German version: [README.de.md](README.de.md)*

Shared test cases for tests and the playground (ADR 0008). One JSON file per case under
`samples/<algorithm>/`; the folder name is the algorithm (method name in lower case, e.g. `area`).

```json
{
  "name": "Island in hole",
  "description": "optional",
  "source": "optional: provenance for imported cases",
  "tags": ["nested", "multi", "playground"],
  "a": "WKT",
  "b": "WKT (optional, second operand)",
  "params": { "distance": 10 },
  "crs": "EPSG:25833 (optional; without = local planar)",
  "expected": "optional: expected value instead of the NTS oracle (WKT or number)",
  "divergence": "mandatory if expected deliberately deviates from the NTS oracle",
  "tolerance": { "area": 1e-6 }
}
```

- **Expected value:** NTS computes it at run time (`Oracle/`); only cases with `expected` use the stored value.
- **Playground:** shows cases with the tag `playground` as sample geometries.
- **Tolerances:** central in `OracleAssert`, overridable per case via `tolerance`.

## Mandatory tag catalog

Every algorithm needs at least one case per tag. If a tag does not apply for domain reasons, it is excluded with a
justification in `samples/<algorithm>/_catalog.json`: `{ "n/a": { "z": "justification" } }`.

| Tag | Meaning |
|---|---|
| `empty` | empty input |
| `degenerate-ring` | ring without area or with fewer than 3 points |
| `collinear-overlap` | collinear edges or overlap |
| `vertex-touch` | touch at a vertex |
| `hole-touch` | hole touches the shell |
| `self-crossing` | invalid input according to ADR 0003 |
| `near-coincident` | distances in the ULP range |
| `large-coords` | coordinates ≥ 1e6 (UTM-like) |
| `z` | 3D input |
| `m` | input with M values (the result must not depend on M, ADR 0015) |
| `multi` | several parts or paths |
| `nested` | island in a hole |

The test `FixtureCompletenessTests` enforces the catalog and the coverage of every public method.

## Example folders without an oracle

`voronoi/`, `triangulation/`, `terrain/`, `terrain-profile/`, `terrain-sight/`, `terrain-compare/`, `smoothing/` and `events/` hold **examples for the playground** only (tag `playground`), no oracle cases: the playground entries `voronoiDiagram`, `voronoiEdges` and `delaunayEdges` share `voronoi/`, `triangulate` and `constrainedDelaunay` share `triangulation/`, `interpolateZ`, `contour`, `steepTriangles`, `terrainVolume`, `naturalNeighbour`, `refine`, `slopeZones`, `contourBands` and `viewshed` share `terrain/`, `drape` has `terrain-profile/`, `lineOfSight` has `terrain-sight/` and `surfaceVolume` has `terrain-compare/`, `smooth` has `smoothing/`, and `locateEvents` and `intersectEvents` share `events/` (`FixtureStore.SharedFolders`, ADR 0036 to 0041). The folders carry no tag catalog; these methods are tested by properties and against NTS in their own tests.

## External corpora

`samples/external/<source>/<algorithm>/` contains cases imported with `tools/SpatialNET.CorpusConverter`
(tags `external` + source, provenance in the field `source`, license of the source next to it):

- `jts/` – JTS XML test suites (`modules/tests/src/test/resources/testxml/general`), EDL 1.0
- `polygon-clipping/` – end-to-end cases from mfogel/polygon-clipping (`test/end-to-end`), MIT

Only inputs and parameters are imported; the expected value is computed by the NTS oracle at run time.
