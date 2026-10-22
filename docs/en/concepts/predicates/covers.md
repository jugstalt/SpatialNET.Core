# Covers

`a.Covers(b)` is `true` if no point of `b` lies outside `a`. Boundary points count as inside. It is the mirror of `CoveredBy`.

## DE-9IM

One of `T*****FF*`, `*T****FF*`, `***T**FF*` or `****T*FF*`: `b` shares a point with `a`, and nothing of `b` is in the exterior of `a`.

In words: the shared point may be anywhere, at II, IB, BI or BB (that is why there are four patterns); the two `F` at EI and EB say that nothing of `b` lies outside `a`.

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Covers(b)` | `b.Covers(a)` |
|---|---|---|
| A point inside an area | false | **true** |
| A point on the edge of an area | false | **true** |
| A point outside an area | false | false |
| A point in a hole of an area | false | false |
| A point on a line (not at its end) | false | **true** |
| A point at the end of a line | false | **true** |
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
| A line along the edge of an area | false | **true** |
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

- **`Contains` versus `Covers`** is the difference that surprises most people: a polygon does not *contain* a line along its edge (the line has no point in the interior), but it *covers* it. If you mean "inside or on the edge", use `Covers`.
- **Equal geometries cover each other.**
- **Empty geometries** cover nothing and are covered by nothing.
- **Look at the roles** in the table: the first column asks whether the first geometry covers the second.

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.Buffer(t).Covers(b);
```

`Buffer(t)` grows the container by `t` on all sides. It is the expensive part; for many tests against the same geometry compute the buffer once and keep it.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`CoveredBy`](covered-by.md), [`Contains`](contains.md), [Predicates and DE-9IM](../predicates.md).
