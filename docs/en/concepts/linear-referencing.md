# Linear referencing

Linear referencing addresses a position on a line by a single number: the **length along the line** or an **M value** that the vertices carry (for example the kilometre of a road). `LinearReferencing` offers six methods on polylines, in two variants ([ADR 0017](../../adr/0017-lineare-referenzierung-laenge-und-m-erster-treffer.md)):

| By length | By M | Meaning |
|---|---|---|
| `Interpolate(distance)` | `InterpolateByM(m)` | the point at a position |
| `Project(point)` | `ProjectToM(point)` | the position of the point nearest to a given point |
| `Substring(from, to)` | `SubstringByM(fromM, toM)` | the piece between two positions |

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 100, 10 0 200, 10 10 250)");

Coordinate? at15 = road.Interpolate(15);        // (10 5), M = 225: M is interpolated, too
double position = road.Project(new Coordinate(4, 3)); // 4: the nearest point is (4 0)
Coordinate? km = road.InterpolateByM(150);      // (5 0): where the line first reaches M = 150
IPolyline2D piece = road.SubstringByM(150, 225); // from (5 0) via (10 0) to (10 5)
```

## Lengths

A length is the planar length along the line, the same as `Length()`; Z does not take part. The paths of a polyline are **laid end to end** in their order, and no connection is assumed between the end of one path and the start of the next. A length exactly on the boundary of two paths belongs to the end of the earlier path.

- `Interpolate` **clamps** the distance to `[0, length]`. A negative distance gives the start (it does not count from the end, as in JTS: use `length - d`).
- `Project` returns the length of the nearest point. If several points are equally near, for example where a line crosses itself or doubles back, the **smallest length** wins.
- `Substring` clamps both lengths. If `from` is greater than `to`, the piece runs in the opposite direction. Equal lengths give an empty polyline: a piece without length is nothing. A piece that spans several paths has one path per touched path; the gap between them is not bridged.

## M values

M addresses a position only where the line carries M, and it never changes the geometry. The rules:

- **Only segments with M at both ends take part.** A stretch without M is neither interpolated across nor an error; it simply cannot answer, so `InterpolateByM` returns `null`, `ProjectToM` returns `NaN` and `SubstringByM` returns an empty polyline.
- **First hit along the line.** Real M values are not always monotonic: a road may loop, or M may reset. The line is scanned in its order, and the first segment whose M range contains the value (closed interval) answers. A value that occurs several times is therefore found at its first occurrence, and nothing throws.
- Z and M of the resulting points are **interpolated on the segment** if both ends carry them; at an original vertex, the point has exactly that vertex's own values ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)).

## Calibrating a line from anchor points

A road section often has no M, and the measures sit on **points along it** (kilometre posts). `CalibrateM` turns the points into M on the line, once ([ADR 0030](../../adr/0030-calibratem-m-werte-aus-ankerpunkten.md)); after that, `ProjectToM` and `InterpolateByM` answer from the line.

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0, 10 20)");
var posts = new[] { new Coordinate(5, 0, M: 100), new Coordinate(10, 15, M: 200) };

IPolyline2D withM = road.CalibrateM(posts, maxSnapDistance: 0.5);
double m = withM.ProjectToM(new Coordinate(10, 4)); // 145: 100 + (200 - 100) / (25 - 5) * (14 - 5)
```

- Every anchor is **projected** onto the line, a vertex is inserted at its position, and M is interpolated **linearly in the length along the line** between the anchors, the same as interpolating between the previous and the next post by hand. The order of the anchors does not matter, M need not be monotonic, and the M the line carries is replaced.
- `maxSnapDistance` (map units) is required. An anchor farther from the line is **an error**, not skipped: a post that is not on the road is a data problem.
- Two anchors at the same position must agree in M, otherwise it is an error.
- Before the first and after the last anchor, `MExtrapolation` decides: `None` (default) leaves that stretch without M, so `ProjectToM` gives `NaN` there; `Clamp` uses the nearest anchor's M; `Linear` continues the slope of the end interval.
- On a line that crosses itself, an anchor near the crossing lands on the earlier pass, as `Project` does.

## Values, not geometries

As elsewhere ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)), point-like and scalar results are values (`Coordinate?`, `double`) that need no factory. Only the pieces of `Substring` and `SubstringByM` are geometries and are created through the factory; they are restricted to what it supports (`SupportsZ`, `SupportsM`).

## Edge cases

Empty and degenerate lines never throw: `Interpolate` returns `null` for a line without points, `Project` returns `NaN`, and `Substring` returns an empty polyline. `NaN` or infinite parameters throw `ArgumentOutOfRangeException`, as for all algorithms.
