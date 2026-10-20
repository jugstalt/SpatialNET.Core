namespace SpatialNET.Internal.Noding;

/// <summary>Eine Eingabe-Punktfolge (Pfad oder geschlossener Ring) mit Herkunft.</summary>
/// <param name="Points">Die Originalkoordinaten (bei Ringen geschlossen).</param>
/// <param name="Operand">Index des Operanden (0 = a, 1 = b, …).</param>
/// <param name="Index">Laufnummer der Folge innerhalb des Aufrufs.</param>
internal sealed record SegmentString(Coordinate[] Points, int Operand, int Index);

/// <summary>Eine genodete Kante auf dem Gitter; Richtung wie im Originalsegment.</summary>
/// <param name="A">Anfangsknoten.</param>
/// <param name="B">Endknoten.</param>
/// <param name="ZA">Interpolierte Höhe an <paramref name="A"/> (NaN = keine).</param>
/// <param name="ZB">Interpolierte Höhe an <paramref name="B"/> (NaN = keine).</param>
/// <param name="Source">Die Eingabe-Punktfolge.</param>
/// <param name="Segment">Index des Originalsegments in <paramref name="Source"/>.</param>
internal readonly record struct NodedEdge(GridPoint A, GridPoint B, double ZA, double ZB, SegmentString Source, int Segment);

/// <summary>
/// Snap-Rounding-Noder (Hobby 1999, Muster JTS SnapRoundingNoder; ADR 0005): Hot Pixels sind alle gerundeten
/// Vertices und Schnittpunkte; jedes Segment wird durch die Mittelpunkte aller Hot Pixels geführt, die es
/// berührt. Die Ausgabekanten schneiden sich nur in Gitterpunkten (Überlappungen sind möglich).
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
        var segments = new List<Segment>();
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

        AddIntersectionPixels(segments, hotPixels, grid);

        var pixels = hotPixels.ToArray();
        Array.Sort(pixels);

        var result = new List<NodedEdge>();
        var hits = new List<(double T, GridPoint Pixel)>();
        foreach (var seg in segments)
        {
            hits.Clear();
            CollectPixels(seg, pixels, hits);
            hits.Sort((a, b) => a.T.CompareTo(b.T));

            var source = seg.Source.Points;
            var p = source[seg.Index];
            var q = source[seg.Index + 1];
            GridPoint? previous = null;
            double previousZ = double.NaN;
            foreach (var (t, pixel) in hits)
            {
                if (previous == pixel)
                {
                    continue;
                }

                double tz = Math.Clamp(t, 0, 1);
                double z = p.HasZ && q.HasZ ? p.Z + tz * (q.Z - p.Z) : double.NaN;
                if (previous is { } prev)
                {
                    result.Add(new NodedEdge(prev, pixel, previousZ, z, seg.Source, seg.Index));
                }

                previous = pixel;
                previousZ = z;
            }
        }

        return result;
    }

    /// <summary>Echte Kreuzungen (exakt über orient2d erkannt) werden als Hot Pixels ergänzt.</summary>
    private static void AddIntersectionPixels(List<Segment> segments, HashSet<GridPoint> hotPixels, GridModel grid)
    {
        var order = segments.OrderBy(s => s.MinX).ToArray();
        var active = new List<Segment>();
        foreach (var s in order)
        {
            active.RemoveAll(a => a.MaxX < s.MinX);
            foreach (var a in active)
            {
                if (a.MaxY < s.MinY || s.MaxY < a.MinY)
                {
                    continue;
                }

                var p0 = a.Source.Points[a.Index];
                var p1 = a.Source.Points[a.Index + 1];
                var q0 = s.Source.Points[s.Index];
                var q1 = s.Source.Points[s.Index + 1];
                int o1 = Orientation.Orient2D(p0, p1, q0), o2 = Orientation.Orient2D(p0, p1, q1);
                int o3 = Orientation.Orient2D(q0, q1, p0), o4 = Orientation.Orient2D(q0, q1, p1);
                if (o1 * o2 < 0 && o3 * o4 < 0)
                {
                    // eigentliche Kreuzung; Endpunkt-Berührungen und Überlappungen liefern Vertices (schon Hot Pixels).
                    // Der Schnittpunkt wird exakt (rational) bestimmt und dann gerundet.
                    hotPixels.Add(grid.RoundExactIntersection(p0, p1, q0, q1));
                }
            }

            active.Add(s);
        }
    }

    /// <summary>Alle Hot Pixels (halboffene Einheitsquadrate um Gitterpunkte), die das Segment berührt, mit Parameter t.</summary>
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
    /// Ob das Segment das halboffene Pixel [x−½, x+½) × [y−½, y+½) berührt (Liang–Barsky auf dem geschlossenen
    /// Quadrat; ein Stück, das nur auf der rechten oder oberen Kante liegt, zählt nicht).
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
