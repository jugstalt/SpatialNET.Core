namespace SpatialNET;

/// <summary>
/// An event at one M value on a route (ADR 0040): a bridge at kilometre 8.2, a damage report, a hydrant. The point-like counterpart of
/// <see cref="LineEvent{T}"/>.
/// </summary>
/// <typeparam name="T">The attributes of the event; any type.</typeparam>
/// <param name="M">The M value of the event.</param>
/// <param name="Value">The attributes.</param>
public readonly record struct PointEvent<T>(double M, T Value);

/// <summary>
/// Operations on the point events of a route (ADR 0040), as <see cref="LineEvents"/> is for the line events: which point events lie in a range
/// (<c>Between</c>), which lie in which line event (<c>Within</c>), and which lie in none (<c>Outside</c>). They compute with the M values only: no
/// geometry, no factory, no database access. Draw point events with <see cref="LinearReferencing.LocateEvents{T}(IPolyline2D, IEnumerable{PointEvent{T}}, IGeometryFactory?)"/>.
/// </summary>
/// <remarks>
/// The input may be in any order; results are sorted by M, and events with the same M keep the order of the input. The ranges of line events
/// are closed, so a point exactly on the boundary of two neighbouring line events lies in both (as with <see cref="LineEvents.At{T}"/>), and a
/// line event without length (<c>From == To</c>) is ignored, as in <see cref="LineEvents"/>. The methods are static for the same reason as there.
/// </remarks>
public static class PointEvents
{
    /// <summary>The point events whose M lies between two values (closed range).</summary>
    /// <typeparam name="T">The values.</typeparam>
    /// <param name="events">The point events.</param>
    /// <param name="from">One end of the range.</param>
    /// <param name="to">The other end; if it is lower than <paramref name="from"/> they are swapped.</param>
    /// <returns>The events inside the range, sorted by M.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has an M that is <c>NaN</c> or infinite, or <paramref name="from"/> or <paramref name="to"/> is.</exception>
    public static IReadOnlyList<PointEvent<T>> Between<T>(IEnumerable<PointEvent<T>> events, double from, double to)
    {
        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        return Prepare(events, nameof(events)).Where(e => e.M >= lo && e.M <= hi).ToList();
    }

    /// <summary>
    /// For every point event the line events whose range contains its M: one result per pair, at the M of the point, with the value that
    /// <paramref name="combine"/> makes of the two values. A point event that lies in no line event gives nothing (see <see cref="Outside{TPoint, TLine}"/>);
    /// one on the boundary of two neighbouring line events gives two results.
    /// </summary>
    /// <remarks>Line events may overlap; every overlapping one gives a result.</remarks>
    /// <typeparam name="TPoint">The values of the point events.</typeparam>
    /// <typeparam name="TLine">The values of the line events.</typeparam>
    /// <typeparam name="TResult">The values of the result.</typeparam>
    /// <param name="points">The point events.</param>
    /// <param name="lines">The line events.</param>
    /// <param name="combine">Makes the value of a result from the two values.</param>
    /// <returns>The results, sorted by M.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has an M, <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    public static IReadOnlyList<PointEvent<TResult>> Within<TPoint, TLine, TResult>(
        IEnumerable<PointEvent<TPoint>> points, IEnumerable<LineEvent<TLine>> lines, Func<TPoint, TLine, TResult> combine)
    {
        ArgumentNullException.ThrowIfNull(combine);
        var ordered = Prepare(points, nameof(points));
        var (sorted, reach) = Index(lines, nameof(lines));

        var result = new List<PointEvent<TResult>>();
        foreach (var point in ordered)
        {
            for (int i = FirstReaching(reach, point.M); i < sorted.Count && sorted[i].From <= point.M; i++)
            {
                if (sorted[i].To >= point.M)
                {
                    result.Add(new PointEvent<TResult>(point.M, combine(point.Value, sorted[i].Value)));
                }
            }
        }

        return result;
    }

    /// <summary>The point events that lie in no line event: a bridge without a speed limit, a report off the surveyed stretch.</summary>
    /// <typeparam name="TPoint">The values of the point events.</typeparam>
    /// <typeparam name="TLine">The values of the line events; not used.</typeparam>
    /// <param name="points">The point events.</param>
    /// <param name="lines">The line events.</param>
    /// <returns>The point events outside, sorted by M.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An event has an M, <c>From</c> or <c>To</c> that is <c>NaN</c> or infinite.</exception>
    public static IReadOnlyList<PointEvent<TPoint>> Outside<TPoint, TLine>(IEnumerable<PointEvent<TPoint>> points, IEnumerable<LineEvent<TLine>> lines)
    {
        var ordered = Prepare(points, nameof(points));
        var (sorted, reach) = Index(lines, nameof(lines));

        var result = new List<PointEvent<TPoint>>();
        foreach (var point in ordered)
        {
            bool inside = false;
            for (int i = FirstReaching(reach, point.M); i < sorted.Count && sorted[i].From <= point.M && !inside; i++)
            {
                inside = sorted[i].To >= point.M;
            }

            if (!inside)
            {
                result.Add(point);
            }
        }

        return result;
    }

    /// <summary>The point events checked and sorted by M (stable).</summary>
    private static List<PointEvent<T>> Prepare<T>(IEnumerable<PointEvent<T>> events, string name)
    {
        ArgumentNullException.ThrowIfNull(events, name);
        var list = new List<PointEvent<T>>();
        int index = 0;
        foreach (var e in events)
        {
            if (!double.IsFinite(e.M))
            {
                throw new ArgumentOutOfRangeException(name, $"Event {index} has an M that is not finite ({e.M}).");
            }

            list.Add(e);
            index++;
        }

        return list.OrderBy(e => e.M).ToList();
    }

    /// <summary>The line events with length, From not greater than To, sorted by From, with for every index the largest To up to it.</summary>
    private static (List<LineEvent<T>> Sorted, double[] Reach) Index<T>(IEnumerable<LineEvent<T>> lines, string name)
    {
        ArgumentNullException.ThrowIfNull(lines, name);
        var list = new List<LineEvent<T>>();
        int index = 0;
        foreach (var e in lines)
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

        var sorted = list.OrderBy(e => e.From).ThenBy(e => e.To).ToList();
        var reach = new double[sorted.Count];
        double max = double.NegativeInfinity;
        for (int i = 0; i < sorted.Count; i++)
        {
            max = Math.Max(max, sorted[i].To);
            reach[i] = max;
        }

        return (sorted, reach);
    }

    /// <summary>The index of the first entry of <paramref name="reach"/> (not decreasing) that is at least <paramref name="m"/>, or its length.</summary>
    private static int FirstReaching(double[] reach, double m)
    {
        int lo = 0, hi = reach.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (reach[mid] >= m)
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

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }
    }
}
