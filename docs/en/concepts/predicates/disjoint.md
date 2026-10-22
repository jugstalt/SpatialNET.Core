# Disjoint

`a.Disjoint(b)` is `true` if the two geometries have no point in common.

## DE-9IM

`FF*FF****`: the interiors and the boundaries do not meet.

In words: the interior of `a` shares nothing with the interior or the boundary of `b` (`F` at II and IB), and neither does the boundary of `a` (`F` at BI and BB). What lies in the exterior is not asked (`*`).

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Disjoint(b)` |
|---|---|
| A point inside an area | false |
| A point on the edge of an area | false |
| A point outside an area | **true** |
| A point in a hole of an area | **true** |
| A point on a line (not at its end) | false |
| A point at the end of a line | false |
| A point at a bend (a vertex) inside a line | false |
| A point beside a line | **true** |
| Two equal points | false |
| Two lines that cross | false |
| Two lines that meet end to end | false |
| A line that ends on another line (a T) | false |
| Two lines that run together for a stretch | false |
| Two equal lines (one reversed) | false |
| A line inside an area | false |
| A line that crosses the edge of an area | false |
| A line along the edge of an area | false |
| A line outside an area that ends on its edge | false |
| A line outside an area | **true** |
| A small area inside a large one | false |
| An area inside another, touching its edge from inside | false |
| Two areas that share an edge | false |
| Two areas that touch at a corner | false |
| Two areas that overlap | false |
| Two equal areas (other start point and direction) | false |
| Two areas apart | **true** |
| An area inside a hole of another | **true** |

## Details

- **The negation of `Intersects`:** `a.Disjoint(b)` equals `!a.Intersects(b)`.
- **The only predicate that is `true` for an empty geometry.** All others are `false`, because an empty geometry has no point to relate.
- **Symmetric.**
- **Apart means apart.** An area inside the hole of another is disjoint from it, however close ("An area inside a hole of another").
- **Exact to the last bit:** "disjoint" holds even if the geometries are a rounding error apart ([Exactness](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance)).

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = !a.IsWithinDistance(b, t);
```

The same call, negated.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Intersects`](intersects.md), [Predicates and DE-9IM](../predicates.md).
