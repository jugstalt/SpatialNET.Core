# Delaunay triangulation and concave hull

Two related operations on the vertices of a geometry. **`DelaunayTriangles`** triangulates them so that no vertex lies inside the circumcircle of a triangle, which makes the triangles as "round" as the points allow. **`ConcaveHull`** (with the variant **`ConcaveHullByRatio`**) wraps the points more tightly than the convex hull: it starts from the triangulation and takes triangles away from the border where an edge is too long. The triangulation is the foundation of the hull, and it is useful on its own for terrain models, interpolation and neighbourhoods, so it is public.

*German version: [0029-delaunay-und-konkave-huelle.de.md](0029-delaunay-und-konkave-huelle.de.md)*

## Consequences

- **Placement:** `DelaunayTriangles`, `ConcaveHull` and `ConcaveHullByRatio` in `Shapes` (now a partial class; ADR 0026), in `SpatialNET.Extensions`, namespace `SpatialNET`. The triangulation itself (`DelaunayMesh`, a half-edge mesh with neighbours) and the exact incircle test are internal. For the package validation and the API tracking this is an addition only (ADR 0028).
- **`DelaunayTriangles(geometry, factory?)`** returns `IReadOnlyList<IPolygon2D>`: a flat list of triangles, each a polygon with its vertices counter-clockwise, in no particular order (like the pieces of `Cut`, ADR 0007). Input as for `ConvexHull`: every vertex of every kind of geometry, X and Y only; vertices with equal X and Y count once, vertices that are not finite are ignored. The list is empty if fewer than three different vertices remain or all of them are collinear. Z of a vertex is kept, M is not. **Vertices on a common circle** (the corners of a rectangle, a regular grid) allow several triangulations, all of them Delaunay; which one comes out is deterministic but not specified, and the documentation says so. Everything else, every point outside every circumcircle, is the contract.
- **`ConcaveHull(geometry, maxEdgeLength, holesAllowed = false, factory?)`** and **`ConcaveHullByRatio(geometry, edgeLengthRatio, holesAllowed = false, factory?)`** return `IGeometry2D`: a polygon that contains every vertex. A geometry with fewer than three different or only collinear vertices gives what `ConvexHull` gives (a point, the line between the extreme points, an empty polygon). `maxEdgeLength` is in the unit of the coordinates, not negative and not `NaN`; infinity gives the convex hull. The ratio is from 0 to 1 and gives the limit `shortest + ratio * (longest - shortest)` over the edge lengths of the triangulation (the definition of JTS), so it does not depend on the scale; 1 gives the convex hull, 0 the tightest hull. Z of a vertex is kept, M is not.
- **The rule.** The hull is the union of triangles that remain. From the border, triangles are taken away, **longest border edge first**, as long as that edge is longer than the limit, under two conditions: a triangle must have **exactly one border edge** (one with two border edges would leave its third vertex outside the hull, and the hull must contain every vertex), and its **third vertex must not be on a border** yet (otherwise taking it away would pinch the polygon). Under these conditions the hull stays connected and without holes. A triangle that fails them stays, even if its edge is long; this is what keeps the result a polygon.
- **Holes.** With `holesAllowed`, after the border is finished, interior triangles of which all three vertices are interior and which have an edge longer than the limit are taken away, longest first, and the hole grows by the same rule from its border. A hole never touches the outer border or another hole, so the result is always a valid polygon with holes and not a multipolygon.
- **Agreement with NTS.** The rule is the one of the JTS `ConcaveHull`; for points in general position (no two equal edge lengths) the area equals the NTS result in the tests, with and without holes, for 400 random cases of 10 to 80 points. For points that allow equal choices the result can differ in which of them is taken first.
- **Robustness.** The triangulation uses exact predicates: the orientation test of the overlay core (ADR 0005) and a new exact incircle test (a floating-point filter with Shewchuk's error bound, and the exact determinant over integers if the sign is uncertain). The algorithm is the sweep of Mapbox's Delaunator in a version of its own: a seed triangle, then the points in order of their distance from its circumcentre, so that each point normally lies outside the hull so far and joins the visible hull edges; every insertion restores the Delaunay property by flipping. A point that lies on a hull edge or inside (which the order cannot exclude, and a regular grid produces often) is inserted by locating its triangle exactly and splitting the triangle or the edge. The tests run every point set through both paths.
- **Speed.** One-shot measurements (`--quick-triangulation`, Release build) on points spread evenly, on a grid and in clusters:

| Points | Layout | `DelaunayTriangles` (ms) | NTS (ms) | `ConcaveHullByRatio` (ms) |
|---|---|---|---|---|
| 10,000 | clustered | 42 | 109 | 15 |
| 10,000 | grid | 78 | 163 | 36 |
| 10,000 | uniform | 52 | 105 | 13 |
| 100,000 | clustered | 558 | 1,697 | 176 |
| 100,000 | grid | 562 | 3,142 | 348 |
| 100,000 | uniform | 501 | 2,154 | 180 |
| 1,000,000 | clustered | 5,563 | – | 2,182 |
| 1,000,000 | grid | 11,458 | – | 5,057 |
| 1,000,000 | uniform | 9,076 | – | 2,388 |

  `DelaunayTriangles` includes creating the triangle polygons. The triangulation scales about linearly; the NTS builder is several times slower.
- **Not now.** The Voronoi diagram, a triangulation with constraints (edges that must be kept), a mesh with neighbour access, and the concave hull of polygons (as opposed to points) are not part of this; each would be a design of its own and can be added without breaking anything. The first three came later: the Voronoi diagram in [ADR 0036](0036-voronoi-diagramm.md), the mesh with neighbours in [ADR 0037](0037-triangulation-netz-hoehe-hoehenlinien.md), the constraints in [ADR 0038](0038-constrained-delaunay-und-polygon-triangulierung.md).
- **Tests (ADR 0008):** `[Covers]` tests instead of fixtures. For the triangulation: hand cases, and property tests over random, grid, collinear and cocircular point sets, with both insertion paths, that check everything that makes a triangulation Delaunay (counter-clockwise triangles, the count `2n - 2 - h`, consistent neighbours, no point in any circumcircle, the triangles tile the convex hull); the exact incircle test against integer arithmetic; extreme magnitudes; and the equality with the NTS triangulation for points in general position. For the hulls: hand cases (a C shape), properties (valid, contains every point, lies in the convex hull, one ring without holes), the area against the NTS `ConcaveHull`, and the independence of the scale. A weekly stress test runs the triangulation and the hull on dirty points.
- **Playground and documentation:** descriptors for the three (ADR 0011; the triangles are shown as a list of parts like `Cut`), a section in the concept page for the measures and constructions, glossary entries, a benchmark.

## Considered Options

- The alpha shape (rejected: its parameter, the radius of a circle, is harder to reason about than a maximum edge length, and there is no reference to test against)
- k nearest neighbours (rejected: not a property of the points alone and not deterministic against a reference)
- Keeping the triangulation internal (rejected: it is built and tested to this standard anyway, hosts need it, and a flat list of triangles is a small, safe contract)
- A public triangulation with neighbours and Voronoi (rejected for now: a larger contract that is better designed once there is a use case)
- Bowyer-Watson with a super triangle (rejected: a finite super triangle can leave wrong hull edges for flat or very large input; the sweep with exact predicates has no such case)
- The quad-edge structure of JTS (rejected: a half-edge array is simpler, faster and easier to test)
- Letting triangles with two border edges go (rejected: it would drop their third vertex out of the hull, and the hull must contain every point)
