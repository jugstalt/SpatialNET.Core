# SpatialNET.IO: WKT and GeoJSON via the factory, geometries only

SpatialNET.IO reads and writes **WKT** and **GeoJSON** and creates all geometries through the geometry factory when reading (optional parameter, fallback `GeometryFactory.Default`, ADR 0004). The consumers are fixtures and oracles (ADR 0008), the playground (upload, Leaflet) and hosts without geometries of their own. gView and WebGIS have their own GeoJSON support. IO therefore stays deliberately slim: no WKB, no feature model (GeoJSON `properties` are ignored), no CRS evaluation. IO depends on Core **and Extensions**, so that the writer does not have to rebuild the grouping into parts and the orientation. JSON runs through System.Text.Json; there are no further dependencies.

*German version: [0009-io-wkt-geojson-ueber-factory.de.md](0009-io-wkt-geojson-ueber-factory.de.md)*

## Consequences

- **Type mapping:** POINT → `IPoint2D`, MULTIPOINT → `IMultiPoint2D`, (MULTI)LINESTRING → `IPolyline2D`, (MULTI)POLYGON → `IPolygon2D` with parts. The leanest type is written, e.g. POLYGON for a single part.
- **GeometryCollection:** reading a single geometry throws a `NotSupportedException`. In `ReadAll`, or for a `FeatureCollection`, it is flattened. `POINT EMPTY` is read as an empty `IMultiPoint2D` and written as `MULTIPOINT EMPTY`.
- **Z/M/CRS:** Z is read. M is silently dropped *(superseded in part by [ADR 0015](0015-m-werte-mitgefuehrt-nicht-ausgewertet.md): WKT will read and write M)*. A CRS (including the GeoJSON `crs` member of 2008) is ignored.
- **Writing Z** via `ZMode`:
  - `Auto` (default): Z is written if the points are `IPoint3D` and at least one Z is ≠ 0 and not NaN. This is necessary because the hosts always deliver `IPoint3D` with Z = 0.
  - `Always` / `Never`.
- **Orientation:** both writers normalize, shell counter-clockwise, hole clockwise (RFC 7946, ADR 0003).
- **API:**
  - **WKT:** `Wkt.Read`, `Wkt.Read<T>`, `ToWkt(WktOptions?)`
  - **GeoJSON:** `GeoJson.Read`/`ReadAll` (string, `ReadOnlySpan<byte>`, `Stream`), `ToGeoJson(options?)`, `WriteTo(Utf8JsonWriter)`
  - **Number format:** invariant and round-trip exact. `Read(ToWkt(g))` yields bit-identical coordinates.
- **Strictness:**
  - Syntax errors → `FormatException` with position or JSON path
  - Geometrically invalid but syntactically correct input is accepted (ADR 0002/0003)
  - `EMPTY` becomes an empty geometry

## Considered Options

- WKB in v1 (postponed: no consumer, can be added later without a break; planned after M values, see [ADR 0015](0015-m-werte-mitgefuehrt-nicht-ausgewertet.md))
- Feature model with properties (rejected: new domain concept, belongs to the hosts)
- IO on Core only, hole grouping rebuilt (rejected: duplicated logic)
