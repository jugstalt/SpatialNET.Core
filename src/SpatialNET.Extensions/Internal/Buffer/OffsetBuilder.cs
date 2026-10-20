namespace SpatialNET.Internal.Buffer;

/// <summary>
/// Erzeugt Roh-Offset-Kurven (ADR 0006) mit positiver Windung innen: Seite rechts der Laufrichtung bei
/// positiver Distanz, äußere Ecken mit Join, innere Ecken über den Offset-Schnittpunkt bzw. den Vertex
/// (Selbstüberlappungen bereinigt die Union mit FillRule.Positive). Bögen quantisiert wie JTS.
/// </summary>
internal sealed class OffsetBuilder(BufferOptions options, double distance)
{
    private readonly double _radius = Math.Abs(distance);
    private readonly int _sign = Math.Sign(distance);

    /// <summary>Winkelschritt der Bögen: Viertelkreis / QuadrantSegments bzw. aus der ArcTolerance.</summary>
    private double Quantum =>
        options.ArcTolerance is { } tol && tol > 0 && tol < _radius
            ? Math.Min(Math.PI / 2, 2 * Math.Acos(1 - tol / _radius))
            : Math.PI / 2 / options.QuadrantSegments;

    // ---------- Punkte, Linien, Ringe ----------

    /// <summary>Kreis (Round) bzw. Quadrat (Square) um einen Punkt, gegen den Uhrzeigersinn; Flat: nichts.</summary>
    public Coordinate[]? Point(Coordinate p)
    {
        switch (options.CapStyle)
        {
            case CapStyle.Flat:
                return null;
            case CapStyle.Square:
                return [new(p.X + _radius, p.Y - _radius), new(p.X + _radius, p.Y + _radius), new(p.X - _radius, p.Y + _radius),
                        new(p.X - _radius, p.Y - _radius), new(p.X + _radius, p.Y - _radius)];
            default:
                var ring = new List<Coordinate> { new(p.X + _radius, p.Y) };
                Fillet(ring, p, 0, 2 * Math.PI, 1, skipFirst: true);
                ring.Add(ring[0]);
                return ring.ToArray();
        }
    }

    /// <summary>Geschlossene Kurve um einen Pfad (beide Seiten, Kappen an den Enden).</summary>
    public Coordinate[]? Line(Coordinate[] path)
    {
        path = Distinct(path);
        if (path.Length == 1) return Point(path[0]);
        if (path.Length == 0) return null;

        double tol = _radius * options.SimplifyFactor;
        var right = Simplifier.Simplify(path, -tol);              // Einbuchtungen der rechten Seite
        var left = Simplifier.Simplify(path, tol).Reverse().ToArray(); // rechte Seite des umgekehrten Pfads

        var ring = new List<Coordinate>();
        Side(ring, right, _radius, closed: false);
        Cap(ring, right[^2], right[^1]);
        Side(ring, left, _radius, closed: false);
        Cap(ring, left[^2], left[^1]);
        ring.Add(ring[0]);
        return ring.ToArray();
    }

    /// <summary>Einseitige Kurve: Band zwischen Pfad und Offset (positiv links, negativ rechts), flache Enden.</summary>
    public Coordinate[]? SingleSided(Coordinate[] path)
    {
        path = Distinct(path);
        if (path.Length < 2) return null;

        double tol = _radius * options.SimplifyFactor;
        var ring = new List<Coordinate>();
        if (_sign > 0)
        {
            // links: Pfad vorwärts, dann rechte Seite des umgekehrten Pfads (= linke Seite) zurück
            var simplified = Simplifier.Simplify(path, tol);
            ring.AddRange(path);
            Side(ring, simplified.Reverse().ToArray(), _radius, closed: false);
        }
        else
        {
            var simplified = Simplifier.Simplify(path, -tol);
            ring.AddRange(path.Reverse());
            Side(ring, simplified, _radius, closed: false);
        }

        ring.Add(ring[0]);
        return ring.ToArray();
    }

    /// <summary>
    /// Offset eines geschlossenen Rings nach rechts (positive Distanz; Innen links bei normalisierter Orientierung),
    /// nach links bei negativer Distanz.
    /// </summary>
    public Coordinate[]? Ring(Coordinate[] closed)
    {
        var points = Distinct(closed);
        if (points.Length < 4) return null;

        double tol = _radius * options.SimplifyFactor;
        points = Simplifier.Simplify(points, _sign > 0 ? -tol : tol);
        if (points.Length < 4) return null;

        var ring = new List<Coordinate>();
        Side(ring, points, distance, closed: true);
        if (ring.Count < 3) return null;
        ring.Add(ring[0]);
        return ring.ToArray();
    }

    // ---------- Seiten, Ecken, Kappen ----------

    /// <summary>Offset-Seite (rechts bei positiver Distanz) eines offenen Pfads bzw. eines geschlossenen Rings.</summary>
    private void Side(List<Coordinate> output, Coordinate[] pts, double offset, bool closed)
    {
        int segments = pts.Length - 1;
        if (segments < 1) return;

        (Coordinate A, Coordinate B) Offset(int i)
        {
            var (a, b) = (pts[i], pts[i + 1]);
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double nx = dy / len * offset, ny = -dx / len * offset; // rechte Normale
            return (new Coordinate(a.X + nx, a.Y + ny), new Coordinate(b.X + nx, b.Y + ny));
        }

        if (!closed)
        {
            output.Add(Offset(0).A);
            for (int i = 1; i < segments; i++)
            {
                Join(output, pts[i - 1], pts[i], pts[i + 1], Offset(i - 1), Offset(i), Math.Sign(offset));
            }

            output.Add(Offset(segments - 1).B);
            return;
        }

        // geschlossen: Ecke an jedem Vertex (pts[0] == pts[^1])
        for (int i = 0; i < segments; i++)
        {
            int prev = (i - 1 + segments) % segments;
            Join(output, pts[prev], pts[i], pts[i + 1], Offset(prev), Offset(i), Math.Sign(offset));
        }
    }

    private void Join(List<Coordinate> output, Coordinate p0, Coordinate v, Coordinate p2,
        (Coordinate A, Coordinate B) o0, (Coordinate A, Coordinate B) o1, int side)
    {
        int turn = Noding.Orientation.Orient2D(p0, v, p2);
        bool reversal = turn == 0 && (v.X - p0.X) * (p2.X - v.X) + (v.Y - p0.Y) * (p2.Y - v.Y) < 0;
        bool outside = reversal || turn * side > 0;

        if (turn == 0 && !reversal)
        {
            output.Add(o0.B); // gerade weiter
            return;
        }

        if (!outside)
        {
            // innere Ecke: Schnittpunkt der Offset-Segmente, sonst über den Vertex (Schleife mit negativer Windung)
            if (Intersection(o0.A, o0.B, o1.A, o1.B) is { } x)
            {
                output.Add(x);
            }
            else
            {
                output.Add(o0.B);
                output.Add(v);
                output.Add(o1.A);
            }

            return;
        }

        if (Distance(o0.B, o1.A) < _radius * 1e-3)
        {
            output.Add(o0.B);
            return;
        }

        switch (options.JoinStyle)
        {
            case JoinStyle.Bevel:
                output.Add(o0.B);
                output.Add(o1.A);
                break;
            case JoinStyle.Mitre:
                Mitre(output, p0, v, o0, o1);
                break;
            default:
                output.Add(o0.B);
                Fillet(output, v, Math.Atan2(o0.B.Y - v.Y, o0.B.X - v.X), Math.Atan2(o1.A.Y - v.Y, o1.A.X - v.X), side, skipFirst: true);
                output.Add(o1.A);
                break;
        }
    }

    /// <summary>Mitre-Ecke; über dem Limit wird die Spitze im Abstand Limit·d senkrecht zur Winkelhalbierenden abgeschnitten.</summary>
    private void Mitre(List<Coordinate> output, Coordinate p0, Coordinate v, (Coordinate A, Coordinate B) o0, (Coordinate A, Coordinate B) o1)
    {
        var m = LineIntersection(o0.A, o0.B, o1.A, o1.B);
        double limit = options.MitreLimit * _radius;
        if (m is { } mitre && Distance(mitre, v) <= limit)
        {
            output.Add(mitre);
            return;
        }

        // Winkelhalbierende von v zur Spitze; bei einer 180°-Wende die Fortsetzung des Segments
        double bx = (o0.B.X - v.X) + (o1.A.X - v.X), by = (o0.B.Y - v.Y) + (o1.A.Y - v.Y);
        double blen = Math.Sqrt(bx * bx + by * by);
        if (blen < _radius * 1e-12)
        {
            (bx, by) = (v.X - p0.X, v.Y - p0.Y);
            blen = Math.Sqrt(bx * bx + by * by);
        }

        bx /= blen;
        by /= blen;
        output.Add(OnLineAtProjection(o0.A, o0.B, v, bx, by, limit) ?? o0.B);
        output.Add(OnLineAtProjection(o1.A, o1.B, v, bx, by, limit) ?? o1.A);
    }

    /// <summary>Punkt auf der Geraden a–b, dessen Projektion auf die Richtung (bx, by) ab v genau <paramref name="length"/> ist.</summary>
    private static Coordinate? OnLineAtProjection(Coordinate a, Coordinate b, Coordinate v, double bx, double by, double length)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double denominator = dx * bx + dy * by;
        if (Math.Abs(denominator) < 1e-300) return null;
        double t = (length - ((a.X - v.X) * bx + (a.Y - v.Y) * by)) / denominator;
        return new Coordinate(a.X + t * dx, a.Y + t * dy);
    }

    private void Cap(List<Coordinate> output, Coordinate p0, Coordinate end)
    {
        double dx = end.X - p0.X, dy = end.Y - p0.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / len, uy = dy / len;
        switch (options.CapStyle)
        {
            case CapStyle.Round:
                // von der rechten Normale (uy, −ux) gegen den Uhrzeigersinn über u zur linken Normale
                Fillet(output, end, Math.Atan2(-ux, uy), Math.Atan2(ux, -uy), 1, skipFirst: true);
                break;
            case CapStyle.Square:
                output.Add(new Coordinate(end.X + (uy + ux) * _radius, end.Y + (-ux + uy) * _radius));
                output.Add(new Coordinate(end.X + (-uy + ux) * _radius, end.Y + (ux + uy) * _radius));
                break;
        }
    }

    /// <summary>Bogenpunkte um <paramref name="center"/> von start nach end (Richtung ±1), Endpunkt ausgenommen, wie JTS quantisiert.</summary>
    private void Fillet(List<Coordinate> output, Coordinate center, double start, double end, int direction, bool skipFirst = false)
    {
        double total = direction > 0 ? end - start : start - end;
        while (total <= 0) total += 2 * Math.PI;
        while (total > 2 * Math.PI) total -= 2 * Math.PI;

        int n = (int)(total / Quantum + 0.5);
        if (n < 1) return;
        double inc = total / n;
        for (int i = skipFirst ? 1 : 0; i < n; i++)
        {
            double angle = start + direction * i * inc;
            output.Add(new Coordinate(center.X + _radius * Math.Cos(angle), center.Y + _radius * Math.Sin(angle)));
        }
    }

    // ---------- Hilfen ----------

    private static Coordinate[] Distinct(Coordinate[] points)
    {
        var result = new List<Coordinate>(points.Length);
        foreach (var p in points)
        {
            var q = new Coordinate(p.X, p.Y); // Buffer-Ergebnisse sind 2D
            if (result.Count == 0 || !Rings.SameXY(result[^1], q)) result.Add(q);
        }

        return result.ToArray();
    }

    private static double Distance(Coordinate a, Coordinate b) => Rings.Distance(a, b);

    private static Coordinate? Intersection(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        int o1 = Noding.Orientation.Orient2D(a, b, c), o2 = Noding.Orientation.Orient2D(a, b, d);
        int o3 = Noding.Orientation.Orient2D(c, d, a), o4 = Noding.Orientation.Orient2D(c, d, b);
        if (o1 * o2 >= 0 || o3 * o4 >= 0) return null;
        return LineIntersection(a, b, c, d);
    }

    private static Coordinate? LineIntersection(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
        double den = rx * sy - ry * sx;
        if (den == 0) return null;
        double t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
        return new Coordinate(a.X + t * rx, a.Y + t * ry);
    }
}

/// <summary>
/// Eingabe-Vereinfachung wie JTS BufferInputLineSimplifier (inklusive seiner Stichprobenprüfung), damit der
/// Buffer eng am NTS-Orakel bleibt: entfernt nur flache Einbuchtungen einer Seite.
/// </summary>
internal static class Simplifier
{
    private const int SamplePoints = 10;

    /// <param name="points">Die Punkte.</param>
    /// <param name="distanceTolerance">Positiv: Einbuchtungen bei Linksknicken, negativ: bei Rechtsknicken.</param>
    public static Coordinate[] Simplify(Coordinate[] points, double distanceTolerance)
    {
        double tol = Math.Abs(distanceTolerance);
        if (tol == 0 || points.Length < 3) return points;
        int orientation = distanceTolerance < 0 ? -1 : 1;
        var deleted = new bool[points.Length];

        bool changed;
        do
        {
            changed = false;
            int index = StartIndex;
            int mid = Next(index), last = Next(mid);
            while (last < points.Length)
            {
                bool midDeleted = false;
                if (IsDeletable(index, mid, last))
                {
                    deleted[mid] = true;
                    midDeleted = true;
                    changed = true;
                }

                index = midDeleted ? last : mid;
                mid = Next(index);
                last = Next(mid);
            }
        }
        while (changed);

        return points.Where((_, i) => !deleted[i]).ToArray();

        int Next(int i)
        {
            int next = i + 1;
            while (next < points.Length && deleted[next]) next++;
            return next;
        }

        bool IsDeletable(int i0, int i1, int i2)
        {
            var (p0, p1, p2) = (points[i0], points[i1], points[i2]);
            if (Noding.Orientation.Orient2D(p0, p1, p2) != orientation) return false;
            if (!IsShallow(p0, p1, p2)) return false;
            return IsShallowSampled(p0, p1, i0, i2);
        }

        // wie JTS: Abstand des mittleren Arguments zur Strecke der äußeren
        bool IsShallow(Coordinate a, Coordinate b, Coordinate c) => Segments.PointDistance(b, a, c) < tol;

        bool IsShallowSampled(Coordinate p0, Coordinate p2, int i0, int i2)
        {
            int inc = (i2 - i0) / SamplePoints;
            if (inc <= 0) inc = 1;
            for (int i = i0; i < i2; i += inc)
            {
                if (!IsShallow(p0, p2, points[i])) return false;
            }

            return true;
        }
    }

    private const int StartIndex = 1; // wie JTS: der erste Vertex ist nie Anfang eines Tripels
}
