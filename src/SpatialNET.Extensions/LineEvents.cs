namespace SpatialNET;

/// <summary>
/// An event on a route: something that holds for a range of M values, with a value of the host's choice (ADR 0033).
/// A speed limit from kilometre 3.0 to 5.5, a surface, a road class.
/// </summary>
/// <typeparam name="T">The attributes of the event; any type.</typeparam>
/// <param name="From">The M value where the event starts.</param>
/// <param name="To">The M value where it ends; if it is lower than <paramref name="From"/>, the operations treat the two as swapped.</param>
/// <param name="Value">The attributes.</param>
public readonly record struct LineEvent<T>(double From, double To, T Value);

/// <summary>
/// Interval arithmetic on the events of a route (ADR 0033): which events hold at the same time (<c>Intersect</c>), the route cut at
/// every boundary of two tables (<c>Union</c>), what one table has that the other has not (<c>Difference</c>), neighbours with the
/// same value joined (<c>Dissolve</c>), and cutting to a range (<c>Clip</c>) or to a point (<c>At</c>). The operations compute with the
/// M values only: no geometry, no factory. Draw an event with <see cref="LinearReferencing.LocateBetween"/> on the calibrated route.
/// </summary>
/// <remarks>
/// <para>
/// Ranges are closed, but only a range <b>with length</b> counts: an event with <c>From == To</c> is ignored, and an operation never
/// returns a piece without length (as <c>Substring</c> in ADR 0017). Events are not touched by the order of the input; results are sorted
/// by <c>From</c>, then <c>To</c>. The operations work on one route: group a table by route yourself before calling them.
/// </para>
/// <para>
/// The methods are static and not extension methods so that they do not collide with <c>Enumerable.Union</c>, <c>Intersect</c> and
/// the like.
/// </para>
/// </remarks>
public static class LineEvents
{
    /// <summary>
    /// The pieces where an event of <paramref name="a"/> and an event of <paramref name="b"/> hold at the same time, one per pair,
    /// with the value that <paramref name="combine"/> makes of the two values. Where only one table has an event, nothing is returned.
    /// </summary>
    /// <remarks>Events of one table may overlap; every overlapping pair gives a piece.</remarks>
    /// <typeparam name="TA">The values of the first table.</typeparam>
    /// <typeparam name="TB">The values of the second table.</typeparam>
    /// <typeparam name="TResult">The values of the result.</typeparam>
    /// <param name="a">The first table.</param>
    /// <param name="b">The second table.</param>
    /// <param name="combine">Makes the value of a piece from the two values.</param>
    /// <returns>The pieces, sorted.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    public static IReadOnlyList<LineEvent<TResult>> Intersect<TA, TB, TResult>(
        IEnumerable<LineEvent<TA>> a, IEnumerable<LineEvent<TB>> b, Func<TA, TB, TResult> combine)
    {
        ArgumentNullException.ThrowIfNull(combine);
        var left = Prepare(a, nameof(a));
        var right = Prepare(b, nameof(b));

        // For every event of a, only the events of b whose range reaches it: skip those that end before it starts.
        var reach = new double[right.Count];
        double max = double.NegativeInfinity;
        for (int i = 0; i < right.Count; i++)
        {
            max = Math.Max(max, right[i].To);
            reach[i] = max;
        }

        var result = new List<LineEvent<TResult>>();
        foreach (var x in left)
        {
            int start = FirstGreater(reach, x.From);
            for (int i = start; i < right.Count && right[i].From < x.To; i++)
            {
                double from = Math.Max(x.From, right[i].From), to = Math.Min(x.To, right[i].To);
                if (to > from)
                {
                    result.Add(new LineEvent<TResult>(from, to, combine(x.Value, right[i].Value)));
                }
            }
        }

        return Sorted(result);
    }

    /// <summary>
    /// The route cut at every boundary of either table: each piece that has an event in at least one table, with the value of
    /// <paramref name="both"/> where both have one, of <paramref name="onlyA"/> or <paramref name="onlyB"/> where only one has. Pieces
    /// that neither table covers are not returned. Neighbouring pieces with equal values are not joined; use <see cref="Dissolve"/>.
    /// </summary>
    /// <remarks>
    /// The events of one table must not overlap (touching is fine), since otherwise a piece would have two values from the same table.
    /// </remarks>
    /// <typeparam name="TA">The values of the first table.</typeparam>
    /// <typeparam name="TB">The values of the second table.</typeparam>
    /// <typeparam name="TResult">The values of the result.</typeparam>
    /// <param name="a">The first table.</param>
    /// <param name="b">The second table.</param>
    /// <param name="both">The value of a piece covered by both tables.</param>
    /// <param name="onlyA">The value of a piece covered only by <paramref name="a"/>.</param>
    /// <param name="onlyB">The value of a piece covered only by <paramref name="b"/>.</param>
    /// <returns>The pieces, sorted.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    /// <exception cref="ArgumentException">Events of one table overlap.</exception>
    public static IReadOnlyList<LineEvent<TResult>> Union<TA, TB, TResult>(
        IEnumerable<LineEvent<TA>> a, IEnumerable<LineEvent<TB>> b,
        Func<TA, TB, TResult> both, Func<TA, TResult> onlyA, Func<TB, TResult> onlyB)
    {
        ArgumentNullException.ThrowIfNull(both);
        ArgumentNullException.ThrowIfNull(onlyA);
        ArgumentNullException.ThrowIfNull(onlyB);
        var left = Prepare(a, nameof(a));
        var right = Prepare(b, nameof(b));
        EnsureDisjoint(left, nameof(a));
        EnsureDisjoint(right, nameof(b));

        var breaks = new SortedSet<double>();
        foreach (var e in left)
        {
            breaks.Add(e.From);
            breaks.Add(e.To);
        }

        foreach (var e in right)
        {
            breaks.Add(e.From);
            breaks.Add(e.To);
        }

        var points = breaks.ToArray();
        var result = new List<LineEvent<TResult>>();
        int ia = 0, ib = 0;
        for (int k = 0; k + 1 < points.Length; k++)
        {
            double p = points[k], q = points[k + 1];
            while (ia < left.Count && left[ia].To <= p)
            {
                ia++;
            }

            while (ib < right.Count && right[ib].To <= p)
            {
                ib++;
            }

            bool hasA = ia < left.Count && left[ia].From <= p;
            bool hasB = ib < right.Count && right[ib].From <= p;
            if (hasA && hasB)
            {
                result.Add(new LineEvent<TResult>(p, q, both(left[ia].Value, right[ib].Value)));
            }
            else if (hasA)
            {
                result.Add(new LineEvent<TResult>(p, q, onlyA(left[ia].Value)));
            }
            else if (hasB)
            {
                result.Add(new LineEvent<TResult>(p, q, onlyB(right[ib].Value)));
            }
        }

        return result;
    }

    /// <summary>
    /// The parts of the events of <paramref name="a"/> that no event of <paramref name="b"/> covers, with the values of
    /// <paramref name="a"/>. An event of <paramref name="a"/> that another event of <paramref name="b"/> splits gives two pieces.
    /// </summary>
    /// <remarks>Only the range of the events of <paramref name="b"/> counts, not their values; they may overlap each other.</remarks>
    /// <typeparam name="TA">The values of the first table.</typeparam>
    /// <typeparam name="TB">The values of the second table; not used.</typeparam>
    /// <param name="a">The events to take from.</param>
    /// <param name="b">The events whose ranges are taken away.</param>
    /// <returns>The pieces, sorted.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    public static IReadOnlyList<LineEvent<TA>> Difference<TA, TB>(IEnumerable<LineEvent<TA>> a, IEnumerable<LineEvent<TB>> b)
    {
        var left = Prepare(a, nameof(a));
        var cover = new List<(double From, double To)>();
        foreach (var e in Prepare(b, nameof(b)))
        {
            if (cover.Count > 0 && e.From <= cover[^1].To)
            {
                cover[^1] = (cover[^1].From, Math.Max(cover[^1].To, e.To));
            }
            else
            {
                cover.Add((e.From, e.To));
            }
        }

        // The merged ranges are disjoint and sorted, so their ends are sorted, too.
        var ends = cover.Select(c => c.To).ToArray();
        var result = new List<LineEvent<TA>>();
        foreach (var x in left)
        {
            double cursor = x.From;
            for (int i = FirstGreater(ends, x.From); i < cover.Count && cover[i].From < x.To; i++)
            {
                if (cover[i].From > cursor)
                {
                    result.Add(x with { From = cursor, To = cover[i].From });
                }

                cursor = Math.Max(cursor, cover[i].To);
            }

            if (cursor < x.To)
            {
                result.Add(x with { From = cursor, To = x.To });
            }
        }

        return Sorted(result);
    }

    /// <summary>
    /// Joins events that touch or overlap and have the same value into one event. Events with different values are left as they
    /// are, even where they overlap. Use it after <see cref="Union"/> or <see cref="Intersect"/>, which cut at every boundary.
    /// </summary>
    /// <typeparam name="T">The values.</typeparam>
    /// <param name="events">The events.</param>
    /// <param name="comparer">Tells whether two values are the same; <c>null</c> = the default comparer of <typeparamref name="T"/>.</param>
    /// <returns>The events, sorted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    public static IReadOnlyList<LineEvent<T>> Dissolve<T>(IEnumerable<LineEvent<T>> events, IEqualityComparer<T>? comparer = null)
    {
        var sorted = Prepare(events, nameof(events));
        var keys = new Dictionary<Box<T>, int>(new BoxComparer<T>(comparer ?? EqualityComparer<T>.Default));
        var runs = new List<LineEvent<T>>();
        foreach (var e in sorted)
        {
            // Sorted by From: of all runs with this value only the latest can still reach e.
            if (keys.TryGetValue(new Box<T>(e.Value), out int run) && e.From <= runs[run].To)
            {
                runs[run] = runs[run] with { To = Math.Max(runs[run].To, e.To) };
            }
            else
            {
                keys[new Box<T>(e.Value)] = runs.Count;
                runs.Add(e);
            }
        }

        return Sorted(runs);
    }

    /// <summary>The events cut to the range between two M values; an event outside it is dropped.</summary>
    /// <typeparam name="T">The values.</typeparam>
    /// <param name="events">The events.</param>
    /// <param name="from">One end of the range.</param>
    /// <param name="to">The other end; if it is lower than <paramref name="from"/> they are swapped.</param>
    /// <returns>The events inside the range, sorted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite, or <paramref name="from"/> or <paramref name="to"/> is.</exception>
    public static IReadOnlyList<LineEvent<T>> Clip<T>(IEnumerable<LineEvent<T>> events, double from, double to)
    {
        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        var result = new List<LineEvent<T>>();
        foreach (var e in Prepare(events, nameof(events)))
        {
            double start = Math.Max(e.From, lo), end = Math.Min(e.To, hi);
            if (end > start)
            {
                result.Add(e with { From = start, To = end });
            }
        }

        return result;
    }

    /// <summary>
    /// The events that hold at an M value. The ranges are closed, so at the boundary of two neighbouring events both are returned.
    /// </summary>
    /// <typeparam name="T">The values.</typeparam>
    /// <param name="events">The events.</param>
    /// <param name="m">The M value, for example of a bridge.</param>
    /// <returns>The events whose range contains <paramref name="m"/>, sorted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has a <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite, or <paramref name="m"/> is.</exception>
    public static IReadOnlyList<LineEvent<T>> At<T>(IEnumerable<LineEvent<T>> events, double m)
    {
        EnsureFinite(m, nameof(m));
        return Prepare(events, nameof(events)).Where(e => e.From <= m && m <= e.To).ToList();
    }

    /// <summary>The events checked, with <c>From</c> not greater than <c>To</c>, without the ones that have no length, sorted.</summary>
    private static List<LineEvent<T>> Prepare<T>(IEnumerable<LineEvent<T>> events, string name)
    {
        ArgumentNullException.ThrowIfNull(events, name);
        var list = new List<LineEvent<T>>();
        int index = 0;
        foreach (var e in events)
        {
            if (!double.IsFinite(e.From) || !double.IsFinite(e.To))
            {
                throw new ArgumentOutOfRangeException(name, $"Event {index} has a From or To that is not finite ({e.From}, {e.To}).");
            }

            index++;
            if (e.From < e.To)
            {
                list.Add(e);
            }
            else if (e.From > e.To)
            {
                list.Add(e with { From = e.To, To = e.From });
            }
        }

        return Sorted(list);
    }

    private static List<LineEvent<T>> Sorted<T>(List<LineEvent<T>> list) =>
        list.OrderBy(e => e.From).ThenBy(e => e.To).ToList();

    private static void EnsureDisjoint<T>(List<LineEvent<T>> sorted, string name)
    {
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].From < sorted[i - 1].To)
            {
                throw new ArgumentException(
                    $"Events of '{name}' overlap: [{sorted[i - 1].From}, {sorted[i - 1].To}] and [{sorted[i].From}, {sorted[i].To}].", name);
            }
        }
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }
    }

    /// <summary>The index of the first element greater than <paramref name="value"/> in a sorted array, or its length.</summary>
    private static int FirstGreater(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] > value)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lo;
    }

    /// <summary>A value as a dictionary key, which may be <c>null</c> (a dictionary does not accept a <c>null</c> key).</summary>
    private readonly record struct Box<T>(T Value);

    private sealed class BoxComparer<T>(IEqualityComparer<T> comparer) : IEqualityComparer<Box<T>>
    {
        public bool Equals(Box<T> x, Box<T> y) => comparer.Equals(x.Value, y.Value);

        public int GetHashCode(Box<T> box) => box.Value is null ? 0 : comparer.GetHashCode(box.Value);
    }
}
