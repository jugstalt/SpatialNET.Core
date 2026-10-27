# Height bands: the areas between two heights

Contour lines (ADR 0037) draw where a surface has one height. A map, a flood analysis or a land-use plan often asks for the **areas**: everything between 100 and 120 m, everything below the flood level, the zones of a height map in colours. `ContourBands` gives them as polygons, one per band, cut exactly along the contour lines.

*German version: [0042-hoehenstufen-als-flaechen.de.md](0042-hoehenstufen-als-flaechen.de.md)*

## Consequences

- **Placement:** a member of `Triangulation` (ADR 0037) in its own file, `Triangulation.Bands.cs` in `SpatialNET.Extensions`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API:**

  ```csharp
  IReadOnlyList<IPolygon2D> ContourBands(IReadOnlyList<double> levels, IGeometryFactory? factory = null)
  ```

  The heights must be finite and strictly ascending. The result has **one more polygon than there are heights**: band 0 is everything below the first height, band `i` from height `i − 1` up to (not including) height `i`, the last band everything from the last height up, as for `SlopeZones` (ADR 0041). A band without area is an empty polygon, without heights the one band is the whole surface. Triangles with a vertex without Z belong to no band. The corners on a contour line have the height as Z, the others keep theirs.
- **The bands tile the surface and fit exactly.** Over a triangle the surface is a plane, so a band is a convex piece of the triangle:
  - Every triangle is cut with every height it spans (Sutherland–Hodgman against the height of the corners), always from the **original triangle**, so that the cut points on its edges come from the whole edge.
  - The cut point on an edge is computed **from the two vertices of the edge in a fixed order** (the smaller vertex id first), so the two triangles that share the edge get the same point to the last bit and the pieces fit without a gap or an overlap. A corner that lies exactly on the height is the point itself and not a point a rounding error away from it.
  - A triangle that is **flat at exactly the height** of a contour line belongs to the band above it, and a piece that is exactly degenerate (all corners on one line) is dropped.
  - The pieces of a band are not united with the overlay. The edges that two pieces have in opposite directions are inside the band and cancel; what is left is the boundary, which is chained to rings with the machinery of `SlopeZones` (ADR 0041): clockwise rings are holes and belong to the smallest ring around them, and a ring that touches itself in a vertex is cut there.
  - **Slivers.** Where a triangle is a sliver (an angle below a millionth of a degree: three corners that lie on one line to within a rounding error), a hole may overlap its shell by that error, and the structure of a polygon is read from the position of its rings, so the polygon would be read wrongly. A triangulation that has such a triangle checks every band and makes an invalid one valid with `MakeValid` (ADR 0020), which costs a pass over each polygon; a triangulation without one does not pay for it.
- **Speed.** One-shot measurements (`--quick-terrain`, Release build): seven heights on 10,000 points take about 0.15 s, on 50,000 points about 0.4 s. No overlay is needed.
- **Tests (ADR 0008):** a ramp cut into strips, a flat triangle at a height, a pyramid whose band has a hole, the area of every band of a plane against an independent clipping of the square, tiling and the position of random points for random terrains, errors; the stress test on dirty sites (valid polygons, the areas add up) for several minutes without a finding.
- **Playground and documentation:** the entry `contourBands` (heights separated by commas, or an interval), a section in the concept page "Triangulation, Voronoi and terrain", glossary entry.
- **Not now:** smoothed contour lines (the lines are polylines through the cut points, with a small crease on every edge; `Refine` or `InterpolateNaturalNeighbour` on a grid make them smoother), bands between heights that differ per area (a flood level that rises along a river), and labels.

## Considered Options

- Uniting the triangles of a band with `UnionAll` after cutting them (rejected: the cut points of neighbouring triangles would differ in the last bit and the overlay would have to repair them, at the cost of the speed that the exact cut points make unnecessary)
- Polygonising the contour lines with `Polygonize` (rejected: the contour lines of all heights have to be noded with the border of the surface and the faces sorted into bands afterwards, which is slower and loses the Z of the corners)
- Dropping a piece whose area is zero in floating point (rejected: a sliver of a few units in the last place gets a wrong sign, and the edges that its neighbours need to cancel against are lost; found by the stress test, and only an exactly degenerate piece may be dropped)
- Making every band valid with `MakeValid` (rejected: the check of a large polygon is not free, and it is needed only next to a sliver)
- A flat triangle at a height in both bands or in the lower one (rejected: in both would count its area twice, and the upper band is the convention of `SlopeZones` and of the usual definition of a class "from a height up")
