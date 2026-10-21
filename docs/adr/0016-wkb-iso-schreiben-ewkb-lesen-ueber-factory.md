# WKB: read ISO and EWKB, write ISO, through the factory

`SpatialNET.IO` gets **WKB** (`Wkb` next to `Wkt` and `GeoJson`), because hosts with a database (PostGIS, SQL Server, GeoPackage blobs) exchange geometries in this format, and gView already has a reader. It follows the pattern of ADR 0009: reading creates all geometries through the geometry factory, there is no feature model, and a CRS is not evaluated. Reading accepts **ISO WKB and EWKB** (the PostGIS flavour with flags), writing produces **ISO WKB** in little endian. Z and M are supported (ADR 0015). This lifts the exclusion of WKB from ADR 0009; the rest of that ADR stands.

*German version: [0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.de.md](0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.de.md)*

## Consequences

- **Placement:** `SpatialNET.IO`, namespace `SpatialNET.IO`. No new package, no new dependency (`System.Buffers.Binary` is part of the framework). `SpatialNET.Core` stays contracts only. For the package validation this is an addition only.
- **API** (the same shape as `Wkt`, ADR 0009):
  - `Wkb.Read(ReadOnlySpan<byte>, IGeometryFactory? factory = null)` returns `IGeometry2D`, `Wkb.Read<T>(…)` expects a kind, `Wkb.ReadAll(…)` returns `IReadOnlyList<IGeometry2D>`.
  - `ToWkb(this IGeometry2D, WkbOptions? options = null)` returns `byte[]`. `WkbOptions` has `ZMode` and `MMode` with the same meaning as in `WktOptions`.
  - No stream, hex or text overloads. A caller that has a `Stream` or a hex string converts it first; this keeps the surface small and can be extended without a break.
- **Reading:**
  - **Formats:** ISO WKB (type codes 1–6 plus 1000/2000/3000 for Z/M/ZM) and EWKB (flag bits `0x80000000` Z, `0x40000000` M, `0x20000000` SRID). Both byte orders. The byte order is read per geometry header, so nested parts of a multi geometry may differ.
  - **Types:** Point, LineString, Polygon, MultiPoint, MultiLineString and MultiPolygon map as for WKT (ADR 0009): a point to `IPoint2D`, a point with NaN coordinates (the WKB form of `POINT EMPTY`) and a MultiPoint to `IMultiPoint2D`, line strings to `IPolyline2D`, polygons to `IPolygon2D`.
  - **GeometryCollection** is rejected by `Read` with `NotSupportedException` and flattened by `ReadAll`, exactly as in WKT and GeoJSON. There is no geometry collection in the model (ADR 0002).
  - **Curves** (CircularString, CompoundCurve, CurvePolygon, MultiCurve, MultiSurface), other surfaces, TIN and triangles throw `NotSupportedException`. The hosts linearise curves themselves (map #1), and the type code is named in the message.
  - **SRID** in an EWKB header is read and **discarded**, the same line as for the CRS in GeoJSON.
  - **Z and M** are kept if the factory supports them (ADR 0015), otherwise dropped. A coordinate that is `NaN` in a Z or M slot means "none".
  - **Invalid input never crashes:** truncated data, an unknown byte order or type, and trailing bytes after the geometry throw `FormatException` with the byte offset. A count (points, rings, parts) that cannot fit into the remaining bytes is rejected before anything is allocated, so a forged header cannot cause an out-of-memory error. Geometrically invalid but syntactically correct input is passed to the factory unchanged, as for WKT.
- **Writing:**
  - ISO WKB, little endian, leanest type as for WKT: a polygon of one part is a Polygon, several parts a MultiPolygon, one path a LineString, several a MultiLineString, a multipoint stays a MultiPoint. A single point is a Point; an empty multipoint is a MultiPoint with zero points, so the writer never has to emit the NaN point.
  - Rings are closed, shells counter-clockwise, holes clockwise, degenerate rings dropped (the same normalisation as the WKT writer, so both agree on the structure of the result).
  - Z follows `ZMode` (`Auto` writes Z only if some point has a height other than 0, because hosts such as gView always deliver `Z = 0`), M follows `MMode` (written if some point has M; points without M get NaN).
  - Non-finite X or Y throws `ArgumentException`, as in the GeoJSON writer, because most consumers cannot read them back, and a NaN pair would collide with the marker of the empty point.
  - No SRID is written. A caller that needs EWKB or a GeoPackage header builds it around the ISO bytes.
- **Origin of the code:** gView's reader (`gView.Framework.OGC`, same owner, Apache-2.0) is the starting point for the format handling, but the code is rewritten against `Coordinate` and the factory, so that no gView type enters the library. Its curve and SRID handling is replaced by the rules above. No entry in `THIRD-PARTY-NOTICES.md` is needed, because it is the same copyright holder.
- **Tests (ADR 0008):** fixtures do not apply, because the result is bytes and not a geometry from an algorithm.
  - Oracle: the NTS `WKBReader` and `WKBWriter`, in both directions. What we write, NTS reads to the same coordinates, and what NTS writes (including EWKB with SRID, big endian, Z and M), we read. Z and M are compared through the ordinates NTS reports.
  - Property tests (CsCheck): round trip `ToWkb` → `Read` over random geometries with Z, M and empty parts, for the reference implementation and the foreign implementation; WKB agrees with WKT on the normalised structure.
  - Hand-written cases: both byte orders, nested byte orders, EWKB flags with and without SRID, the NaN point, every rejected type, truncated and forged input (counts, offsets), M of zero is a measure.
  - The `Covers` rule of ADR 0008 is met by naming `Wkb.Read`, `Wkb.ReadAll` and `ToWkb` in these tests.
- **Playground:** unchanged; WKB is not an input format there.

## Considered Options

- Keep WKB out and let hosts use their own readers (rejected: every host with a database needs one, and gView's already exists; the IO package is the place for formats, ADR 0009)
- Write EWKB (rejected: ISO is the standard and PostGIS reads it; EWKB with SRID would reintroduce a CRS that IO deliberately does not carry)
- Read ISO only (rejected: PostGIS `ST_AsEWKB` and many libraries still produce EWKB)
- Keep the SRID (rejected: it needs a place in the model; the `crs` of GeoJSON is dropped for the same reason)
- Convert curves to line strings on reading (rejected for now: needs a tolerance parameter and its own decision; hosts linearise already)
- Stream, hex and `byte[]`-only overloads on day one (rejected: a larger surface for conveniences that a caller adds in one line; can be added later without a break)
- Take the gView code unchanged (rejected: it creates gView types and has no factory)
- A separate package `SpatialNET.IO.Wkb` (rejected: no dependency justifies it; ADR 0014 argued the same for the index)
