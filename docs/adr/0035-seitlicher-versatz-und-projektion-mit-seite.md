# Offset to the side and projection with distance and side

Two things that a web GIS does with a route and that belong together. First, a symbol, a label or an event is drawn **beside** the road, not on its axis: "kilometre 12.4, 3 m to the right". Second, a click should say not only "kilometre 12.437" but also **"3 m left of the axis"**, so that the host can decide whether the click meant the road at all. Both are one quantity, the signed distance to the line, and they are the two directions of one conversion: `Interpolate(position, offset)` goes from a position and an offset to a point, and **`ProjectDetailed`** goes from a point back to position and offset.

*German version: [0035-seitlicher-versatz-und-projektion-mit-seite.de.md](0035-seitlicher-versatz-und-projektion-mit-seite.de.md)*

## Consequences

- **Placement:** two new overloads, `Interpolate(distance, offset)` and `InterpolateByM(m, offset)`, the method `ProjectDetailed` (four overloads) and the value type `LineProjection` in `LinearReferencing` (ADR 0017) in `SpatialNET.Extensions`, namespace `SpatialNET`. The existing methods are unchanged, so for the package validation and the API tracking this is an addition only (ADR 0028).
- **The sign:** a **positive offset is to the left of the direction of the line, a negative one to the right**, in the usual mathematical sense (counter-clockwise): going east, left is north. This is the convention of `BufferOptions.SingleSided` (positive left, ADR 0006) and of NTS `LengthIndexedLine.ExtractPoint(index, offset)`, with which the length variant is checked in the tests inside the segments.
- **`Interpolate(distance, offset)`** returns the point at `distance` along the line, moved by `offset` perpendicular to the line. With 0 it is `Interpolate(distance)`. `InterpolateByM(m, offset)` does the same for the first hit of an M value. Both return a `Coordinate?`, a value without a factory (ADR 0012).
  - **The direction** is that of the segment that contains the position. At a **vertex** it is the segment that **ends there**, the earlier one, as the boundary of two paths belongs to the end of the earlier path (ADR 0017), **where NTS takes the segment that starts there** (a documented divergence, tested); at the start of the line it is the first segment. A distance outside the line is clamped (ADR 0017) and uses the first or the last segment. The point is not an arc around a corner: at a vertex the offset point jumps from one side of the bend to the other, which is what a drawing at that position wants.
  - **Segments of length 0 are skipped** for the direction. A line without any segment of length has no direction, so an offset other than 0 gives `null` (and a line without points `null`); an offset of 0 needs no direction and gives the point.
  - Z and M are those of the point **on the line**; the offset moves X and Y only (ADR 0015).
- **`ProjectDetailed(point)`** returns a `LineProjection?` (`null` for a line without points):

  | Field | Meaning |
  |---|---|
  | `Position` | the length of the nearest point, as `Project` |
  | `Point` | the nearest point on the line, with Z and M interpolated on the segment, as `ProjectToM` reads it |
  | `Distance` | the distance of the point to it, not negative |
  | `Offset` | `Distance` with the side as its sign: positive left, negative right |

  `Position` and `Point` are those of `Project` and `ProjectToM`, with the same tie rule (the smallest length of equally near points). Overloads for `IPoint2D`, and with a **window of lengths** `(from, to)` as in ADR 0031, so that a point near a loop can choose its pass and still get its side.
- **Round trip.** For a point whose nearest point lies inside a segment, `Interpolate(Position, Offset)` gives the point back. Where the nearest point is a **vertex or an end** the perpendicular is not unique, and `Interpolate` uses a different segment than the point's nearest one in general, so the round trip is not promised there.
- **The side of the segment of the nearest point.** At a convex vertex both segments are equally near for a point in the outer wedge; the first one wins as for `Project`, so the side is that of the **earlier** segment, as the direction rule of `Interpolate` is. A point beyond the end of the line has its nearest point at the end and the side of the **extension** of the end segment. A point exactly **on that extension** (or on the line, with distance 0) is on neither side: its `Offset` counts as left, that is `+Distance`, which is 0 on the line. A segment of length 0 at the nearest vertex gives way to a real one, so a line that starts with a duplicate vertex still has a side.
- **Exceptions (ADR 0012):** `ArgumentNullException` for `null`, `ArgumentOutOfRangeException` for a non-finite point, `offset`, `distance`, `m`, `from` or `to`. An empty line answers `null`, never throws.
- **Cost:** O(n) like `Project`; the directions of the line are built once per call, only when an offset is asked for.
- **Tests (ADR 0008):** NTS has `ExtractPoint(index, offset)`, so the offset variant is compared with it for random lines inside the segments (the vertex rule is this library's own and is tested by hand). The side of `ProjectDetailed` is tested by hand (left, right, on the line, beyond the end, on the extension, a loop with a window, duplicate vertices), and a **round-trip property** checks `Interpolate(Position, Offset)` against the input point for random lines and points. Also `ProjectDetailed` against `Project` and `ProjectToM`, null results and invalid input.
- **Playground and documentation:** `Interpolate` keeps its descriptor under the same name; `ProjectDetailed` has none (ADR 0011), as its result is a structure the surface does not show; it is listed as exempt with this reason. The concept page "linear referencing" has a section with an example, and the glossary has "offset to the side".
- **Out of scope for now:** an arc or a mitre around a corner for an offset line (that is `OffsetCurve`, ADR 0006), the offset of a whole `Substring`, and a `LineProjection` for events.

## Considered Options

- Adding `offset` as an optional parameter to `Interpolate(distance)`, rejected: it would change a shipped signature and break binary compatibility; a new overload keeps the old call as it is
- A result of `Project` with the offset as `out` parameters, rejected: a record struct is easier to read and to extend, and `Project` stays a plain number
- ADR 0017 had rejected "a structure with length, point and distance", on the grounds that `Interpolate(Project(p))` and `Distance` give the rest; that holds for the point but not for the **side**, which cannot be had from those calls, and the side is what makes the conversion reversible. This ADR adds the structure for that reason
- Positive offset to the right, rejected: the mathematical and NTS convention is left, and `SingleSided` uses it
- A separate `Side` enum next to the unsigned distance, rejected: one signed number is what `Interpolate` takes back, and a side of "on the line" would need a third value that a sign of 0 already is
- The bisector of the bend at a vertex for the direction, rejected: it gives a point that is not at the offset distance from either segment and differs from JTS; the earlier segment is simple and predictable
- Offsetting along the clamped distance as an arc beyond the ends, rejected: a position beyond the line is clamped to its end anywhere in this API (ADR 0017), and the end segment's direction is the natural continuation
- Following NTS at a vertex (the segment that starts there), rejected: this API resolves every boundary to the end of the earlier part (ADR 0017), and one rule for positions that fall on a vertex is easier to remember than a different one for offsets; the divergence is documented and tested
- Returning `null` instead of 0 for the offset of a point on the extension, rejected: it has a distance and a nearest point, and "left" by convention keeps `Offset` a number that `Interpolate` accepts
