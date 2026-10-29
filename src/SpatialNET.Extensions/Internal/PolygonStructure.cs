namespace SpatialNET.Internal;

/// <summary>A part with closed, normalized rings (shell counter-clockwise, holes clockwise).</summary>
internal sealed record Part(Coordinate[] Shell, IReadOnlyList<Coordinate[]> Holes)
{
    public PolygonPart ToPolygonPart() =>
        new(Shell, Holes.Select(h => (ReadOnlyMemory<Coordinate>)h).ToArray());
}

/// <summary>
/// Part construction (ADR 0003): orders the flat ring list of a polygon by nesting depth –
/// even depth = shell, odd = hole of the nearest enclosing shell; an island in a hole is a
/// part of its own. Degenerate rings are ignored, the orientation is normalized.
/// </summary>
/// <remarks>
/// Requires valid input (rings do not cross); for invalid input the result is
/// deterministic but not guaranteed. O(n²) over the rings – optimization follows with #38.
/// </remarks>
internal static class PolygonStructure
{
    public static List<Part> Build(IPolygon2D polygon)
    {
        var rings = new List<Coordinate[]>();
        for (int i = 0; i < polygon.RingCount; i++)
        {
            var closed = Rings.Closed(polygon[i]);
            if (!Rings.IsDegenerate(closed))
            {
                rings.Add(closed);
            }
        }

        rings = CancelDuplicates(rings);
        int n = rings.Count;
        var (depth, parent) = Nesting(rings);

        var holes = new Dictionary<int, List<Coordinate[]>>();
        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 1)
            {
                (holes.TryGetValue(parent[i], out var list) ? list : holes[parent[i]] = []).Add(Oriented(rings[i], counterClockwise: false));
            }
        }

        var parts = new List<Part>();
        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 0)
            {
                parts.Add(new Part(Oriented(rings[i], counterClockwise: true), holes.TryGetValue(i, out var h) ? h : []));
            }
        }

        return parts;
    }

    /// <summary>
    /// Nesting of closed, non-degenerate rings: depth (number of enclosing rings) and direct
    /// parent ring (smallest enclosing ring, −1 for the top level).
    /// </summary>
    public static (int[] Depth, int[] Parent) Nesting(IReadOnlyList<Coordinate[]> rings)
    {
        int n = rings.Count;
        var area = rings.Select(r => Math.Abs(Rings.SignedArea(r))).ToArray();
        var depth = new int[n];
        var parent = new int[n];
        Array.Fill(parent, -1);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i != j && Contains(rings[j], rings[i]))
                {
                    depth[i]++;
                    if (parent[i] < 0 || area[j] < area[parent[i]])
                    {
                        parent[i] = j; // der kleinste umgebende Ring ist der direkte Elternring
                    }
                }
            }
        }

        return (depth, parent);
    }

    /// <summary>
    /// Even-odd: coincident rings (same point sequence, any start and direction) cancel each other in pairs.
    /// </summary>
    public static List<Coordinate[]> CancelDuplicates(List<Coordinate[]> rings)
    {
        // Coincident rings have the same points: only rings whose point count and point hash collide need the exact key.
        var hashes = new (int Count, long Hash)[rings.Count];
        var collisions = new Dictionary<(int Count, long Hash), int>();
        for (int i = 0; i < rings.Count; i++)
        {
            hashes[i] = PointHash(rings[i]);
            collisions[hashes[i]] = collisions.GetValueOrDefault(hashes[i]) + 1;
        }

        if (collisions.Count == rings.Count)
        {
            return rings;
        }

        var keys = rings.Select((r, i) => collisions[hashes[i]] > 1 ? Key(r) : null).ToList();
        var counts = keys.Where(k => k is not null).GroupBy(k => k!).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var result = new List<Coordinate[]>(rings.Count);
        for (int i = 0; i < rings.Count; i++)
        {
            if (keys[i] is not { } key)
            {
                result.Add(rings[i]);
                continue;
            }

            int count = counts[key];
            int index = seen[key] = seen.GetValueOrDefault(key) + 1;
            if (count % 2 == 1 && index == count)
            {
                result.Add(rings[i]); // bei ungerader Anzahl bleibt einer
            }
        }

        return result;
    }

    /// <summary>
    /// The number of points of the open ring and a hash of their set, independent of start and direction: rings with the
    /// same <see cref="Key"/> have the same value (a coordinate compares as in its round-trip text, every NaN alike).
    /// </summary>
    private static (int Count, long Hash) PointHash(Coordinate[] closed)
    {
        int count = Math.Max(0, closed.Length - 1);
        long hash = 0;
        for (int i = 0; i < count; i++)
        {
            long x = double.IsNaN(closed[i].X) ? 1 : BitConverter.DoubleToInt64Bits(closed[i].X);
            long y = double.IsNaN(closed[i].Y) ? 1 : BitConverter.DoubleToInt64Bits(closed[i].Y);
            unchecked
            {
                ulong mixed = (ulong)(x * -7046029254386353131L) ^ (ulong)(y * -4658895280553007687L);
                mixed ^= mixed >> 29;
                hash += (long)(mixed * 0xBF58476D1CE4E5B9UL);
            }
        }

        return (count, hash);
    }

    private static string Key(Coordinate[] closed)
    {
        var open = closed.Take(closed.Length - 1).Select(c => (c.X, c.Y)).ToArray();
        if (open.Length == 0) return "";
        int start = 0;
        for (int i = 1; i < open.Length; i++) if (open[i].CompareTo(open[start]) < 0) start = i;
        var forward = Enumerable.Range(0, open.Length).Select(i => open[(start + i) % open.Length]).ToArray();
        var backward = Enumerable.Range(0, open.Length).Select(i => open[(start - i + open.Length) % open.Length]).ToArray();
        var chosen = string.CompareOrdinal(string.Join(";", forward), string.Join(";", backward)) <= 0 ? forward : backward;
        return string.Join(";", chosen.Select(p => $"{p.X:R},{p.Y:R}"));
    }

    /// <summary>Part per ring: shells (even depth) are their own, holes belong to their parent ring.</summary>
    public static int[] PartOf(IReadOnlyList<Coordinate[]> rings)
    {
        var (depth, parent) = Nesting(rings);
        return Enumerable.Range(0, rings.Count).Select(i => depth[i] % 2 == 0 ? i : parent[i]).ToArray();
    }

    /// <summary>Whether <paramref name="inner"/> lies inside <paramref name="outer"/> (rings do not cross).</summary>
    private static bool Contains(Coordinate[] outer, Coordinate[] inner)
    {
        // Ein Vertex, der nicht auf dem Rand liegt, entscheidet; sonst ein Kantenmittelpunkt.
        foreach (var p in inner)
        {
            var location = Rings.Locate(p, outer);
            if (location != Location.Boundary)
            {
                return location == Location.Inside;
            }
        }

        for (int i = 1; i < inner.Length; i++)
        {
            var mid = new Coordinate((inner[i - 1].X + inner[i].X) / 2, (inner[i - 1].Y + inner[i].Y) / 2);
            var location = Rings.Locate(mid, outer);
            if (location != Location.Boundary)
            {
                return location == Location.Inside;
            }
        }

        return false; // deckungsgleicher Ring – ungültige Eingabe
    }

    internal static Coordinate[] Oriented(Coordinate[] closed, bool counterClockwise)
    {
        bool isCcw = Rings.SignedArea(closed) > 0;
        if (isCcw == counterClockwise)
        {
            return closed;
        }

        var reversed = (Coordinate[])closed.Clone();
        Array.Reverse(reversed);
        return reversed;
    }
}
