# Overlay algorithms and precision models: options

*German version: [overlay-algorithmen.de.md](overlay-algorithmen.de.md)*

> Research for ticket #11. The decision will be made later together with the human in ticket #5. This recommendation is not binding.

## Constraints from the repo

- **ADR 0001**: Every **algorithm** is implemented in-house and works directly against the **geometry interfaces**. Internally there is neither NetTopologySuite nor Clipper2. NTS may appear only in the test project, as a comparison oracle.
- The target platform is net10.0. Computation is planar 2D; Z is interpolated at new nodes.
- Inputs arrive read-only via interfaces from the **host**. Results are created through the **geometry factory**. An algorithm may therefore build any working structures of its own internally (edge lists, sweep events, node graph) as long as it does not modify the host geometries.
- Needed are intersection, union, difference, symmetric difference and clip, for polygons/multipolygons with holes and additionally for polyline against polygon.

So we take over no third-party code. Third-party implementations serve only as reading material and as a source of test cases, which is why their license matters (e.g. if we take over test cases or closely re-implement code).

---

## 1. Candidates for overlay algorithms

### 1.1 Weiler-Atherton (1977)

- **Idea**: Walk along the boundaries and jump from the subject to the clip contour at intersection points. This requires entry and exit markers.
- **Robustness**: Weak. Touching corners, collinear edges and corners lying exactly on an edge produce ambiguous in/out classifications. The original paper hardly treats these cases.
- **Holes/multipolygons**: Possible in principle, but orientations must be tracked carefully and holes assigned separately.
- **Effort**: Low to medium for the normal case. For the edge cases the effort is high and hard to estimate.
- **Reference**: Weiler, K., Atherton, P.: *Hidden surface removal using polygon area sorting*, SIGGRAPH 1977. No modern, maintained reference implementation is known.
- **Assessment**: Historically important, not recommended for a robust GIS library.

### 1.2 Greiner-Hormann (1998) with the extensions by Foster/Hormann/Popa (2019)

- **Idea**: Two doubly linked vertex lists, insert intersection points into both, set entry/exit flags, then traverse. The method is very compact.
- **Robustness**: The original fails on degeneracies (vertex on edge, overlapping edges) and works around them by perturbing vertices. This falsifies the result. Foster, Hormann and Popa (2019) extend the method by a clean classification of degenerate intersection points without perturbation. However, this holds only for **non-self-intersecting** polygons (multiple and nested components are allowed). An older approach (Foster/Overfelt 2012, arXiv) was withdrawn, according to the arXiv note, because it does not cover all degeneracies.
- **Holes/multipolygons**: Yes with the extension. For assigning the output (which ring is a hole of which shell), however, a post-processing step of its own is needed.
- **Intersection computation**: Naively O(n·m). For large geometries an additional spatial index is needed.
- **Effort**: Low (original) to medium (2019 extension, many case distinctions).
- **References**:
  - Greiner, G., Hormann, K.: *Efficient clipping of arbitrary polygons*, ACM TOG 17(2), 1998.
  - Foster, E. L., Hormann, K., Popa, R. T.: *Clipping simple polygons with degenerate intersections*, Computers & Graphics: X, 2019. [TU Delft](https://research.tudelft.nl/en/publications/clipping-simple-polygons-with-degenerate-intersections-2)
  - C++ implementation: [Fribur/FosterHormannPopaClipping](https://github.com/Fribur/FosterHormannPopaClipping)
  - Withdrawn predecessor: [arXiv 1211.3376](https://arxiv.org/abs/1211.3376)
  - A GPU parallelization shows that the method is under active development: [GH GPU paper](https://www.cs.mu.edu/~satish/papers/GH_GPU_Camera_ready.pdf)
- **Assessment**: Attractive because it is small. The restriction to inputs without self-intersections and the missing set semantics for lines are drawbacks, however.

### 1.3 Vatti (1992), basis of Clipper/Clipper2

- **Idea**: A scanline method over scanbeams (horizontal strips between vertex y values). It works with local minima/maxima, an active edge table and fill rules (EvenOdd, NonZero, Positive, Negative).
- **Robustness**: Very good in Clipper2, but only because **Int64 coordinates** are used internally. `ClipperD` also scales to integers internally. The Clipper author himself writes that tiny self-intersection artifacts and nearly arealess polygons can occur "very occasionally" due to rounding ([Clipper2 Robustness](https://angusj.com/clipper2/Docs/Robustness.htm)).
- **Holes/multipolygons/self-intersections**: Yes; thanks to the fill rules also for arbitrarily self-intersecting input. Optionally a PolyTree with the hierarchy of shells and holes is produced.
- **Polylines**: Clipper2 can clip open paths against polygons.
- **Effort**: High. After more than 10 years of development, Clipper2 is full of special cases (horizontal edges, join logic, horizontal processing).
- **References**:
  - Vatti, B. R.: *A generic solution to polygon clipping*, CACM 35(7), 1992.
  - [Clipper2](https://github.com/AngusJohnson/Clipper2), Boost Software License 1.0, C++/C#/Delphi. [Clipper64 docs](https://angusj.com/clipper2/Docs/Units/Clipper.Engine/Classes/Clipper64/_Body.htm)
- **Assessment**: Proven in industry. A re-implementation of our own is laborious, however, and the integer grid changes the coordinates (see section 2).

### 1.4 Martinez-Rueda-Feito (2009 and 2013)

- **Idea**: A Bentley-Ottmann-like plane sweep over segment endpoints (event queue plus sweep-line status as a balanced tree). Each segment carries the flags `inOut`, `otherInOut` and `inResult`. The result segments are selected according to the operation, after which the result contours are chained. The 2013 version simplifies the algorithm and additionally computes the **hole/shell hierarchy** of the output.
- **Robustness**: Overlapping (collinear) edges are handled explicitly: they are split and marked `SAME_TRANSITION` or `DIFFERENT_TRANSITION`. Touching vertices are covered, self-intersections within an operand only to a limited extent. The weak spot is floating-point intersection points. A computed intersection point does not lie exactly on both segments, and this can destroy the sort order in the sweep. The consequences are infinite loops or wrong results. mfogel's `polygon-clipping` introduces an iteration limit specifically for this ([README](https://arches.lcc.ufmg.br/static/polygon-clipping/README.md)).
- **Holes/multipolygons**: Yes. The output hierarchy exists from the 2013 version on.
- **Running time**: O((n+k) log n), so it scales well.
- **Effort**: Medium. Manageable if an existing implementation is used as reading material. The core comprises roughly 1,000 to 2,000 lines of C#.
- **References**:
  - Martínez, F., Rueda, A. J., Feito, F. R.: *A new algorithm for computing Boolean operations on polygons*, Computers & Geosciences 35(6), 2009.
  - Martínez, F., Ogayar, C., Jiménez, J. R., Rueda, A. J.: *A simple algorithm for Boolean operations on polygons*, Advances in Engineering Software 64, 2013, pp. 11–19.
  - [w8r/martinez](https://github.com/w8r/martinez) (JS, MIT). According to the README, multipolygons, holes, self-intersecting and degenerate polygons with overlapping edges are supported. ([README](https://cdn.jsdelivr.net/npm/martinez-polygon-clipping@0.9.0/README.md))
  - [mfogel/polygon-clipping](https://github.com/mfogel/polygon-clipping) (JS, MIT): a more robust further development focused on correctness and with a large collection of test cases.
  - [21re/rust-geo-booleanop](https://github.com/21re/rust-geo-booleanop) (Rust, MIT)
  - [SixLabors/PolygonClipper](https://github.com/SixLabors/PolygonClipper) (**C#**, Six Labors Split License, i.e. not a standard license). For the Boolean operations it uses Martínez-Rueda, for normalization a Vatti/Clipper2-like approach, and it computes with double. For us this is the closest reading material in the same language. Because of the license, however, one should not follow it closely.
- **Assessment**: Among the "polygon-only" methods the best compromise of effort, performance and functionality. It becomes robust only with clean predicates and controlled rounding of intersection points, however.

### 1.5 polybooljs-style segment sweep (velipso)

- **Idea**: Also "somewhat based on F. Martinez (2008)". Each segment carries fill annotations (`myFill.above/below`, `otherFill.above/below`). First both inputs are decomposed in a common sweep ("combine"). The Boolean operation is then only a selection via a 16-entry table of the fill states. An important advantage follows: once decomposed, inputs can be reused for several operations.
- **Robustness**: The algorithm works throughout with **epsilon comparisons** (default 1e-10, globally adjustable). Coincident segments are handled (full overlap, shared vertices, segment within segment), and superfluous vertices are removed. If the epsilon is chosen wrongly, zero-length segments arise and an error is thrown. An absolute epsilon is scale-dependent, however. For GIS coordinates (degrees versus meters in the 10^6 range) it must be chosen per coordinate range.
- **Holes/multipolygons/self-intersections**: Yes. The region semantics is EvenOdd-like.
- **Effort**: Medium. The data model is very tidy and didactically good.
- **References**: [velipso/polybooljs](https://github.com/velipso/polybooljs) (MIT, no longer maintained, successor `@velipso/polybool` in TypeScript), Dart port [polybool](https://pub.dev/packages/polybool) (BSD-3).
- **Assessment**: Didactically the clearest sweep model. The epsilon model, however, has to be replaced by something better.

### 1.6 JTS/NTS OverlayNG (noding + topology graph)

- **Idea**: Not a pure clipping algorithm but a pipeline:
  1. All edges of both inputs are **noded** together, i.e. split at all intersection points. This is done with an exchangeable *noder*, e.g. snap rounding, MCIndex or snapping.
  2. A planar **topology graph** is built from the edges. Collinear duplicates are merged, and every edge gets a label with its position (inside/outside/on the boundary) with respect to both inputs.
  3. The labels are propagated, and for unlabelled edges there is a point-in-polygon test.
  4. Result edges are selected according to the operation, rings are built, holes assigned, and lines or points extracted.
- **Robustness**: The highest of all candidates. With a **fixed PrecisionModel** and the snap-rounding noder, OverlayNG computes robustly according to its documentation (up to about 13 significant decimal digits). With floating precision (MCIndexNoder) it is *not* fully robust and may throw a `TopologyException`. `OverlayNGRobust` catches this heuristically: first floating, then snapping with an automatically chosen, growing tolerance, finally snap rounding. ([OverlayNG Javadoc](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/operation/overlayng/OverlayNG.html), [OverlayNGRobust.h in GEOS](https://gemfury.com/flet/python:flet-libgeos/-/content/include/geos/operation/overlayng/OverlayNGRobust.h))
- **Holes, multipolygons, self-touching, mixed dimensions**: Yes. Line against polygon and line against line are part of the same framework. A strict mode ensures homogeneous result dimensions and discards collapse lines and points.
- **Effort**: High. There are many components: noder, edge merge, graph, labelling, RingBuilder, LineBuilder, hole assignment and spatial index. In return, the parts are reusable, e.g. for validation, buffer, polygonize and line overlay.
- **References**:
  - [JTS](https://github.com/locationtech/jts) (EPL 2.0 / EDL 1.0) and [NTS](https://github.com/NetTopologySuite/NetTopologySuite) (BSD-3)
  - Martin Davis' announcement on jts-dev, 2019: [msg00261](https://www.eclipse.org/lists/jts-dev/msg00261.html)
  - GEOS 3.9 / PostGIS 3.1: [Crunchy Data blog](https://www.crunchydata.com/blog/waiting-for-postgis-3.1-geos-3.9)
  - Known edge-case bugs: [GEOS #942](https://github.com/libgeos/geos/issues/942), [GEOS-Trac #1051](https://trac.osgeo.org/geos/ticket/1051)
- **Assessment**: Robustness and functionality are the "gold standard". Important for us: it is exactly the behavior of our **test oracle**. The more similar our semantics are to those of OverlayNG, the easier the comparison tests become.

---

## 2. Precision models

| Model | Principle | Advantages | Disadvantages / consequences |
|---|---|---|---|
| **Floating without tolerance** | Raw double everywhere | No coordinate change | Orientation tests can "lie". Topology becomes inconsistent, sweeps get into infinite loops, missing or duplicate edges occur |
| **Floating with epsilon** (polybooljs) | Comparisons with `abs(a-b) < eps` | Simple, catches many near-degeneracies | Epsilon is scale-dependent and not transitive (a≈b, b≈c, but a≉c). No guarantee, merely a shift of the failure cases |
| **Robust predicates** (Shewchuk) | Adaptive, exact signs of `orient2d` (and `incircle`). The exact arithmetic kicks in only when the fast result is uncertain | Exact decisions on the input coordinates, practically as fast as double, code is public domain | Solves only the *predicate* problem. **Constructed** intersection points are still rounded and do not lie exactly on the segments. Follow-up predicates with these points are uncertain again |
| **Fixed-precision grid** (Clipper: Int64) | Scale all coordinates to an integer grid, round intersection points to the grid | Predicates are exact on integers (with 128-bit intermediates), deterministic and fast | The output changes in every coordinate. Rounding can produce mini self-intersections and "slivers" (Clipper advises not to expect near-zero areas). The scale must be chosen per dataset |
| **Snap rounding** (Hobby 1999, Goodrich et al. 1997, Hershberger 2013) | Grid with "hot pixels". Every vertex and intersection point is pulled to its pixel center, and every segment crossing a hot pixel is rerouted through its center | **Provably** consistent topology: the output is a correctly noded arrangement, and the distance of every segment from the original is ≤ half a pixel diagonal. This is the JTS/GEOS route. Hershberger's *stable snap rounding* prevents subsequent re-snapping from producing further drift. *Iterated snap rounding* (Halperin/Packer) keeps vertices farther from foreign edges | Coordinates are rounded. Additional nodes on crossed edges. Medium to high effort (hot pixel index). Small structures can collapse, the dimension drops (polygon becomes a line), which strict mode has to deal with |
| **Snapping noder with tolerance** (JTS SnappingNoder) | Vertices within a tolerance are pulled onto each other, without a fixed grid | Largely keeps the original coordinates | Heuristic, not guaranteed. In JTS only a fallback step |
| **Exact arithmetic** (rational/BigInteger, CGAL style) | Intersection points as exact rational numbers | Mathematically exact | Slow, memory growth. The output has to be rounded to double in the end anyway, and then one faces the rounding problem again |

Sources: Shewchuk, J. R.: *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, Discrete & Computational Geometry 18, 1997 ([abstract and code](https://people.eecs.berkeley.edu/~jrs/papers/robust-predicates.abstract), [DOI](https://www.doi.org/10.1007/PL00009321)). Ports: [robust-predicates (JS)](https://npmjs.com/package/robust-predicates), [robust (Rust)](https://docs.rs/robust). No maintained C# version is known, but a port of our own of `predicates.c` is small and license-free. Hobby, J. D.: *Practical segment intersection with finite precision output*, CGTA 13(4), 1999. Hershberger, J.: *Stable snap rounding*, CGTA 46(4), 2013. Goodrich, Guibas, Hershberger, Tanenbaum: *Snap rounding line segments efficiently in two and three dimensions*, SoCG 1997 ([slides](https://www.uni-trier.de/fileadmin/fb4/prof/INF/DEA/Seminar0708/Snap_goodrich.pdf)). [Iterated Snap Rounding](https://www.cgl.cs.tau.ac.il/projects/iterated-snap-rounding/). [CGAL 2D Snap Rounding](https://doc.cgal.org/5.3/Snap_rounding_2/citelist.html). [GEOS SnapRoundingNoder.h](https://gemfury.com/flet/python:flet-libgeos/-/content/include/geos/noding/snapround/SnapRoundingNoder.h).

### 2.1 Key statement on precision

Robust predicates are necessary but not sufficient. The actual problem of an overlay is the **constructed intersection points**. There are only two clean ways out:

1. A **rounding model with a topology guarantee** (snap rounding to a grid). This is the JTS route with a fixed PrecisionModel.
2. Exact arithmetic. For a library serving double interfaces, this is disproportionate.

Floating with tolerance remains a heuristic that works most of the time and occasionally does not.

### 2.2 Consequences for result stability

- **Grid or snap rounding**: The results are deterministic and topologically valid. Output coordinates lie on the grid, however, and differ from the input coordinates by up to half a grid size. The grid size must come from the host as an API parameter, e.g. as "precision/scale" or a tolerance in map units, or be derived from the coordinate magnitude (like JTS `PrecisionUtil`). For GIS data a grid of 1e-6 to 1e-9 in map units is usually uncritical from a domain point of view.
- **Floating**: The original coordinates are preserved. For nearly degenerate input (almost collinear, distances in the range of 1 ULP), however, failures or invalid results must be expected. A fallback cascade like `OverlayNGRobust` is then mandatory.
- **Z interpolation**: With snap rounding, nodes move. Z should therefore be interpolated on the *original segment*, i.e. via the parameter t of the unrounded intersection, and not from the rounded position. This is independent of the algorithm and easy to test.

### 2.3 Consequences for tests against NTS

- **Same precision model on both sides**: If we compute with grid s, the oracle should run `OverlayNG` with `new PrecisionModel(s)`. Then the coordinates are also largely comparable. Exact equality is still not guaranteed, because hot pixel order and node choice may differ.
- **Never test for coordinate equality**, but for:
  1. `IsValid` of the result
  2. Area equality with relative tolerance
  3. Symmetric difference to the oracle result: its area must be smaller than ε·total area. NTS itself computes this check.
  4. Hausdorff distance smaller than grid size × k
  5. Topological figures: number of polygons, number of holes, dimension
- **Normalization before comparing**: Ring start, orientation and order of the parts differ, so compare both sides with `Normalize()`.
- **Semantic differences** to be fixed deliberately: What happens with touches? Does the intersection of two touching polygons yield a line or a point, or (strict mode) empty? How are collapsing slivers handled? The simplest is to adopt the **OverlayNG strict semantics**. Then the oracle is directly usable.
- **Test corpora**: The JTS TestRunner XML suites (`modules/tests/src/test/resources/testxml`, EPL/EDL) and the fixtures of mfogel/polygon-clipping (MIT) contain many documented degeneracies. They should be taken over as *inputs*, with the NTS result as expected value. In addition there are **property-based tests**:
  - A∪B ⊇ A
  - area(A∪B) = area(A) + area(B) − area(A∩B)
  - A \ B ∩ B = ∅
  - invariance under translation and rotation by 90°
  - randomly snapped coordinates to force collinearities

---

## 3. Polyline against polygon and line against line

- **Polygon-only algorithms** (Greiner-Hormann, Weiler-Atherton, Martinez, polybooljs) know only areas. Clipping a polyline against a polygon is easy to add, however:
  1. Split the line at all intersection points with the polygon boundary. For this, noding of line and boundary suffices, without the sweep of the area Boolean operation.
  2. Do a midpoint test for every piece (inside, outside or on the boundary).
  3. Select pieces according to the operation (intersection: inside plus optionally boundary; difference: outside) and chain adjacent pieces again.

  Martinez implementations often have no line mode. Clipper2 has a mode for open paths.
- **Line against line** (intersection, union, difference of lines) is pure **noding and edge-merge work**: node all segments, merge collinear overlaps, select by origin and reassemble lines. Results can have mixed dimensions (points from crossings, lines from overlaps).
- **Conclusion**: A noding layer (segment decomposition with robust predicates and a snap-rounding or grid model) is the common core of *all* three cases: polygon/polygon, line/polygon and line/line. With OverlayNG this is so from the outset. With Martinez/polybooljs the noding is inside the sweep. There, however, the same predicate and rounding core can be used and the line case solved separately.

---

## 4. Comparison

| Criterion | Weiler-Atherton | Greiner-Hormann (+FHP 2019) | Vatti/Clipper2 | Martinez-Rueda-Feito 2013 | polybooljs sweep | JTS OverlayNG |
|---|---|---|---|---|---|---|
| Collinear/overlapping edges | weak | yes with extension | yes (integer) | yes (explicit) | yes (epsilon) | yes (edge merge) |
| Touching vertices | weak | yes with extension | yes | yes | yes | yes |
| Holes | laborious | yes | yes (PolyTree) | yes, incl. hierarchy | yes | yes |
| Multipolygons | laborious | yes | yes | yes | yes | yes |
| Self-touching rings / self-intersections | no | self-intersection-free only | yes (fill rules) | limited | yes (EvenOdd) | inputs must be valid, self-touching (inverted holes) ok |
| Line/polygon, line/line | no | no | line/polygon (open paths) | no (separate) | no (separate) | yes, all combinations |
| Running time | O(n·m) | O(n·m) without index | O((n+k) log n) | O((n+k) log n) | O((n+k) log n) | O((n+k) log n) with index |
| Typical precision model | float | float (+perturbation) | Int64 grid | float | float + epsilon | fixed + snap rounding or floating + fallback |
| Overall robustness | low | medium | high | medium (high with snap core) | medium | very high |
| Closeness to the NTS oracle (semantics) | low | low | medium | medium | medium | **identical** |
| Implementation effort | low/unclear | low to medium | high | **medium** | medium | high |
| Reusable for other algorithms | no | no | partly | partly (sweep) | partly | **yes** (noder, graph, validation, polygonize, buffer) |
| Reference code (license) | – | C++ FHP clipping | Clipper2 (Boost 1.0) | w8r/martinez (MIT), polygon-clipping (MIT), rust-geo-booleanop (MIT), SixLabors.PolygonClipper (C#, Split License) | polybooljs (MIT) | JTS (EPL 2.0/EDL 1.0), NTS (BSD-3), GEOS (LGPL) |

---

## 5. Recommendation (not binding, decision in #5)

1. **Precision core first and independent of the algorithm**:
   - a C# port of our own of Shewchuk's `orient2d` (public domain)
   - a configurable **fixed-precision model with snap rounding** as the default. The grid size is given by the host, or derived automatically from the coordinate magnitude.
   - optionally a floating mode with a fallback cascade modeled on `OverlayNGRobust`

   This core can be shared by clip, cut and all overlays.
2. **Architecture after the OverlayNG pattern** (noding → edge merge → topology graph with labels → result construction), but step by step:
   - **Stage 1**: noder plus line/polygon clip and line/line. This is the smallest graph and at the same time an early usable feature (clip).
   - **Stage 2**: polygon/polygon with labelling, RingBuilder and hole assignment.
   - **Stage 3**: optimizations (envelope clipping, spatial index).

   Main reason: this covers all dimension combinations, and the **semantics coincide with the NTS oracle** (strict mode). This simplifies the tests considerably, and the graph is reusable for later algorithms.
3. **Pragmatic alternative**, if the effort seems too large: **Martinez-Rueda-Feito 2013** for polygon/polygon, built on the same predicate and snap core, plus a separate line clip via noding and midpoint test. It is finished sooner, but delivers two separate code paths, and the semantics in edge cases deviate more likely from the oracle.
4. **Not recommended**: Weiler-Atherton and the original Greiner-Hormann (too fragile). A Vatti re-implementation pays off only if fill rules for self-intersecting input become a core requirement.
5. **Testing**:
   - same fixed PrecisionModel in the NTS oracle
   - comparison via validity, area, symmetric difference, Hausdorff distance and topology figures, never via coordinate equality
   - test corpora from the JTS XML suite and the fixtures of polygon-clipping
   - property-based tests with forced degeneracies

Open questions for #5:

- May the output have rounded coordinates (snap rounding as the default)?
- Which semantics apply to touch results and collapses (strict mode yes or no)?
- Must self-intersecting or invalid input be supported, or is validity assumed (as in JTS)?
