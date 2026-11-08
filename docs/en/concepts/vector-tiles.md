# Vector tiles (MVT)

`SpatialNET.IO` writes and reads **Mapbox Vector Tiles** (MVT 2.1): a vector tile cache next to a raster tile cache. It does this **for any tile grid in any coordinate reference system**, not only WebMercator. The host supplies the features, their attributes and the grid; SpatialNET does everything that concerns geometry and the tile format: grid arithmetic, clipping, simplifying, rounding to the tile's integer coordinates, the winding of the rings and the protobuf encoding. Serving the tiles and writing a GL style are left to the host ([ADR 0048](../../adr/0048-vektorkacheln-mvt.md)).

There are no new dependencies: the protobuf codec is written by hand from the [specification](https://github.com/mapbox/vector-tile-spec), and gzip uses `System.IO.Compression`.

## Build a tile

```csharp
// The grid of the tile cache, in any CRS: here EPSG:31256 with the scales of the service at 96 dpi.
var grid = TileGrid.FromScales(-115000, 375000, [500000, 250000, 100000, 50000, 10000], dpi: 96);
Envelope2D tile = grid.TileEnvelope(4, 12, 30);    // level 4, row 12, column 30

// A parcel of 30 x 20 m near the lower left corner of the tile, in world coordinates.
var parcel = new Envelope2D(tile.MinX + 10, tile.MinY + 10, tile.MinX + 40, tile.MinY + 30).ToPolygon();

// One builder per tile; one MVT layer per map layer; the object id becomes the feature id.
var builder = new MvtTileBuilder(grid, level: 4, row: 12, column: 30);
builder.AddFeature("parcels", parcel, [new("name", "Parcel 7"), new("area", 600.0)], id: 7);
byte[] pbf = builder.ToBytes(gzip: true);

MvtTile read = Mvt.Decode(pbf);                   // gzip is detected
MvtFeature feature = read["parcels"]!.Features[0];
Console.WriteLine(feature.Id);                    // 7
Console.WriteLine(feature.ToGeometry().ToWkt());  // POLYGON ((60 3915, 242 3915, 242 4036, 60 4036, 60 3915))
```

A tile is one `.pbf` with one MVT layer per map layer. Layers keep the order of their first feature, and a layer without features is not written; `builder.IsEmpty` tells whether there is anything to store at all. `AddFeature` returns `false` when nothing of the geometry is left in the tile.

## The tile grid

`TileGrid` is pure arithmetic and never projects. It knows the origin, the **origin corner** (`TileOrigin.UpperLeft`: row 0 at the top, as XYZ and WMTS; `TileOrigin.LowerLeft`: row 0 at the bottom, as TMS), the tile size in pixels (width and height separately) and one resolution per level, given directly or as scales with a dpi (`resolution = scale · 0.0254 / dpi`). `TileEnvelope`, `TileAt` and `TileRange` convert between tiles and world coordinates.

`TileGrid.WebMercator()` is the standard grid of Google, OSM and Mapbox (EPSG:3857, origin −20037508.342789244 / 20037508.342789244 upper left, 256 px tiles): level = z, row = y, column = x. Whether a level of your own grid is such a standard zoom decides whether its tiles can be served as z/x/y unchanged (`webGrid` here is a grid built from the WebMercator scales of a service):

```csharp
// Is level 10 of the grid a standard zoom? Then its tiles can be served as z/x/y unchanged.
bool standard = webGrid.TryGetWebMercatorZoom(10, out int zoom);  // True, zoom = 10

// The same for a single scale (1:577791.71 at 96 dpi is zoom 10).
bool fromScale = TileGrid.TryGetWebMercatorZoom(577791.71, out int z);  // True, z = 10
```

Scales in service metadata are often rounded; the default relative tolerance of `1e-6` accepts two decimal places at zoom 10, larger roundings need a larger `relativeTolerance`.

## From world to tile coordinates

`MvtTileBuilder.AddFeature` takes each geometry through these steps (`MvtTileOptions` sets the numbers):

1. **Clip** to the tile plus a buffer (`Buffer`, default 64 of 4096 units), so that lines and outlines do not end visibly at the tile edge. Polygons and lines go through `Clip` ([Overlays](overlay-and-grid.md)); points outside the buffer are dropped.
2. **Simplify** by Douglas-Peucker with a tolerance of about one pixel of the rendered tile (`SimplifyTolerance`, default 1 px; 0 switches it off, `PreserveTopology` uses the topology-preserving variant, see [Simplify](simplify-and-validation.md)).
3. **Transform** affinely into tile coordinates: X from the left edge, **Y down** from the top edge, 0..`Extent` (default 4096). For a non-square tile both axes are scaled separately.
4. **Round** to integers, drop repeated points, and drop parts that collapse: a line with fewer than two points, a ring with fewer than three or without area. A part whose exterior ring collapses goes with its holes.
5. **Orient** the rings as MVT requires: the exterior ring of each part with **positive area** in tile coordinates (clockwise on screen, with Y down), its holes with negative area. A multipolygon is written part by part, each exterior ring followed by its holes.

`MvtTileTransform` is the mapping alone (`ToTile`, `ToWorld`, `BufferedEnvelope`), `builder.ToTileGeometry(g)` shows what a world geometry becomes in the tile, and `feature.ToGeometry(factory, builder.Transform)` converts a decoded feature back into world coordinates.

Rounding to integers can make a valid polygon touch or cross itself at details smaller than one tile unit. This is the usual trade-off of the format and renderers handle it; a tile is a picture, not a data exchange.

## Attributes and values

Attributes are `(key, value)` pairs; `null` values are skipped (MVT has no null), and a repeated key keeps its first value. `MvtValue.From` maps CLR values onto the protobuf fields of the `Value` message: `string`, `bool`, `float`, `double` (and `decimal`), unsigned integers to `uint_value`, signed integers to `uint_value` when ≥ 0 and to `sint_value` when negative, anything else as a string in the invariant culture. `MvtValue.Int(…)` and the other factory methods choose a field explicitly. A decoded value keeps its field, so decoding and encoding again gives the same bytes. Keys and values are deduplicated per layer in order of first use.

## Lower level

`Mvt.Encode` and `Mvt.Decode` work on `MvtTile`, `MvtLayer` and `MvtFeature` directly; a feature carries its geometry as the command integers of the specification (`MvtGeometry.Encode`, `Decode`, `Command`, `ZigZag`). `MvtFeature.Create` builds a feature from a geometry that is already in tile coordinates. The decoder accepts any field order, packed and unpacked repeated fields, skips unknown fields, and reads a version 1 tile whose first ring is an interior ring as an exterior ring. `Mvt.Gzip`, `Mvt.Gunzip` and `Mvt.IsGzip` are the gzip helpers.

The writer uses the field order of the common reference encoders (layer: version, name, features, keys, values, extent; feature: id, tags, type, geometry). The tests reproduce the expectations of the [mvt-fixtures](https://github.com/mapbox/mvt-fixtures) and the examples of the specification byte for byte.
