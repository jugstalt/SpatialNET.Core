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

        var result = new List<Coordinate>(ring.Length);
        if (!any)
        {
            // ganzer Ring außerhalb: nur seine Projektion auf den Rand (umschließt er R, bleibt er ein Rechteck)
            for (int i = 0; i < segments; i++) Retract(ring[i], ring[i + 1], r, result, 0);
            Add(result, Clamp(ring[0], r));
            return result.ToArray();
        }

        // Start an einem behaltenen Segment, damit jeder Außenlauf zusammenhängend bleibt
        int start = Array.IndexOf(keep, true), run = 0;
        for (int k = 0; k < segments; k++)
        {
            int i = (start + k) % segments;
            if (keep[i])
            {
                Add(result, ring[i]);
                continue;
            }

            // Außenlauf: Anfangspunkt original, dann seine Projektion entlang des Randes
            if (k == 0 || keep[(i - 1 + segments) % segments])
            {
                Add(result, ring[i]);
                run = result.Count;
            }

            Retract(ring[i], ring[i + 1], r, result, run);
        }

        Add(result, ring[start]);
        return result.ToArray();
    }

    /// <summary>Whether the segment can touch the closed rectangle (envelope test, conservative).</summary>
    private static bool Touches(Coordinate a, Coordinate b, Envelope2D r) =>
        Math.Max(a.X, b.X) >= r.MinX && Math.Min(a.X, b.X) <= r.MaxX &&
        Math.Max(a.Y, b.Y) >= r.MinY && Math.Min(a.Y, b.Y) <= r.MaxY;

    private static Coordinate Clamp(Coordinate c, Envelope2D r) =>
        new(Math.Clamp(c.X, r.MinX, r.MaxX), Math.Clamp(c.Y, r.MinY, r.MaxY));

    /// <summary>
    /// Projection r(a→b) of a segment completely outside: the segment lies entirely beyond a rectangle edge
    /// (envelope test), so r(a→b) lies on its line, and the segment r(a)→r(b) is exactly r(a→b) –
    /// kinks at the corners coincide with the end points or are collinear. Both points lie exactly on the boundary.
    /// <c>run</c> is the index of the first point of the outer run in the result (original points precede it).
    /// </summary>
    private static void Retract(Coordinate a, Coordinate b, Envelope2D r, List<Coordinate> result, int run)
    {
        AddOnBoundary(result, Clamp(a, r), run);
        AddOnBoundary(result, Clamp(b, r), run);
    }

    /// <summary>
    /// Like <see cref="Add"/>, but removes a clamped predecessor that lies on the same axis-parallel line
    /// between its neighbors (same path). Otherwise long outer runs would yield thousands of collinear
    /// boundary segments of both operands on the same lines, which make the noding expensive.
    /// </summary>
    private static void AddOnBoundary(List<Coordinate> result, Coordinate c, int run)
    {
        int n = result.Count;
        if (n >= 2 && n - 1 >= run)
        {
            var a = result[n - 2];
            var b = result[n - 1];
            bool between =
                (a.X == b.X && b.X == c.X && Math.Min(a.Y, c.Y) <= b.Y && b.Y <= Math.Max(a.Y, c.Y)) ||
                (a.Y == b.Y && b.Y == c.Y && Math.Min(a.X, c.X) <= b.X && b.X <= Math.Max(a.X, c.X));
            if (between)
            {
                result.RemoveAt(n - 1);
            }
        }

        Add(result, c);
    }

    private static void Add(List<Coordinate> result, Coordinate c)
    {
        if (result.Count == 0 || result[^1].X != c.X || result[^1].Y != c.Y) result.Add(c);
    }
}
