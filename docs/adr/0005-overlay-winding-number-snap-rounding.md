# Overlay: OverlayNG-style pipeline with winding numbers and snap rounding

All overlay operations run through **one** core following the pattern of JTS OverlayNG: snap-rounding noding of all edges, planar graph, labelling of every face, selection by operation, construction of rings/parts or lines. Unlike OverlayNG, every face gets **one winding number per operand**, and a selectable **fill rule** (EvenOdd, NonZero, Positive, Negative) decides from it between inside and outside. Only this way can self-crossing input (ADR 0003, even-odd) and the buffer as `Union(FillRule.Positive)` over raw offsets (#12) be served by the same core. Constructed intersection points are always rounded by **snap rounding to a grid**, predicates compute exactly (Shewchuk `orient2d`). Output coordinates therefore lie on the grid, but the topology is provably consistent. This is the prerequisite for the guarantee "results are valid" from ADR 0003.

*German version: [0005-overlay-winding-number-snap-rounding.de.md](0005-overlay-winding-number-snap-rounding.de.md)*

## Consequences

- **Grid size:** `double? gridSize = null` → `SpatialNET.Precision.DefaultGridSize` (static, initially `null`) → automatic from max|coordinate| of both inputs · 1e-12, rounded to a power of ten. This is the same cascade pattern as for the factory (ADR 0004).
- **Strict semantics as in OverlayNG strict mode:** results are dimension-homogeneous. Touch and collapse remnants of lower dimension are dropped (e.g. polygon ∩ polygon touching along an edge = empty polygon).
- **Predicates** (Intersects, Contains …) compute exactly on the input coordinates, without a grid. For near-touches below the grid size, `Intersects` can be true while `Intersection` is empty. This is documented and the same in NTS.
- **FillRule** is public as an optional parameter on all overlay operations, default EvenOdd. `Union(fillRule: NonZero)` merges, e.g., overlapping shells from host data.
- **Z** at new nodes is interpolated via the parameter t on the unrounded original segment. At crossings the Z of the first operand applies, or that of the only 3D operand.
- **Build order:**
  1. Precision core (`orient2d` port, snap-rounding noder, **internal** segment index)
  2. Line/polygon, line/line, Cut
  3. Polygon/polygon with winding labelling (after that, Buffer can start)
  4. Optimizations (envelope prefilter, possibly a floating fast path without API change)
- **Tests:** The NTS oracle uses OverlayNG with the same `PrecisionModel` in strict mode. Comparison is via validity, area, symmetric difference, Hausdorff distance and topology figures, never via coordinate equality.

## Considered Options

- Martinez-Rueda-Feito 2013 plus a separate line clip (rejected: two code paths, fill rules and buffer hard to connect)
- Vatti/Clipper re-implementation (rejected: no line/line, high effort)
- Floating point with a fallback cascade like `OverlayNGRobust` or with epsilon (rejected as default: no provable validity, two stages hard to test)
- Full OGC semantics with touch lines/points (rejected: mixed results, contradicts ADR 0004)

Research: #11 (`research/overlay-algorithmen`), #12 (`research/buffer-offset`).
