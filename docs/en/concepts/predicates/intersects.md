# Intersects

`a.Intersects(b)` is `true` if the two geometries have at least one point in common, even a single point on an edge.

## DE-9IM

There is no single pattern: `Intersects` is the negation of `Disjoint` (`FF*FF****`), so it is true if one of the cells II, IB, BI or BB is not empty (`T********`, `*T*******`, `***T*****` or `****T****`).

In words: the shared point may be anywhere, in any of the four pairs interior/boundary of `a` with interior/boundary of `b`; it is enough that one of them shares something.

How to read a pattern: see [Reading a pattern](../predicates.md#reading-a-pattern). The nine positions are `II IB IE BI BB BE EI EB EE` (interior, boundary and exterior of `a` against those of `b`); `T` = shares something, `F` = shares nothing, `*` = anything, `0`/`1`/`2` = shares exactly points, lines or areas.

## Situations

`a` is the first and `b` the second geometry. Each row is a situation; the exact geometries are in [Predicates](../predicates.md#the-situations).

| Situation (a, b) | `a.Intersects(b)` |
|---|---|
| A point inside an area | **true** |
| A point on the edge of an area | **true** |
| A point outside an area | false |
| A point in a hole of an area | false |
| A point on a line (not at its end) | **true** |
| A point at the end of a line | **true** |
| A point at a bend (a vertex) inside a line | **true** |
| A point beside a line | false |
| Two equal points | **true** |
| Two lines that cross | **true** |
| Two lines that meet end to end | **true** |
| A line that ends on another line (a T) | **true** |
| Two lines that run together for a stretch | **true** |
| Two equal lines (one reversed) | **true** |
| A line inside an area | **true** |
| A line that crosses the edge of an area | **true** |
| A line along the edge of an area | **true** |
| A line outside an area that ends on its edge | **true** |
| A line outside an area | false |
| A small area inside a large one | **true** |
| An area inside another, touching its edge from inside | **true** |
| Two areas that share an edge | **true** |
| Two areas that touch at a corner | **true** |
| Two areas that overlap | **true** |
| Two equal areas (other start point and direction) | **true** |
| Two areas apart | false |
| An area inside a hole of another | false |

## Details

- **Touching counts.** Two areas that only share an edge or a corner intersect. If you mean "they overlap in area", use `Overlaps`, or ask for `Intersects` and not `Touches`.
- **Symmetric.** `a.Intersects(b)` equals `b.Intersects(a)`.
- **Empty geometries** intersect nothing: the result is `false`.
- **Fast.** `Intersects` does not compute the full matrix; it takes a fast exact path (segment intersection first, then containment). For many tests against one geometry use `Prepare()` ([Predicates](../predicates.md)).
- **Exact to the last bit.** A point that you only place on a line, by clicking or by computing, almost never intersects it ([Exactness](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance)).

## With a tolerance

The predicate is exact to the last bit. To ask "close enough", with a tolerance `t` in the unit of your coordinates (for survey data 0.01 for 1 cm):

```csharp
bool result = a.IsWithinDistance(b, t);
```

Cheap: it checks the envelopes first and stops at the first pair of segments within the distance. For many queries use `Prepare()` and `prepared.IsWithinDistance(b, t)`.

The library has no global tolerance; see [Exactness in practice](../predicates.md#exactness-in-practice-and-the-alternatives-with-a-tolerance).

## See also

[`Disjoint`](disjoint.md), [`Touches`](touches.md), [`Overlaps`](overlaps.md), [Predicates and DE-9IM](../predicates.md).
