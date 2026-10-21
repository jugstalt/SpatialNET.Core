# Spatial index

`SpatialIndex<T>` answers two questions over many items quickly: *which items lie in this rectangle?* and *which items are nearest to this point?* Without an index, both are a loop over all items. The index is a building block for in-memory data and for hosts without an index of their own; it does not replace the index of a database or a GIS host ([ADR 0014](../../adr/0014-oeffentlicher-raeumlicher-index-statisch-str.md)).

## Build once, query often

The index is **static**: you build it from items with an envelope and a payload, and it never changes afterwards. To reflect changes, build a new one. In exchange it is packed densely (Sort-Tile-Recursive) and **thread-safe**: any number of threads may query the same instance.

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((8 8, 22 8, 8 12, 8 8))"),
};

// Build once; the index is immutable, so any number of threads may query it.
SpatialIndex<IPolygon2D> index = parcels.ToSpatialIndex();

// Range query: envelope candidates first, then the exact test on the real geometry.
var probe = Wkt.Read<IPolygon2D>("POLYGON((16 10, 18 10, 18 12, 16 12, 16 10))");
Console.WriteLine(index.Query(probe).Count());                         // 1 - the envelope of the triangle meets the probe's
Console.WriteLine(index.Query(probe).Count(p => p.Intersects(probe))); // 0 - but the triangle itself does not

// "Within 5 map units of a point": query with the point's envelope widened by 5.
var here = new Coordinate(15, 5);
Console.WriteLine(index.Query(new Envelope2D(here.X - 5, here.Y - 5, here.X + 5, here.Y + 5)).Count()); // 3

// The nearest parcel by exact distance (without the delegate, the envelope distance is used).
var hereGeometry = Wkt.Read<IGeometry2D>("POINT(15 5)");
IReadOnlyList<IPolygon2D> nearest = index.Nearest(here, 1, (parcel, _) => parcel.Distance(hereGeometry));
Console.WriteLine(nearest[0].ToWkt()); // POLYGON ((8 8, 22 8, 8 12, 8 8))
```

For items that are not geometries, give the envelope yourself: `records.ToSpatialIndex(r => r.Bounds)`, or construct `new SpatialIndex<T>(items)` from `(Envelope2D, T)` pairs.

## What a query returns

The index only knows **envelopes**. `Query` returns the items whose envelope touches or overlaps the query envelope (closed intervals), so the result is a pre-filter: the exact test on the real geometry is yours (`.Where(x => x.Intersects(g))`, see [Predicates](predicates.md)). For many tests against one geometry, combine it with `Prepare()`.

- **Order.** The order of `Query` results is unspecified.
- **Without allocation.** `Query(envelope, results)` adds to a collection you pass in (for example a reused `List<T>`), without an enumerator.
- **Within a distance.** There is no separate API: widen the envelope by the distance, then test exactly.

## Nearest neighbours

`Nearest(point, k)` returns up to `k` items, ascending by distance. By default the distance is the one from the point to the item's **envelope** (0 inside), which is exact only for rectangles. For geometries, pass the exact distance as a delegate; it must never be smaller than the envelope distance, which holds for any real geometry inside its envelope. Items at equal distance keep their input order, so results are deterministic.

## Joining two indexes

`Join` finds all pairs of an item of one index and an item of another whose envelopes meet, by walking both trees together, so that whole subtrees that cannot meet are skipped ([ADR 0021](../../adr/0021-spatial-join-doppelbaum-nur-vorfilter.md)). Like `Query` it is a pre-filter on envelopes; the exact test is yours:

```csharp
var parcels = parcelGeometries.ToSpatialIndex();
var zones = zoneGeometries.ToSpatialIndex();

// which parcels meet which flood zone? Envelope candidates first, then the exact test:
var hits = parcels.Join(zones).Where(p => p.Left.Intersects(p.Right));

// all overlapping pairs within one layer: every pair once, never a parcel with itself
var overlaps = parcels.SelfJoin().Where(p => p.First.Overlaps(p.Second));

// pairs that are close: the envelopes are widened by 5 map units
var near = parcels.Join(zones, distance: 5);
```

- **`SelfJoin` returns every unordered pair once**, never an entry with itself. A loop of `Query` calls cannot do that: it returns every pair twice, and every item with itself.
- `distance` widens the envelopes of one side; it stays an envelope test, not an exact distance.
- The order of the pairs is not specified. The result can be enumerated more than once, and any number of threads may join the same indexes.
- Measured: for large, evenly spread sets `Join` is about twice as fast as a loop of queries; for small or very dense sets it is on a par, because the number of pairs dominates.

## Rules

- **Empty envelopes** (`Envelope2D.Empty`, the envelope of an empty geometry) are skipped when building and are not counted in `Count`. An empty query envelope returns nothing.
- **Invalid values** throw `ArgumentOutOfRangeException`: an envelope that is partly NaN, infinite or has `Min > Max`, a non-finite point, `k < 1`, a node capacity below 2 ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.md)). Points and zero-width envelopes (lines) are valid.
- **Duplicates** are kept; each item is an entry of its own. The payload may be `null`.
- **Snapshot.** The input is copied when the index is built; later changes to the source collection have no effect ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.md)). If you index host geometries, remember that the index holds their envelopes from the moment of building: after a geometry moves, rebuild.
- **Thread safety.** Queries are safe in parallel. This differs from `PreparedGeometry`, which is not thread-safe.
