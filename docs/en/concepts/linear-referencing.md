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

## What it is for

A road is a line geometrically, but for the business it is a **route with a chainage**: damage at kilometre 12.4, speed limit 50 from 3.0 to 5.5, bridge at 8.2. The data hangs on the chainage, not on coordinates, and linear referencing translates between the two: coordinate to measure, and measure to point or stretch. The typical flow in a web GIS:

1. **Load the route once.** A road section often has no M, and the measures sit on separate points (kilometre posts). `CalibrateM` turns the posts into M on the line. Afterwards one call per click is enough, instead of looking up the previous and the next post and calculating each time, and the calibrated line can be stored or cached. An anchor farther from the line than the tolerance is an error: a post that lies 40 m off the road shows up at once instead of silently giving wrong kilometres.
2. **A click becomes a measure.** `ProjectToM` turns a click on the map into "kilometre 12.437", to show or to store as the position of a report.
3. **A measure becomes a point.** `LocateAlong` answers "show kilometre 12.5". Usually that is one point; where a route passes the same place twice or M was reset, there are several, and it returns all of them.
4. **A range becomes a stretch.** `LocateBetween` draws "speed limit 50 from 3.0 to 5.5" or "roadworks from 12 to 15" as a line that follows the road, not only its two end points. It runs in the direction of the line and has several paths if the route passes the range more than once or has gaps without M.
5. **Hairpin bends and loops.** Where a line comes near itself, a click lies between two passes and `Project` gives the earlier one. `Project(point, from, to)` restricts the search to the section you are working in, or to the part after the last hit. The same helps when a GPS track follows the road: every point is projected near the previous one.

The [cookbook](../cookbook.md#a-road-with-kilometre-posts) has this flow as a runnable recipe.

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

## All stretches and all points for M

The `...ByM` methods above follow the first hit. When M is not monotonic, or you want everything between two values, `LocateBetween` and `LocateAlong` find all of it ([ADR 0032](../../adr/0032-locatebetween-und-locatealong.md)):

```csharp
IPolyline2D km12to15 = route.LocateBetween(12_000, 15_000);  // every stretch with M in [12000, 15000]
IMultiPoint2D at12_5 = route.LocateAlong(12_500);            // every point where M is 12500
```

- `LocateBetween` returns the stretches as one polyline: consecutive segments form one path, a gap (outside the interval, or without M) starts the next. The interval is closed, swapped bounds are the same interval, and the direction of the line is kept. A stretch without length (M touches the interval at one point) is left out.
- `LocateAlong` returns every hit in the order along the line; a hit at a vertex counts once, and a segment with constant M answers with its start, like `InterpolateByM`.
- Both use only segments with M at both ends; no hit gives an empty result.

## Lines that cross themselves: a window of lengths

A hairpin bend or a loop gives a click several nearest points, and `Project` answers with the smallest length, the earlier pass. `Project(point, from, to)` and `ProjectToM(point, from, to)` search only the stretch between two lengths and still answer with a length (or M) on the whole line ([ADR 0031](../../adr/0031-project-mit-laengenfenster.md)):

```csharp
double first = road.Project(click);                     // the earlier pass
double second = road.Project(click, 11, road.Length()); // only after length 11
```

Both lengths are clamped, swapped if `from` is greater than `to`, and the window is closed. A point that is nearer to the line outside the window gets the nearest point inside it. "After a position" is the window `[position, Length()]`.

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
