# EqualsTopo

`a.EqualsTopo(b)` is `true` if the two geometries cover the same set of points, whatever their representation.

## DE-9IM

`T*F**FFF*`: the interiors meet, and nothing of either lies in the exterior of the other.

In words: the interiors share something (`T` at II), nothing of `a` lies outside `b` (`F` at IE and BE), and nothing of `b` lies outside `a` (`F` at EI and EB).

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.EqualsTopo(b)` |
|---|---|
| A point inside an area | false |
| A point on the edge of an area | false |
| A point outside an area | false |
| A point in a hole of an area | false |
| A point on a line (not at its end) | false |
| A point at the end of a line | false |
| A point at a bend (a vertex) inside a line | false |
| A point beside a line | false |
| Two equal points | **true** |
| Two lines that cross | false |
| Two lines that meet end to end | false |
| A line that ends on another line (a T) | false |
| Two lines that run together for a stretch | false |
| Two equal lines (one reversed) | **true** |
| A line inside an area | false |
| A line that crosses the edge of an area | false |
| A line along the edge of an area | false |
| A line outside an area that ends on its edge | false |
| A line outside an area | false |
| A small area inside a large one | false |
| An area inside another, touching its edge from inside | false |
| Two areas that share an edge | false |
| Two areas that touch at a corner | false |
| Two areas that overlap | false |
| Two equal areas (other start point and direction) | **true** |
| Two areas apart | false |
| An area inside a hole of another | false |

## Details

- **The representation does not matter:** the start point of a ring, its direction, the direction of a line, the order of the parts, and extra vertices on a straight stretch ("Two equal lines (one reversed)", "Two equal areas (other start point and direction)").
- **`EqualsExact` is the other question:** it compares the coordinates one by one, as given ([Measures and constructions](../measures-and-constructions.md)). Use `EqualsTopo` for "the same shape", `EqualsExact` for "the same data".
- **`false` if one is empty.** Two empty geometries are not `EqualsTopo` either: the predicates are false for empty input (`Disjoint` is the only exception).
- **Symmetric.** Exact to the last bit: a copy that is a rounding error away is not equal ([Exactness](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance)).

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.HausdorffDistance(b) <= t;
```

`HausdorffDistance` is discrete (vertices, and with `densifyFraction` below 1 points in between) and a lower bound; pass `densifyFraction` (for example 0.1) for geometries with long segments.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`EqualsExact`](../measures-and-constructions.md), [`Covers`](covers.md), [Predicates and DE-9IM](../predicates.md).
