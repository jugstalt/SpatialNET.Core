namespace SpatialNET.Internal;

/// <summary>Sort-Tile-Recursive ordering of envelopes, shared by the spatial index (ADR 0014) and <c>UnionAll</c> (ADR 0018).</summary>
internal static class StrPacking
{
    /// <summary>Sort-Tile-Recursive order: sort by X, cut into vertical slices, sort each slice by Y.</summary>
    /// <param name="envelopes">The envelopes.</param>
    /// <param name="capacity">The number of entries per node.</param>
    /// <returns>The permutation that puts neighbouring envelopes next to each other; ties are resolved by input order.</returns>
    public static int[] Order(IReadOnlyList<Envelope2D> envelopes, int capacity)
    {
        int n = envelopes.Count;
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;
        if (n <= capacity) return perm;

        Array.Sort(perm, (a, b) => Compare(CenterX(envelopes[a]), CenterX(envelopes[b]), a, b));
        int leafCount = (n + capacity - 1) / capacity;
        int sliceSize = (int)Math.Ceiling(Math.Sqrt(leafCount)) * capacity;
        for (int start = 0; start < n; start += sliceSize)
        {
            Array.Sort(perm, start, Math.Min(sliceSize, n - start),
                Comparer<int>.Create((a, b) => Compare(CenterY(envelopes[a]), CenterY(envelopes[b]), a, b)));
        }

        return perm;
    }

    private static double CenterX(Envelope2D e) => e.MinX / 2 + e.MaxX / 2;

    private static double CenterY(Envelope2D e) => e.MinY / 2 + e.MaxY / 2;

    private static int Compare(double a, double b, int ia, int ib)
    {
        int c = a.CompareTo(b);
        return c != 0 ? c : ia.CompareTo(ib);
    }
}
