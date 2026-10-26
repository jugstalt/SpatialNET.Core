# Voronoi diagram and the edges of the Delaunay triangulation

The **Voronoi diagram** is the dual of the Delaunay triangulation (ADR 0029): for every site it gives the area that lies nearer to that site than to every other (the Thiessen polygon). A GIS uses it for service areas ("which hydrant, which school is the nearest one, and how much of the district does it serve"), for weighting measurements by area and for neighbourhoods. **`VoronoiDiagram`** returns the cells, **`VoronoiEdges`** the lines between them, and **`DelaunayEdges`** the edges of the triangulation that the cells come from.

*German version: [0036-voronoi-diagramm.de.md](0036-voronoi-diagramm.de.md)*

## Consequences

- **Placement:** `VoronoiDiagram`, `VoronoiEdges` and `DelaunayEdges` in `Shapes` (a partial class, ADR 0026) and the value type `VoronoiCell` in `SpatialNET.Extensions`, namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API:**

  ```csharp
  IReadOnlyList<VoronoiCell> VoronoiDiagram(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
  IPolyline2D VoronoiEdges(this IGeometry2D sites, IPolygon2D? clip = null, IGeometryFactory? factory = null, double? gridSize = null)
  IPolyline2D DelaunayEdges(this IGeometry2D geometry, IGeometryFactory? factory = null)
  readonly record struct VoronoiCell(Coordinate Site, IPolygon2D Cell)
  ```

  The sites are the vertices of any geometry, exactly as for `DelaunayTriangles` (ADR 0029): X and Y, vertices with equal X and Y count once, vertices that are not finite are ignored.
- **The result keeps the cell with its site.** `DelaunayTriangles` returns a list in no particular order, because nobody asks which triangle belongs to which point. For a Voronoi diagram the pairing is the whole point, so every entry is a `VoronoiCell` with the site (with its Z, if it has one; M is not kept) and its cell. The list has **one entry per distinct site, in the order in which the sites first occur**.
- **The clip area is part of the question.** The cells at the border of the sites are unbounded, so they have to be cut. Without `clip` the area is the **envelope of the sites widened on every side by half of its larger extent** (by 1 if it has no extent, as for a single site); with a polygon the cells are cut with it, which is what a service area within a boundary needs. A site outside the polygon can still have a cell inside it; a cell that does not reach the polygon is empty. A clip area without area gives an empty result. A rectangle is `Envelope2D.ToPolygon`.
- **The cells have no gaps and do not overlap.** Their areas add up to the area of the clip area, and every cell is a valid polygon (a property test checks both, for random sites, a regular grid and UTM coordinates). How this is achieved:
  - The corners of a cell are the **circumcentres** of the triangles around its site, computed once per triangle, so two neighbouring cells share them bit for bit. The topology comes from the triangulation, which is decided with exact predicates (ADR 0029, ADR 0005).
  - A cell at the hull is closed far away, outside the clip area, along the bisectors of the two hull edges and a polygonal arc between them, and then cut with the rectangle by Sutherland–Hodgman. The crossing of a cell edge with a side of the rectangle is computed **from the end that lies inside**, so both cells that share the edge get the same point, and with the precision of the near end: the closure of an unbounded cell lies far away (1e18 for almost collinear sites at the border), where a crossing computed from the far end would be off by the resolution of that magnitude.
  - The corners are then **rounded to the grid** (`gridSize`; cascade as for the overlays: explicit, the global default, otherwise automatic from the magnitude of the coordinates, ADR 0005). Almost cocircular sites give circumcentres that differ by rounding errors, and a cell would fold at that size; rounding to the grid collapses them. A rounded cell is accepted if it is still convex and has an area; otherwise the overlay core makes it valid (`MakeValid`, ADR 0020).
  - With a clip polygon the cell, cut to the envelope of the polygon, is intersected with the polygon by the overlay (`Intersection`) on the same explicit grid, so neighbouring cells meet along the clip polygon as well.
  - A cell that collapses at the size of the grid (sites closer together than that) is **empty**, not an error.
- **Degenerate sites.** One site gets the whole clip area, two sites are split by their bisector, and collinear sites get strips; their neighbours are the previous and the next site along the line, and the bisector is computed the same way for both. With no site the result is empty.
- **`VoronoiEdges`** returns the **edges between neighbouring cells**, one path per edge, without the edges that lie on the rectangle of the clip area (they are not part of the diagram). The edges come from the unrounded cells, each once, and are rounded to the same grid; with a clip polygon they are cut with it. It needs at least two sites.
- **`DelaunayEdges`** returns the edges of the triangulation of `DelaunayTriangles`, **every edge once**, as one path of two points, with Z of the vertices kept. It is empty for fewer than three different vertices or collinear ones, as the triangles are. The neighbourhood graph of points is this list.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `NotSupportedException` for an unknown geometry kind, `ArgumentOutOfRangeException` for a `gridSize` that is not finite or not greater than 0, `InvalidOperationException` without a factory.
- **Speed.** One-shot measurements (`--quick-voronoi`, Release build), the cells of `VoronoiDiagram` against the NTS `VoronoiDiagramBuilder`, with the same points as ADR 0029:

  | Points | Layout | `VoronoiDiagram` (ms) | NTS (ms) | `VoronoiEdges` (ms) |
  |---|---|---|---|---|
  | 10,000 | uniform | 45 | 165 | 52 |
  | 10,000 | grid | 65 | 123 | 53 |
  | 10,000 | clustered | 42 | 102 | 47 |
  | 100,000 | uniform | 381 | 1,463 | 544 |
  | 100,000 | grid | 465 | 1,561 | 532 |
  | 100,000 | clustered | 394 | 1,272 | 519 |

  The time includes creating the cell polygons. It grows about linearly. `VoronoiEdges` is slower than the cells for uniform points since it builds the cells first and then the edges.
- **Tests (ADR 0008):** `[Covers]` tests, no fixtures. Hand cases (two sites, the corners of a square, a single site, collinear sites, duplicates, the order, Z, a clip polygon, errors); properties over random sites (the cells are valid, tile the clip area, a point lies in the cell of its nearest site, a site lies in its own cell); a regular grid, where every four neighbouring sites are cocircular; UTM-sized coordinates; **the NTS diagram as an oracle** (the area of every cell, for random sites in general position and the same clip rectangle); the edges lie between two equally near sites; `DelaunayEdges` equals the edges of `DelaunayTriangles` (every edge once).
- **Playground and documentation:** descriptors for the three with example sites (ADR 0011); the clip area is an **optional second input** of the playground, which is new (an `optional` flag of the input descriptor). The concept page "triangulation, Voronoi and terrain" and a cookbook recipe on service areas; glossary entries.
- **Not now:** power and weighted diagrams, the Voronoi diagram of lines or polygons (the sites are points), a tolerance that merges sites that are almost equal (as NTS has it; a caller can round the sites with `ReducePrecision`), and the diagram on the sphere.

## Considered Options

- Computing every cell by cutting the clip rectangle with the bisectors of its Delaunay neighbours (rejected: the corners of two neighbouring cells come out of different pairs of lines and differ by rounding errors, so they would not share them; and a site with many neighbours is quadratic)
- Cutting every cell with the overlay, without cutting it to the rectangle first (rejected: the closed far corners and the circumcentres of slivers can be very far away, which the grid of the overlay cannot take)
- The NTS result: a collection of polygons with the site as user data (rejected: the interfaces have no user data, and a typed `VoronoiCell` is the honest form of the pairing)
- A required clip area (rejected: the default of a widened envelope is what NTS and JTS do and is good for drawing; the clip polygon is the choice for service areas)
- A tolerance parameter that merges almost equal sites (rejected: sites are distinct by X and Y everywhere in the library; `ReducePrecision` (ADR 0027) does it for the caller who wants it, and then with a grid)
- Exact rational circumcentres (rejected: the cells are rounded to the grid anyway, and the shared corners already match bit for bit)
- Returning the cells of sites that do not reach the clip area as `null` or leaving them out (rejected: the list has one entry per site so that it can be matched by index and by site; an empty polygon says what it is)
