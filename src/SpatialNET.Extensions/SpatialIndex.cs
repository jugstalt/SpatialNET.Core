using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>
/// A static, bulk-loaded spatial index over envelopes with a payload (ADR 0014). The tree is packed once
/// with the Sort-Tile-Recursive (STR) method and never changes afterwards; to reflect changes, build a new index.
/// </summary>
/// <typeparam name="T">The payload, for example a geometry or an id.</typeparam>
/// <remarks>
/// <para>The index works on envelopes only. A query returns the items whose envelope touches or overlaps the
/// query envelope (closed intervals); an exact test on the real geometry is up to the caller.</para>
/// <para>Thread-safe: after construction the instance is immutable and every query uses only local state,
/// so any number of threads may query the same instance.</para>
/// </remarks>
public sealed partial class SpatialIndex<T>
{
    /// <summary>The default maximum number of children per node.</summary>
    public const int DefaultNodeCapacity = 16;

    private readonly Envelope2D[] _envelopes; // entries in tree order
    private readonly T[] _items;
    private readonly int[] _order;            // original position of each entry, for deterministic ties
    private readonly Node[] _nodes;           // level by level, leaves first; the root is the last node

    private readonly record struct Node(Envelope2D Envelope, int First, int Count, bool IsLeaf);

    /// <summary>Builds the index. The input is copied (snapshot, ADR 0013); later changes have no effect.</summary>
    /// <param name="items">The envelopes with their payload. Items with an empty envelope are skipped.</param>
    /// <param name="nodeCapacity">The maximum number of children per node, at least 2.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="nodeCapacity"/> is below 2, or an envelope is invalid (partly NaN, infinite, or Min greater than Max).
    /// </exception>
    public SpatialIndex(IEnumerable<(Envelope2D Envelope, T Item)> items, int nodeCapacity = DefaultNodeCapacity)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCapacity, 2);

        var envelopes = new List<Envelope2D>();
        var payload = new List<T>();
        foreach (var (envelope, item) in items)
        {
            if (envelope.IsEmpty && double.IsNaN(envelope.MinY) && double.IsNaN(envelope.MaxX) && double.IsNaN(envelope.MaxY))
            {
                continue; // an empty geometry never meets anything
            }

            EnsureValid(envelope, nameof(items));
            envelopes.Add(envelope);
            payload.Add(item);
        }

        int n = envelopes.Count;
        var perm = StrPacking.Order(envelopes, nodeCapacity);
        _envelopes = new Envelope2D[n];
        _items = new T[n];
        _order = perm;
        for (int i = 0; i < n; i++)
        {
            _envelopes[i] = envelopes[perm[i]];
            _items[i] = payload[perm[i]];
        }

        _nodes = BuildNodes(_envelopes, nodeCapacity);
    }

    /// <summary>The number of indexed items (items with an empty envelope are not counted).</summary>
    public int Count => _items.Length;

    /// <summary>The envelope of all indexed items; <see cref="Envelope2D.Empty"/> if the index is empty.</summary>
    public Envelope2D Extent => _nodes.Length == 0 ? Envelope2D.Empty : _nodes[^1].Envelope;

    /// <summary>
    /// The items whose envelope touches or overlaps <paramref name="envelope"/>, in no particular order.
    /// An empty query envelope yields nothing.
    /// </summary>
    /// <param name="envelope">The query envelope.</param>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid (partly NaN, infinite, or Min greater than Max).</exception>
    public IEnumerable<T> Query(Envelope2D envelope)
    {
        if (!Prepare(envelope)) return [];
        return Iterate(envelope);
    }

    /// <summary>Like <see cref="Query(Envelope2D)"/>, but adds the items to <paramref name="results"/> (no allocation of an enumerator).</summary>
    /// <param name="envelope">The query envelope.</param>
    /// <param name="results">The collection that receives the items; it is not cleared first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The envelope is invalid (partly NaN, infinite, or Min greater than Max).</exception>
    public void Query(Envelope2D envelope, ICollection<T> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (!Prepare(envelope)) return;
        foreach (int i in Search(envelope)) results.Add(_items[i]);
    }

    /// <summary>
    /// The <paramref name="k"/> items nearest to <paramref name="point"/>, ascending by distance; items at equal
    /// distance keep their input order. Fewer are returned if the index holds fewer.
    /// </summary>
    /// <param name="point">The query point.</param>
    /// <param name="k">How many items to return, at least 1.</param>
    /// <param name="distance">
    /// The exact distance of an item to the query point. Without it, the distance from the point to the item's
    /// envelope is used (0 inside). The delegate must never return less than the envelope distance, which holds
    /// for any real distance to a geometry inside the envelope.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The point is not finite, or <paramref name="k"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="distance"/> returned <see cref="double.NaN"/>.</exception>
    public IReadOnlyList<T> Nearest(Coordinate point, int k, Func<T, Coordinate, double>? distance = null)
    {
        if (!double.IsFinite(point.X)) throw new ArgumentOutOfRangeException(nameof(point), "The point must be finite.");
        if (!double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point), "The point must be finite.");
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);

        var result = new List<T>(Math.Min(k, Count));
        if (_nodes.Length == 0) return result;

        // Best-first search. At equal distance, nodes are expanded before unresolved items before resolved items,
        // so every candidate of that distance is queued before the first of them is returned.
        const int NodeKind = 0, UnresolvedKind = 1, ResolvedKind = 2;
        var queue = new PriorityQueue<(int Kind, int Index), (double Distance, int Kind, int Order)>();
        queue.Enqueue((NodeKind, _nodes.Length - 1), (DistanceTo(_nodes[^1].Envelope, point), NodeKind, 0));
        while (queue.TryDequeue(out var entry, out var priority) && result.Count < k)
        {
            switch (entry.Kind)
            {
                case NodeKind:
                    var node = _nodes[entry.Index];
                    for (int c = node.First; c < node.First + node.Count; c++)
                    {
                        if (node.IsLeaf)
                        {
                            double d = DistanceTo(_envelopes[c], point);
                            queue.Enqueue((distance is null ? ResolvedKind : UnresolvedKind, c),
                                (d, distance is null ? ResolvedKind : UnresolvedKind, _order[c]));
                        }
                        else
                        {
                            queue.Enqueue((NodeKind, c), (DistanceTo(_nodes[c].Envelope, point), NodeKind, 0));
                        }
                    }

                    break;
                case UnresolvedKind:
                    double exact = distance!(_items[entry.Index], point);
                    if (double.IsNaN(exact)) throw new InvalidOperationException("The distance delegate returned NaN.");
                    queue.Enqueue((ResolvedKind, entry.Index), (Math.Max(exact, priority.Distance), ResolvedKind, _order[entry.Index]));
                    break;
                default:
                    result.Add(_items[entry.Index]);
                    break;
            }
        }

        return result;
    }

    private bool Prepare(Envelope2D envelope)
    {
        if (envelope.IsEmpty && double.IsNaN(envelope.MinY) && double.IsNaN(envelope.MaxX) && double.IsNaN(envelope.MaxY))
        {
            return false;
        }

        EnsureValid(envelope, nameof(envelope));
        return _nodes.Length > 0;
    }

    private IEnumerable<T> Iterate(Envelope2D envelope)
    {
        foreach (int i in Search(envelope)) yield return _items[i];
    }

    private IEnumerable<int> Search(Envelope2D query)
    {
        var stack = new Stack<int>();
        stack.Push(_nodes.Length - 1);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (!Meets(node.Envelope, query)) continue;
            for (int c = node.First; c < node.First + node.Count; c++)
            {
                if (node.IsLeaf)
                {
                    if (Meets(_envelopes[c], query)) yield return c;
                }
                else
                {
                    stack.Push(c);
                }
            }
        }
    }

    private static bool Meets(Envelope2D a, Envelope2D b) =>
        a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY;

    private static double DistanceTo(Envelope2D e, Coordinate p)
    {
        double dx = Math.Max(Math.Max(e.MinX - p.X, p.X - e.MaxX), 0);
        double dy = Math.Max(Math.Max(e.MinY - p.Y, p.Y - e.MaxY), 0);
        return dx == 0 ? dy : dy == 0 ? dx : Math.Sqrt(dx * dx + dy * dy);
    }

    private static void EnsureValid(Envelope2D e, string paramName)
    {
        if (!double.IsFinite(e.MinX) || !double.IsFinite(e.MinY) || !double.IsFinite(e.MaxX) || !double.IsFinite(e.MaxY)
            || e.MinX > e.MaxX || e.MinY > e.MaxY)
        {
            throw new ArgumentOutOfRangeException(paramName,
                $"Invalid envelope {e}: values must be finite and Min must not exceed Max (use Envelope2D.Empty for nothing).");
        }
    }

    private static Node[] BuildNodes(Envelope2D[] entries, int capacity)
    {
        if (entries.Length == 0) return [];

        var nodes = new List<Node>();
        int levelStart = 0;
        var level = entries;
        bool leaf = true;
        while (true)
        {
            // Entries are already in STR order; upper levels are ordered here (the nodes they refer to keep their children).
            int count = level.Length;
            var parents = new List<Node>();
            for (int first = 0; first < count; first += capacity)
            {
                int size = Math.Min(capacity, count - first);
                var box = level[first];
                for (int i = 1; i < size; i++) box = Union(box, level[first + i]);
                parents.Add(new Node(box, levelStart + first, size, leaf));
            }

            int parentStart = nodes.Count;
            nodes.AddRange(parents);
            if (parents.Count == 1) return nodes.ToArray();

            // Pack the next level in STR order; the order of the nodes in the array must follow it so that children stay contiguous.
            var boxes = parents.Select(p => p.Envelope).ToList();
            var perm = StrPacking.Order(boxes, capacity);
            for (int i = 0; i < perm.Length; i++) nodes[parentStart + i] = parents[perm[i]];
            level = perm.Select(i => boxes[i]).ToArray();
            levelStart = parentStart;
            leaf = false;
        }
    }

    private static Envelope2D Union(Envelope2D a, Envelope2D b) =>
        new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));

}

/// <summary>Building and querying a <see cref="SpatialIndex{T}"/> from geometries (ADR 0014).</summary>
public static class SpatialIndexing
{
    /// <summary>Builds an index over <paramref name="source"/>, with <paramref name="envelopeOf"/> giving each item's envelope.</summary>
    /// <typeparam name="T">The item type, kept as the payload.</typeparam>
    /// <param name="source">The items.</param>
    /// <param name="envelopeOf">The envelope of an item.</param>
    /// <param name="nodeCapacity">The maximum number of children per node, at least 2.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="envelopeOf"/> is <c>null</c>.</exception>
    public static SpatialIndex<T> ToSpatialIndex<T>(this IEnumerable<T> source, Func<T, Envelope2D> envelopeOf,
        int nodeCapacity = SpatialIndex<T>.DefaultNodeCapacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(envelopeOf);
        return new SpatialIndex<T>(source.Select(item => (envelopeOf(item), item)), nodeCapacity);
    }

    /// <summary>Builds an index over geometries, keyed by their <see cref="Measures.Envelope(IGeometry2D)"/>.</summary>
    /// <typeparam name="TGeometry">The geometry type, kept as the payload.</typeparam>
    /// <param name="geometries">The geometries; empty geometries are skipped.</param>
    /// <param name="nodeCapacity">The maximum number of children per node, at least 2.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometries"/> or one of its elements is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">An element is none of the known geometry kinds.</exception>
    public static SpatialIndex<TGeometry> ToSpatialIndex<TGeometry>(this IEnumerable<TGeometry> geometries,
        int nodeCapacity = SpatialIndex<TGeometry>.DefaultNodeCapacity)
        where TGeometry : IGeometry2D
    {
        ArgumentNullException.ThrowIfNull(geometries);
        return new SpatialIndex<TGeometry>(geometries.Select(g =>
        {
            Internal.GeometryAccess.EnsureKnown(g, nameof(geometries));
            return (g.Envelope(), g);
        }), nodeCapacity);
    }

    /// <summary>The items whose envelope touches or overlaps the envelope of <paramref name="geometry"/>; an exact test is up to the caller.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="geometry">The query geometry; an empty geometry yields nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="geometry"/> is <c>null</c>.</exception>
    public static IEnumerable<T> Query<T>(this SpatialIndex<T> index, IGeometry2D geometry)
    {
        ArgumentNullException.ThrowIfNull(index);
        Internal.GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return index.Query(geometry.Envelope());
    }
}
