namespace SpatialNET.Internal.Overlay;

/// <summary>
/// Envelope prefilter for overlays (#38, after JTS OverlayNG RingClipper): ring parts far outside a
/// rectangle R are replaced by their projection onto the boundary of R. Unlike clipping, all
/// segments that touch R are kept unchanged (no new intersection points), and only runs of segments
/// completely outside are replaced: P → r(P) → r(run) → r(Q) → Q with r = clamping to R.
/// </summary>
/// <remarks>
/// The winding number of every point in the interior of R is preserved exactly: run and replacement are
/// homotopic via t ↦ (1 − t)·γ + t·r(γ), and this homotopy never leaves the complement of the open R
/// (the segment from a point outside to its projection onto a convex R does not touch its interior).
/// Artifact faces can arise outside R; the caller therefore selects only edges in the result region.
/// </remarks>
internal static class RingClipper
{
    /// <summary>The ring (closed) with outer runs replaced; unchanged if every segment touches R.</summary>
    public static Coordinate[] Clip(Coordinate[] ring, Envelope2D r)
    {
        int segments = ring.Length - 1;
        if (segments < 1)
        {
            return ring;
        }

        var keep = new bool[segments];
        bool any = false, all = true;
        for (int i = 0; i < segments; i++)
        {
            keep[i] = Touches(ring[i], ring[i + 1], r);
            any |= keep[i];
            all &= keep[i];
        }

        if (all)
        {
            return ring;
        }

        var result = new List<Coordinate>();
        if (!any)
        {
            // whole ring outside: its projection onto the border, reduced to its net turns (a rectangle if it encloses R)
            Retract(ring, 0, segments, r, result);
            return result.ToArray();
        }

        // start at a kept segment so that every outer run stays contiguous
        int start = Array.IndexOf(keep, true);
        for (int k = 0; k < segments;)
        {
            int i = (start + k) % segments;
            if (keep[i])
            {
                Add(result, ring[i]);
                k++;
                continue;
            }

            // outer run: original start point, then its projection along the border (net turns only)
            int length = 0;
            while (k + length < segments && !keep[(start + k + length) % segments]) length++;
            Add(result, ring[i]);
            Retract(ring, i, length, r, result);
            k += length;
        }

        Add(result, ring[start]);
        return result.ToArray();
    }

    /// <summary>
    /// The projection r(run) of <paramref name="count"/> segments completely outside, starting at <paramref name="first"/>
    /// (cyclic). Every such segment lies beyond one rectangle edge, so r(a)→r(b) runs along one side and the run projects to a
    /// path on the border loop. That path is replaced by the monotone one between the same unwrapped perimeter positions: the
    /// start point, the corners passed and the end point. Both are homotopic on the border, so the winding number of every
    /// interior point is kept. Keeping every projected point instead left a long, noisy outer run as thousands of overlapping
    /// segments on one border line, which made the noding nearly quadratic (a 100000-vertex ring took 2 s and 1 GB).
    /// </summary>
    private static void Retract(Coordinate[] ring, int first, int count, Envelope2D r, List<Coordinate> result)
    {
        int segments = ring.Length - 1;
        double w = r.MaxX - r.MinX, h = r.MaxY - r.MinY, perimeter = 2 * (w + h);
        var a = Clamp(ring[first], r);
        double s0 = Position(a, r), previous = s0, travelled = 0;
        for (int k = 0; k < count; k++)
        {
            double next = Position(Clamp(ring[(first + k + 1) % segments], r), r);
            double delta = next - previous;
            if (delta > perimeter / 2) delta -= perimeter;
            else if (delta < -perimeter / 2) delta += perimeter;
            travelled += delta;
            previous = next;
        }

        var b = Clamp(ring[(first + count) % segments], r);
        // exact end position: the end's own position plus the whole loops travelled
        double s1 = perimeter > 0 ? previous + perimeter * Math.Round((s0 + travelled - previous) / perimeter) : s0;

        Add(result, a);
        if (s1 != s0)
        {
            double lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            long m0 = (long)Math.Floor(lo / perimeter), m1 = (long)Math.Floor(hi / perimeter);
            var passed = new List<(double Value, int Corner)>();
            for (long m = m0; m <= m1; m++)
            {
                for (int c = 0; c < 4; c++)
                {
                    double v = Corner(c, w, h) + m * perimeter;
                    if (v > lo && v < hi) passed.Add((v, c));
                }
            }

            if (s1 < s0) passed.Reverse();
            foreach (var (_, c) in passed)
            {
                Add(result, c switch
                {
                    0 => new Coordinate(r.MinX, r.MinY),
                    1 => new Coordinate(r.MaxX, r.MinY),
                    2 => new Coordinate(r.MaxX, r.MaxY),
                    _ => new Coordinate(r.MinX, r.MaxY),
                });
            }
        }

        Add(result, b);
    }

    private static double Corner(int c, double w, double h) => c switch { 0 => 0, 1 => w, 2 => w + h, _ => 2 * w + h };

    /// <summary>Position of a border point along the perimeter, counter-clockwise from (MinX, MinY).</summary>
    private static double Position(Coordinate c, Envelope2D r)
    {
        double w = r.MaxX - r.MinX, h = r.MaxY - r.MinY;
        if (c.Y == r.MinY) return c.X - r.MinX;
        if (c.X == r.MaxX) return w + (c.Y - r.MinY);
        if (c.Y == r.MaxY) return w + h + (r.MaxX - c.X);
        return 2 * w + h + (r.MaxY - c.Y);
    }

    /// <summary>Whether the segment can touch the closed rectangle (envelope test, conservative).</summary>
    private static bool Touches(Coordinate a, Coordinate b, Envelope2D r) =>
        Math.Max(a.X, b.X) >= r.MinX && Math.Min(a.X, b.X) <= r.MaxX &&
        Math.Max(a.Y, b.Y) >= r.MinY && Math.Min(a.Y, b.Y) <= r.MaxY;

    private static Coordinate Clamp(Coordinate c, Envelope2D r) =>
        new(Math.Clamp(c.X, r.MinX, r.MaxX), Math.Clamp(c.Y, r.MinY, r.MaxY));

    private static void Add(List<Coordinate> result, Coordinate c)
    {
        if (result.Count == 0 || result[^1].X != c.X || result[^1].Y != c.Y) result.Add(c);
    }
}
