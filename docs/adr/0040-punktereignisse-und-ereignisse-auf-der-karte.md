# Point events and drawing events on the route

`LineEvents` (ADR 0033) does the arithmetic on tables of events with a range. A route system also has events at a **single M value**: a bridge, a culvert, a damage report, a hydrant. Their questions are different: *which speed limit holds at this bridge?*, *which bridges lie on this stretch?*, *which have no speed limit at all?* And every table, of ranges or of points, has to be **drawn on the map**. This adds the type **`PointEvent<T>`**, the operations **`PointEvents.Between`**, **`Within`** and **`Outside`**, and **`LocateEvents`**, which draws a whole table on the route.

*German version: [0040-punktereignisse-und-ereignisse-auf-der-karte.de.md](0040-punktereignisse-und-ereignisse-auf-der-karte.de.md)*

## Consequences

- **Placement:** `PointEvent<T>` and the static class `PointEvents` in `SpatialNET.Extensions` (their own file), and the two `LocateEvents` extension methods in `LinearReferencing` (ADR 0017), namespace `SpatialNET`. For the package validation and the API tracking this is an addition only (ADR 0028).
- **The data type:** `readonly record struct PointEvent<T>(double M, T Value)`, the counterpart of `LineEvent<T>` (ADR 0033): a plain value without validation in the constructor, with the attributes of the host's choice. As there, the library never reads a table and knows no route id (the host groups by route), and the M values are M values, not lengths.
- **Operations** (static, not extension methods, for the reason of ADR 0033; results are lists, sorted by M, and events with the same M keep the order of the input):

  | Method | Meaning |
  |---|---|
  | `PointEvents.Between(points, from, to)` | the point events whose M lies in the closed range; swapped bounds are the same range |
  | `PointEvents.Within(points, lines, combine)` | for every point event the line events that contain its M: one result per pair, at the M of the point, with the value `combine` makes of the two |
  | `PointEvents.Outside(points, lines)` | the point events that lie in no line event |

  The ranges of line events are **closed**, so a point exactly on the boundary of two neighbouring events lies in **both** and `Within` gives two results (as `LineEvents.At`, ADR 0033); line events may overlap, and every overlapping one gives a result. A line event **without length** (`From == To`) is ignored and `From` greater than `To` is read swapped, as there. `NaN` or an infinite M, `From` or `To` is an `ArgumentOutOfRangeException` that names the event; a `null` table or function is an `ArgumentNullException`.
- **`Outside` is the quality check**: a bridge without a speed limit, a report off the surveyed stretch, a gap in a table. Together with `Within` it splits the point events into those with and those without a line event, and `LineEvents.Difference` of an event that covers the whole route and a line table gives the gaps of that table.
- **`LocateEvents`** draws a table on the route, **one entry per event, in the order of the input**:

  ```csharp
  IReadOnlyList<(LineEvent<T> Event, IPolyline2D Shape)> LocateEvents<T>(this IPolyline2D route, IEnumerable<LineEvent<T>> events, IGeometryFactory? factory = null)
  IReadOnlyList<(PointEvent<T> Event, IMultiPoint2D Points)> LocateEvents<T>(this IPolyline2D route, IEnumerable<PointEvent<T>> events, IGeometryFactory? factory = null)
  ```

  A line event becomes the stretch of `LocateBetween` (ADR 0032), a point event the points of `LocateAlong` (several where the route passes the value more than once). An event whose M the route does not reach keeps its entry, with an empty polyline or multipoint, so that the result can be matched to the input by position and by event. The index of the route is built **once** for the whole table, so a table of many events costs little more than one, where a loop of `LocateBetween` would build it for each. The route needs M (as for those methods); an event with a `NaN` or infinite bound is an `ArgumentOutOfRangeException`.
- **The result is a tuple list, not a new type**: the event and its geometry are all there is, and a tuple deconstructs in a `foreach`. The geometries are created through the factory; the events are the host's own values.
- **Playground (ADR 0011):** a new parameter type **`text`** (several lines) for the tables of events, and the entries `LocateEvents` (a table drawn on the route) and `IntersectEvents` (two tables intersected, drawn on the route). The table is plain text, one event per line: `from..to value` for a line event, `m value` for a point event, `#` starts a comment. The examples are a road with speed limits, surfaces and bridges, and a hairpin where an M range and a value occur twice.
- **Tests (ADR 0008):** `[Covers]` tests. `Between` (closed range, swapped bounds, sorted, equal M keep the input order); `Within` (a result per pair, two on a boundary, one per overlapping event, line events without length ignored, swapped bounds, unsorted input); `Outside`; a **model test** that compares `Within` and `Outside` with a brute-force check over random tables; errors; `LocateEvents` (the order and one entry per event, empty shapes, several points where the route passes a value twice, agreement with `LocateBetween` and `LocateAlong`, errors).
- **Not now:** assigning a point event to the nearest route (a point on the map is `ProjectToM`, ADR 0017, and `LocateFeature`, ADR 0034, gives the range of a line or area), several routes in one call (the host groups), and a tolerance for "near" a line event (a point event is exactly in a range or not).

## Considered Options

- A point event as a line event with `From == To` (rejected: `LineEvents` ignores events without length because a piece without length is nothing, whereas a point event is exactly such a thing; and the types tell a reader which is which)
- Extension methods on `IEnumerable<PointEvent<T>>` (rejected for the reason of ADR 0033: `Enumerable` has its own `Where`, `Except` and the like, and the operations are not a LINQ pipeline)
- `Within` that returns the line event's value only, or a pair type (rejected: `combine` gives the caller the choice, and a tuple or a record is one lambda; ADR 0033 does the same for `Intersect`)
- `LocateEvents` that returns only the geometries (rejected: the association with the event is what the caller needs to style or label it, and matching by index is fragile when the input is a query)
- Dropping the events that the route does not reach (rejected: one entry per event keeps the result aligned with the table, and an empty geometry says what it is; Voronoi does the same, ADR 0036)
- A `LocateEvents` that takes a table of any events through a function (rejected: the two kinds already differ in what they return, a polyline or a multipoint, and two overloads are clearer than a generic selector)
- A text format for tables in the library (rejected: the library never reads tables, ADR 0033; the format belongs to the playground, which needs it to enter a table in a text box)
