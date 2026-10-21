# Predicates: full DE-9IM, public Relate, prepared geometries as a snapshot

All predicates follow the **full DE-9IM model** as in OGC and NTS and compute exactly on the input coordinates (ADR 0005). The core computes the intersection matrix after the pattern of JTS RelateNG with fast paths (envelope, point-in-polygon). Because this matrix is produced anyway for correct `Contains`/`Touches`, `Relate` is public, and all OGC predicates are merely patterns on it. DE-9IM instead of a simplified reading, because it can be checked exactly against the oracle and the hosts and their databases (PostGIS, SQL Server) compute the same way. The intuitive reading "lies inside, boundary counts" is available as `Covers`/`CoveredBy`.

*German version: [0013-praedikate-de9im-relate-prepared.de.md](0013-praedikate-de9im-relate-prepared.de.md)*

## Consequences

- **API:**
  - `Relate(a, b)` returns `IntersectionMatrix`, `Relate(a, b, pattern)` returns `bool`.
  - Predicates: `Intersects`, `Disjoint`, `Contains`, `Within`, `Touches`, `Covers`, `CoveredBy`, `Crosses`, `Overlaps`, `EqualsTopo`.
  - A consequence of DE-9IM: `polygon.Contains(lineOnTheBoundary)` is false, whereas `Covers` is true.
- **Empty geometries:** `Disjoint` is true, all other predicates are false, and `Relate` returns a matrix consisting only of `F`.
- **Invalid input:** point-in-area tests use even-odd (ADR 0003). Results are deterministic, and there is never an exception. Whether the predicates are consistent with one another is then not guaranteed.
- **Boundary rule for lines:** mod-2 only (OGC): end points are boundary, closed paths have no boundary, and for multi-lines only an odd number of path ends counts. An endpoint option can be added later without a break.
- **Prepared geometries:** `Prepare()` returns a prepared geometry with a segment index and a point-in-polygon structure for repeated predicates (Intersects, Contains, Covers, Within, CoveredBy, Disjoint, Touches). For this a **snapshot** of the coordinates is copied, because host geometries are mutable. The open map item "performance" then concerns only a general public index.

## Considered Options

- Simplified semantics ("Contains = nothing lies outside") (rejected: not OGC-conformant, not testable against NTS)
- Only the five predicates from the charter, `Relate` internal (rejected: hardly any savings)
- Endpoint boundary rule or selectable rule (postponed)
- Prepared geometry with reference instead of snapshot (rejected: undefined on changes to the host object)
