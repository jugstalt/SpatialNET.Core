# Test strategy: shared WKT fixtures, NTS oracle at run time, two implementations

Test cases live as **one JSON file per case** under `samples/<algorithm>/`. They contain name, description, `tags`, the inputs `a`/`b` as **WKT**, `params`, optionally `crs`, `expected` (with a `divergence` justification) and `tolerance`. Tests **and** the playground read the same files; sample geometries are the cases with the tag `playground`. The expected value is computed by **NTS at run time** (OverlayNG with the same `PrecisionModel` in strict mode, `BufferOp`, `OffsetCurve`, `Polygonize` …). Stored expected values exist only for deliberate, justified deviations. This way every NTS change shows up immediately, no snapshots go stale, and every deviation is documented in the fixture.

*German version: [0008-teststrategie-fixtures-nts-orakel.de.md](0008-teststrategie-fixtures-nts-orakel.de.md)*

## Consequences

- **Structure per algorithm** in `SpatialNET.Tests/<Algorithm>/`:
  - `…FixtureTests`: `[Theory]` over all fixtures against the oracle
  - `…ContractTests`: empty inputs, missing `GeometryFactory.Default`, `SupportsZ`, Z, invalid options, bit-exactly untouched parts for Cut
  - `…PropertyTests`: invariants with **CsCheck**
- **Factory contract test:** thread safety, no reference to passed buffers, empty geometries, `SupportsZ`. Tests always pass the factory explicitly.
- **Tolerances** centrally in `OracleAssert`:
  - Area and length relative 1e-9, symmetric difference ≤ 1e-9·area(expected)
  - Hausdorff distance ≤ 2·gridSize·√2
  - Topology figures, validity and predicates exact
  - Buffer area relative 1e-6
  - A fixture may override individual values. Comparison is never via coordinate equality.
- **Two implementations:** every fixture test runs against the reference implementation **and** against a deliberately awkward third-party implementation in the test project. It has class-based indexers, rings without closing point, shells clockwise, additionally `IReadOnlyList`, and always `IPoint3D` with Z = 0. This exposes assumptions about concrete types.
- **Second reference path for the buffer:** a capsule union, only in the test project.
- **Third-party corpora** (JTS XML suites, EDL 1.0; fixtures from polygon-clipping, MIT) are imported once via a converter tool to `samples/external/<source>/`, with LICENSE and provenance note.
- **Completeness:** a reflection test checks that every public method of SpatialNET.Extensions has fixtures or `[Covers]` tests and covers the **mandatory tag catalog**: `empty`, `degenerate-ring`, `collinear-overlap`, `vertex-touch`, `hole-touch`, `self-crossing`, `near-coincident`, `large-coords`, `z`, `multi`, `nested`. Tags that do not apply are marked `n/a` with a justification. A coverage threshold follows with CI.
- **CRS:** fixtures without `crs` count as local planar; the playground shows them with `L.CRS.Simple`.

## Considered Options

- GeoJSON fixtures (rejected: RFC 7946 requires WGS84, projected coordinates do not fit, corpora are WKT)
- Golden files or snapshots of the NTS results (rejected: they go stale silently)
- FsCheck (rejected in favor of CsCheck: C#-native, without an F# dependency)
