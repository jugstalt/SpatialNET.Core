# Triangulation, Voronoi and terrain

Points, a Delaunay triangulation and the Voronoi diagram are three views of one idea, and a terrain model is what you do with the triangulation. This page covers what the library offers, which call answers which question, and what to keep in mind. The decisions are in [ADR 0036](../../adr/0036-voronoi-diagramm.md) (Voronoi), [ADR 0037](../../adr/0037-triangulation-netz-hoehe-hoehenlinien.md) (the triangulation as a surface) and [ADR 0038](../../adr/0038-constrained-delaunay-und-polygon-triangulierung.md) (break lines and polygons); the plain triangulation is [ADR 0029](../../adr/0029-delaunay-und-konkave-huelle.md).

| You want to … | Call | Result |
|---|---|---|
| draw the triangles of some points | `DelaunayTriangles(points)` | a flat list of triangles |
| draw or count the neighbourhood of points | `DelaunayEdges(points)` | the edges, each once |
| know which point is the nearest, as areas | `VoronoiDiagram(points, clip)` | one cell per point, cut to the clip area |
| draw the borders between those areas | `VoronoiEdges(points, clip)` | the lines between neighbouring cells |
| ask a surface for its height, its triangle, its contour | `Triangulation.FromPoints(points)` | a `Triangulation` |
| keep ridges, ditches or boundaries as edges | `Triangulation.FromPointsAndLines(points, lines)` | a `Triangulation` that contains them |
| cut a polygon into triangles | `polygon.Triangulate()` or `Triangulation.FromPolygon(polygon)` | the triangles inside the polygon |

Everywhere, the points are the **vertices of any geometry**: a multipoint, a polyline or a polygon. Vertices with the same X and Y count once, vertices that are not finite are ignored, and Z is kept where the result can carry it.

## Voronoi: which point is the nearest?

The cell of a point is the area that lies nearer to it than to every other point (a Thiessen polygon). It answers "which hydrant, which station, which school serves this place", and with the areas "how much of the district does each serve".

```csharp
var stations = Wkt.Read<IMultiPoint2D>("MULTIPOINT ((10 20), (35 80), (60 30), (85 70), (50 55))");
var boundary = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 100 0, 100 100, 0 100, 0 0))");

IReadOnlyList<VoronoiCell> cells = stations.VoronoiDiagram(boundary);   // five cells that tile the boundary
double total = cells.Sum(c => c.Cell.Area());                            // 10000
IPolyline2D edges = stations.VoronoiEdges(boundary);                     // the lines between the cells
```

- **The cell keeps its point.** `VoronoiCell` has `Site` (with its Z) and `Cell`, and the list has **one entry per distinct point, in the order of its first occurrence**.
- **The clip area decides what a border cell is.** The cells at the border are unbounded and must be cut. Without a clip area they are cut to the envelope of the points widened on every side by half of its larger extent; with a polygon they are cut to it, which is what a service area within a district needs. A point outside the polygon can still have a cell inside it; a cell that does not reach the polygon is empty (the entry stays, with an empty polygon). Use `Envelope2D.ToPolygon` for a rectangle.
- **The cells tile the clip area.** They have no gaps, do not overlap, and are valid polygons; their corners are rounded to the grid of the overlays (`gridSize`, [Overlays and the grid](overlay-and-grid.md)). Two cells that share an edge share its corners exactly. A cell that would collapse at the size of the grid (points closer together than that) is empty.
- **Few points are fine.** One point gets the whole clip area, two are split by their bisector, collinear points get strips.
- `VoronoiEdges` gives the lines **between** cells (not the border of the clip area), one path per edge, and needs at least two points.

## The triangulation as a surface

`Triangulation` keeps the triangles with their neighbours and answers questions about the surface. It is **immutable and thread-safe**: build it once, ask it many times, from many threads.

```csharp
var terrain = Triangulation.FromPoints(Wkt.Read("MULTIPOINT Z ((0 0 10), (100 0 20), (100 100 40), (0 100 30), (50 50 25))"));

double height = terrain.InterpolateZ(new Coordinate(25, 10));   // 14.5
int triangle = terrain.Locate(new Coordinate(25, 10));          // the triangle that contains the point
IPolyline2D contour = terrain.Contour(25);                      // the line at height 25, from (0 75) to (100 25)
```

- **Structure.** `Vertices`, `TriangleCount`, `Triangle(t)` (three vertex ids, counter-clockwise), `Neighbour(t, side)` (the triangle across the edge from vertex `side` to `side + 1`, or −1 at the border) and `IsConstrained(t, side)`. `ToPolygons`, `Edges` and `ConstrainedEdges` give geometries to draw.
- **`Locate(point)`** is the triangle that contains the point, on its border or inside, or −1. The test is exact; on a shared edge or vertex the smallest index wins. It uses an index of the triangles, so a question costs about a microsecond or two, however large the model.
- **`InterpolateZ(point)`** is the height in the plane of the triangle: linear, exact at the vertices, exact on a surface that is a plane. It is **`NaN`** outside the triangulation and where a vertex has no Z.
- **`Contour(z)`** is the line of one height: where the surface, linear in every triangle, has that height. Its vertices have Z = `z`; it is closed where it returns to its start and ends at the border of the model; a vertex exactly at `z` counts as above the line. For contours at many heights call it once per height.

## Break lines and polygons

A plain triangulation puts its edges where the empty-circle rule says. A **break line** (a ridge, a ditch, a bank) must be an edge, whatever the rule says. `FromPointsAndLines` keeps the segments of lines (or the rings of polygons) as chains of edges and is Delaunay everywhere else (the *constrained* Delaunay triangulation).

```csharp
var spots = Wkt.Read("MULTIPOINT Z ((0 0 5), (100 0 5), (100 40 15), (0 40 15), (50 20 9))");
var ditch = Wkt.Read("LINESTRING Z (0 20 8, 100 20 8)");   // passes through the vertex (50 20) and is split there

var surface = Triangulation.FromPointsAndLines(spots, ditch);
IPolyline2D kept = surface.ConstrainedEdges();             // two edges that together are the ditch
double onTheDitch = surface.InterpolateZ(new Coordinate(25, 20));   // 8.5: interpolated along the break line
```

- The **vertices of the lines are vertices of the triangulation**, with their Z, so a break line with Z fixes the height along it.
- A line that passes exactly through a vertex is split there. Lines may touch, share end points and overlap.
- Where two lines **cross**, they are first noded like in an overlay: the crossing becomes a vertex and the lines are rounded to the grid (`gridSize`). Without a crossing nothing is rounded.
- The triangles tile the **convex hull** of all vertices. Use `IsConstrained` or `ConstrainedEdges` to tell the break lines from the rest.

A polygon is cut into the triangles inside it:

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 0, 10 10, 6 10, 6 4, 4 4, 4 10, 0 10, 0 0))");

IReadOnlyList<IPolygon2D> triangles = parcel.Triangulate();   // 8 vertices: 6 triangles inside the U
```

- A polygon without holes and with `n` vertices has `n − 2` triangles, every hole adds two. No vertex is added. Every edge of a ring is an edge of a triangle.
- The inside is decided by the even-odd rule, so holes stay out and an invalid polygon is read the way `MakeValid` reads it.
- `Triangulation.FromPolygon(polygon)` gives the same triangles with their neighbours; at the border of the polygon the neighbour is −1.

## Things to keep in mind

- **Exact where it decides, rounded where it constructs.** Which points are neighbours, which triangle contains a point and whether a constraint is an edge are decided with the exact predicates of the overlays; the corners of Voronoi cells (circumcentres) and contour points are computed in floating point. The Voronoi cells are rounded to a grid so that they stay valid.
- **Points on a common circle** (the corners of a rectangle, a regular grid) allow several equally Delaunay triangulations. Which one comes out is deterministic but not specified; everything else is the contract.
- **Triangulating a long boundary is slower than triangulating the same number of scattered points**, because the Delaunay triangulation of points on a curve consists of long thin triangles. A ring of 10,000 vertices takes about a tenth of a second; polygons of 100,000 vertices and more take seconds ([ADR 0038](../../adr/0038-constrained-delaunay-und-polygon-triangulierung.md)).
- **Z is the height**, and only Z: a triangulation of points without Z has no heights (`InterpolateZ` is `NaN`, `Contour` is empty), and M is not kept.
- **Not included:** slope, aspect and volume, other interpolations than linear in the triangle, changing a triangulation after it is built, and triangulations that add points (conforming or quality meshes).
