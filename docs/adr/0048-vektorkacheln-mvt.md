# Vector tiles (MVT) in SpatialNET.IO

gView GIS gets a vector tile cache next to its raster tile cache, for any projection and the scales of its tile service metadata, not only WebMercator; where a level is a standard WebMercator zoom, its tiles must be fully conform to what Mapbox GL and MapLibre expect. Everything that is geometry in this – the grid arithmetic, clipping, simplifying, rounding to the tile's integer coordinates, the winding of the rings – and the tile format itself are not specific to gView. **`SpatialNET.IO` encodes and decodes Mapbox Vector Tiles (MVT 2.1) and builds a tile of any grid from world geometries**; the host supplies the features, the attributes (its object id becomes the feature id) and the grid, and stores and serves the tiles (one `.pbf` per tile, one MVT layer per map layer, gzipped). The GL style and the serving stay in the host.

*German version: [0048-vektorkacheln-mvt.de.md](0048-vektorkacheln-mvt.de.md)*

## Consequences

- **Placement:** new files under `src/SpatialNET.IO/Mvt/`, namespace `SpatialNET.IO`, as the other formats (ADR 0009, ADR 0016). For the package validation and the API tracking this is an addition only (ADR 0028).
- **No dependency:** the protobuf codec (varint, zigzag, fixed32/64, length-delimited) is written by hand from the specification, gzip uses `System.IO.Compression`. A protobuf library or a vector tile package would be the first runtime dependency of the libraries for about 300 lines of code. A projection is never needed: the grid lives in the CRS of the data.
- **API:**
  - `Mvt.Encode(MvtTile)`, `Mvt.Decode(bytes)` (detects gzip), `Mvt.Gzip`, `Mvt.Gunzip`, `Mvt.IsGzip`.
  - The model `MvtTile` → `MvtLayer` (name, version 2, extent 4096) → `MvtFeature` (optional id, geometry type, geometry as command integers, attributes as `(string, MvtValue)` pairs). `MvtFeature.Create` from a tile-coordinate geometry, `ToGeometry(factory, transform?)` back.
  - `MvtValue` keeps the protobuf field of a value (`string`, `float`, `double`, `int`, `uint`, `sint`, `bool`), so that a decoded tile is encoded again into the same bytes; `MvtValue.From(object)` maps CLR values (signed integers ≥ 0 to `uint`, negative ones to `sint`, unknown types as invariant strings).
  - `MvtGeometry`: the command encoding (MoveTo, LineTo, ClosePath, zigzag deltas, a cursor across the parts of a feature).
  - `TileGrid`: origin, origin corner (`TileOrigin.UpperLeft` / `LowerLeft`), tile width and height in pixels, one resolution per level (or scales with a dpi, 0.0254 m per inch); `TileEnvelope`, `TileAt`, `TileRange`; `TileGrid.WebMercator()` and the detection of standard zooms for a level, a scale or a resolution.
  - `MvtTileTransform` (world ↔ tile coordinates of one tile) and `MvtTileBuilder` with `MvtTileOptions` (extent, buffer, simplify tolerance, preserve topology).
- **The pipeline of the builder:** clip to the tile plus buffer (default 64 of 4096) with `Clip` (ADR 0022), simplify by Douglas-Peucker with about one pixel of the rendered tile (ADR 0012), transform affinely to Y down and 0..extent, round to integers, drop repeated points and collapsed parts (a part whose exterior ring collapses goes with its holes), and orient the rings: exterior ring positive area in tile coordinates (clockwise on screen), holes negative, each exterior ring followed by its holes. The part structure comes from the normalized polygon (ADR 0003), so a multipolygon is split correctly. The steps run on a small internal capture factory, independent of `GeometryFactory.Default`.
- **Order of clip and simplify:** clipping first keeps the work proportional to the tile, not to the feature; the edge of the buffer lies outside the visible tile, so a vertex that simplify moves there is not seen.
- **Rounding can make a polygon invalid** at details smaller than one tile unit (touching or crossing rings). There is no `MakeValid` after rounding: the tile is a picture, renderers fill by their own rule, and a repair per feature and tile would cost far more than it shows. This is what the common tilers do.
- **Field order on writing** follows the reference encoders (layer: version, name, features, keys, values, extent; feature: id if set, tags if any, type, geometry if any). Keys and values are deduplicated per layer in order of first use. Layers without features are not written, a tile without layers is zero bytes.
- **Reading is lenient** where the specification allows it: any field order, packed and unpacked repeated fields, unknown fields skipped, version defaults to 1, extent to 4096. A ring with zero area is dropped, an interior ring before any exterior ring is read as an exterior ring (version 1 tiles). Invalid command streams, missing names, odd tags and references beyond the key or value table throw `FormatException`.
- **Non-square tiles:** X and Y are scaled separately to the one extent of the layer.
- **Tests (ADR 0008):** the examples of the specification as command integers in both directions; the expectations of the mapbox/mvt-fixtures (empty tile, point with and without id, the seven value types, deduplication, several layers) reproduced byte for byte with an independent protobuf builder in the test project, one of them as literal bytes; decoding of other field orders, unpacked and unknown fields, and of invalid tiles; round trips; the grid arithmetic of WebMercator and of an EPSG:31256 grid with upper-left and lower-left origin; clipping, buffer, simplification, winding of shells and holes, multipolygon parts, and dropping of what collapses. The fixture files themselves are not copied into the repository.
- **Documentation:** this ADR, the concept page "Vector tiles (MVT)" and glossary entries.
- **Not now:** a GL style, serving, tile seeding, overzoom and label points across tile borders; Z and M (MVT has neither); the MVT 3 draft.

## Considered Options

- Keeping vector tiles in gView (rejected: the geometry steps are generic and belong where the clip and the simplification live; other hosts get them too)
- A protobuf library (Google.Protobuf, protobuf-net) or a vector tile package (rejected: the first runtime dependency of the libraries for a small, stable schema)
- Only the WebMercator grid (rejected: gView serves tile caches in national grids; WebMercator is a special case of the generic grid)
- Simplifying before clipping (rejected: costs in proportion to the whole feature at every tile)
- Repairing polygons after rounding (rejected: see above)
- Plain CLR objects as attribute values (rejected as the only way: the protobuf field of an integer would be lost between decoding and encoding; kept as `MvtValue.From`)
