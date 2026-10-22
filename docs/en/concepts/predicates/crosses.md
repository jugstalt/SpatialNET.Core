# Crosses

`a.Crosses(b)` is `true` if the geometries cross each other. What that means depends on the dimensions (point 0, line 1, area 2).

## DE-9IM

By the dimensions: `T*T******` if `a` has the smaller dimension (point/line, point/area, line/area), `T*****T**` if `a` has the greater one, and `0********` for two lines.

In words, for `a` of the smaller dimension: the interiors share something (`T` at II) and part of `a` lies outside `b` (`T` at IE). For `a` of the greater dimension it is the mirror: `T` at II and EI. For two lines the interiors share only single points (`0` at II).

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Crosses(b)` |
|---|---|
| A point inside an area | false |
| A point on the edge of an area | false |
| A point outside an area | false |
| A point in a hole of an area | false |
| A point on a line (not at its end) | false |
| A point at the end of a line | false |
| A point at a bend (a vertex) inside a line | false |
| A point beside a line | false |
| Two equal points | false |
| Two lines that cross | **true** |
| Two lines that meet end to end | false |
| A line that ends on another line (a T) | false |
| Two lines that run together for a stretch | false |
| Two equal lines (one reversed) | false |
| A line inside an area | false |
| A line that crosses the edge of an area | **true** |
| A line along the edge of an area | false |
| A line outside an area that ends on its edge | false |
| A line outside an area | false |
| A small area inside a large one | false |
| An area inside another, touching its edge from inside | false |
| Two areas that share an edge | false |
| Two areas that touch at a corner | false |
| Two areas that overlap | false |
| Two equal areas (other start point and direction) | false |
| Two areas apart | false |
| An area inside a hole of another | false |

## Details

- **Two lines cross** if they meet in single points where they pass through each other ("Two lines that cross"). Lines that meet end to end, in a T, or run together for a stretch do not cross.
- **A line crosses an area** if part of the line is inside and part outside ("A line that crosses the edge of an area"). A line inside, outside or along the edge does not cross it.
- **Points:** a multipoint crosses a line or an area if some of its points are inside and some outside. A single point cannot cross anything.
- **Two areas never cross** (use `Overlaps`).
- **Symmetric.** Empty geometries do not cross.

## With a tolerance

There is no simple counterpart. A crossing is a statement about the interiors, and "almost crossing" has no single meaning. Ask for what you mean: `IsWithinDistance` for "they meet", and `NearestPoints` for where.

## See also

[`Touches`](touches.md), [`Overlaps`](overlaps.md), [`Intersects`](intersects.md), [Predicates and DE-9IM](../predicates.md).
