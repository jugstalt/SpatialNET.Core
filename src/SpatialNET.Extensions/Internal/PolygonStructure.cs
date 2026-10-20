namespace SpatialNET.Internal;

/// <summary>Eine Teilfläche mit geschlossenen, normalisierten Ringen (außen gegen, Löcher im Uhrzeigersinn).</summary>
internal sealed record Part(Coordinate[] Shell, IReadOnlyList<Coordinate[]> Holes)
{
    public PolygonPart ToPolygonPart() =>
        new(Shell, Holes.Select(h => (ReadOnlyMemory<Coordinate>)h).ToArray());
}

/// <summary>
/// Teilflächen-Bildung (ADR 0003): ordnet die flache Ringliste eines Polygons nach Verschachtelungstiefe –
/// gerade Tiefe = Außenring, ungerade = Loch des nächst umgebenden Außenrings; eine Insel im Loch ist eine
/// eigene Teilfläche. Degenerierte Ringe werden ignoriert, die Orientierung normalisiert.
/// </summary>
/// <remarks>
/// Setzt eine gültige Eingabe voraus (Ringe kreuzen sich nicht); für ungültige Eingaben ist das Ergebnis
/// deterministisch, aber nicht zugesichert. O(n²) über die Ringe – Optimierung folgt mit #38.
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

    /// <summary>Ob <paramref name="inner"/> innerhalb von <paramref name="outer"/> liegt (Ringe kreuzen sich nicht).</summary>
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

    private static Coordinate[] Oriented(Coordinate[] closed, bool counterClockwise)
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
