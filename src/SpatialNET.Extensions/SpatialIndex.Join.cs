namespace SpatialNET;

public sealed partial class SpatialIndex<T>
{
    /// <summary>
    /// All pairs of an item of this index and an item of <paramref name="other"/> whose envelopes touch or overlap
    /// (closed intervals), found by walking both trees together (ADR 0021). The envelopes of <paramref name="other"/> are widened
    /// by <paramref name="distance"/> for the comparison. Like <c>Query</c>, this is a pre-filter: the exact test on the
    /// geometries is up to the caller.
    /// </summary>
    /// <typeparam name="TOther">The payload of the other index.</typeparam>
    /// <param name="other">The other index; it may be this index itself (every pair then occurs in both orders, and every item with itself).</param>
    /// <param name="distance">The distance by which the envelopes of <paramref name="other"/> are widened; finite and not negative.</param>
    /// <returns>Every pair exactly once, in no particular order; nothing if either index is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is negative, <c>NaN</c> or infinite.</exception>
    public IEnumerable<(T Left, TOther Right)> Join<TOther>(SpatialIndex<TOther> other, double distance = 0)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureJoinDistance(distance);
        if (_nodes.Length == 0 || other._nodes.Length == 0) return [];
        return JoinIterate(other, distance);
    }

    /// <summary>Like <see cref="Join{TOther}(SpatialIndex{TOther}, double)"/>, but adds the pairs to <paramref name="results"/>.</summary>
    /// <typeparam name="TOther">The payload of the other index.</typeparam>
    /// <param name="other">The other index.</param>
    /// <param name="results">The collection that receives the pairs; it is not cleared first.</param>
    /// <param name="distance">The distance by which the envelopes of <paramref name="other"/> are widened; finite and not negative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> or <paramref name="results"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is negative, <c>NaN</c> or infinite.</exception>
    public void Join<TOther>(SpatialIndex<TOther> other, ICollection<(T Left, TOther Right)> results, double distance = 0)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(results);
        EnsureJoinDistance(distance);
        if (_nodes.Length == 0 || other._nodes.Length == 0) return;
        foreach (var pair in JoinIterate(other, distance)) results.Add(pair);
    }

    /// <summary>
    /// All unordered pairs of different entries of this index whose envelopes touch or overlap (ADR 0021): each pair once,
    /// never an entry with itself. Entries with equal payload are different entries and pair up. The envelopes of one side
    /// are widened by <paramref name="distance"/> for the comparison.
    /// </summary>
    /// <param name="distance">The distance by which the envelopes are widened; finite and not negative.</param>
    /// <returns>Every pair exactly once, in no particular order and orientation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is negative, <c>NaN</c> or infinite.</exception>
    public IEnumerable<(T First, T Second)> SelfJoin(double distance = 0)
    {
        EnsureJoinDistance(distance);
        if (_nodes.Length == 0) return [];
        return SelfJoinIterate(distance);
    }

    private IEnumerable<(T, TOther)> JoinIterate<TOther>(SpatialIndex<TOther> other, double distance)
    {
        var stack = new Stack<(int Left, int Right)>();
        stack.Push((_nodes.Length - 1, other._nodes.Length - 1));
        while (stack.Count > 0)
        {
            var (l, r) = stack.Pop();
            var left = _nodes[l];
            var right = other._nodes[r];
            if (!MeetsWidened(left.Envelope, right.Envelope, distance)) continue;

            if (left.IsLeaf && right.IsLeaf)
            {
                for (int i = left.First; i < left.First + left.Count; i++)
                    for (int j = right.First; j < right.First + right.Count; j++)
                        if (MeetsWidened(_envelopes[i], other._envelopes[j], distance)) yield return (_items[i], other._items[j]);
            }
            else if (left.IsLeaf)
            {
                for (int c = right.First; c < right.First + right.Count; c++) stack.Push((l, c));
            }
            else if (right.IsLeaf)
            {
                for (int c = left.First; c < left.First + left.Count; c++) stack.Push((c, r));
            }
            else
            {
                for (int a = left.First; a < left.First + left.Count; a++)
                    for (int b = right.First; b < right.First + right.Count; b++)
                        stack.Push((a, b));
            }
        }
    }

    /// <summary>
    /// Walks pairs of nodes (x, y) with x ≤ y, so that every unordered pair of nodes, and with it every pair of entries,
    /// is visited once; inside one node only entries i &lt; j are paired.
    /// </summary>
    private IEnumerable<(T, T)> SelfJoinIterate(double distance)
    {
        var stack = new Stack<(int First, int Second)>();
        stack.Push((_nodes.Length - 1, _nodes.Length - 1));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var a = _nodes[x];
            var b = _nodes[y];
            if (!MeetsWidened(a.Envelope, b.Envelope, distance)) continue;

            if (a.IsLeaf && b.IsLeaf)
            {
                for (int i = a.First; i < a.First + a.Count; i++)
                    for (int j = x == y ? i + 1 : b.First; j < b.First + b.Count; j++)
                        if (MeetsWidened(_envelopes[i], _envelopes[j], distance)) yield return (_items[i], _items[j]);
            }
            else if (a.IsLeaf)
            {
                for (int c = b.First; c < b.First + b.Count; c++) stack.Push(Canonical(x, c));
            }
            else if (b.IsLeaf)
            {
                for (int c = a.First; c < a.First + a.Count; c++) stack.Push(Canonical(c, y));
            }
            else
            {
                for (int p = a.First; p < a.First + a.Count; p++)
                    for (int q = x == y ? p : b.First; q < b.First + b.Count; q++)
                        stack.Push(Canonical(p, q));
            }
        }

        static (int, int) Canonical(int p, int q) => p <= q ? (p, q) : (q, p);
    }

    /// <summary>Whether <paramref name="a"/> meets <paramref name="b"/> widened by <paramref name="distance"/> (symmetric).</summary>
    private static bool MeetsWidened(Envelope2D a, Envelope2D b, double distance) =>
        a.MinX <= b.MaxX + distance && b.MinX - distance <= a.MaxX && a.MinY <= b.MaxY + distance && b.MinY - distance <= a.MaxY;

    private static void EnsureJoinDistance(double distance)
    {
        if (!double.IsFinite(distance) || distance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be finite and not negative.");
        }
    }
}
