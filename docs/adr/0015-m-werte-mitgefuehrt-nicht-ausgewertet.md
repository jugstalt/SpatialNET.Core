# M values: carried and interpolated like Z, never evaluated by the geometry algorithms

SpatialNET gets **M values** (a measure attached to a vertex, for example the distance along a road). M is **carried, not computed with**: all algorithms stay planar 2D, and M never influences a result's X/Y (nor Z). Where an algorithm creates a vertex, M is interpolated the way Z already is. This reverses the exclusion of M from the charter (map #1, ADR 0009) because the hosts (gView) store M, WKB interchange needs it, and linear referencing (planned) is only useful with it. The reversal happens now, in the preview phase, because it changes `Coordinate`.

*German version: [0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md](0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)*

## Consequences

- **Core:**
  - `Coordinate` gets a fourth positional parameter: `Coordinate(X, Y, Z = NaN, M = NaN)`, with `HasM`. `NaN` means "no M". This is a binary break of the constructor, accepted in 0.x (package validation needs a suppression for it).
  - `IPointM : IPoint2D` with `double M { get; }`. It is independent of `IPoint3D`: a point may implement either or both, and algorithms test the two interfaces separately (as `GeometryAccess` does for Z). There is no combined `IPointZM`.
  - `IGeometryFactory.SupportsM` is a default interface member returning `false`, so existing host factories keep compiling. If it is `false`, algorithms drop M from their results (as for `SupportsZ`).
- **Host contract:** as for Z, a class that implements `IPointM` reports its `M` as valid, including 0. A host that cannot tell "no M" from 0 (gView's `Point` always stores `M = 0.0`; only `IGeometryDef.HasM` knows) must not implement `IPointM` on that base class. It implements it on an M-capable subclass or wrapper that it uses where `HasM` is true. This goes into the host integration guide.
- **Interpolation:**
  - At a new vertex, M comes from the **source segment** of the result edge, linearly by the parameter along it, exactly as Z does in the noder. If several edges meet in one grid pixel, the first one in the noder's deterministic order wins.
  - M is interpolated **only if both ends of the segment have M**. Otherwise the new vertex has no M. Nothing is guessed, and 0 is never substituted.
  - Original vertices keep their M, even if the other end of the segment has none (the noder takes the segment's own end points' M directly). Z still follows the older rule of ADR 0005 and is not changed by this ADR.
- **Algorithms:** overlays (`Intersection`, `Union`, `Difference`, `SymDifference`), `Cut`, `Simplify` and `OffsetCurve` carry M like Z. `Buffer` and `ConvexHull` produce no M, just as `Buffer` produces no Z (ADR 0006). `Centroid` returns a `Coordinate?` without M (as without Z, ADR 0012). Predicates and measures ignore M.
- **IO:** WKT reads and writes `M` and `ZM` (`POINT M (1 2 3)`, `POINT ZM (1 2 3 4)`). GeoJSON stays without M, so that exports remain standard (RFC 7946); whoever needs M uses WKT and, later, WKB.
- **Tests (ADR 0008):** the NTS oracle does not interpolate M in overlays, so M is not checked against it. Instead:
  - hand-written expectations (original M is kept, M at new vertices lies between the ends of the source segment, no M if an end has none);
  - a **invariant property test**: the same operation with and without M yields identical X/Y (and Z). This is the strongest guarantee that M never affects the computation and needs no oracle;
  - a new mandatory fixture tag `m` (one `m.json` per algorithm: input with M, X/Y compared against the oracle as for `z`); the foreign implementation runs these fixtures too, but drops M because its points never implement `IPointM`. The M-capable test geometries for the propagation tests are separate (`tests/SpatialNET.Tests/MValues`).
- **Delivery:** in three stages (ADR and core; extensions and tests; IO, documentation), but **one release** (`0.3.0-preview.1`), so that no intermediate state with half an M lands on NuGet.
- **Follow-ups that depend on this:** WKB (reading ISO and EWKB, writing ISO) and linear referencing (`Interpolate`, `Project`, `Substring` by length, plus `...ByM` variants). Each gets its own ADR.

## Considered Options

- Keep M out and let hosts carry it themselves (rejected: WKB and linear referencing would be unusable for gView data)
- M as a separate coordinate type next to `Coordinate` (rejected: doubles the factory and algorithm paths; one value type with `NaN` for "none" is consistent with Z)
- `IPointZM` and further combined interfaces (rejected: larger interface surface; testing two small interfaces separately is enough)
- A `HasM` flag on `IPointM` (rejected: enlarges the contract without solving the host problem; the contract "implements it, so it counts" is the same as for Z)
- `M == 0` meaning "no M" (rejected: 0 is a valid measure, linear referencing often starts there)
- Averaging M where two operands meet at one vertex (rejected: the result edge belongs to one input edge, whose interpolated M is the natural value)
- M in GeoJSON as a fourth position element (rejected: not standardised, exports should stay interoperable)
- Algorithms that evaluate M (for example filtering by M) in this step (rejected: keeps the 2D core untouched; linear referencing is a separate API on top)
