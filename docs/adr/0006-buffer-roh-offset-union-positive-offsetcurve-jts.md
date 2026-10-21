# Buffer as raw offset + union(Positive), OffsetCurve after the JTS pattern

The buffer is built from the raw offset of each ring or line (with joins and caps), which is then cleaned up with `Union(FillRule.Positive)` in our own overlay (ADR 0005). The offset curve follows the pattern of JTS `OffsetCurve`: buffer with flat ends, raw offset of the desired side, matching against the buffer boundary. There is no separate trimming logic for self-intersections. This leaves only one code path in the product, built on the overlay core, and the behavior stays close to the NTS oracle. Defaults and parameters are therefore deliberately JTS-compatible.

*German version: [0006-buffer-roh-offset-union-positive-offsetcurve-jts.de.md](0006-buffer-roh-offset-union-positive-offsetcurve-jts.de.md)*

## Consequences

- **API:** `Buffer(this IGeometry2D, double distance, BufferOptions? options = null, IGeometryFactory? factory = null, double? gridSize = null)` always returns `IPolygon2D`. `OffsetCurve` exists on `IPolyline2D` and `IPolygon2D` with `OffsetOptions`, and always returns `IPolyline2D`. The options are records with `init` properties and `static Default`. Both share the join parameters.
- **Defaults (as in JTS):** join `Round`, cap `Round`, `MitreLimit = 5`, `QuadrantSegments = 8`, input simplifier 0.01·|d|, `SingleSided` optional. An optional `ArcTolerance` (maximum chord deviation in map units) takes precedence over `QuadrantSegments` when set.
- **Offset direction:** for polylines, positive means left in the direction of travel. For polygons, positive means outwards and negative inwards, **regardless of ring orientation** (which ADR 0003 ignores). The result is the boundary of `Buffer(d)` as closed paths. An optional joined mode connects pieces into one path.
- **Negative distance** on polygons means buffering **inwards**: the polygon is shrunk by |d|. Narrow parts may disappear or it may split into several parts.
- **Edge cases:**
  - Polygon disappears completely under a negative buffer → empty polygon
  - Line or point with d ≤ 0 → empty polygon
  - Polygon with d = 0 → input normalized by even-odd
  - Invalid input is interpreted by even-odd, without an exception
  - Exceptions only for `NaN`/`Infinity` as distance or invalid options
- **Z:** buffer results are 2D. Offset curves interpolate Z on the respective source segment (if `SupportsZ`).
- The **capsule union** (one capsule per segment, everything united) exists only in the test project, as a second reference path next to NTS.

## Considered Options

- Capsule union as an internal fallback (rejected: snap rounding throws no topology exceptions, ADR 0005)
- JTS `BufferBuilder` with depth labelling (remains at most an optimization, not a subsystem of its own)
- Minkowski sum, straight skeleton (rejected for buffer)
- Options as individual optional arguments (rejected: long signatures, every new option breaks)

Research: #12 (`research/buffer-offset`).
