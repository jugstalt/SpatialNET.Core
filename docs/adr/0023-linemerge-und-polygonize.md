# LineMerge and Polygonize

Two operations close the gap between lines and areas. **`LineMerge`** joins the paths of a polyline into longer paths where exactly two path ends meet; it is what `Union` of lines deliberately does not do (ADR 0005, ADR 0018: a path is cut where different input lines meet or end). **`Polygonize`** turns a polyline into the **areas it encloses**: every bounded face of the arrangement of the lines becomes a polygon, which together with `Cut` gives a complete subdivision of an area by lines. There is no public `Node`: `Union` of a polyline with itself (for example `new[] { line }.UnionAll()`) already nodes the lines at their crossings and removes duplicates, and `Polygonize` nodes internally.

*German version: [0023-linemerge-und-polygonize.de.md](0023-linemerge-und-polygonize.de.md)*

## Consequences

- **Placement:** `LineMerge` in the static class `Constructions` (a geometric result through the factory, ADR 0019), `Polygonize` in `Overlays` (it runs the overlay core), both in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation this is an addition only.
- **`LineMerge(this IPolyline2D line, bool directed = false, IGeometryFactory? factory = null)`** returns `IPolyline2D`:
  - A node joins two paths only if **exactly two path ends** meet there (exact equality of X and Y, no tolerance; snap the data first if needed). A node where three or more ends meet, or where a single end lies, keeps its paths apart.
  - A cycle of paths becomes one **closed** path. A path that already closes on itself stays as it is unless another path ends at its start point (then three ends meet and nothing is joined).
  - **Direction:** by default a path is reversed where needed, and the direction of the first path in the input order prevails for the merged path. With `directed: true`, two paths are joined only if the end of one is the start of the other, and exactly one path ends and exactly one path starts at the node.
  - `LineMerge` does **not node**. Crossing lines stay as they are; run `Union` first for that.
  - Paths of fewer than two distinct points are dropped (they are no lines, as in `MakeValid`, ADR 0020). Z and M of the coordinates are kept, at a joined vertex those of the earlier path. The result is deterministic: the paths come in the order in which their first path appears in the input.
- **`Polygonize(this IPolyline2D line, IGeometryFactory? factory = null, double? gridSize = null)`** returns `IReadOnlyList<IPolygon2D>`:
  - The lines are noded first, by snap rounding on the grid like `Union` (`gridSize`, otherwise `Precision.DefaultGridSize`, otherwise automatic, ADR 0005), so the vertices of the result lie on the grid. Overlapping duplicates collapse into one edge.
  - **Every bounded face** becomes a polygon of its own, in a **flat list** like the pieces of `Cut` (ADR 0007): a face with the components of lines inside it as holes (shell counter-clockwise, holes clockwise, ADR 0003), an island in a hole a face of its own. Adjacent faces are separate polygons that share their edge. A face that touches itself at a vertex gives one polygon per lobe, so every polygon is valid.
  - **Dangles** (edges with a free end) and **cut edges** (edges that have the same face on both sides, for example a bridge between two parts) enclose no area and are dropped; a face is not cut by them. The unbounded outer face is never returned.
  - A polyline that encloses nothing gives an empty list. The order of the list is deterministic.
  - Z and M of the vertices follow the rules of the overlays (ADR 0005, ADR 0015).
  - The result type is the plain list. A result with the dangles and cut edges (the JTS `Polygonizer` has them) can follow without a break.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for an invalid `gridSize`, `InvalidOperationException` without a factory. Empty and degenerate lines never throw.
- **Implementation:** `Polygonize` builds the planar graph of the overlay core from the lines and assembles the polygons from all faces of it, the assembly that the overlays use for their results. A closed ring of lines needs no special case.
- **Tests (ADR 0008):**
  - `lineMerge` and `polygonize` fixtures with the mandatory tag catalog (exceptions justified in `_catalog.json`: lines have no rings, holes or nesting in the sense of the tags). `LineMerge` is compared with the NTS `LineMerger` (as sets of lines, each in a canonical direction and, for rings, a canonical start), `Polygonize` with the NTS `Polygonizer` on the same lines noded by NTS (as sets of polygons by area and symmetric difference, since the nodes differ by the grid).
  - Property and stress tests: every polygon is valid; the faces do not overlap and their areas add up; every edge of a face belongs to at most two faces; `LineMerge` keeps the total length and the set of vertices, never merges at a node of three ends, and merging twice changes nothing.
- **Playground and documentation:** descriptors for both (ADR 0011; `Polygonize` shows the list of faces like `Cut`), a section in the concept page for the overlays and the constructions, glossary entries.

## Considered Options

- A public `Node` (rejected: `Union` of the line with itself does it, and `Polygonize` nodes internally; one more name for the same step)
- `LineMerge` that nodes first (rejected: merging and noding are different steps, and noding rounds to the grid, which `LineMerge` does not)
- `LineMerge` with a snap tolerance (rejected: exact equality is predictable; snapping is a separate, data-dependent step)
- Directed merging only (rejected: most line data has no meaningful direction, and the undirected case is what JTS offers by default)
- `Polygonize` returning one multi-part polygon of all faces (rejected: adjacent faces would lose their identity; the flat list of `Cut` is the established form)
- A result type with dangles, cut edges and invalid rings (rejected for now: nothing asks for it yet, and it can be added without a break)
- `Polygonize` that expects noded input and throws or returns garbage otherwise (rejected: noding on the grid is the library's own step, and the result must not depend on the input being prepared)
