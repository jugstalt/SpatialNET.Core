using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// Interval arithmetic on route events (ADR 0033). NTS has no counterpart, so the cases are hand-written, plus a model test that
/// compares every operation with a cell-by-cell evaluation on random events.
/// </summary>
[Covers("Intersect")]
[Covers("Union")]
[Covers("Difference")]
[Covers("Dissolve")]
[Covers("Clip")]
[Covers("At")]
public class LineEventsTests
{
    private static LineEvent<T> E<T>(double from, double to, T value) => new(from, to, value);

    private static readonly LineEvent<int>[] Speed = [E(0, 5000, 50), E(5000, 12000, 70)];

    private static readonly LineEvent<string>[] Surface = [E(0, 3000, "asphalt"), E(3000, 8000, "gravel"), E(8000, 12000, "asphalt")];

    private static (double From, double To, TV Value)[] Rows<TV>(IEnumerable<LineEvent<TV>> events) =>
        events.Select(e => (e.From, e.To, e.Value)).ToArray();

    // ---------- Intersect ----------

    [Fact]
    public void Intersect_gives_where_both_tables_hold()
    {
        var result = LineEvents.Intersect(Speed, Surface, (s, b) => $"{s}/{b}");

        Assert.Equal(
            [(0d, 3000d, "50/asphalt"), (3000d, 5000d, "50/gravel"), (5000d, 8000d, "70/gravel"), (8000d, 12000d, "70/asphalt")],
            Rows(result));
    }

    [Fact]
    public void Intersect_leaves_out_what_only_one_table_has_and_pieces_without_length()
    {
        var a = new[] { E(0, 10, "a") };
        var b = new[] { E(5, 20, "b"), E(10, 30, "c"), E(-5, 0, "d") };   // c and d only touch a

        Assert.Equal([(5d, 10d, "a/b")], Rows(LineEvents.Intersect(a, b, (x, y) => $"{x}/{y}")));
    }

    [Fact]
    public void Intersect_pairs_overlapping_events_of_one_table()
    {
        var a = new[] { E(0, 10, 1), E(5, 15, 2) };
        var b = new[] { E(8, 12, "x") };

        Assert.Equal([(8d, 10d, "1x"), (8d, 12d, "2x")], Rows(LineEvents.Intersect(a, b, (p, q) => $"{p}{q}")));
    }

    // ---------- Union ----------

    [Fact]
    public void Union_cuts_at_every_boundary_and_names_the_side()
    {
        var a = new[] { E(0, 10, "a1"), E(20, 30, "a2") };
        var b = new[] { E(5, 25, "b1") };

        var result = LineEvents.Union(a, b, (x, y) => $"{x}+{y}", x => x, y => y);

        Assert.Equal(
            [(0d, 5d, "a1"), (5d, 10d, "a1+b1"), (10d, 20d, "b1"), (20d, 25d, "a2+b1"), (25d, 30d, "a2")],
            Rows(result));
    }

    [Fact]
    public void Union_does_not_return_what_neither_table_covers_and_does_not_join_neighbours()
    {
        var a = new[] { E(0, 10, "x"), E(10, 20, "x") };
        var b = new[] { E(30, 40, "y") };

        var result = LineEvents.Union(a, b, (p, q) => "both", p => p, q => q);

        Assert.Equal([(0d, 10d, "x"), (10d, 20d, "x"), (30d, 40d, "y")], Rows(result));
    }

    [Fact]
    public void Union_throws_if_events_of_one_table_overlap()
    {
        var a = new[] { E(0, 10, 1), E(5, 15, 2) };
        var b = new[] { E(0, 1, 1) };

        var ex = Assert.Throws<ArgumentException>(() => LineEvents.Union(a, b, (p, q) => 0, p => p, q => q));
        Assert.Equal("a", ex.ParamName);
        Assert.Throws<ArgumentException>(() => LineEvents.Union(b, a, (p, q) => 0, p => p, q => q));
        _ = LineEvents.Union([E(0, 10, 1), E(10, 15, 2)], b, (p, q) => 0, p => p, q => q); // touching is fine
    }

    // ---------- Difference ----------

    [Fact]
    public void Difference_takes_away_the_ranges_of_the_second_table()
    {
        var a = new[] { E(0, 100, "road") };
        var b = new[] { E(10, 20, 0), E(15, 30, 0), E(30, 40, 0), E(90, 120, 0), E(-5, 0, 0) };

        // 10..40 covered (the three ranges overlap or touch), 90..120 cuts the end, -5..0 only touches
        Assert.Equal([(0d, 10d, "road"), (40d, 90d, "road")], Rows(LineEvents.Difference(a, b)));
    }

    [Fact]
    public void Difference_keeps_events_the_second_table_does_not_touch_and_drops_covered_ones()
    {
        var a = new[] { E(0, 10, "a"), E(20, 30, "b"), E(40, 50, "c") };
        var b = new[] { E(18, 35, 0) };

        Assert.Equal([(0d, 10d, "a"), (40d, 50d, "c")], Rows(LineEvents.Difference(a, b)));
        Assert.Equal(Rows(a.OrderBy(e => e.From)), Rows(LineEvents.Difference(a, Array.Empty<LineEvent<int>>())));
    }

    // ---------- Dissolve ----------

    [Fact]
    public void Dissolve_joins_neighbours_with_the_same_value()
    {
        var events = new[] { E(3000, 5000, "asphalt"), E(0, 3000, "asphalt"), E(5000, 8000, "gravel"), E(8000, 9000, "asphalt") };

        Assert.Equal([(0d, 5000d, "asphalt"), (5000d, 8000d, "gravel"), (8000d, 9000d, "asphalt")], Rows(LineEvents.Dissolve(events)));
    }

    [Fact]
    public void Dissolve_joins_through_events_of_other_values_and_leaves_those_alone()
    {
        var events = new[] { E(0, 5, "a"), E(2, 3, "b"), E(4, 8, "a") };

        Assert.Equal([(0d, 8d, "a"), (2d, 3d, "b")], Rows(LineEvents.Dissolve(events)));
    }

    [Fact]
    public void Dissolve_keeps_a_gap_and_accepts_null_values_and_a_comparer()
    {
        Assert.Equal([(0d, 5d, "a"), (6d, 9d, "a")], Rows(LineEvents.Dissolve([E(0, 5, "a"), E(6, 9, "a")])));
        Assert.Equal([(0d, 9d, (string?)null)], Rows(LineEvents.Dissolve([E<string?>(0, 5, null), E<string?>(5, 9, null)])));
        Assert.Equal([(0d, 9d, "a")], Rows(LineEvents.Dissolve([E(0, 5, "a"), E(5, 9, "A")], StringComparer.OrdinalIgnoreCase)));
    }

    // ---------- Clip and At ----------

    [Fact]
    public void Clip_cuts_to_the_range_and_drops_what_lies_outside()
    {
        var result = LineEvents.Clip(Speed, 4000, 9000);

        Assert.Equal([(4000d, 5000d, 50), (5000d, 9000d, 70)], Rows(result));
        Assert.Equal(Rows(LineEvents.Clip(Speed, 4000, 9000)), Rows(LineEvents.Clip(Speed, 9000, 4000)));
        Assert.Empty(LineEvents.Clip(Speed, 5000, 5000));
        Assert.Empty(LineEvents.Clip(Speed, 20000, 30000));
    }

    [Fact]
    public void At_returns_the_events_that_hold_and_both_at_a_boundary()
    {
        Assert.Equal([(5000d, 12000d, 70)], Rows(LineEvents.At(Speed, 8200)));
        Assert.Equal([(0d, 5000d, 50), (5000d, 12000d, 70)], Rows(LineEvents.At(Speed, 5000)));
        Assert.Empty(LineEvents.At(Speed, 13000));
    }

    // ---------- Input rules ----------

    [Fact]
    public void Unsorted_input_swapped_bounds_and_events_without_length_are_handled()
    {
        var events = new[] { E(50d, 30d, "b"), E(5d, 5d, "dot"), E(0d, 10d, "a") };

        Assert.Equal([(0d, 10d, "a"), (30d, 50d, "b")], Rows(LineEvents.Dissolve(events)));
        Assert.Equal([(0d, 10d, "a")], Rows(LineEvents.At(events, 5)));      // the dot at 5 has no length: it does not hold
    }

    [Fact]
    public void Bad_input_throws()
    {
        var ok = Speed;
        var nan = new[] { E(double.NaN, 1, 0) };
        var inf = new[] { E(0, double.PositiveInfinity, 0) };

        Assert.Throws<ArgumentNullException>(() => LineEvents.Dissolve<int>(null!));
        Assert.Throws<ArgumentNullException>(() => LineEvents.Intersect<int, int, int>(ok, null!, (x, y) => 0));
        Assert.Throws<ArgumentNullException>(() => LineEvents.Intersect<int, int, int>(ok, ok, null!));
        Assert.Throws<ArgumentNullException>(() => LineEvents.Union<int, int, int>(ok, ok, (x, y) => 0, null!, y => y));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineEvents.Dissolve(nan));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineEvents.Difference(ok, inf));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineEvents.Clip(ok, double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineEvents.At(ok, double.PositiveInfinity));
    }

    [Fact]
    public void Empty_tables_give_empty_results()
    {
        var none = Array.Empty<LineEvent<int>>();

        Assert.Empty(LineEvents.Intersect(none, Speed, (x, y) => x));
        Assert.Empty(LineEvents.Union(none, none, (x, y) => x, x => x, y => y));
        Assert.Equal(Rows(Speed), Rows(LineEvents.Union(none, Speed, (x, y) => y, x => x, y => y)));
        Assert.Empty(LineEvents.Difference(none, Speed));
        Assert.Empty(LineEvents.Dissolve(none));
    }

    // ---------- Model test ----------

    private static LineEvent<int>[] RandomTable(Random rng, bool disjoint)
    {
        var list = new List<LineEvent<int>>();
        int value = 0;
        if (disjoint)
        {
            int at = rng.Next(0, 4);
            while (at < 30 && list.Count < 6)
            {
                int to = at + rng.Next(1, 8);
                list.Add(E(at, to, value++));
                at = to + rng.Next(0, 4);
            }
        }
        else
        {
            for (int i = 0; i < rng.Next(0, 7); i++)
            {
                int from = rng.Next(0, 30);
                list.Add(E(from, from + rng.Next(1, 12), value++));
            }
        }

        return list.OrderBy(_ => rng.Next()).ToArray();
    }

    private static int[] ValuesAt(IEnumerable<LineEvent<int>> table, double m) =>
        table.Where(e => e.From <= m && m <= e.To).Select(e => e.Value).Order().ToArray();

    [Fact]
    public void Dissolve_keeps_every_value_where_it_held_and_leaves_no_touching_pair_of_equal_values()
    {
        var rng = new Random(77);
        for (int round = 0; round < 300; round++)
        {
            var events = Enumerable.Range(0, rng.Next(0, 10))
                .Select(_ => { int from = rng.Next(0, 30); return E(from, from + rng.Next(1, 10), rng.Next(0, 3)); })
                .ToArray();

            var dissolved = LineEvents.Dissolve(events);

            for (int cell = -1; cell < 45; cell++)
            {
                Assert.Equal(ValuesAt(events, cell + 0.5).Distinct(), ValuesAt(dissolved, cell + 0.5));
            }

            foreach (var group in dissolved.GroupBy(e => e.Value))
            {
                var ordered = group.OrderBy(e => e.From).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                {
                    Assert.True(ordered[i].From > ordered[i - 1].To, $"value {group.Key}: {ordered[i - 1]} and {ordered[i]} touch or overlap");
                }
            }
        }
    }

    [Fact]
    public void Operations_agree_with_a_cell_by_cell_evaluation()
    {
        var rng = new Random(2026);
        for (int round = 0; round < 300; round++)
        {
            var a = RandomTable(rng, disjoint: true);
            var b = RandomTable(rng, disjoint: true);
            var loose = RandomTable(rng, disjoint: false);

            var intersect = LineEvents.Intersect(a, loose, (x, y) => x * 100 + y);
            var union = LineEvents.Union(a, b, (x, y) => 1000 + x * 100 + y, x => x, y => 5000 + y);
            var difference = LineEvents.Difference(loose, b);
            var dissolved = LineEvents.Dissolve(loose);

            for (int cell = -1; cell < 45; cell++)
            {
                double mid = cell + 0.5;   // inside one cell; no event boundary lies there (the ends are integers)
                var inA = ValuesAt(a, mid);
                var inB = ValuesAt(b, mid);
                var inLoose = ValuesAt(loose, mid);

                Assert.Equal(inA.SelectMany(x => inLoose.Select(y => x * 100 + y)).Order(), ValuesAt(intersect, mid));

                int[] expectedUnion = (inA, inB) switch
                {
                    ([var x], [var y]) => [1000 + x * 100 + y],
                    ([var x], []) => [x],
                    ([], [var y]) => [5000 + y],
                    _ => [],
                };
                Assert.Equal(expectedUnion, ValuesAt(union, mid));

                Assert.Equal(inB.Length == 0 ? inLoose : [], ValuesAt(difference, mid));
                Assert.Equal(inLoose, ValuesAt(dissolved, mid)); // every value is unique in this table, so nothing is joined or lost
            }

            // sorted and without length-less pieces
            foreach (var result in new[] { Rows(intersect), Rows(union.Select(e => e)), Rows(difference), Rows(dissolved) })
            {
                Assert.All(result, r => Assert.True(r.From < r.To));
                Assert.Equal(result.OrderBy(r => r.From).ThenBy(r => r.To).Select(r => (r.From, r.To)), result.Select(r => (r.From, r.To)));
            }
        }
    }
}
