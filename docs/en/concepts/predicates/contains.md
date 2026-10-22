# Contains

`a.Contains(b)` is `true` if `b` lies in `a` and the two share at least one interior point. It is the mirror of `Within`: `a.Contains(b)` equals `b.Within(a)`.

## DE-9IM

`T*****FF*`: the interiors meet, and no part of `b` lies in the exterior of `a`.

In words: the interiors share something (`T` at II), and the exterior of `a` shares nothing with the interior or the boundary of `b` (`F` at EI and EB): nothing of `b` lies outside `a`.

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Contains(b)` | `b.Contains(a)` |
|---|---|---|
| A point inside an area | false | **true** |
| A point on the edge of an area | false | false |
| A point outside an area | false | false |
| A point in a hole of an area | false | false |
| A point on a line (not at its end) | false | **true** |
| A point at the end of a line | false | false |
| A point at a bend (a vertex) inside a line | false | **true** |
| A point beside a line | false | false |
| Two equal points | **true** | **true** |
| Two lines that cross | false | false |
| Two lines that meet end to end | false | false |
| A line that ends on another line (a T) | false | false |
| Two lines that run together for a stretch | false | false |
| Two equal lines (one reversed) | **true** | **true** |
| A line inside an area | false | **true** |
| A line that crosses the edge of an area | false | false |
| A line along the edge of an area | false | false |
| A line outside an area that ends on its edge | false | false |
| A line outside an area | false | false |
| A small area inside a large one | false | **true** |
| An area inside another, touching its edge from inside | false | **true** |
| Two areas that share an edge | false | false |
| Two areas that touch at a corner | false | false |
| Two areas that overlap | false | false |
| Two equal areas (other start point and direction) | **true** | **true** |
| Two areas apart | false | false |
| An area inside a hole of another | false | false |

## Details

- **A point or a line on the edge is not contained.** A polygon does not *contain* a point on its edge or a line along its edge, because they have no point in its interior. `Covers` says "inside or on the edge" ("A point on the edge of an area" and "A line along the edge of an area" in the table, read with the roles swapped).
- **Equal geometries contain each other.** `a.Contains(a)` is `true` (for a geometry that is not empty).
- **An area contains a part of itself** that touches its edge from inside.
- **Empty geometries** contain nothing and are contained in nothing.
- **Look at the roles.** In the table the first geometry is the container for the first column and the second for the other.

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.Buffer(t).Contains(b);
```

`Buffer(t)` grows the container by `t` on all sides. It is the expensive part; for many tests against the same geometry compute the buffer once and keep it.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Within`](within.md), [`Covers`](covers.md), [Predicates and DE-9IM](../predicates.md).
