# Touches

`a.Touches(b)` is `true` if the two geometries meet, but only in boundary points: their interiors do not intersect.

## DE-9IM

One of `FT*******`, `F**T*****` or `F***T****`: the interiors do not meet (`F` in II), and the boundaries or an interior and a boundary do.

In words: the interiors share nothing (`F` at II), but something else is shared: the interior of `a` meets the boundary of `b` (`T` at IB), or the boundary of `a` meets the interior of `b` (`T` at BI), or the boundaries meet (`T` at BB).

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Touches(b)` |
|---|---|
| A point inside an area | false |
| A point on the edge of an area | **true** |
| A point outside an area | false |
| A point in a hole of an area | false |
| A point on a line (not at its end) | false |
| A point at the end of a line | **true** |
| A point at a bend (a vertex) inside a line | false |
| A point beside a line | false |
| Two equal points | false |
| Two lines that cross | false |
| Two lines that meet end to end | **true** |
| A line that ends on another line (a T) | **true** |
| Two lines that run together for a stretch | false |
| Two equal lines (one reversed) | false |
| A line inside an area | false |
| A line that crosses the edge of an area | false |
| A line along the edge of an area | **true** |
| A line outside an area that ends on its edge | **true** |
| A line outside an area | false |
| A small area inside a large one | false |
| An area inside another, touching its edge from inside | false |
| Two areas that share an edge | **true** |
| Two areas that touch at a corner | **true** |
| Two areas that overlap | false |
| Two equal areas (other start point and direction) | false |
| Two areas apart | false |
| An area inside a hole of another | false |

## Details

- **What "interior" and "boundary" are:** a point has no boundary (the point is its interior); a line has its two end points as boundary and the rest as interior; an area has its edge as boundary.
- **A point at the end of a line touches the line, a point in the middle does not.** The end is the boundary, the middle is the interior ("A point at the end of a line", "A point on a line", "A point at a bend"). A bend (a vertex) inside a line is not special: it belongs to the interior.
- **A closed line has no boundary.** A point at its start point lies in its interior, so it does not touch it. With a polyline of several paths, an end point counts as boundary only if an odd number of path ends lie there ([Predicates](../predicates.md)).
- **Two points never touch:** a point has no boundary.
- **Areas touch** along a shared edge or at a corner. If they overlap in area, they do not touch ("Two areas that overlap").
- **Lines touch** end to end, or where one ends on the other (a T) ("Two lines that meet end to end", "A line that ends on another line"). Crossing lines do not touch; lines that run together for a stretch do not touch either.
- **A line along the edge of an area touches it:** the line lies on the boundary and does not enter the interior.
- **Symmetric.** Empty geometries touch nothing.

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b);
```

Areas: they meet within `t` and overlap by less than `t` (`Buffer(-t)` shrinks the area). For a line or a point use the distance: they touch if `IsWithinDistance(b, t)`. For "at the end of a line" ask for the boundary: `line.Boundary().IsWithinDistance(point, t)`.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Intersects`](intersects.md), [`Overlaps`](overlaps.md), [`Crosses`](crosses.md), [Predicates and DE-9IM](../predicates.md).
