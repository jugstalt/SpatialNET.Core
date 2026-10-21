using CsCheck;
using NetTopologySuite.Index.Strtree;
using SpatialNET.Imp;
using SpatialNET.IO;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;

namespace SpatialNET.Tests.Indexing;

// Orakel: Brute Force über alle Paare und eine Schleife aus NTS-STRtree-Abfragen (ADR 0021).
public class SpatialJoinTests
{
    private static readonly Gen<Envelope2D> Boxes =
        Gen.Select(Gen.Double[-100, 100], Gen.Double[-100, 100], Gen.Double[0, 30], Gen.Double[0, 30], Gen.Int[0, 3])
            .Select(t =>
            {
                var (x, y, w, h, kind) = t;
                // coarse rounding produces many duplicates and touching envelopes
                x = Math.Round(x / 10) * 10;
                y = Math.Round(y / 10) * 10;
                return kind switch
                {
                    0 => new Envelope2D(x, y, x, y),
                    1 => new Envelope2D(x, y, x + w, y),
                    _ => new Envelope2D(x, y, x + w, y + h),
                };
            });

    private static readonly Gen<(Envelope2D[] Left, Envelope2D[] Right, int CapacityLeft, int CapacityRight, double Distance)> Inputs =
        Gen.Select(Boxes.Array[0, 120], Boxes.Array[0, 120], Gen.Int[2, 20], Gen.Int[2, 20], Gen.OneOfConst(0.0, 0.0, 5.0, 10.0, 25.0));

    private static SpatialIndex<int> Build(Envelope2D[] boxes, int capacity) => new(boxes.Select((e, i) => (e, i)), capacity);

    private static bool Meets(Envelope2D a, Envelope2D b, double distance) =>
        a.MinX <= b.MaxX + distance && b.MinX - distance <= a.MaxX && a.MinY <= b.MaxY + distance && b.MinY - distance <= a.MaxY;

    private static (int, int)[] Sorted(IEnumerable<(int, int)> pairs) => pairs.Order().ToArray();

    [Fact]
    public void Join_equals_brute_force_and_a_loop_of_NTS_STRtree_queries()
    {
        Inputs.Sample(t =>
        {
            var (left, right, capacityLeft, capacityRight, distance) = t;
            var expected = Sorted(from i in Enumerable.Range(0, left.Length)
                                  from j in Enumerable.Range(0, right.Length)
                                  where Meets(left[i], right[j], distance)
                                  select (i, j));

            var actual = Build(left, capacityLeft).Join(Build(right, capacityRight), distance).ToArray();

            Assert.Equal(expected, Sorted(actual)); // every pair exactly once

            var tree = new STRtree<int>();
            for (int j = 0; j < right.Length; j++) tree.Insert(new NtsEnvelope(right[j].MinX, right[j].MaxX, right[j].MinY, right[j].MaxY), j);
            var viaTree = Sorted(Enumerable.Range(0, left.Length).SelectMany(i =>
                tree.Query(new NtsEnvelope(left[i].MinX - distance, left[i].MaxX + distance, left[i].MinY - distance, left[i].MaxY + distance)).Select(j => (i, j))));
            Assert.Equal(expected, viaTree);
        });
    }

    [Fact]
    public void The_collection_overload_adds_the_same_pairs_without_clearing()
    {
        Inputs.Sample(t =>
        {
            var (left, right, capacityLeft, capacityRight, distance) = t;
            var a = Build(left, capacityLeft);
            var b = Build(right, capacityRight);
            var results = new List<(int, int)> { (-1, -1) };

            a.Join(b, results, distance);

            Assert.Equal((-1, -1), results[0]);
            Assert.Equal(Sorted(a.Join(b, distance)), Sorted(results.Skip(1)));
        });
    }

    [Fact]
    public void SelfJoin_has_every_unordered_pair_of_different_entries_once()
    {
        Gen.Select(Boxes.Array[0, 150], Gen.Int[2, 20], Gen.OneOfConst(0.0, 0.0, 5.0, 25.0)).Sample(t =>
        {
            var (boxes, capacity, distance) = t;
            var expected = Sorted(from i in Enumerable.Range(0, boxes.Length)
                                  from j in Enumerable.Range(i + 1, Math.Max(0, boxes.Length - i - 1))
                                  where Meets(boxes[i], boxes[j], distance)
                                  select (i, j));
            // the orientation of a pair is not specified, so each pair is compared as (min, max)
            var actual = Sorted(Build(boxes, capacity).SelfJoin(distance).Select(p => (Math.Min(p.First, p.Second), Math.Max(p.First, p.Second))));

            Assert.Equal(expected, actual);
            Assert.DoesNotContain(actual, p => p.Item1 == p.Item2); // never an entry with itself
        });
    }

    [Fact]
    public void Joining_an_index_with_itself_gives_every_ordered_pair_including_the_entry_with_itself()
    {
        Gen.Select(Boxes.Array[0, 80], Gen.Int[2, 20]).Sample(t =>
        {
            var (boxes, capacity) = t;
            var index = Build(boxes, capacity);
            var expected = Sorted(from i in Enumerable.Range(0, boxes.Length)
                                  from j in Enumerable.Range(0, boxes.Length)
                                  where Meets(boxes[i], boxes[j], 0)
                                  select (i, j));

            Assert.Equal(expected, Sorted(index.Join(index)));
        });
    }

    [Fact]
    public void Entries_with_equal_payload_are_different_entries()
    {
        var box = new Envelope2D(0, 0, 1, 1);
        var index = new SpatialIndex<string>([(box, "x"), (box, "x"), (box, "x")]);

        Assert.Equal(3, index.SelfJoin().Count()); // three entries: three pairs
        Assert.Equal(9, index.Join(index).Count());
    }

    [Fact]
    public void Different_payload_types_join_and_results_are_repeatable()
    {
        var parcels = new SpatialIndex<string>([(new Envelope2D(0, 0, 10, 10), "A"), (new Envelope2D(20, 0, 30, 10), "B")]);
        var zones = new SpatialIndex<int>([(new Envelope2D(5, 5, 25, 6), 1), (new Envelope2D(100, 100, 101, 101), 2)]);

        var pairs = parcels.Join(zones);

        Assert.Equal([("A", 1), ("B", 1)], pairs.Order().ToArray());
        Assert.Equal(pairs.Count(), pairs.Count()); // enumerating twice
        Assert.Equal(4, parcels.Join(zones, 100).Count()); // widened by 100, every parcel reaches every zone
    }

    [Fact]
    public void Empty_indexes_give_no_pairs_and_null_arguments_throw()
    {
        var empty = new SpatialIndex<int>([]);
        var one = new SpatialIndex<int>([(new Envelope2D(0, 0, 1, 1), 1)]);

        Assert.Empty(empty.Join(one));
        Assert.Empty(one.Join(empty));
        Assert.Empty(empty.SelfJoin());
        Assert.Empty(one.SelfJoin());
        Assert.Throws<ArgumentNullException>(() => one.Join((SpatialIndex<int>)null!));
        Assert.Throws<ArgumentNullException>(() => one.Join(one, (ICollection<(int, int)>)null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_invalid_distance_throws(double distance)
    {
        var index = new SpatialIndex<int>([(new Envelope2D(0, 0, 1, 1), 1)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => index.Join(index, distance));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Join(index, new List<(int, int)>(), distance));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SelfJoin(distance));
    }

    [Fact]
    public void Joins_are_thread_safe_on_shared_indexes()
    {
        var rng = new Random(7);
        Envelope2D Box()
        {
            double x = rng.NextDouble() * 1000, y = rng.NextDouble() * 1000;
            return new Envelope2D(x, y, x + 20, y + 20);
        }

        var left = new SpatialIndex<int>(Enumerable.Range(0, 2000).Select(i => (Box(), i)));
        var right = new SpatialIndex<int>(Enumerable.Range(0, 2000).Select(i => (Box(), i)));
        int expected = left.Join(right).Count();
        int expectedSelf = left.SelfJoin().Count();

        var counts = new (int, int)[16];
        Parallel.For(0, counts.Length, i => counts[i] = (left.Join(right).Count(), left.SelfJoin().Count()));

        Assert.All(counts, c => Assert.Equal((expected, expectedSelf), c));
    }

    [Fact]
    public void Exact_geometry_tests_stay_with_the_caller()
    {
        var factory = ImpGeometryFactory.Instance;
        var parcels = new[] { Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", factory) };
        var zones = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((8 8, 20 8, 20 12, 12 12, 8 8))", factory),   // overlaps the parcel
            Wkt.Read<IPolygon2D>("POLYGON((11 0, 12 10, 14 10, 11 0))", factory),       // envelope meets, polygon does not
        };

        var candidates = parcels.ToSpatialIndex().Join(zones.ToSpatialIndex()).ToList();
        var exact = candidates.Where(p => p.Left.Intersects(p.Right)).ToList();

        Assert.Single(candidates);
        Assert.Single(exact);
    }
}
