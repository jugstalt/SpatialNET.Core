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

A length is the planar length along the line, the same as `Length()`; Z does not take part. The paths of a polyline are **laid end to end** in their order, and no connection is assumed between the end of one path and the start of the next.

- `Interpolate` **clamps** the distance to `[0, length]`. A negative distance gives the start (it does not count from the end, as in JTS: use `length - d`).
- `Project` returns the length of the nearest point. If several points are equally near, for example where a line crosses itself or doubles back, the **smallest length** wins.
- `Substring` clamps both lengths. If `from` is greater than `to`, the piece runs in the opposite direction. Equal lengths give an empty polyline: a piece without length is nothing. A piece that spans several paths has one path per touched path; the gap between them is not bridged.

## M values

M addresses a position only where the line carries M, and it never changes the geometry. The rules:

- **Only segments with M at both ends take part.** A stretch without M is neither interpolated across nor an error; it simply cannot answer, so `InterpolateByM` returns `null`, `ProjectToM` returns `NaN` and `SubstringByM` returns an empty polyline.
- **First hit along the line.** Real M values are not always monotonic: a road may loop, or M may reset. The line is scanned in its order, and the first segment whose M range contains the value (closed interval) answers. A value that occurs several times is therefore found at its first occurrence, and nothing throws.
- Z and M of the resulting points are **interpolated on the segment** if both ends carry them; at an original vertex, the point has exactly that vertex's own values ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)).

## Values, not geometries

As elsewhere ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)), point-like and scalar results are values (`Coordinate?`, `double`) that need no factory. Only the pieces of `Substring` and `SubstringByM` are geometries and are created through the factory; they are restricted to what it supports (`SupportsZ`, `SupportsM`).

## Edge cases

Empty and degenerate lines never throw: `Interpolate` returns `null` for a line without points, `Project` returns `NaN`, and `Substring` returns an empty polyline. `NaN` or infinite parameters throw `ArgumentOutOfRangeException`, as for all algorithms.
