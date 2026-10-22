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
- **`LineMerge`** joins the paths of a polyline where exactly two path ends meet ([ADR 0023](../../adr/0023-linemerge-und-polygonize.md)); this is what `Union` of lines does not do. Equality of the end points is exact (no tolerance), a node where three or more ends meet keeps its paths apart, a cycle becomes one closed path, and with `directed: true` only end to start is joined. It does not node: crossing lines stay as they are (run `Union` first for that).
- **`Reverse`** reverses every path and the order of the paths of a polyline. There is none for polygons, because their orientation is normalised ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.md)).
