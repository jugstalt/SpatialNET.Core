# The viewshed: what can be seen from a point

`LineOfSight` (ADR 0041) answers whether one target can be seen. Planning a wind turbine, a lookout, a mast or a road asks for **all targets at once**: which part of the landscape can be seen from here, and which lies behind a ridge. `Viewshed` returns it as a polygon.

*German version: [0044-sichtbarkeitsflaeche.de.md](0044-sichtbarkeitsflaeche.de.md)*

## Consequences

- **Placement:** a member of `Triangulation` (ADR 0037) in its own file, `Triangulation.Viewshed.cs` in `SpatialNET.Extensions`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **API:**

  ```csharp
  IPolygon2D Viewshed(Coordinate observer, double maxDistance, double observerHeight = 0, double targetHeight = 0, int rays = 360, IGeometryFactory? factory = null)
  ```

  The eye is at `observerHeight` above the ground at `observer`, a target at `targetHeight` above the ground at its place; the result is everything within `maxDistance` that can be seen. `rays` is from 8 to 100,000. A point outside the surface, or a vertex without height, has nothing to see: an empty polygon. The result may have several parts (and holes, where a rise hides a part of the ground).
- **How it works: a fan of rays, exact along every ray.** `rays` rays leave the observer at equal angles.
  - **The profile of a ray** is the line over the surface that `Drape` (ADR 0039) computes, with the height at every edge it crosses; the surface is linear between two such points. The code of `Drape` was split for this (the part that drapes one segment is shared; `Drape` is unchanged).
  - **The horizon is followed exactly.** Along the profile the steepest slope from the eye to the ground so far (the horizon) is kept. Over a stretch of the profile the ground is `z = z₀ + s · (d − d₀)`, so the slope from the eye to it is `s + c / d`, and to a target above it `s + c′ / d`: both are monotonic in the distance `d`, so every boundary of a visible stretch is the solution of a linear equation. The visible stretches of a ray come out as exact intervals, not as the result of sampling. A rising stretch whose slope rises above the horizon becomes the new horizon (the ground itself is seen); a falling one is below it, and a target above it is seen only where its slope is not below the horizon. A stretch of a ray that leaves the surface has no ground to see and does not block the view. (A first version took ground that falls away behind a crest, where the slope to the ground is equal to the horizon exactly, for ground on the horizon and showed everything behind a ridge: found by the test on a ridge.)
  - **Slices instead of a union.** A ray stands for the slice of the angle around it, from half a step before to half a step after. The visible stretches of a ray are the radial extent of its slice. Neighbouring slices meet exactly along the angle between two rays, so the boundary of the visible area is what is left of the sides of a slice where the neighbouring ray does not see (the difference of two sorted lists of intervals), and the arcs at the near and the far end of every stretch. These edges, directed with the area on the left, are chained to rings by the code of `SlopeZones` (ADR 0041): holes are given to the smallest ring around them, and a ring that touches itself is cut. **No overlay is made**: the union of the thousands of quadrilaterals by `UnionAll` was the slowest part (0.56 s against 0.075 s for 360 rays on 10,000 points). The ends of the stretches are rounded to a billionth of the distance, so that the same place seen along two rays (the end of the distance, the edge of the surface) is the same number and does not make a sliver of the side between two slices.
  - **Accurate along the rays, approximate between them.** The arcs are chords, and the edge between two rays is only known at the rays: at a distance `r` it is accurate to about `r · π / rays`, which is 1 % of the distance for 360 rays and 0.1 % for 3600. A ridge that is narrower than a slice can slip through between two rays. The parallel computation of the rays is joined in the order of the rays, so the result is the same on every run.
- **Monotone.** Along every ray a higher eye, a higher target or a longer distance sees at least what the lower one saw, and so does the polygon (tested). A target that is seen by `LineOfSight` lies in the viewshed, and one that is not does not, up to the approximation between the rays (tested against random terrain: at most one in ten of the targets near an edge disagree, with 720 rays).
- **Speed.** One-shot measurements (`--quick-terrain`, Release build, the observer in the middle, looking a third of the extent far):

  | Points | 360 rays | 1,440 rays |
  |---|---|---|
  | 10,000 | 75 ms | 304 ms |
  | 50,000 | 250 ms | 2.7 s |

  The cost is that of `rays` lines over the surface, not of the size of the surface, so it grows with the number of rays and the distance.
- **Tests (ADR 0008):** a disc on flat ground and a quarter of it at a corner, a ridge that hides what is behind it (the area and the position of points in front of and behind it), monotony in the heights and the distance, the agreement with `LineOfSight` for random targets on random terrain, an observer outside, no heights, the order of the threads, errors; the stress test on dirty sites (valid polygons, within the disc, growing with the height of the eye).
- **Playground and documentation:** the entry `viewshed` (the second input is the observer, the hill example is shared), a cookbook recipe "What can be seen from a point", the concept page, glossary.
- **Not now:** the curvature of the Earth and the refraction of the air (the surface is planar, ADR 0010; for a view of tens of kilometres project and correct the heights beforehand), the viewshed of a line or an area (the union of the viewsheds of its points; call `Viewshed` per point and unite), a view that is limited in the angle (a cone, a field of view), and an exact boundary between the rays (the visibility region of a triangulation is a polygon with curved edges, which a fan of rays approximates).

## Considered Options

- Testing `LineOfSight` to every cell of a grid (rejected: a raster, with the cost of cells times the length of the line, and the result is a staircase; the rays follow the horizon exactly and the result is a polygon)
- Uniting the slices with `UnionAll` (rejected: eight times slower; neighbouring slices meet along an exact angle, so their common sides need not be computed twice by an overlay)
- Rays at the vertices of the triangulation (the exact method of Franklin and Ray, a sweep around the observer that stops at every vertex and edge) (rejected for now: it gives the exact boundary but is a much larger piece of work with many degenerate cases (collinear vertices, edges through the observer); the fan answers the question for the uses named here, with an accuracy that is a parameter)
- A smaller default of the number of rays or a rule from the distance (rejected: the accuracy depends on how the result is used; 360 rays is one degree, and the documentation says how the error grows)
- Returning a raster of visible cells (rejected: the library works with vector geometry; a raster can be made from the polygon by the caller)
