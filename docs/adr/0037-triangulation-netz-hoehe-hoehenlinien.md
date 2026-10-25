# The triangulation as a surface: neighbours, height at a point, contour lines

`DelaunayTriangles` (ADR 0029) returns a flat list of triangles, which is enough to draw them but not to work with them: the triangles do not know their neighbours, and finding the one that contains a point needs a search. A terrain model needs exactly that. The class **`Triangulation`** keeps the triangulation as a structure: vertices, triangles that know their neighbours, **`Locate`** (which triangle contains a point), **`InterpolateZ`** (the height at a point) and **`Contour`** (the line of a height). A triangulation of points is one of three ways to make it; the other two, with break lines and of a polygon, are ADR 0038.

*German version: [0037-triangulation-netz-hoehe-hoehenlinien.de.md](0037-triangulation-netz-hoehe-hoehenlinien.de.md)*

## Consequences

- **Placement:** the class `Triangulation` in `SpatialNET.Extensions`, namespace `SpatialNET`. The half-edge mesh of ADR 0029 stays internal. For the package validation and the API tracking this is an addition only (ADR 0028).
- **A class and not extension methods**, because a triangulation is **state that is built once and asked many times**: a terrain model is built from thousands of points and then asked for the height at many more. It is **immutable and thread-safe**; the index for `Locate` is built on the first question and is safe to share.
- **Structure.** Vertices are `Coordinate`s in a read-only list, and a triangle is addressed by its index from 0 to `TriangleCount` − 1:

  | Member | Meaning |
  |---|---|
  | `Vertices` | the vertices; Z is kept, M is not |
  | `TriangleCount` | the number of triangles; 0 for fewer than three different vertices or collinear ones |
  | `Triangle(t)` | the three vertex ids, counter-clockwise |
  | `Neighbour(t, side)` | the triangle across the edge from vertex `side` to vertex `side` + 1 (modulo 3), or −1 at the border |
  | `IsConstrained(t, side)` | whether that edge is a constraint (ADR 0038) |

  Ids are plain `int`s, as in any mesh library, and an id out of range is an `ArgumentOutOfRangeException`. The neighbours of a triangle on the border are −1, so a walk over the surface needs no special case.
- **`FromPoints(geometry)`** is the Delaunay triangulation of the vertices of a geometry, with the rules of ADR 0029 (X and Y, equal vertices once, not finite ones ignored). The first vertex of an (X, Y) that has a Z gives it. It never throws for too few or collinear points: the triangulation is empty.
- **`Locate(point)`** returns the triangle that contains the point **on its border or inside it**, or −1. The decision is exact (the orientation test of the overlays, ADR 0005). On an edge or a vertex shared by triangles it is **the one with the smallest index**, so the answer is deterministic. The lookup is not a walk from a start triangle but a query of an immutable `SpatialIndex` of the triangle envelopes (ADR 0014), then the exact test on the few candidates, which is O(log n) and needs no state of the caller; many threads may ask at once.
- **`InterpolateZ(point)`** is the height at a point: Z interpolated linearly in the triangle that contains it (the plane through its three vertices). At a vertex it is the vertex's Z exactly (the barycentric weights are 1 and 0 without rounding), on a surface that is a plane it is exact up to rounding (a property test), and on an edge it is the linear interpolation along the edge, which is the same for both triangles. It is **`NaN`** outside every triangle or if a vertex of the triangle has no Z; it never throws for that. A non-finite point is an `ArgumentOutOfRangeException`.
- **`Contour(z, factory)`** returns the **line of one height** as a polyline, by marching through the triangles: a triangle whose vertices are on both sides of the height has one segment of the line, and the segments are joined where exactly two meet. The rules:
  - A vertex whose Z is exactly `z` counts as **above**, so the contour passes through it and does not split there. A flat area at exactly `z` has no contour, as usual.
  - The crossing on an edge is computed from its end points **in the order of the vertex ids**, so the two triangles that share the edge get the same point bit for bit, and the pieces join exactly (the join is by equal coordinates).
  - The vertices of the line have **Z = `z`**. A contour that comes back to its start is closed (first = last); a contour that reaches the border of the triangulation ends there.
  - Triangles with a vertex without Z are skipped. A height the surface does not reach gives an empty polyline.
  - Contours at many heights are one call per height; the playground does it for an interval. A smoothing of the lines is not part of this.
- **As geometries:** `ToPolygons` (the triangles as polygons, with Z, in the order of their ids, as `DelaunayTriangles` but ordered), `Edges` (every edge once as one path, as `DelaunayEdges`), and `ConstrainedEdges` (only the constraints, ADR 0038).
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `NotSupportedException` for an unknown geometry kind, `ArgumentOutOfRangeException` for an id out of range or a non-finite point or height, `InvalidOperationException` without a factory (only the methods that create geometries).
- **Speed.** One-shot measurements (`--quick-voronoi`, Release build) on points with a smooth height and noise, and 100,000 random questions each:

  | Points | Layout | `FromPoints` (ms) | 100,000 × `Locate` (ms) | 100,000 × `InterpolateZ` (ms) | 11 contours (ms) |
  |---|---|---|---|---|---|
  | 10,000 | uniform | 8 | 117 | 132 | 10 |
  | 10,000 | grid | 24 | 78 | 77 | 4 |
  | 10,000 | clustered | 6 | 99 | 101 | 6 |
  | 100,000 | uniform | 79 | 208 | 185 | 89 |
  | 100,000 | grid | 200 | 142 | 140 | 65 |
  | 100,000 | clustered | 79 | 155 | 165 | 63 |

  The building includes the vertices and the neighbours; a question takes about 1 to 2 microseconds, almost independent of the size, which is what the index is for.
- **Tests (ADR 0008):** `[Covers]` tests. The structure on random points (counter-clockwise triangles, neighbours that agree and name each other's edge, the area is the area of the hull, the count and the edges match `DelaunayTriangles` and `DelaunayEdges`); `Locate` against a brute-force search for random points inside and outside, on vertices and edges, and from many threads at once; `InterpolateZ` on a plane (exact up to rounding), at vertices, outside and without Z; contours of a plane (a straight line, every vertex at the height), of a cone (a closed ring whose points interpolate to the height) and for heights that the surface does not reach; index and argument errors.
- **Playground and documentation:** descriptors for `InterpolateZ` (the height at a point) and `Contour` (one height, or all heights at an interval) with the example terrains (ADR 0011); the concept page "triangulation, Voronoi and terrain" and a cookbook recipe.
- **Not now:** slope, aspect and volume, other interpolations (natural neighbour, inverse distance), smoothing of contour lines, **changing a triangulation** (inserting or removing a point; it is built once), and quality triangulations with added points.

## Considered Options

- Only extension methods (`geometry.InterpolateZ(point)`) that triangulate on every call (rejected: a terrain model is asked many times, and building it each time would make a question as expensive as a model)
- A mutable triangulation that points can be added to (rejected: it would need locking or a single owner, and the Delaunay flips would become part of the contract; an immutable one is thread-safe and can be rebuilt)
- `Locate` as a walk from a start triangle (rejected: it is O(√n) and wants a hint from the caller to be fast; the index is O(log n) and stateless)
- An exception when a height is asked outside the surface (rejected: `NaN` is the usual answer of an interpolation and a caller maps many points; the other methods of the library answer "cannot" with an empty result or `NaN` as well, ADR 0017)
- Returning the triangles as `int[]` of ids only, without `Neighbour` (rejected: the neighbours are what a walk, a flood fill or a slope computation needs, and rebuilding them costs a dictionary)
- A contour as a list of line pieces without joining them (rejected: a contour is a line to draw and measure, and the pieces share their end points bit for bit anyway)
- `Contour` that interpolates heights between several triangulations (rejected: out of scope; the surface is one triangulation)
- Letting a vertex at exactly the height count as below (rejected: either choice works if it is made consistently; "above" was chosen, and with it a contour through a vertex does not split)
