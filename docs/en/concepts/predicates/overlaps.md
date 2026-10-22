# Overlaps

`a.Overlaps(b)` is `true` if two geometries of the **same dimension** overlap partly: they share a part of that dimension, and neither lies in the other.

## DE-9IM

`T*T***T**` for two areas or two multipoints, `1*T***T**` for two lines.

In words: the interiors share something (`T` at II), part of `a` lies outside `b` (`T` at IE), and part of `b` lies outside `a` (`T` at EI). For two lines the shared part must itself be a line (`1` at II): lines that only cross share a point (`0`), which does not count.

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Overlaps(b)` |
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
| Two lines that cross | false |
| Two lines that meet end to end | false |
| A line that ends on another line (a T) | false |
| Two lines that run together for a stretch | **true** |
| Two equal lines (one reversed) | false |
| A line inside an area | false |
| A line that crosses the edge of an area | false |
| A line along the edge of an area | false |
| A line outside an area that ends on its edge | false |
| A line outside an area | false |
| A small area inside a large one | false |
| An area inside another, touching its edge from inside | false |
| Two areas that share an edge | false |
| Two areas that touch at a corner | false |
| Two areas that overlap | **true** |
| Two equal areas (other start point and direction) | false |
| Two areas apart | false |
| An area inside a hole of another | false |

## Details

- **Same dimension only.** A line and an area never overlap, whatever the relation ("A line inside an area" is `Within`, not overlapping).
- **Areas** overlap if they share area and each also has area of its own ("Two areas that overlap"). If one lies in the other, they do not overlap; if they only share an edge or a corner, they touch.
- **Lines** overlap if they run together for a stretch and each also has a part of its own ("Two lines that run together for a stretch"). Lines that cross at a point share only a point, which is not the dimension of a line: they cross, they do not overlap.
- **Equal geometries do not overlap** (each lies in the other).
- **Symmetric.** Empty geometries do not overlap.

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.Buffer(-t).Intersects(b.Buffer(-t)) && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a);
```

Areas only: the formula uses `Buffer(-t)`, which works for polygons.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Intersects`](intersects.md), [`Touches`](touches.md), [`Contains`](contains.md), [Predicates and DE-9IM](../predicates.md).
