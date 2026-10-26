# Event tables

An **event** is a fact about a stretch of a route that is addressed by its M values and has no geometry of its own: *speed limit 50 from kilometre 0 to 5*, *surface gravel from 3 to 8*, *bridge at 8.2*. The route itself is the calibrated polyline of [linear referencing](linear-referencing.md); the events live in the host's database. `LineEvents` does the arithmetic **between** event tables ([ADR 0033](../../adr/0033-ereignistabellen-intervallrechnung.md)): which events hold at the same time, what one table has that another does not, which neighbours can be joined. It computes with the numbers only, so it needs **no geometry, no factory and no database access**; the library never reads a table, you hand it the rows.

## What goes in, what comes out

An event is a `LineEvent<T>(From, To, Value)`: a range of M values and any value you like for the attributes (an `int` for a speed, a string, a record with several fields). A table is any `IEnumerable<LineEvent<T>>`.

Two tables of one route:

| From | To | Speed |  | From | To | Surface |
|---:|---:|---:|---|---:|---:|---|
| 0 | 5 000 | 50 |  | 0 | 3 000 | asphalt |
| 5 000 | 12 000 | 70 |  | 3 000 | 8 000 | gravel |
| | | |  | 8 000 | 12 000 | asphalt |

The question "where does which speed limit hold on which surface?" is the **intersection** of the two tables, and the result is a table again:

| From | To | Speed | Surface |
|---:|---:|---:|---|
| 0 | 3 000 | 50 | asphalt |
| 3 000 | 5 000 | 50 | gravel |
| 5 000 | 8 000 | 70 | gravel |
| 8 000 | 12 000 | 70 | asphalt |

```csharp
var speed = new[] { new LineEvent<int>(0, 5000, 50), new LineEvent<int>(5000, 12000, 70) };
var surface = new[]
{
    new LineEvent<string>(0, 3000, "asphalt"), new LineEvent<string>(3000, 8000, "gravel"), new LineEvent<string>(8000, 12000, "asphalt"),
};

// Where do both hold? One piece per overlap, with the two values combined.
var both = LineEvents.Intersect(speed, surface, (s, b) => (Speed: s, Surface: b));
// 0-3000 (50, asphalt), 3000-5000 (50, gravel), 5000-8000 (70, gravel), 8000-12000 (70, asphalt)

// Join neighbours with the same value: the surface alone, after cutting at the speed limits.
var surfaceOnly = LineEvents.Dissolve(both.Select(e => new LineEvent<string>(e.From, e.To, e.Value.Surface)));
// 0-3000 asphalt, 3000-8000 gravel, 8000-12000 asphalt

// Which speed limit holds at a bridge at 8200?
var atBridge = LineEvents.At(speed, 8200);               // one event: 5000-12000, 70

// Where is the road without a bicycle path?
var bikePath = new[] { new LineEvent<bool>(2000, 6000, true) };
var withoutPath = LineEvents.Difference(speed, bikePath); // 0-2000 (50) and 6000-12000 (70)
```

## The operations

| Operation | Question | Result |
|---|---|---|
| `Intersect(a, b, combine)` | where do an event of `a` and an event of `b` hold at the same time? | one piece per overlapping pair, with the value `combine` makes of the two |
| `Union(a, b, both, onlyA, onlyB)` | the route cut at every boundary of either table | one piece wherever at least one table has an event, with the value of `both`, `onlyA` or `onlyB` |
| `Difference(a, b)` | what does `a` have where `b` has nothing? | the parts of the events of `a` that no event of `b` covers, with the values of `a` |
| `Dissolve(events, comparer?)` | join what belongs together | events that touch or overlap and have the same value become one |
| `Clip(events, from, to)` | only a section | the events cut to the range |
| `At(events, m)` | what holds here? | the events whose range contains `m` |

A few things worth knowing:

- **`Union` names the side.** A piece that only one table covers has no value from the other, and for a `T` that may be a number or a struct there is no honest "empty". So you pass what to make of each case: `Union(speed, surface, (s, b) => $"{s}, {b}", s => $"{s}, no data", b => $"no limit, {b}")`.
- **`Union` and `Intersect` cut, `Dissolve` joins.** Both cut at every boundary of both tables, so neighbouring pieces may have equal values; follow with `Dissolve` where you want them joined. `Dissolve` joins through events of other values (an `A`, a `B` inside it, an `A` that continues join the two `A`s) and leaves events of different values alone, even where they overlap. `comparer` decides what "the same value" means; without it the default equality of `T` applies, which for a record compares all fields.
- **Overlaps.** `Intersect` accepts events that overlap within one table and gives a piece for every overlapping pair. `Union` does not: a piece would have two values from the same table, so overlapping events within one table are an `ArgumentException` that names the table. `Difference` only looks at the ranges of its second table, so overlaps there are fine.
- **Boundaries.** Ranges are closed, but only a range **with length** counts. An event with `From == To` is ignored, and no operation returns a piece without length: two neighbours `[0, 5]` and `[5, 9]` meet at 5 without a piece between them. `At(events, 5)` is the one place where this shows: both neighbours contain 5, so both are returned. If you need one, decide by your own rule (the later one, say).
- **Order and swapped bounds.** The input may be in any order, and an event with `From` greater than `To` is read with the two swapped (M may run downwards). Results are sorted by `From`, then `To`.
- **Invalid numbers.** `NaN` or an infinite `From` or `To` is an `ArgumentOutOfRangeException` that names the event.

## From a table to the map

The arithmetic gives M ranges. To draw them, hand each range to `LocateBetween` on the calibrated route: the result follows the road with its bends, in the direction of the route, and has several paths if the route passes the range more than once ([Linear referencing](linear-referencing.md)). The [cookbook](../cookbook.md) shows this as a complete recipe, from the tables to the lengths of the pieces on the road.

A point event (a bridge at 8 200) is a `LocateAlong` away from a point; a point inside a line event is `At`.

## Several routes, and the database

`LineEvents` works on **one route at a time**. A real table has a route id; group by it yourself, which is one line of LINQ and keeps the library free of database terms:

```csharp
// The rows come from your database; the library only gets the numbers of one route at a time.
var rows = new[]
{
    (Route: "A12", From: 0.0, To: 5000.0, Speed: 50),
    (Route: "A12", From: 5000.0, To: 12000.0, Speed: 70),
    (Route: "B3", From: 0.0, To: 800.0, Speed: 30),
};

Dictionary<string, LineEvent<int>[]> byRoute = rows
    .GroupBy(r => r.Route)
    .ToDictionary(g => g.Key, g => g.Select(r => new LineEvent<int>(r.From, r.To, r.Speed)).ToArray());
```

Tables are in M values, not in lengths: if your events are stored as metres from the start of the route, convert them first (or calibrate the route so that M is that length).

## Point events and drawing a table

An event at a single M value (a bridge, a damage report) is a `PointEvent<T>(M, Value)` ([ADR 0040](../../adr/0040-punktereignisse-und-ereignisse-auf-der-karte.md)). `PointEvents` answers the questions that go with it, again on the numbers alone:

```csharp
var bridges = new[] { new PointEvent<string>(8200, "bridge"), new PointEvent<string>(11450, "culvert") };
var limits = new[] { new LineEvent<int>(0, 5000, 50), new LineEvent<int>(5000, 12000, 70) };

// Which speed limit holds at each? One result per pair, at the M of the point.
var withLimit = PointEvents.Within(bridges, limits, (b, kmh) => $"{b}: {kmh} km/h");   // bridge 70, culvert 70

// Which have no speed limit in a table that stops at 9000?
var uncovered = PointEvents.Outside(bridges, [new LineEvent<int>(0, 9000, 50)]);       // the culvert

// Draw a table on the calibrated route: one entry per event, with its stretch.
var route = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 0, 12000 0 12000)");
var drawn = route.LocateEvents(limits);                                                // (event, shape) for 0-5000 and 5000-12000
```

| Operation | Question | Result |
|---|---|---|
| `PointEvents.Between(points, from, to)` | which lie in this range? | the point events with M in the closed range |
| `PointEvents.Within(points, lines, combine)` | what holds at each? | one result per point event and line event that contains it (two on a boundary) |
| `PointEvents.Outside(points, lines)` | which lie in no line event? | the point events without one: the check for gaps |
| `route.LocateEvents(events)` | where on the map? | one entry per event, in the input order: the stretch of a line event, the points of a point event; an empty shape if the route does not reach it |

Ranges are closed and line events without length are ignored, as in `LineEvents`. `LocateEvents` builds the index of the route once for the whole table.

## From a geometry to an event

The reverse direction is `LocateFeature` on the route: a parcel, a pipe or a flooded area becomes the ranges of M where it lies, and from there an event ([ADR 0034](../../adr/0034-locatefeature-geometrie-zu-m-bereichen.md)):

```csharp
var ranges = route.LocateFeature(parcel, tolerance: 0);
var events = ranges.Select(r => new LineEvent<string>(r.From, r.To, "parcel 123/4"));
```

The events can then be combined with the others, for example with `Intersect` against a speed table to find where a parcel borders a 50 km/h stretch.

## Not included

Several routes in one call (the host groups), and a tolerance for "near" a line event: a point event lies exactly in a range or not. A point on the map becomes a measure with `ProjectToM`.
