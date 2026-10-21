# Buffer and offset algorithms: options

*German version: [buffer-offset.de.md](buffer-offset.de.md)*

Research for [#12](https://github.com/jugstalt/SpatialNET.Core/issues/12). Framework: [ADR 0001](../docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.md) – all algorithms are implemented in-house against the read-only **geometry interfaces**, no NetTopologySuite/Clipper in the lib (NTS only as test oracle), target `net10.0`, planar 2D. Result geometries are created through the **geometry factory** of the **host**.

Closely related: [#11 Overlay algorithms and precision models](https://github.com/jugstalt/SpatialNET.Core/issues/11) and [#5 Decide the overlay algorithm](https://github.com/jugstalt/SpatialNET.Core/issues/5) – practically every serious buffer approach needs part of the overlay infrastructure.

---

## 1. Terms and semantics

| Term | Meaning |
|---|---|
| **Buffer(G, d)**, d > 0 | The set of all points with distance ≤ d to G = Minkowski sum of G with a disk of radius d. The result is always a (multi)polygon. |
| **Buffer(P, d)**, d < 0 | Erosion: all points of P that are at least \|d\| away from the boundary (Minkowski difference). Meaningful only for areal input; lines/points → empty. Can split polygons or make them vanish completely. |
| **Offset curve** (parallel line) | A one-sided line at distance d to the left (d > 0) or right (d < 0) of a polyline. The result is a *line*, not a polygon. No hard mathematical definition for loops/self-intersections (see §4). |
| **Single-sided buffer** | The polygon between a line and its offset curve (JTS `BufferParameters.setSingleSided`). |
| **Join style** | Treatment of *convex* corners (outside): **round** (circular arc), **mitre** (intersection of the extended offset segments, limited by the **mitre limit**, otherwise beveled), **bevel** (direct connection of the segment ends). Concave corners (inside) are treated the same for all styles – overlaps arise there that have to be cleaned up. |
| **Cap style** | Ends of open lines: **round** (semicircle), **flat/butt** (cut off perpendicular at the end point), **square** (extended by d, rectangular). |
| **Quadrant segments** | Number of chords per quarter circle in the arc approximation (JTS default 8). Alternative parametrization: **arc tolerance** (maximum chord deviation, Clipper2). From tolerance t follows the segment angle θ = 2·acos(1 − t/d). |

Planar consequence: a buffer in geographic coordinates (degrees) is distorted – that is a matter for the host or the projection (#8), not for the algorithm.

---

## 2. Building blocks that all approaches need

1. **Offset segment generator**: for each segment the segment shifted in parallel by d, join geometry at corners depending on the turning direction (cross product), cap geometry at line ends. Pure local geometry, ~300–600 LOC, easy to test.
2. **Arc approximation** (circle, semicircle, corner arc) with quadrant segments or arc tolerance – choose the same support points as NTS, otherwise oracle comparisons are possible only via area/Hausdorff tolerance.
3. **Cleanup**: the raw offset overlaps itself (at concave corners, narrow constrictions, for d > local radius of curvature). This cleanup is *the actual effort* – and it is exactly what distinguishes the approaches.

---

## 3. Approaches

### A. Raw offset curve + noding + depth/winding polygonization (JTS `BufferBuilder`)

**Principle** ([JTS buffer package](https://github.com/locationtech/jts/tree/master/modules/core/src/main/java/org/locationtech/jts/operation/buffer)):

1. `BufferCurveSetBuilder` creates closed **raw offset rings** per component (`OffsetCurveBuilder` / `OffsetSegmentGenerator`): point → circle; line → one ring running left forth, cap, right back, cap; polygon → shell and holes, each one-sided, orientation chosen so that erosion goes inwards for negative d. Each edge carries a *depth marking* (left/right: inside/outside).
2. **Concave corners**: instead of intersecting the two offset segments exactly, the curve is routed via the original vertex ("closing segment"). This deliberately creates small loops that drop out later, but avoids gaps from rounding errors.
3. **Prefilter**: `BufferInputLineSimplifier` removes concavities smaller than a fraction of d (default `simplifyFactor` = 0.01·d) – drastically fewer intersection points for noisy lines. Completely eroded rings (e.g. bounding rectangle narrower than 2·|d|) are discarded beforehand.
4. **Noding** of all raw curves (`MCIndexNoder`, monotone chains), followed by construction of a planar graph.
5. **Depth computation**: `BufferSubgraph`/`RightmostEdgeFinder`/`SubgraphDepthLocater` determine, for each connected subgraph, the depth starting from the outermost edge; edges with a depth transition 0↔1 form the boundary. This is equivalent to "winding number ≥ 1 is inside".
6. `PolygonBuilder` assembles maximal/minimal edge rings into polygons with holes.
7. **Robustness fallback** (`BufferOp`): if the floating noding throws a `TopologyException`, the computation is repeated with stepwise reduced precision and a **snap-rounding** noder.

**Assessment**: industry standard (JTS, GEOS, NTS, hence PostGIS/Shapely/QGIS). All join/cap styles, negative distances, single-sided buffer. Steps 4–6 are *exactly* the overlay infrastructure (noding, planar graph, ring construction, hole assignment). High effort, but almost completely shared with the overlay.

### B. Clipper offset principle (`ClipperOffset`)

**Principle** ([Clipper2 ClipperOffset](https://angusj.com/clipper2/Docs/Units/Clipper.Offset/Classes/ClipperOffset/_Body.htm), source [`clipper.offset.cpp`](https://github.com/AngusJohnson/Clipper2/blob/main/CPP/Clipper2Lib/src/clipper.offset.cpp)):

1. For each path an offset contour is built (`DoRound`, `DoMiter`, `DoSquare`, `DoBevel`). Concave corners are likewise routed via the original vertex; according to a source comment, this creates artifacts that are removed "later by the finishing union operation".
2. Open paths (`EndType.Butt/Square/Round/Joined`) are turned into a closed outline contour (both sides + caps).
3. **A single union step** with fill rule **Positive** (or Negative for inverted orientation) over all raw contours. Contours and loops with winding ≤ 0 drop out.
4. Coordinates are integers (`Path64`); the floating-point API scales internally. Mitre limit and arc tolerance are parameters; the default arc fineness results from the arc tolerance relative to d.

This is a direct implementation of **Chen & McMains (2005)** "Polygon Offsetting by Computing Winding Numbers" ([PDF](https://mcmains.me.berkeley.edu/pubs/DAC05OffsetPolygon.pdf)), which is also cited in the Clipper1 sources: build the raw offset curve so that all invalid loops have winding number ≤ 0, then apply the positive winding rule. Complexity O((n+k) log n), k = self-intersections.

**Assessment**: conceptually simpler than A, because no per-edge depth marking is needed – only "union with fill rule Positive". Prerequisite: our own overlay can evaluate **self-overlaps with a fill rule** (non-zero/positive), not just Boolean ops of two valid polygons. Clipper hints: input polygons should be cleaned up beforehand by a union; remove very short segments beforehand (`SimplifyPaths`), otherwise "blemishes".

### C. Minkowski sum (convolution / convex decomposition)

**Principle**: buffer = P ⊕ D, D = regular n-gon as a circle approximation.
- *Convex decomposition*: decompose P into convex parts, for each part a convex Minkowski sum in O(n+m) (sorting edges by angle), then the union of all parts.
- *Convolution* ([Wein 2006, "Exact and Efficient Construction of Planar Minkowski Sums using the Convolution Method"](https://doi.org/10.1007/11841036_73); in [CGAL Minkowski_sum_2](https://doc.cgal.org/latest/Minkowski_sum_2/index.html)): form the convolution cycle of the edges of P and D, then keep regions with winding number > 0 – structurally the same as B.

**Assessment**: elegant formalism, but natively delivers only **round** (or the shape of D). Mitre/bevel, flat/square caps and negative distance (Minkowski difference = complement trick) fit poorly. For a buffer with a circle, the convolution degenerates practically to approach B. CGAL is GPL/commercial – usable only as literature. Own added value is low; Minkowski pays off only if SpatialNET wants to offer general Minkowski sums (e.g. collision, configuration space).

### D. Segment-wise capsule union ("stadium union")

**Principle**: each segment → capsule (rectangle + two semicircles, Minkowski sum segment ⊕ circle), point → circle. Buffer(line) = union of all capsules. Buffer(polygon, d > 0) = P ∪ capsules of all boundary segments. Buffer(polygon, d < 0) = P \ (union of the capsules of all boundary segments with |d|).

Variants: unite capsules tree-like (cascaded union, like JTS `CascadedPolygonUnion`/`UnaryUnionOp`) so that one does not unite n times against a growing result.

**Assessment**:
- + Mathematically trivially correct for **round/round**; each partial operation is an ordinary overlay – 100 % reuse, no cleanup code of its own.
- + A very good **reference/fallback path** and test oracle *inside* the lib (e.g. for comparison with approach A/B).
- − Naturally only round join. Mitre/bevel need additional wedges/triangles at convex corners (feasible, but fiddly); flat/square caps by capsule variation at the end segments.
- − Adjacent capsules share almost identical arcs → many nearly collinear, nearly coincident edges. This is a **stress test for overlay robustness** (exactly the cases on which floating overlays fail) and produces many support points. Performance clearly worse than A/B (factor 5–50 depending on the overlay).
- − Result vertices deviate from NTS (different arc support points at corners), oracle comparison only via tolerance.

### E. Straight skeleton / Voronoi-based offsets

**Principle**:
- **Straight skeleton** ([Aichholzer, Aurenhammer et al. 1995, "A Novel Type of Skeleton for Polygons"](https://www.jucs.org/jucs_1_12/a_novel_type_of.html)): a wavefront whose edges move inwards in parallel at constant speed; edge and split events change the topology. The offset at distance d is the cut of the wavefront at time d – corresponds to a **mitre offset without mitre limit**. Implementations: Felkel & Obdržálek 1998 (known to be error-prone), [Palfrader/Held/Huber 2012 "On Computing Straight Skeletons by Means of Kinetic Triangulations"](https://doi.org/10.1007/978-3-642-33090-2_66), [CGAL Straight_skeleton_2 incl. polygon offsetting](https://doc.cgal.org/latest/Straight_skeleton_2/index.html) (GPL/commercial, author F. Cacciola).
- **Voronoi diagram of segments / medial axis** (Held 1998, "Voronoi diagrams and offset curves of curvilinear polygons", CAD 30(4); Held: [VRONI / Voronoi project](https://www.cosy.sbg.ac.at/~held/projects/voronoi_2d/voronoi.html)): delivers exact **round** offsets; offsets for many d without recomputation (typical for CNC pocket milling).

**Assessment**: algorithmically the hardest option. Robustness (split events, simultaneous events, parallel edges, degeneracies) is a research topic of its own; usable implementations are found almost only in CGAL/VRONI, neither of which can be taken over. Natural use: many *inner* offsets of the same polygon, roof shapes, center lines. Oversized for GIS buffers (positive, lines with caps, multi-geometries, round as default). No overlay needed, but also no reuse of the overlay – a second large robustness construction site. **Not recommended** for the SpatialNET buffer; at most later as an algorithm of its own, "StraightSkeleton/MedialAxis".

### Further references with the same basic pattern

- **Boost.Geometry `buffer`** ([docs](https://www.boost.org/doc/libs/latest/libs/geometry/doc/html/geometry/reference/algorithms/buffer/buffer_7_with_strategies.html), BSL-1.0, Barend Gehrels): builds "pieces" per segment/corner/end (strategies for distance, side, join, end, point), computes turns (intersection points) between pieces, classifies turns by `turn_in_piece_visitor`/`turn_in_input` as inside/outside and traverses as in the overlay. A hybrid of A and D.
- **georust `geo` `Buffer`** ([buffer.rs](https://github.com/georust/geo/blob/main/geo/src/algorithm/buffer.rs), MIT/Apache-2.0): delegates to [iOverlay](https://github.com/iShape-Rust/iOverlay) (Apache-2.0) `OutlineOffset`/`StrokeOffset` + `unary_union` – thus principle B with an integer overlay.

---

## 4. Offset curves for polylines

### Naive approach
Form offset segments of one side, joins only on the outside, intersect neighboring segments on the inside. Fails as soon as d is larger than the length of short segments or the local radius of curvature (loops, "swallowtails"). Self-intersection trimming (Tiller & Hanson 1984, "Offsets of Two-Dimensional Profiles", IEEE CG&A; Choi & Park 1999, "A pair-wise offset algorithm for 2D point-sequence curve", CAD 31) removes local loops, while global interferences (constrictions of the line, self-intersections of the input) remain unsolved.

### Buffer-derived (old: GEOS `OffsetCurve`/PostGIS `ST_OffsetCurve` before 2022)
Compute a single-sided buffer or buffer, compute the raw offset curve, **intersect** both and keep the matching parts. Problem: buffer heuristics (simplifier, snap rounding) let the buffer boundary and the raw curve diverge minimally → broken cuts, torn or additional pieces. Martin Davis 2018: "nothing 'simple' about parallel offset curves" ([jts-dev](https://www.eclipse.org/lists/jts-dev/msg00225.html)).

### JTS `OffsetCurve` (new, end of 2021/2022)
Introduced with [JTS PR #810](https://github.com/locationtech/jts/pull/810) (Dec 2021), described in the blog post ["JTS Offset Curves" (Jan 2022)](http://lin-ear-th-inking.blogspot.com/2022/01/jts-offset-curves.html), extended in [PR #956](https://github.com/locationtech/jts/pull/956) (Feb 2023, multi result + "joined mode"), mitre joins for polygons in [PR #1109](https://github.com/locationtech/jts/pull/1109) (2025). GEOS port in 3.11; source [`OffsetCurve.java`](https://github.com/locationtech/jts/blob/master/modules/core/src/main/java/org/locationtech/jts/operation/buffer/OffsetCurve.java).

Algorithm:
1. Compute the buffer of the line with distance |d| (normal, robust buffer).
2. Compute the raw offset curve of the desired side.
3. **Match** raw curve segments to segments of the buffer boundary via a spatial index and a **distance tolerance** (no exact intersection!).
4. The output consists of the sections of the buffer boundary that match the raw curve – ordered along the raw curve, main curve first; direction like the input.

Differences from buffer-derived intersecting:
- The result **lies guaranteed on the buffer boundary** → consistent with the buffer, exact distance d (up to quantization), never self-intersecting.
- Robust, because of tolerance matching instead of fragile intersection point computation.
- With self-intersections or constrictions < 2d: since #956 a **MultiLineString** with disjoint sections (possibly rings); in "joined mode" one continuous line, which may then self-intersect and, for large d, have "flat-line artifacts".
- Consequence: offset is *not a geometry core of its own*, but buffer + raw offset generator + matching (~300 LOC additional). Whoever has a buffer gets a robust offset almost for free.

Clipper2 has **no** one-sided offset curve (open paths only as a closed outline, negative distance for open paths not permitted).

---

## 5. Robustness – typical pitfalls

| Problem | Cause | Remedy |
|---|---|---|
| Narrow concavities / constrictions < 2·\|d\| | The raw curve folds, many self-intersections | Cleanup via winding/depth (A/B); upfront simplification of small concavities (JTS `BufferInputLineSimplifier`) |
| Collapse with negative buffer | A ring or part vanishes; the raw ring reverses orientation | Discard rings with bounding rectangle < 2·\|d\| beforehand; detect inverted raw rings (JTS `isRingCurveInverted`); create an empty result cleanly through the factory |
| Splitting with negative buffer | The polygon falls apart into several | Always treat the result as a multipolygon; hole assignment by polygonization |
| Tiny segments, duplicate points | Offset normals numerically unstable, micro loops | Remove duplicates; skip/merge segments < ε·d (Clipper `SimplifyPaths`, JTS fix in 1.20 for repeated points) |
| Nearly collinear neighboring segments | Mitre intersection point at infinity | Collinearity test with tolerance, then continue without join; enforce mitre limit |
| Sharp angles with mitre | Mitre tip very long | Mitre limit (JTS default 5.0); beyond it bevel or truncated tip |
| Noding errors (floating point) | Intersection points not consistent | Snap rounding, retry with reduced precision (JTS `BufferOp`) or integer coordinates (Clipper) – decision belongs to #11/#5 |
| Large d relative to the geometry | Arcs dominate, many vertices | Quadrant segments/arc tolerance configurable; convex hull as a shortcut for very large d possible |
| Invalid input (self-intersecting polygons) | Undefined buffer | Validate/"make valid" beforehand or documented behavior (JTS: `buffer(0)` as a repair trick) |

---

## 6. Building blocks shared with the overlay

| Building block | Overlay | A JTS | B Clipper | C Minkowski | D Capsules | E Skeleton |
|---|---|---|---|---|---|---|
| Segment intersection, orientation predicate | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (other predicates) |
| Noding (monotone chains / sweep, snap rounding) | ✓ | ✓ | ✓ | ✓ | ✓ (via overlay) | – |
| Planar graph / edge ring construction | ✓ | ✓ | ✓ | ✓ | ✓ | own graph |
| Winding number / depth labelling | ✓ (Bool ops = labelling) | ✓ depth | ✓ fill rule Positive | ✓ | ✓ | – |
| Union of several / self-overlapping rings | Cascaded union | implicit | ✓ central | ✓ | ✓ central | – |
| Hole assignment, polygon assembly | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Spatial index (STR tree, monotone chains) | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Offset segment generator, arcs, joins, caps | – | ✓ | ✓ | (circle polygon) | (capsule shape) | – |

Key statement: if the overlay (#5/#11) is built so that it **(1)** nodes arbitrary edge sets and **(2)** labels areas by **winding number with a selectable fill rule**, then buffers by approach B (and also A) are essentially "offset segment generator + `Overlay.Union(rawRings, FillRule.Positive)`". This is the most important requirement flowing back from this ticket into the overlay design.

---

## 7. Effort (rough, without overlay core)

Assumption: a robust overlay with noding and winding labelling exists (own effort in #5/#11).

| Component | Effort |
|---|---|
| Offset segment generator incl. round/mitre/bevel, caps round/flat/square, quadrant segments, mitre limit | 3–5 PD |
| Buffer facade for point/multipoint/line/polygon/multi*, orientation, negative distance, collapse prefilter, empty results | 2–4 PD |
| Cleanup approach B (union with fill rule) – if the overlay can do it | 1–2 PD |
| Cleanup approach A (depth labelling, BufferSubgraph logic) in addition to the overlay | 5–10 PD |
| Input simplifier (small concavities), duplicate/short-segment filter | 1–2 PD |
| Offset curve à la JTS `OffsetCurve` (raw curve + matching against buffer boundary, multi result, optional joined mode) | 3–5 PD |
| Capsule union (D) as reference/fallback path | 1–2 PD |
| Tests against the NTS oracle (area difference, Hausdorff distance, validity), fuzzing, edge cases | 5–8 PD |
| **Total B (+ offset + D as reference + tests)** | **~ 17–30 PD** |
| Straight skeleton (E) robust, additionally | 20–40+ PD, high risk |

Without an existing overlay, noding + labelling + polygon assembly add several weeks by experience – the buffer work should therefore be scheduled **after** the overlay core.

---

## 8. Open-source references (as study material only, not as a dependency)

| Project | Approach | License | Note |
|---|---|---|---|
| [JTS](https://github.com/locationtech/jts) `operation.buffer` | A + OffsetCurve | EPL-2.0 / EDL-1.0 (BSD-like) | Reference design, very well commented |
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | Port of JTS | BSD-3-Clause | Test oracle according to ADR 0001 |
| [GEOS](https://github.com/libgeos/geos) | C++ port of JTS | LGPL-2.1 | `OffsetCurve` from 3.11 |
| [Clipper2](https://github.com/AngusJohnson/Clipper2) | B | BSL-1.0 | C# source available, integer coordinates |
| [Boost.Geometry](https://github.com/boostorg/geometry) | Pieces + turns (A/D hybrid) | BSL-1.0 | Strategy design for join/end/side |
| [georust geo](https://github.com/georust/geo) + [iOverlay](https://github.com/iShape-Rust/iOverlay) | B with integer overlay | MIT/Apache-2.0, Apache-2.0 | Modern, compact implementation |
| [CGAL Straight_skeleton_2](https://doc.cgal.org/latest/Straight_skeleton_2/index.html), [Minkowski_sum_2](https://doc.cgal.org/latest/Minkowski_sum_2/index.html) | E, C | GPL-3+ / commercial | Literature only, do not take over code |

License note: even with permissive licenses ADR 0001 applies – no inclusion. If code is *ported* (e.g. the structure of JTS `OffsetSegmentGenerator`), observe license/copyright notices; EPL-2.0 is not trivial for derived source code, EDL-1.0 (BSD) is the simpler choice with JTS.

## 9. Papers

- X. Chen, S. McMains: *Polygon Offsetting by Computing Winding Numbers*, ASME IDETC/CIE 2005, DETC2005-85513. [PDF](https://mcmains.me.berkeley.edu/pubs/DAC05OffsetPolygon.pdf)
- W. Tiller, E. Hanson: *Offsets of Two-Dimensional Profiles*, IEEE CG&A 4(9), 1984.
- B. K. Choi, S. C. Park: *A pair-wise offset algorithm for 2D point-sequence curve*, Computer-Aided Design 31(12), 1999.
- R. Wein: *Exact and Efficient Construction of Planar Minkowski Sums using the Convolution Method*, ESA 2006, pp. 829–840. [DOI](https://doi.org/10.1007/11841036_73)
- O. Aichholzer, F. Aurenhammer, D. Alberts, B. Gärtner: *A Novel Type of Skeleton for Polygons*, J.UCS 1(12), 1995. [J.UCS](https://www.jucs.org/jucs_1_12/a_novel_type_of.html)
- P. Felkel, Š. Obdržálek: *Straight Skeleton Implementation*, SCCG 1998.
- P. Palfrader, M. Held, S. Huber: *On Computing Straight Skeletons by Means of Kinetic Triangulations*, ESA 2012. [DOI](https://doi.org/10.1007/978-3-642-33090-2_66)
- M. Held: *Voronoi diagrams and offset curves of curvilinear polygons*, Computer-Aided Design 30(4):287–300, 1998. [Project page](https://www.cosy.sbg.ac.at/~held/projects/voronoi_2d/voronoi.html)
- J. Hobby: *Practical segment intersection with finite precision output*, Computational Geometry 13(4), 1999 (snap rounding, relevant for noding).
- M. Davis: [JTS Offset Curves](http://lin-ear-th-inking.blogspot.com/2022/01/jts-offset-curves.html) (blog, 2022) and [jts-dev discussion 2018](https://www.eclipse.org/lists/jts-dev/msg00225.html).

---

## 10. Comparison

| Criterion | A Raw offset + depth (JTS) | B Raw offset + union Positive (Clipper) | C Minkowski | D Capsule union | E Straight skeleton / Voronoi |
|---|---|---|---|---|---|
| Point / line / polygon | ✓ / ✓ / ✓ | ✓ / ✓ / ✓ | ✓ / (✓) / ✓ | ✓ / ✓ / ✓ | – / – / ✓ (inside) |
| Negative distance | ✓ | ✓ | cumbersome | ✓ (difference) | ✓ (core competence) |
| Join round / mitre / bevel | ✓ ✓ ✓ | ✓ ✓ ✓ | only shape of D | ✓ / extra / extra | mitre (skeleton) or round (Voronoi) |
| Cap round / flat / square | ✓ ✓ ✓ | ✓ ✓ ✓ | only round | ✓ / extra / extra | n/a |
| One-sided offset derivable | ✓ (OffsetCurve) | ✓ (with matching à la JTS) | – | ✓ (matching) | partly |
| Robustness | high (with snap-rounding fallback) | high (if the overlay is robust) | like the overlay | like the overlay, but stress cases | delicate |
| Performance | very good | very good | good | moderate to poor | good for many d |
| Reuse of overlay | high | very high | high | complete | low |
| Additional effort vs. overlay | medium–high | low–medium | medium | low | very high |
| Closeness to the NTS oracle | identical method | very similar | deviating | deviating vertices | deviating |

---

## 11. Recommendation (non-binding)

1. **Buffer by approach B** (Chen–McMains/Clipper principle): an offset segment generator of our own with all join/cap styles and quadrant segments, cleanup through **one union with fill rule Positive** in our own overlay. Choose arc support points, mitre limit and defaults (8 quadrant segments, mitre limit 5, closing segment at concave corners, input simplifier 0.01·d) **as in JTS**, so that comparisons with the NTS oracle stay close.
2. **Record the overlay requirement** (for #5/#11): the overlay must node arbitrary, self-overlapping ring sets and label them by winding number with a selectable fill rule (at least non-zero/positive). Approach A is then only an optimization, not a subsystem of its own.
3. **Capsule union (D)** as a simple reference path in the tests or as an internal fallback if B yields a topology exception on an edge case.
4. **Offset curve after the JTS `OffsetCurve` pattern**: buffer + raw offset of the side + tolerance matching against the buffer boundary; result multi-line, optionally "joined mode". Do not build self-intersection trimming logic of our own.
5. **No straight skeleton for the buffer**; at most later as a standalone algorithm if medial-axis/roof-surface use cases show up.
6. Order: overlay core → offset segment generator → buffer (B) → OffsetCurve → fine tuning (simplifier, collapse shortcuts, performance).

Open questions for the specification: Should buffer parameters appear as a parameter object of their own (analogous to `BufferParameters`) or as optional arguments of the extension method? Should arc tolerance be offered in addition to quadrant segments? Which behavior for invalid input (error vs. "make valid")?
