# A mutable mesh for Delaunay refinement

`Refine` (ADR 0041) adds points to a triangulation until every triangle is good enough. The first version rebuilt the whole constrained triangulation after every round of insertions: simple, but a full construction per round, which took 28 seconds for 100,000 points. This ADR describes the structure that replaced it: a mesh that points are inserted into one at a time. It is internal (`RefinementMesh`); the contract of `Refine` did not change.

*German version: [0045-veraenderliches-netz-fuer-die-verfeinerung.de.md](0045-veraenderliches-netz-fuer-die-verfeinerung.de.md)*

## Consequences

- **Placement:** the internal class `RefinementMesh` in `SpatialNET.Extensions` (`Internal/Triangulation/RefinementMesh.cs`), used by `Triangulation.Refine`. No public API changes; the result is an ordinary immutable `Triangulation`.
- **The structure** is that of `Triangulation` (ADR 0037): three vertex ids per triangle, counter-clockwise, and the neighbour across the edge from vertex `k` to `k + 1`. Every decision uses the exact predicates `Orient2D` and `InCircle` (ADR 0005). Triangles are **never removed**: dividing a triangle keeps its index for one of the parts, so an index stays valid, and a **version number** (the stamp) of every triangle tells a queue whether an entry is stale. The vertex ids of the original triangulation stay the same.
- **Operations:**
  - **Insert a point in a triangle** (the triangle is divided into three) **or on an edge** (two triangles into four, or one into two at the border), and then restore the Delaunay property by **flips** of the edges around the new vertex, as long as an edge is locally not Delaunay and the quadrilateral is strictly convex. A flip is never made across a protected edge.
  - **Protected edges** are those of the border (no neighbour) and the constraints. They are never flipped, only **split**: the middle is inserted on the edge, topologically, so both halves keep the flags of the edge (protected, and reported as a constraint, which the result keeps). Because the edge is divided in the structure and not found again from the points, the rounding of the middle cannot make a sliver of the two halves with their chord (a rebuild from the points could, and needed the nudge and the flood fill of the first version; both are gone).
  - **Walk.** A straight walk from the centre of a triangle to a point, by orientation tests, finds the triangle that contains the point; if a protected edge is in the way, that edge is returned instead.
- **The refinement** (the algorithm of Ruppert) works on this mesh with two queues:
  - **Edges first.** A protected edge that a vertex lies in the diametral circle of (the vertex opposite it in either of its two triangles) is split in the middle. After every change, the protected edges of the triangles that were made or changed are checked, which is enough, since only those can have a new vertex opposite.
  - **Then the worst triangle** (the smallest angle, or the largest area, first, from a priority queue; entries whose triangle has changed since are dropped). Its circumcentre is found by a walk from the triangle. If a protected edge is in the way (a sliver at the border has its centre beyond it), that edge is split instead. Otherwise the cavity (the triangles whose circumcircle holds the centre, and their neighbours) is searched for protected edges that the centre would encroach upon; if there are any, they are split and the triangle is tried again afterwards, and only if there is none the centre is inserted, with the height from the surface that is refined.
  - **A split that is forced** (because the centre of a triangle lies in the diametral circle of the edge) is made without asking whether a vertex that is there already encroaches on it: the first version of this code asked, found none, skipped the edge, and the same triangle came back again and again (found by a test that stopped at 12 points with an angle of 1.1°).
  - **The floor.** A triangle whose circumcircle is smaller than 1e-7 of the extent is left as it is, and an edge shorter than twice that is not split, as before, so that a sharp angle in the input ends in a finite mesh. The number of points added is limited by `maxSteinerPoints`, and a guard bounds the work.
- **Speed.** One-shot measurements (`--quick-terrain`, Release build), `Refine(25)` of uniformly scattered points, against the first version, which rebuilt the triangulation per round:

  | Points | rebuild per round (ms) | mesh changed in place (ms) | Triangles after |
  |---|---|---|---|
  | 1,000 | 204 | 31 | 7,333 → 5,705 |
  | 10,000 | 1,814 | 181 | 69,864 → 51,884 |
  | 100,000 | 27,707 | 2,195 | 680,104 → 510,423 |

  It is twelve times faster, and the mesh has about a quarter fewer triangles, since the worst triangle is repaired first and one at a time (the rounds repaired all the triangles that did not touch, and many of them were gone by the time the neighbours had been repaired).
- **Tests (ADR 0008):** the tests of `Refine` (ADR 0041) are unchanged and ran against both versions: no angle below the limit, the area, every original vertex kept, every new vertex on the surface, the largest area, a break line that stays a chain of constrained edges, a polygon with its border, scattered points with an irregular hull, the limit of the points, errors; the stress test on dirty sites (area preserved, no angle below the limit, heights on the surface). During development the mesh itself was checked after every few insertions: counter-clockwise triangles, neighbours that point back, no edge that is not Delaunay.
- **Not now:** a way to insert a point or to refine a triangulation that is given from outside as a public operation (changing a triangulation after it is built is not part of the library, ADR 0037; `RefinementMesh` is a tool of `Refine`), the offcentres and the concentric shells of Shewchuk for small input angles (ADR 0041), and refining with a size that depends on the place (a sizing function).

## Considered Options

- Rebuilding the constrained Delaunay triangulation after every round (the first version, ADR 0041; rejected after the measurement: a full construction per round)
- An incremental Delaunay triangulation with a point location structure (a history DAG or a grid) (rejected: the walk from the triangle whose circumcentre is wanted is short, since the centre lies near it; a structure for locating points anywhere would be needed to insert arbitrary points, which `Refine` does not)
- Removing triangles from the arrays when they are divided (rejected: the indices are used by the queues, and keeping one part in place makes a stale entry recognisable by a version number)
- Checking the whole mesh for encroached edges after every change (rejected: the edges of the triangles that were changed are the only ones whose opposite vertex can be new; that was the cost of the first version's rounds)
