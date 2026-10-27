# Cut

`Cut` splits a polygon or a polyline along a cutting line. It returns a flat list of the pieces, without a left/right assignment, like the cut functions of editing tools ([ADR 0007](../../adr/0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.md)).

```csharp
var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var cutter = Wkt.Read<IPolyline2D>("LINESTRING(5 -1, 5 11)");

IReadOnlyList<IPolygon2D> pieces = square.Cut(cutter);

Console.WriteLine(pieces.Count);      // 2
Console.WriteLine(pieces[0].Area());  // 50
```

The signatures are `Cut(this IPolygon2D, IPolyline2D cutter, IGeometryFactory? factory = null, double? gridSize = null)` returning `IReadOnlyList<IPolygon2D>`, and `Cut(this IPolyline2D, IPolyline2D cutter, …)` returning `IReadOnlyList<IPolyline2D>`.

## What counts as a piece

- Every **cut piece** is an element of its own.
- All **untouched** parts (or paths) stay together as **one element** and keep their original coordinates, without snap rounding. A multipart feature therefore does not fall apart incidentally, and whatever the cut does not hit stays unchanged.
- If nothing is cut, the list contains exactly one element.
- The order of the elements is not fixed, but the result is deterministic.

## Polygons

The pieces are the faces of the noded polygon boundary plus the parts of the cutting line in the interior. Only complete crossings cut: loose ends of the cutting line, runs along the boundary and touches at vertices do not. Parts of the cutting line that lie in a hole do not count, and the holes are distributed over the pieces accordingly.

## Polylines

A polyline is split at every contact with the cutting line: a crossing, a touch at a point, and the start and end of a collinear overlap. Each piece is a polyline with exactly one path.

## Good to know

- **All paths of the cutting line cut**, so several cuts in one call are possible. There is no options class.
- **The cutting line is not extended** by `Cut`. To bring its ends onto the boundary, snap it first (next section).
- Z and M at intersection points are interpolated on the original segment.

## Data with small errors: snap first

`Cut` is exact: an end of the cutting line that stops 4 mm short of the edge is a loose end and cuts nothing. That happens easily with drawn or projected data: a map that snaps the end onto the edge as it is straight in Web Mercator leaves it beside the edge once the vertices are projected to UTM (a fraction of a millimetre on a 100 m edge, centimetres on a few kilometres). **`SnapTo` moves the ends onto the polygon first** ([ADR 0046](../../adr/0046-fangen-an-ein-ziel.md)):

```csharp
IReadOnlyList<IPolygon2D> pieces = parcel.Cut(fence.SnapTo(parcel, tolerance: 0.01));
```

For a polyline `SnapTo` moves only the **loose ends** by default (`SnapMode.Ends`): an end short of the target goes to the nearest point (or, with `SnapEnds.Extend`, along its last segment), an end that runs over it by less than the tolerance is cut back, and a corner of the target within the tolerance wins, so that no sliver is left beside it. The inner vertices and the rest of the line stay. The tolerance is required and in the units of the coordinates; a point beyond it stays where it is. The same step helps before the overlays and the predicates: `SnapMode.Vertices` moves every vertex and inserts the corners of the target, so that two neighbouring parcels share their boundary afterwards.
