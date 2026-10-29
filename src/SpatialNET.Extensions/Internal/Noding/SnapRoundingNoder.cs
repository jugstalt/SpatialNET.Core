namespace SpatialNET.Internal.Noding;

/// <summary>An input point sequence (path or closed ring) with its origin.</summary>
/// <param name="Points">The original coordinates (closed for rings).</param>
/// <param name="Operand">Index of the operand (0 = a, 1 = b, …).</param>
/// <param name="Index">Sequence number of the sequence within the call.</param>
internal sealed record SegmentString(Coordinate[] Points, int Operand, int Index);

/// <summary>A noded edge on the grid; direction as in the original segment.</summary>
/// <param name="A">Start node.</param>
/// <param name="B">End node.</param>
/// <param name="ZA">Interpolated height at <paramref name="A"/> (NaN = none).</param>
/// <param name="ZB">Interpolated height at <paramref name="B"/> (NaN = none).</param>
/// <param name="MA">Interpolated measure at <paramref name="A"/> (NaN = none).</param>
/// <param name="MB">Interpolated measure at <paramref name="B"/> (NaN = none).</param>
/// <param name="Source">The input point sequence.</param>
/// <param name="Segment">Index of the original segment in <paramref name="Source"/>.</param>
internal readonly record struct NodedEdge(GridPoint A, GridPoint B, double ZA, double ZB, double MA, double MB, SegmentString Source, int Segment);

/// <summary>
/// Snap-rounding noder (Hobby 1999, after JTS SnapRoundingNoder; ADR 0005): hot pixels are all rounded
/// vertices and intersection points; each segment is routed through the centers of all hot pixels it
/// touches. The output edges intersect only at grid points (overlaps are possible).
/// </summary>
internal static class SnapRoundingNoder
{
    private readonly record struct Segment(SegmentString Source, int Index, double X0, double Y0, double X1, double Y1)
    {
        public double MinX => Math.Min(X0, X1);
        public double MaxX => Math.Max(X0, X1);
        public double MinY => Math.Min(Y0, Y1);
        public double MaxY => Math.Max(Y0, Y1);
    }

    public static List<NodedEdge> Node(IReadOnlyList<SegmentString> input, GridModel grid)
    {
        var segments = new List<Segment>(input.Sum(s => Math.Max(0, s.Points.Length - 1)));
        var hotPixels = new HashSet<GridPoint>();
        foreach (var s in input)
        {
            for (int i = 0; i < s.Points.Length; i++)
            {
                hotPixels.Add(grid.Round(s.Points[i]));
                if (i > 0)
                {
                    segments.Add(new Segment(s, i - 1,
                        grid.ToUnits(s.Points[i - 1].X), grid.ToUnits(s.Points[i - 1].Y),
                        grid.ToUnits(s.Points[i].X), grid.ToUnits(s.Points[i].Y)));
                }
            }
        }

        var crossings = AddIntersectionPixels(segments, hotPixels, grid);

        var pixels = hotPixels.ToArray();
        Array.Sort(pixels);

        var result = new List<NodedEdge>(segments.Count + 2 * crossings.Values.Sum(c => c.Count));
        var hits = new List<(double T, GridPoint Pixel)>();
        foreach (var seg in segments)
        {
            hits.Clear();
            CollectPixels(seg, pixels, hits);
            AddCrossingPixels(seg, crossings, hits);
            hits.Sort((a, b) => a.T.CompareTo(b.T));

            var source = seg.Source.Points;
            var p = source[seg.Index];
            var q = source[seg.Index + 1];
            GridPoint? previous = null;
            double previousZ = double.NaN, previousM = double.NaN;
            foreach (var (t, pixel) in hits)
            {
                if (previous == pixel)
                {
                    continue;
                }

                double tz = Math.Clamp(t, 0, 1);
                // The segment's own end points keep their Z and M even if the other end has none (ADR 0005, ADR 0015).
                double z = double.IsNegativeInfinity(t) ? p.Z
                    : double.IsPositiveInfinity(t) ? q.Z
                    : p.HasZ && q.HasZ ? p.Z + tz * (q.Z - p.Z) : double.NaN;
                double m = double.IsNegativeInfinity(t) ? p.M
                    : double.IsPositiveInfinity(t) ? q.M
                    : p.HasM && q.HasM ? p.M + tz * (q.M - p.M) : double.NaN;
                if (previous is { } prev)
                {
                    result.Add(new NodedEdge(prev, pixel, previousZ, z, previousM, m, seg.Source, seg.Index));
                }

                previous = pixel;
                previousZ = z;
                previousM = m;
            }
        }

        return result;
    }

    /// <summary>
    /// Proper crossings (detected exactly via orient2d) are added as hot pixels. The pixel of a crossing is also recorded for both
    /// segments: the exact intersection lies in it by definition, while the floating-point test of <c>Touches</c> can miss it
    /// when the crossing lies within rounding error of the pixel border; without the record, a segment could bypass the hot pixel
    /// that the other one is routed through, and the output edges would cross.
    /// </summary>
    private static Dictionary<(int String, int Segment), List<GridPoint>> AddIntersectionPixels(List<Segment> segments, HashSet<GridPoint> hotPixels, GridModel grid)
    {
        var crossings = new Dictionary<(int String, int Segment), List<GridPoint>>();

        // Sweep in x (stable by MinX); the active segments keep their order, those left of the sweep line drop out
        // before the next segment is compared with them. The bounds are kept in arrays for the inner loop.
        int n = segments.Count;
        var minX = new double[n];
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            minX[i] = segments[i].MinX;
            order[i] = i;
        }

        Array.Sort(order, (i, j) => minX[i] != minX[j] ? minX[i].CompareTo(minX[j]) : i.CompareTo(j));
        var maxX = new double[n];
        var minY = new double[n];
        var maxY = new double[n];
        foreach (int i in order)
        {
            maxX[i] = segments[i].MaxX;
            minY[i] = segments[i].MinY;
            maxY[i] = segments[i].MaxY;
        }

        var active = new int[n];
        int count = 0;
        foreach (int si in order)
        {
            var s = segments[si];
            double sMinX = minX[si], sMinY = minY[si], sMaxY = maxY[si];
            int kept = 0;
            for (int k = 0; k < count; k++)
            {
                int ai = active[k];
                if (maxX[ai] < sMinX)
                {
                    continue;
                }

                active[kept++] = ai;
                if (maxY[ai] < sMinY || sMaxY < minY[ai])
                {
                    continue;
                }

                var a = segments[ai];

                var p0 = a.Source.Points[a.Index];
                var p1 = a.Source.Points[a.Index + 1];
                var q0 = s.Source.Points[s.Index];
                var q1 = s.Source.Points[s.Index + 1];
                if (Orientation.Orient2D(p0, p1, q0) * Orientation.Orient2D(p0, p1, q1) >= 0 ||
                    Orientation.Orient2D(q0, q1, p0) * Orientation.Orient2D(q0, q1, p1) >= 0)
                {
                    continue;
                }

                // eigentliche Kreuzung; Endpunkt-Berührungen und Überlappungen liefern Vertices (schon Hot Pixels).
                // Der Schnittpunkt wird exakt (rational) bestimmt und dann gerundet. Die Orientierungen werden
                // nur so weit ausgewertet, bis eine Seite keine echte Trennung zeigt (früher Abbruch).
                var pixel = grid.RoundExactIntersection(p0, p1, q0, q1);
                hotPixels.Add(pixel);
                Record(crossings, a, pixel);
                Record(crossings, s, pixel);
            }

            count = kept;
            active[count++] = si;
        }

        return crossings;

        static void Record(Dictionary<(int String, int Segment), List<GridPoint>> crossings, Segment segment, GridPoint pixel)
        {
            var key = (segment.Source.Index, segment.Index);
            if (!crossings.TryGetValue(key, out var list))
            {
                crossings[key] = list = [];
            }

            list.Add(pixel);
        }
    }

    /// <summary>Adds the pixels of the crossings of the segment as pass-through points (see <see cref="AddIntersectionPixels"/>).</summary>
    private static void AddCrossingPixels(Segment seg, Dictionary<(int String, int Segment), List<GridPoint>> crossings, List<(double, GridPoint)> hits)
    {
        if (!crossings.TryGetValue((seg.Source.Index, seg.Index), out var pixels))
        {
            return;
        }

        var startPixel = new GridPoint(GridModel.RoundUnits(seg.X0), GridModel.RoundUnits(seg.Y0));
        var endPixel = new GridPoint(GridModel.RoundUnits(seg.X1), GridModel.RoundUnits(seg.Y1));
        double dx = seg.X1 - seg.X0, dy = seg.Y1 - seg.Y0, len2 = dx * dx + dy * dy;
        foreach (var pixel in pixels)
        {
            if (pixel == startPixel || pixel == endPixel)
            {
                continue;
            }

            double t = len2 == 0 ? 0 : ((pixel.X - seg.X0) * dx + (pixel.Y - seg.Y0) * dy) / len2;
            hits.Add((t, pixel));
        }
    }

    /// <summary>All hot pixels (half-open unit squares around grid points) that the segment touches, with parameter t.</summary>
    private static void CollectPixels(Segment seg, GridPoint[] pixels, List<(double, GridPoint)> hits)
    {
        long minX = (long)Math.Floor(seg.MinX - 0.5) - 1, maxX = (long)Math.Ceiling(seg.MaxX + 0.5) + 1;
        long minY = (long)Math.Floor(seg.MinY - 0.5) - 1, maxY = (long)Math.Ceiling(seg.MaxY + 0.5) + 1;

        // Die Pixel der eigenen Endpunkte sind immer erster und letzter Knoten (gleiche Rundung wie beim
        // Anlegen der Hot Pixels); innere Pixel werden nach ihrer nicht abgeschnittenen Projektion geordnet,
        // auch wenn sie über das Segmentende hinaus projizieren.
        var startPixel = new GridPoint(GridModel.RoundUnits(seg.X0), GridModel.RoundUnits(seg.Y0));
        var endPixel = new GridPoint(GridModel.RoundUnits(seg.X1), GridModel.RoundUnits(seg.Y1));
        hits.Add((double.NegativeInfinity, startPixel));
        hits.Add((double.PositiveInfinity, endPixel));

        int start = LowerBound(pixels, new GridPoint(minX, long.MinValue));
        double dx = seg.X1 - seg.X0, dy = seg.Y1 - seg.Y0, len2 = dx * dx + dy * dy;
        for (int i = start; i < pixels.Length && pixels[i].X <= maxX; i++)
        {
            var pixel = pixels[i];
            if (pixel.Y < minY || pixel.Y > maxY || pixel == startPixel || pixel == endPixel || !Touches(seg, pixel))
            {
                continue;
            }

            double t = len2 == 0 ? 0 : ((pixel.X - seg.X0) * dx + (pixel.Y - seg.Y0) * dy) / len2;
            hits.Add((t, pixel));
        }
    }

    /// <summary>
    /// Whether the segment touches the half-open pixel [x−½, x+½) × [y−½, y+½) (Liang–Barsky on the closed
    /// square; a piece lying only on the right or top edge does not count).
    /// </summary>
    private static bool Touches(Segment seg, GridPoint pixel)
    {
        double left = pixel.X - 0.5, right = pixel.X + 0.5, bottom = pixel.Y - 0.5, top = pixel.Y + 0.5;
        double dx = seg.X1 - seg.X0, dy = seg.Y1 - seg.Y0;
        double t0 = 0, t1 = 1;
        if (!Clip(-dx, seg.X0 - left, ref t0, ref t1) || !Clip(dx, right - seg.X0, ref t0, ref t1) ||
            !Clip(-dy, seg.Y0 - bottom, ref t0, ref t1) || !Clip(dy, top - seg.Y0, ref t0, ref t1))
        {
            return false;
        }

        // Endpunkte exakt übernehmen; X0 + 1·dx ist durch Rundung nicht zwingend X1.
        double ax = t0 == 0 ? seg.X0 : seg.X0 + t0 * dx, ay = t0 == 0 ? seg.Y0 : seg.Y0 + t0 * dy;
        double bx = t1 == 1 ? seg.X1 : seg.X0 + t1 * dx, by = t1 == 1 ? seg.Y1 : seg.Y0 + t1 * dy;
        bool onRight = ax >= right && bx >= right;
        bool onTop = ay >= top && by >= top;
        return !onRight && !onTop;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        double r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }

    private static int LowerBound(GridPoint[] sorted, GridPoint value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid].CompareTo(value) < 0) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}
