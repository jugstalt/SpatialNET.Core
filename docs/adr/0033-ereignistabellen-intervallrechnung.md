# Event tables: interval arithmetic on M ranges

A route system holds its facts as **events**: a range of M values on a route and some attributes (a speed limit from kilometre 3.0 to 5.5, a surface, a road class). The questions a web GIS asks are interval questions: *where do two tables hold at the same time?* (speed limit and surface), *where does one table have something that another lacks?* (a road without a bicycle path), *which neighbouring pieces with the same value can be joined?* This is what ArcGIS offers as "overlay route events", and NTS has nothing like it. **`LineEvents`** does this arithmetic on the numbers of one route; the geometry comes afterwards with `LocateBetween` (ADR 0032).

*German version: [0033-ereignistabellen-intervallrechnung.de.md](0033-ereignistabellen-intervallrechnung.de.md)*

## Consequences

- **Placement:** the value type `LineEvent<T>` and the static class `LineEvents` in `SpatialNET.Extensions`, namespace `SpatialNET`, in their own file. No new package. For the package validation and the API tracking this is an addition only (ADR 0028).
- **No database access, no database terms.** The library never reads a table and has no notion of a route id, a column or a connection: the host hands over the rows as `LineEvent<T>` values and groups by route itself (one line of LINQ, shown in the documentation). This keeps the library as free of data-access code as the rest of it (ADR 0001, ADR 0004), and it avoids a second, DB-shaped model of a table that every host would have to map onto its own.
- **The data type:** `readonly record struct LineEvent<T>(double From, double To, T Value)`. `T` is the host's choice (an `int`, a string, a record with several fields). It is a plain value without validation in the constructor, like `Circle` (ADR 0026); the operations check what they read. A table is any `IEnumerable<LineEvent<T>>`.
- **Events are in M values, not in lengths.** A table that is stored as metres from the start of the route is converted by the host first, or the route is calibrated so that M is that length (ADR 0030). The operations never look at a route; they have no geometry.
- **Operations** (all static, results are `IReadOnlyList<LineEvent<…>>` sorted by `From`, then `To`):

  | Method | Meaning |
  |---|---|
  | `Intersect(a, b, combine)` | one piece per overlapping pair of an event of `a` and one of `b`, with the value `combine(valueA, valueB)` |
  | `Union(a, b, both, onlyA, onlyB)` | the route cut at every boundary of either table, one piece wherever at least one table has an event, with the value of `both`, `onlyA` or `onlyB` |
  | `Difference(a, b)` | the parts of the events of `a` that no event of `b` covers, with the values of `a`; only the ranges of `b` count |
  | `Dissolve(events, comparer?)` | events that touch or overlap and have equal values become one |
  | `Clip(events, from, to)` | the events cut to a range |
  | `At(events, m)` | the events whose range contains `m` |

  They are **static methods and not extension methods**, because `Union`, `Intersect` and `Except`-like names would collide with `Enumerable` for every caller that has `using System.Linq`, and an overload that takes `IEnumerable<LineEvent<T>>` would be offered next to the LINQ ones in every list.
- **`Union` takes three functions instead of a "missing" value.** For a `TA` that is an `int` or a struct there is no honest `null`: a piece that only `a` covers would need a value for `b` that does not exist. `both`, `onlyA` and `onlyB` make the caller say what each case means, without `default` standing for "none". A pair type with `HasA`/`HasB` flags was considered and rejected as heavier to read.
- **Ranges are closed, but only a range with length counts.** An event with `From == To` is ignored. No operation returns a piece without length: two neighbours `[0, 5]` and `[5, 9]` touch at 5 and nothing lies between them, as `Substring` and `LocateBetween` do not return a piece of length 0 (ADR 0017, ADR 0032). `At` is the exception that shows the boundary: both neighbours contain 5, so both are returned, and the caller decides which one it needs.
- **Input rules.** The input may be unsorted. An event with `From` greater than `To` is read with the two swapped (M may run downwards); this is the rule of `LocateBetween` (ADR 0032). `NaN` or an infinite `From` or `To` is an `ArgumentOutOfRangeException` that names the table and the position of the event; a `null` table or function is an `ArgumentNullException`. An empty table is a normal input and gives an empty or pass-through result.
- **Overlaps within one table.** `Intersect` accepts them: every overlapping pair gives a piece, which is what a join of two tables does. `Union` does not: a piece would have two values from the same table and `both` would need to take a list, so it is an `ArgumentException` that names the table (touching events are fine). `Difference` looks only at the ranges of `b`, so they may overlap. `Dissolve`, `Clip` and `At` accept anything.
- **`Dissolve` joins equal values through other events.** It keeps one open run per value, so `A [0,5]`, `B [2,3]`, `A [4,8]` gives `A [0,8]` and `B [2,3]`: events of other values do not stop two `A`s from joining, and they are not changed, also where they overlap. Equality is that of the `comparer`, by default the default equality of `T` (for a record, all fields). A `null` value is allowed. Union and Intersect do **not** join their results themselves, since cutting at every boundary is what they are for; the caller follows with `Dissolve` where it wants it.
- **Cost:** `Intersect` and `Difference` sort and then use a binary search for the first event that can reach, so about O((n + m) log(n + m) + results); `Union` is a sweep over the sorted boundaries; `Dissolve` is one sort and a dictionary. No state is kept.
- **Tests (ADR 0008):** NTS has no counterpart. The cases are hand-written (`[Covers]`) with the example of the documentation (speed limit, surface, bridge, bicycle path), the boundaries and the overlap rules, and a **model test**: random tables with integer ends are compared with a cell-by-cell evaluation at every half-integer M, for `Intersect`, `Union`, `Difference` and `Dissolve` (every value is kept where it held, no equal values touch). The result is also checked to be sorted and free of pieces without length.
- **Playground and documentation:** no descriptor (ADR 0011): the operations take tables, not geometries, which the surface does not offer; they are listed as exempt with this reason, as `UnionAll`. The new concept page "event tables" in English and German explains tables in and out with the example, the rules above and the way to the map; the cookbook has a recipe from the tables to the lengths of the pieces on the road; the glossary has "event".
- **Out of scope for now:** point events as a type of their own (a point in a line event is `At`, a point on the map is `LocateAlong`), the reverse direction from a geometry to an event (a parcel along a road becomes a range of M values), a window of M values in the operations beyond `Clip`, and several routes in one call.

## Considered Options

- A table object (`EventTable<T>`) with route id, columns and a connection, rejected: the library would take on database terms and a second model of a table that every host has to map; plain `LineEvent<T>` values keep it free of data access (ADR 0001)
- Extension methods on `IEnumerable<LineEvent<T>>`, rejected: they would collide with `Enumerable.Union`, `Intersect` and `Except` and clutter every list
- `Union` with a "missing" value (`TA?`/`TB?`, or a pair with flags), rejected: unusable for value types without ambiguity, and heavier to read than three functions
- Joining the results of `Union` and `Intersect` automatically, rejected: the cut at every boundary is the point of them, and a caller who wants one piece per equal value is one `Dissolve` away; it would also have to decide what equal means
- Half-open ranges `[From, To)` for `At`, rejected: ranges are closed everywhere else in `LinearReferencing` (ADR 0032), and a half-open rule would pick a side for the caller; both are returned instead
- Throwing for overlaps in every operation, rejected: a join of two tables (`Intersect`) is well defined with overlaps, and dirty data should not stop a request where the result is clear
- Silently using one of two overlapping events in `Union`, rejected: it would hide a data problem and give a result that depends on the order
- Taking lengths instead of M values, rejected: the events of a route system are stored in M; a conversion is one call for the host, and routes whose M is a length need none
- Several routes in one call, with a key function, rejected for now: grouping is one line for the host and the operations stay free of keys
