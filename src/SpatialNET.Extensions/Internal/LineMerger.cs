namespace SpatialNET.Internal;

/// <summary>Joins paths where exactly two path ends meet (ADR 0023). Nodes are compared by exact X and Y.</summary>
internal static class LineMerger
{
    /// <param name="paths">The paths, each with at least two distinct points.</param>
    /// <param name="directed">Whether paths are joined only end to start.</param>
    public static List<Coordinate[]> Merge(IReadOnlyList<Coordinate[]> paths, bool directed)
    {
        // an end is identified by an id: 2 * path for its start, 2 * path + 1 for its end
        var at = new Dictionary<(double X, double Y), List<int>>();
        for (int i = 0; i < paths.Count; i++)
        {
            Add(paths[i][0], 2 * i);
            Add(paths[i][^1], 2 * i + 1);
        }

        void Add(Coordinate c, int end)
        {
            if (!at.TryGetValue((c.X, c.Y), out var list))
            {
                at[(c.X, c.Y)] = list = [];
            }

            list.Add(end);
        }

        // The end that meets the given end, or -1: a node joins exactly two ends of different paths (and, directed, one start with one end).
        int PartnerOf(int end)
        {
            var path = paths[end >> 1];
            var c = (end & 1) == 0 ? path[0] : path[^1];
            var list = at[(c.X, c.Y)];
            if (list.Count != 2)
            {
                return -1;
            }

            int other = list[0] == end ? list[1] : list[0];
            if ((other >> 1) == (end >> 1) || (directed && (other & 1) == (end & 1)))
            {
                return -1;
            }

            return other;
        }

        var visited = new bool[paths.Count];
        var result = new List<Coordinate[]>();
        for (int seed = 0; seed < paths.Count; seed++)
        {
            if (visited[seed])
            {
                continue;
            }

            visited[seed] = true;
            var chain = new List<Coordinate>(paths[seed]);
            int tail = 2 * seed + 1, head = 2 * seed; // the open ends at the back and at the front of the chain

            // forward: the back of the chain meets another path; the joined vertex keeps the coordinate of the chain
            while (PartnerOf(tail) is var p && p >= 0 && !visited[p >> 1])
            {
                visited[p >> 1] = true;
                var path = paths[p >> 1];
                if ((p & 1) == 0)
                {
                    chain.AddRange(path.Skip(1)); // the partner is a start: continue through the path
                    tail = p | 1;
                }
                else
                {
                    chain.AddRange(Enumerable.Reverse(path).Skip(1)); // the partner is an end: run through the path backwards
                    tail = p & ~1;
                }
            }

            // backward: the front of the chain meets another path
            while (PartnerOf(head) is var p && p >= 0 && !visited[p >> 1])
            {
                visited[p >> 1] = true;
                var path = paths[p >> 1];
                if ((p & 1) == 1)
                {
                    chain.InsertRange(0, path.SkipLast(1)); // the partner is an end: the path flows into the chain
                    head = p & ~1;
                }
                else
                {
                    chain.InsertRange(0, Enumerable.Reverse(path).SkipLast(1));
                    head = p | 1;
                }
            }

            result.Add(chain.ToArray());
        }

        return result;
    }
}
