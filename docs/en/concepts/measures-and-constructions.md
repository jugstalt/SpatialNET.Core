# Measures and constructions

Beyond `Area`, `Length`, `Centroid` and `Distance`, a handful of small helpers cover what hosts need every day ([ADR 0019](../../adr/0019-mess-und-punkt-helfer-werte-statt-geometrien.md)). Point-like and scalar results are **values** that need no factory; geometric results are created through the factory and restricted to what it supports.

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var well = Wkt.Read<IPoint2D>("POINT(14 5)");

var (onParcel, onWell) = parcel.NearestPoints(well)!.Value;  // (10 5) and (14 5)
bool near = parcel.IsWithinDistance(well, 5);                // true: 4 away, and it stops as soon as it knows
Coordinate? label = parcel.PointOnSurface();                 // a point inside, for a label
IPolyline2D outline = parcel.Boundary();                     // the ring as a line
IPolyline2D dense = outline.Densify(2.5);                    // no segment longer than 2.5
IPolyline2D back = outline.Reverse();                        // the same line, backwards
```

## Measures

- **`NearestPoints(a, b)`** returns the two points that realise `Distance`, as `(OnA, OnB)?`, `null` if either geometry is empty. If the geometries intersect or one lies in the area of the other, both points are the same. Among equally near pairs the first in input order wins. Z and M are interpolated on their segment. A line between the points is one call to the factory away.
- **`IsWithinDistance(a, b, d)`** is `Distance(a, b) <= d`, but starts with the envelopes and stops at the first pair of segments within the distance, so it is much faster than the full distance for large inputs. `d = 0` means the geometries touch or overlap; empty geometries give `false`; a negative or non-finite `d` throws.
- **`PointOnSurface(g)`** returns a point that lies on the geometry, without Z and M. For a polygon it is an interior point from the widest stretch of a horizontal line through the envelope, so it also works for a C-shaped parcel where the centroid lies outside. For a line it is the interior vertex nearest to the centroid, otherwise the middle of the line.

## Constructions

- **`Boundary`** of a polygon is its rings as a polyline (normalised: shells counter-clockwise, holes clockwise). The boundary of a polyline is its end points by the mod-2 rule ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.md)): an end point that occurs an odd number of times; closed paths have none. Points have an empty boundary.
- **`Densify(maxSegmentLength)`** splits every longer segment into equal pieces no longer than the limit. The shape and all original vertices stay; Z and M of the new vertices are interpolated. Polygons stay valid. A limit so small that the result would exceed 50 million vertices throws.
- **`Smooth(iterations, method)`** rounds the corners of a polyline, or the rings of a polygon ([ADR 0043](../../adr/0043-glaetten-von-linien-und-flaechen.md)): `Chaikin` cuts the corners (the curve stays inside the hull of the vertices), `CatmullRom` is a spline through every vertex. The ends of an open path stay, a closed path is smoothed all the way round, and Z and M are interpolated along the segments, so a contour line keeps its height. Every iteration doubles the number of segments; two or three look smooth. `SmoothToTolerance(tolerance)` stops by itself, when no vertex lies further than the tolerance (in the units of the coordinates) from the line between its neighbours. Two lines that run closer than the curve moves can cross after smoothing; to smooth contour lines see `Triangulation.Resample` ([triangulation and terrain](triangulation-and-voronoi.md)).
- **`LineMerge`** joins the paths of a polyline where exactly two path ends meet ([ADR 0023](../../adr/0023-linemerge-und-polygonize.md)); this is what `Union` of lines does not do. Equality of the end points is exact (no tolerance), a node where three or more ends meet keeps its paths apart, a cycle becomes one closed path, and with `directed: true` only end to start is joined. It does not node: crossing lines stay as they are (run `Union` first for that).
- **`Reverse`** reverses every path and the order of the paths of a polyline. There is none for polygons, because their orientation is normalised ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)).

## Transformations and checks

Moving, scaling and rotating, a few yes/no questions about the form of a geometry, and the Hausdorff distance ([ADR 0025](../../adr/0025-transformationen-pruefungen-und-hausdorff.md)):

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var track = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 4 4, 8 0)");

IPolygon2D moved = parcel.Translate(100, 50);                          // dx, dy
IPolygon2D turned = parcel.Rotate(Math.PI / 4, parcel.Centroid());     // radians, counter-clockwise
IPolygon2D mirrored = parcel.Scale(-1, 1);                             // still valid, rings oriented again

bool simple = track.IsSimple();                                        // true: no self-intersection
double apart = track.HausdorffDistance(Wkt.Read("LINESTRING(0 0, 8 0)")); // 4: the apex is 4 away
```

- **`Translate(dx, dy)`, `Scale(sx, sy, origin?)`, `Rotate(angle, origin?)`** change X and Y and keep Z and M. The origin is (0, 0) unless given; angles are radians, counter-clockwise; quarter turns are exact. A negative factor mirrors, and the rings of a polygon are oriented again, so it stays valid. **`Transform(Func<Coordinate, Coordinate>)`** maps every vertex through your function (it can make a polygon invalid if it is not a one-to-one map). There are overloads for each geometry kind.
- **`IsSimple`** is the OGC definition: a line is simple if it does not touch or cross itself except at the end points; open paths may meet at their ends, the closing point of a ring may not be touched. Points and polygons are simple (use `IsValid` for areas). **`IsClosed`** and **`IsRing`** are for polylines: closed paths, and one closed, simple path of four points or more.
- **`EqualsExact(a, b, tolerance = 0)`** compares coordinates **as given**, vertex by vertex, within the tolerance; a ring that starts elsewhere is different. For the same set of points use `EqualsTopo`.
- **`HausdorffDistance(a, b, densifyFraction = 1)`** is the discrete Hausdorff distance: how far the shapes are apart as a whole (the largest distance from any vertex of one to the line work of the other, in both directions). A smaller fraction adds sample points along the segments and approaches the exact value from below. Polygons count by their rings.

## Bounding shapes and a label point

Three constructions for maps and layouts ([ADR 0026](../../adr/0026-huellformen-und-beschriftungspunkt.md)):

```csharp
var lot = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 10, 0 10, 0 0), (6 2, 14 2, 14 8, 6 8, 6 2))");

IGeometry2D box = lot.MinimumRotatedRectangle();   // the tightest rectangle, at any angle
Circle? around = lot.MinimumBoundingCircle();      // centre and radius of the smallest circle
IPolygon2D disc = around!.Value.ToPolygon();       // the same circle as a polygon
Coordinate? label = lot.Polylabel();               // inside, and as far from the edge as possible: not in the hole
```

- **`MinimumRotatedRectangle`** is the rectangle of least area (not of least width), found with rotating calipers over the convex hull. A degenerate input gives a point or the line between the extreme points, as `ConvexHull` does.
- **`MinimumBoundingCircle`** returns a `Circle` (centre and radius), `null` if the geometry is empty. `ToPolygon(factory, quadrantSegments)` makes an inscribed polygon from it.
- **`Polylabel(polygon, tolerance?)`** is the pole of inaccessibility: the point inside that is farthest from the boundary, holes included, within the tolerance (default a thousandth of the larger side of the envelope). For a polygon of several parts it is the pole of the part with the largest circle.

## Triangulation and concave hull

The Delaunay triangulation of the vertices of a geometry, and a hull that wraps them more tightly than the convex hull ([ADR 0029](../../adr/0029-delaunay-und-konkave-huelle.md)):

```csharp
var wells = Wkt.Read("MULTIPOINT ((0 0), (4 0), (8 0), (0 4), (4 4), (8 4), (0 8), (4 8), (8 8), (12 2), (12 6), (13 4))");

IReadOnlyList<IPolygon2D> triangles = wells.DelaunayTriangles();      // no vertex inside the circumcircle of a triangle
IGeometry2D around = wells.ConcaveHull(5.5);                          // keeps every edge on the border up to 5.5 long
IGeometry2D byRatio = wells.ConcaveHullByRatio(0.3);                  // the same, scale-free: 0 = tightest, 1 = convex hull
```

- **`DelaunayTriangles`** returns a flat list of triangles (counter-clockwise polygons) that tile the convex hull, such that no vertex lies in the circumcircle of a triangle. Points on a common circle allow several triangulations; the result is one of them. The triangulation as a structure (neighbours, height at a point, contour lines), break lines, polygons and the Voronoi diagram are on the page [Triangulation, Voronoi and terrain](triangulation-and-voronoi.md).
- **`ConcaveHull(maxEdgeLength, holesAllowed)`** takes triangles away from the border, longest border edge first, while that edge is longer than the limit; the polygon stays connected and contains every vertex. `ConcaveHullByRatio` gives the limit as a ratio of the edge lengths of the triangulation (0 to 1; 1 is the convex hull). With `holesAllowed` it may also open holes inside. A geometry with fewer than three different or only collinear vertices gives what `ConvexHull` gives.
- Choosing the limit: a limit a little above the typical distance between neighbouring points keeps the outline faithful; a larger one fills gaps, a smaller one opens bays.
