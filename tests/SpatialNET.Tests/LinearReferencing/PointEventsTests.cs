using CsCheck;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>Point events and drawing events on the route (ADR 0040); NTS has no counterpart, so hand cases and a model test.</summary>
[Covers("Between")]
[Covers("Within")]
[Covers("Outside")]
[Covers("LocateEvents")]
public class PointEventsTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static PointEvent<T> P<T>(double m, T value) => new(m, value);

    private static LineEvent<T> L<T>(double from, double to, T value) => new(from, to, value);

    private static Coordinate C(double x, double y, double m = double.NaN) => new(x, y, double.NaN, m);

    private static readonly LineEvent<int>[] Speed = [L(0, 5000, 50), L(5000, 12000, 70)];

    // ---------- Between ----------

    [Fact]
    public void Between_keeps_the_events_in_the_closed_range_sorted_by_M()
    {
        var events = new[] { P(9000.0, "c"), P(2000.0, "a"), P(5000.0, "b"), P(12000.0, "d"), P(-5.0, "e") };

        Assert.Equal(["a", "b", "c"], PointEvents.Between(events, 2000, 9000).Select(e => e.Value));
        Assert.Equal(["a", "b", "c"], PointEvents.Between(events, 9000, 2000).Select(e => e.Value));   // swapped bounds
        Assert.Equal(["d"], PointEvents.Between(events, 12000, 12000).Select(e => e.Value));
        Assert.Empty(PointEvents.Between(events, 20000, 30000));
    }

    [Fact]
    public void Events_with_the_same_M_keep_the_order_of_the_input()
    {
        var events = new[] { P(5.0, "first"), P(1.0, "x"), P(5.0, "second"), P(5.0, "third") };

        Assert.Equal(["x", "first", "second", "third"], PointEvents.Between(events, 0, 10).Select(e => e.Value));
    }

    // ---------- Within ----------

    [Fact]
    public void Within_gives_a_result_for_every_line_event_that_contains_the_point()
    {
        var bridges = new[] { P(8200.0, "bridge"), P(300.0, "culvert") };

        var result = PointEvents.Within(bridges, Speed, (b, speed) => $"{b} at {speed} km/h");

        Assert.Equal([(300d, "culvert at 50 km/h"), (8200d, "bridge at 70 km/h")], result.Select(e => (e.M, e.Value)));
    }

    [Fact]
    public void Within_gives_two_results_on_the_boundary_of_neighbours_and_one_per_overlapping_event()
    {
        var onBoundary = PointEvents.Within([P(5000.0, "x")], Speed, (p, s) => s);
        Assert.Equal([50, 70], onBoundary.Select(e => e.Value));

        var overlapping = PointEvents.Within([P(5.0, "x")], new[] { L(0, 10, "a"), L(4, 6, "b"), L(7, 9, "c") }, (p, s) => s);
        Assert.Equal(["a", "b"], overlapping.Select(e => e.Value));
    }

    [Fact]
    public void Within_ignores_line_events_without_length_and_accepts_swapped_bounds_and_unsorted_input()
    {
        var lines = new[] { L(10.0, 5.0, "reversed"), L(7.0, 7.0, "dot"), L(0.0, 3.0, "early") };

        var result = PointEvents.Within([P(8.0, 0), P(1.0, 0), P(7.0, 0)], lines, (p, s) => s);

        Assert.Equal([(1d, "early"), (7d, "reversed"), (8d, "reversed")], result.Select(e => (e.M, e.Value)));
    }

    // ---------- Outside ----------

    [Fact]
    public void Outside_gives_the_point_events_that_are_in_no_line_event()
    {
        var lines = new[] { L(0, 100, "a"), L(200, 300, "b") };
        var points = new[] { P(150.0, "gap"), P(50.0, "in"), P(400.0, "beyond"), P(-1.0, "before"), P(100.0, "edge"), P(200.0, "edge2") };

        Assert.Equal(["before", "gap", "beyond"], PointEvents.Outside(points, lines).Select(e => e.Value));
        Assert.Equal(["before", "in", "edge", "gap", "edge2", "beyond"], PointEvents.Outside(points, Array.Empty<LineEvent<int>>()).Select(e => e.Value));
    }

    // ---------- model test ----------

    [Fact]
    public void Within_and_Outside_agree_with_a_brute_force_check()
    {
        Gen.Select(Gen.Int[0, 40].Array[0, 20], Gen.Int[0, 40].Array[0, 12], Gen.Int[1, 15].Array[12]).Sample((ms, starts, lengths) =>
        {
            var points = ms.Select((m, i) => P(m + 0.0, i)).ToArray();
            var lines = starts.Select((s, i) => L(s + 0.0, s + lengths[i % lengths.Length], i)).ToArray();

            var within = PointEvents.Within(points, lines, (p, l) => (p, l));
            var outside = PointEvents.Outside(points, lines);

            var expected = new List<(int, int)>();
            foreach (var p in points.OrderBy(e => e.M))
            {
                foreach (var l in lines)
                {
                    if (p.M >= l.From && p.M <= l.To)
                    {
                        expected.Add((p.Value, l.Value));
                    }
                }
            }

            Assert.Equal(expected.Order(), within.Select(e => e.Value).Order());
            Assert.Equal(points.Where(p => !lines.Any(l => p.M >= l.From && p.M <= l.To)).Select(p => p.Value).Order(), outside.Select(p => p.Value).Order());
            Assert.True(within.Zip(within.Skip(1)).All(pair => pair.First.M <= pair.Second.M));
        }, iter: 300);
    }

    [Fact]
    public void Bad_input_throws()
    {
        Assert.Throws<ArgumentNullException>(() => PointEvents.Between<int>(null!, 0, 1));
        Assert.Throws<ArgumentNullException>(() => PointEvents.Within<int, int, int>(null!, Speed, (a, b) => 0));
        Assert.Throws<ArgumentNullException>(() => PointEvents.Within<int, int, int>([], null!, (a, b) => 0));
        Assert.Throws<ArgumentNullException>(() => PointEvents.Within<int, int, int>([], Speed, null!));
        Assert.Throws<ArgumentNullException>(() => PointEvents.Outside<int, int>(null!, Speed));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointEvents.Between([P(double.NaN, 0)], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointEvents.Between([P(1.0, 0)], double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointEvents.Within([P(double.PositiveInfinity, 0)], Speed, (a, b) => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointEvents.Outside([P(1.0, 0)], new[] { L(double.NaN, 1, 0) }));
    }

    // ---------- LocateEvents ----------

    private static MPolyline Route() => new([[C(0, 0, 12000), C(100, 0, 12200), C(100, 80, 12360)]]);

    [Fact]
    public void LocateEvents_draws_every_line_event_in_the_order_of_the_input()
    {
        var events = new[] { L(12100, 12250, "b"), L(12000, 12100, "a"), L(20000, 21000, "far") };

        var drawn = Route().LocateEvents(events, ZM);

        Assert.Equal(["b", "a", "far"], drawn.Select(d => d.Event.Value));
        Assert.Equal(75, drawn[0].Shape.Length(), 9);                  // 12100 is x = 50, 12250 is (100 25)
        Assert.Equal(50, drawn[1].Shape.Length(), 9);
        Assert.Equal(0, drawn[2].Shape.PathCount);                     // the route does not reach it: an entry with an empty shape
    }

    [Fact]
    public void LocateEvents_draws_every_point_event_and_all_its_points()
    {
        var route = new MPolyline([[C(0, 0, 0), C(10, 0, 100), C(20, 0, 0)]]);   // M rises and falls again

        var drawn = route.LocateEvents([P(50.0, "twice"), P(500.0, "never"), P(100.0, "top")], ZM);

        Assert.Equal([2, 0, 1], drawn.Select(d => d.Points.PointCount));
        Assert.Equal([5d, 15d], Enumerable.Range(0, 2).Select(i => drawn[0].Points[i].X));
    }

    [Fact]
    public void LocateEvents_agrees_with_LocateBetween_and_LocateAlong_and_checks_its_arguments()
    {
        var route = Route();
        var line = L(12010.0, 12330.0, 0);

        Assert.Equal(route.LocateBetween(12010, 12330, ZM).Length(), route.LocateEvents([line], ZM)[0].Shape.Length(), 12);
        Assert.Equal(route.LocateAlong(12100, ZM).PointCount, route.LocateEvents([P(12100.0, 0)], ZM)[0].Points.PointCount);

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).LocateEvents([line], ZM));
        Assert.Throws<ArgumentNullException>(() => route.LocateEvents((IEnumerable<LineEvent<int>>)null!, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.LocateEvents([L(double.NaN, 1.0, 0)], ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.LocateEvents([P(double.NaN, 0)], ZM));
        Assert.Empty(route.LocateEvents(Array.Empty<LineEvent<int>>(), ZM));
    }
}
