using System.Buffers;

namespace SpatialNET;

/// <summary>
/// Options for <see cref="Shapes.FastAlternativePolygonLabelsLazy"/>. One instance can be kept and reused for every call.
/// </summary>
public sealed record AlternativeLabelOptions
{
    /// <summary>The least distance of every point to the seed and to every point yielded before; 0 = only distinct points.</summary>
    public double Separation { get; init; }

    /// <summary>The least distance of every point to the boundary of the polygon (holes included); 0 = any interior point.</summary>
    public double MinClearance { get; init; }

    /// <summary>The most points yielded. Default 16.</summary>
    public int MaxCount { get; init; } = 16;

    /// <summary>The work budget: the most horizontal scanlines computed, each costs one pass over all vertices. Default 32.</summary>
    public int MaxScanlines { get; init; } = 32;

    /// <summary>Stops the sequence (it ends without an exception) when cancelled.</summary>
    public CancellationToken CancellationToken { get; init; }

    internal static readonly AlternativeLabelOptions Default = new();
}

public static partial class Shapes
{
    // ---------- fast label points ----------

    /// <summary>
    /// One point in the interior of the polygon for a map label, computed as cheaply as possible, where speed matters more than
    /// the nicest position. In order: the area centroid if it lies inside (even-odd over all rings, the rings need not be typed
    /// as shell or hole), the middle of the widest stretch of the horizontal line through the centroid or through the middle of
    /// the envelope, then <see cref="Measures.PointOnSurface"/> and last <see cref="Polylabel"/>. The first steps are one or two
    /// passes over the vertices and allocate nothing but pooled buffers. Without Z and M.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <returns>The point, or <c>null</c> if the polygon is empty or has no area.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    public static Coordinate? FastPolygonLabel(this IPolygon2D polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        // pass 1: the centroid; pass 2: the crossings of the horizontal line through it, which tell both whether it is inside
        // (between crossing 2k and 2k+1, the same rule as the inside test) and the widest stretch of that line as the fallback
        if (LabelScan.Centroid(polygon) is not { } centroid)
        {
            return null;
        }

        double[]? buffer = null;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                double y = centroid.Y;
                if (attempt == 1)
                {
                    // the line through the centroid met nothing: the middle of the envelope
                    if (!LabelScan.Bounds(polygon, out _, out double minY, out _, out double maxY) || !(maxY > minY)) break;
                    y = (minY + maxY) / 2;
                    if (y == centroid.Y) break;
                }

                int n = LabelScan.Crossings(polygon, y, ref buffer);
                double best = 0, bestX = double.NaN;
                for (int i = 0; i + 1 < n; i += 2)
                {
                    double x0 = buffer![i], x1 = buffer[i + 1];
                    if (attempt == 0 && x0 < centroid.X && centroid.X < x1)
                    {
                        return centroid;
                    }

                    if (x1 - x0 > best)
                    {
                        best = x1 - x0;
                        bestX = (x0 + x1) / 2;
                    }
                }

                // the middle of a stretch between crossing 2k and 2k+1 is inside by the same rule as the inside test
                if (best > 0)
                {
                    return new Coordinate(bestX, y);
                }
            }
        }
        finally
        {
            if (buffer is not null) ArrayPool<double>.Shared.Return(buffer);
        }

        return polygon.PointOnSurface() is { } p && LabelScan.Inside(polygon, p.X, p.Y) ? p : polygon.Polylabel();
    }

    /// <summary>
    /// Alternative label points in the interior of the polygon, after the first one (<paramref name="seed"/>) failed a collision
    /// test, computed lazily: nothing is computed before the first point is asked for, and the caller stops at the first point that
    /// fits. The points lie on horizontal scanlines, first the one through the seed, then lines that halve the envelope again and
    /// again (½, ¼, ¾, ⅛, …); on each line the points closest to the seed come first. Every point lies strictly inside by the
    /// even-odd rule over all rings (parts and holes need not be typed), keeps <see cref="AlternativeLabelOptions.Separation"/>
    /// from the seed and the points before and <see cref="AlternativeLabelOptions.MinClearance"/> from the boundary. The order is
    /// deterministic. An empty or degenerate polygon gives an empty sequence. Without Z and M.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="seed">The point that failed, usually the result of <see cref="FastPolygonLabel"/>.</param>
    /// <param name="options">The options; <c>null</c> = defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A distance in <paramref name="options"/> is negative or not finite, or a count is negative.</exception>
    public static IEnumerable<Coordinate> FastAlternativePolygonLabelsLazy(this IPolygon2D polygon, Coordinate seed, AlternativeLabelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var o = options ?? AlternativeLabelOptions.Default;
        if (!(double.IsFinite(o.Separation) && o.Separation >= 0))
            throw new ArgumentOutOfRangeException(nameof(options), o.Separation, "Separation must be finite and not negative.");
        if (!(double.IsFinite(o.MinClearance) && o.MinClearance >= 0))
            throw new ArgumentOutOfRangeException(nameof(options), o.MinClearance, "MinClearance must be finite and not negative.");
        if (o.MaxCount < 0 || o.MaxScanlines < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxCount and MaxScanlines must not be negative.");
        return AlternativeLabels(polygon, seed, o);
    }

    private static IEnumerable<Coordinate> AlternativeLabels(IPolygon2D polygon, Coordinate seed, AlternativeLabelOptions o)
    {
        if (o.MaxCount == 0 || polygon.RingCount == 0)
        {
            yield break;
        }

        // the bounds are needed from the second scanline on only: the first one runs through the seed
        double minY = double.NaN, maxY = double.NaN;

        double sep2 = o.Separation * o.Separation;
        double[]? crossings = null;
        (double X, double Key)[]? candidates = null;
        Coordinate[] yielded = ArrayPool<Coordinate>.Shared.Rent(Math.Min(o.MaxCount, 4096));
        int count = 0;
        try
        {
            bool seedLine = double.IsFinite(seed.Y);
            int computed = 0;
            for (int line = seedLine ? 0 : 1; computed < o.MaxScanlines; line++)
            {
                if (o.CancellationToken.IsCancellationRequested) yield break;
                // line 0: through the seed; then the van der Corput fractions ½, ¼, ¾, ⅛, … of the envelope height
                if (line > 0 && double.IsNaN(minY))
                {
                    if (!LabelScan.Bounds(polygon, out _, out minY, out _, out maxY) || maxY <= minY) yield break;
                }

                double y = line == 0 ? seed.Y : minY + (maxY - minY) * VanDerCorput(line);
                if (line > 0 && y == seed.Y && seedLine) continue;

                computed++;
                LabelScan.Lines++;
                int n = LabelScan.Crossings(polygon, y, ref crossings);
                int k = 0;
                for (int i = 0; i + 1 < n; i += 2)
                {
                    double x0 = crossings![i] + o.MinClearance, x1 = crossings[i + 1] - o.MinClearance;
                    double length = x1 - x0;
                    // a stretch narrower than a relative epsilon could put the middle on the boundary
                    if (length < 0 || crossings[i + 1] - crossings[i] <= 1e-12 * Math.Max(1, Math.Abs(crossings[i]))) continue;
                    double mid = (x0 + x1) / 2;
                    double step = o.Separation > 0 ? o.Separation : length / 4;
                    for (int j = 0; j < 9; j++)
                    {
                        // mid, mid+step, mid−step, mid+2·step, …
                        double x = j == 0 ? mid : mid + ((j + 1) / 2) * step * (j % 2 == 1 ? 1 : -1);
                        if (x < x0 || x > x1 || (j > 0 && step == 0)) break;
                        if (candidates is null || k == candidates.Length) Grow(ref candidates, k);
                        double dx = x - seed.X, dy = y - seed.Y;
                        candidates![k++] = (x, double.IsFinite(seed.X) ? dx * dx + dy * dy : 0);
                    }
                }

                if (k == 0) continue;
                // closest to the seed first; ties by x for determinism
                Array.Sort(candidates!, 0, k, LabelScan.CandidateOrder);
                for (int i = 0; i < k; i++)
                {
                    var p = new Coordinate(candidates![i].X, y);
                    if (!Separated(p, seed, sep2, yielded, count)) continue;
                    if (o.MinClearance > 0 && !LabelScan.Clear(polygon, p.X, p.Y, o.MinClearance)) continue;
                    if (count < yielded.Length) yielded[count] = p;
                    count++;
                    yield return p;
                    if (count >= o.MaxCount || o.CancellationToken.IsCancellationRequested) yield break;
                }
            }
        }
        finally
        {
            if (crossings is not null) ArrayPool<double>.Shared.Return(crossings);
            if (candidates is not null) ArrayPool<(double, double)>.Shared.Return(candidates);
            ArrayPool<Coordinate>.Shared.Return(yielded);
        }

        static bool Separated(Coordinate p, Coordinate seed, double sep2, Coordinate[] yielded, int count)
        {
            if (Distance2(p, seed) < sep2 || (p.X == seed.X && p.Y == seed.Y)) return false;
            for (int i = 0; i < Math.Min(count, yielded.Length); i++)
            {
                var q = yielded[i];
                if (Distance2(p, q) < sep2 || (p.X == q.X && p.Y == q.Y)) return false;
            }

            return true;
        }

        static double Distance2(Coordinate a, Coordinate b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

        static void Grow(ref (double X, double Key)[]? array, int used)
        {
            var bigger = ArrayPool<(double, double)>.Shared.Rent(Math.Max(16, used * 2));
            if (array is not null)
            {
                Array.Copy(array, bigger, used);
                ArrayPool<(double, double)>.Shared.Return(array);
            }

            array = bigger;
        }
    }

    private static double VanDerCorput(int n)
    {
        double result = 0, f = 0.5;
        while (n > 0)
        {
            if ((n & 1) == 1) result += f;
            n >>= 1;
            f /= 2;
        }

        return result;
    }
}

/// <summary>Allocation-free passes over the rings of a polygon for the fast label points (even-odd over all rings).</summary>
internal static class LabelScan
{
    /// <summary>Scanlines computed by the alternative label points on this thread (for tests of the laziness).</summary>
    [ThreadStatic] internal static long Lines;

    internal static readonly IComparer<(double X, double Key)> CandidateOrder =
        Comparer<(double X, double Key)>.Create(static (a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.X.CompareTo(b.X));

    public static bool Bounds(IPolygon2D polygon, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = double.PositiveInfinity;
        maxX = maxY = double.NegativeInfinity;
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var ring = polygon[r];
            for (int i = 0; i < ring.PointCount; i++)
            {
                var p = ring[i];
                double x = p.X, y = p.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return minX <= maxX;
    }

    /// <summary>
    /// The area centroid; rings oriented like the largest ring add, the others subtract (the usual shell/hole convention, without
    /// typing). Computed relative to the first vertex so that large coordinates do not cancel. <c>null</c> without area.
    /// </summary>
    public static Coordinate? Centroid(IPolygon2D polygon)
    {
        int rings = polygon.RingCount;
        if (rings == 0 || polygon[0].PointCount == 0) return null;
        var origin = polygon[0][0];
        double ox = origin.X, oy = origin.Y;
        // one pass: per ring its signed area and moments (and the bounds), then the roles by the largest ring
        Span<double> moments = rings <= 32 ? stackalloc double[3 * rings] : new double[3 * rings];
        double largest = 0;
        for (int r = 0; r < rings; r++)
        {
            double a = RingMoments(polygon[r], ox, oy, out moments[3 * r + 1], out moments[3 * r + 2]);
            moments[3 * r] = a;
            if (Math.Abs(a) > Math.Abs(largest)) largest = a;
        }

        if (largest == 0) return null;
        double area = 0, cx = 0, cy = 0;
        for (int r = 0; r < rings; r++)
        {
            double a = moments[3 * r];
            if (a == 0) continue;
            // rings oriented like the largest add, the others subtract; the moments carry the sign of a
            double role = Math.Sign(a) == Math.Sign(largest) ? 1 : -1;
            area += role * Math.Abs(a);
            double s = role * Math.Sign(a);
            cx += s * moments[3 * r + 1];
            cy += s * moments[3 * r + 2];
        }

        if (area <= 0) return null;
        double x = ox + cx / area, y = oy + cy / area;
        return double.IsFinite(x) && double.IsFinite(y) ? new Coordinate(x, y) : null;
    }

    /// <summary>Signed area of a ring (open or closed) and its first moments, relative to (ox, oy).</summary>
    private static double RingMoments(IRing2D ring, double ox, double oy, out double mx, out double my)
    {
        int n = ring.PointCount;
        double a2 = 0;
        mx = my = 0;
        if (n == 0) return 0;
        var first = ring[0];
        double x0 = first.X - ox, y0 = first.Y - oy, px = x0, py = y0;
        for (int i = 1; i <= n; i++)
        {
            double qx, qy;
            if (i < n)
            {
                var q = ring[i];
                qx = q.X - ox;
                qy = q.Y - oy;
            }
            else
            {
                qx = x0;
                qy = y0;
            }

            double cross = px * qy - qx * py;
            a2 += cross;
            mx += (px + qx) * cross;
            my += (py + qy) * cross;
            px = qx;
            py = qy;
        }

        mx /= 6;
        my /= 6;
        return n < 3 ? 0 : a2 / 2;
    }

    /// <summary>Whether the point lies strictly inside by the even-odd rule over all rings (a point on a ring is not inside).</summary>
    public static bool Inside(IPolygon2D polygon, double x, double y)
    {
        bool inside = false;
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var ring = polygon[r];
            int n = ring.PointCount;
            if (n < 3) continue;
            var last = ring[n - 1];
            double ax = last.X, ay = last.Y;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                double bx = p.X, by = p.Y;
                if ((ay > y) != (by > y))
                {
                    double cx = ax + (y - ay) * (bx - ax) / (by - ay);
                    if (cx == x) return false;
                    if (cx > x) inside = !inside;
                }
                else if (ay == y && by == y && Math.Min(ax, bx) <= x && x <= Math.Max(ax, bx))
                {
                    return false; // on a horizontal edge
                }

                ax = bx;
                ay = by;
            }
        }

        return inside;
    }

    /// <summary>
    /// The sorted x values where the rings cross the horizontal line at <paramref name="y"/> (half-open rule as in
    /// <see cref="Inside"/>, so the middle of a stretch between crossing 2k and 2k+1 is inside). The buffer is rented and grown.
    /// </summary>
    public static int Crossings(IPolygon2D polygon, double y, ref double[]? buffer)
    {
        int count = 0;
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var ring = polygon[r];
            int n = ring.PointCount;
            if (n < 3) continue;
            var last = ring[n - 1];
            double ax = last.X, ay = last.Y;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                double bx = p.X, by = p.Y;
                if ((ay > y) != (by > y))
                {
                    if (buffer is null || count == buffer.Length)
                    {
                        var bigger = ArrayPool<double>.Shared.Rent(Math.Max(64, count * 2));
                        if (buffer is not null)
                        {
                            Array.Copy(buffer, bigger, count);
                            ArrayPool<double>.Shared.Return(buffer);
                        }

                        buffer = bigger;
                    }

                    buffer[count++] = ax + (y - ay) * (bx - ax) / (by - ay);
                }

                ax = bx;
                ay = by;
            }
        }

        if (count > 1) Array.Sort(buffer!, 0, count);
        return count;
    }

    /// <summary>Whether every ring keeps at least <paramref name="clearance"/> from the point.</summary>
    public static bool Clear(IPolygon2D polygon, double x, double y, double clearance)
    {
        double c2 = clearance * clearance;
        for (int r = 0; r < polygon.RingCount; r++)
        {
            var ring = polygon[r];
            int n = ring.PointCount;
            if (n == 0) continue;
            var last = ring[n - 1];
            double ax = last.X, ay = last.Y;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                double bx = p.X, by = p.Y;
                // quick reject: the segment's envelope is farther than the clearance
                if (!(Math.Min(ax, bx) - x >= clearance || x - Math.Max(ax, bx) >= clearance
                      || Math.Min(ay, by) - y >= clearance || y - Math.Max(ay, by) >= clearance))
                {
                    double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                    double t = len2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
                    double ex = ax + t * dx - x, ey = ay + t * dy - y;
                    if (ex * ex + ey * ey < c2) return false;
                }

                ax = bx;
                ay = by;
            }
        }

        return true;
    }
}
