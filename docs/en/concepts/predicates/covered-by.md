# CoveredBy

`a.CoveredBy(b)` is `true` if no point of `a` lies outside `b`. Boundary points count as inside. It is the mirror of `Covers`: `a.CoveredBy(b)` equals `b.Covers(a)`.

## DE-9IM

One of `T*F**F***`, `*TF**F***`, `**FT*F***` or `**F*TF***`: `a` shares a point with `b`, and nothing of `a` is in the exterior of `b`.

In words: the shared point may be anywhere, at II, IB, BI or BB (that is why there are four patterns); the two `F` at IE and BE say that nothing of `a` lies outside `b`.

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.CoveredBy(b)` | `b.CoveredBy(a)` |
|---|---|---|
| A point inside an area | **true** | false |
| A point on the edge of an area | **true** | false |
| A point outside an area | false | false |
| A point in a hole of an area | false | false |
| A point on a line (not at its end) | **true** | false |
| A point at the end of a line | **true** | false |
| A point at a bend (a vertex) inside a line | **true** | false |
| A point beside a line | false | false |
| Two equal points | **true** | **true** |
| Two lines that cross | false | false |
| Two lines that meet end to end | false | false |
| A line that ends on another line (a T) | false | false |
| Two lines that run together for a stretch | false | false |
| Two equal lines (one reversed) | **true** | **true** |
| A line inside an area | **true** | false |
| A line that crosses the edge of an area | false | false |
| A line along the edge of an area | **true** | false |
| A line outside an area that ends on its edge | false | false |
| A line outside an area | false | false |
| A small area inside a large one | **true** | false |
| An area inside another, touching its edge from inside | **true** | false |
| Two areas that share an edge | false | false |
| Two areas that touch at a corner | false | false |
| Two areas that overlap | false | false |
| Two equal areas (other start point and direction) | **true** | **true** |
| Two areas apart | false | false |
| An area inside a hole of another | false | false |

## Details

- **The practical "inside or on the edge".** A point on the edge of an area, and a line along its edge, are covered by the area, but not "within" it.
- **Equal geometries are covered by each other.**
- **Empty geometries** are covered by nothing.

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = b.Buffer(t).Covers(a);
```

`Buffer(t)` grows the container by `t` on all sides. It is the expensive part; for many tests against the same geometry compute the buffer once and keep it.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Covers`](covers.md), [`Within`](within.md), [Predicates and DE-9IM](../predicates.md).
