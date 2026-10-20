namespace SpatialNET.Internal;

/// <summary>
/// Topologieerhaltende Vereinfachung nach dem Muster von JTS TaggedLinesSimplifier: Douglas-Peucker je Linie,
/// aber ein Abschnitt wird nur durch eine Strecke ersetzt, wenn diese weder die bereits erzeugten noch die
/// verbliebenen Eingabe-Segmente (aller Linien) im Inneren schneidet. Ringe behalten mindestens 4 Punkte.
/// </summary>
internal sealed class TopologyPreservingSimplifier(double tolerance)
{
    private sealed class Line(Coordinate[] points, bool ring)
    {
        public readonly Coordinate[] Points = points;
        public readonly bool IsRing = ring;
        public readonly List<(Coordinate A, Coordinate B)> Result = [];
        public int MinimumSize => IsRing ? 4 : 2;
        public int ResultSize => Result.Count == 0 ? 0 : Result.Count + 1;
    }

    // Eingabe-Segmente, die noch nicht vereinfacht wurden: (Linie, Index)
    private readonly HashSet<(Line, int)> _input = [];
    private readonly List<(Coordinate A, Coordinate B)> _output = [];
    private List<Line> _lines = [];

    /// <summary>Vereinfacht alle Linien gemeinsam; Rückgabe in Eingabereihenfolge.</summary>
    public List<Coordinate[]> Simplify(IReadOnlyList<(Coordinate[] Points, bool Ring)> lines)
    {
        var tagged = lines.Select(l => new Line(l.Points, l.Ring)).ToList();
        _lines = tagged;
        foreach (var line in tagged)
            for (int i = 0; i < line.Points.Length - 1; i++)
                _input.Add((line, i));

        var result = new List<Coordinate[]>();
        foreach (var line in tagged)
        {
            if (line.Points.Length < 2)
            {
                result.Add(line.Points);
                continue;
            }

            SimplifySection(line, 0, line.Points.Length - 1, 0);
            if (line.IsRing) SimplifyRingEndpoint(line);
            var pts = new List<Coordinate> { line.Result[0].A };
            pts.AddRange(line.Result.Select(s => s.B));
            result.Add(pts.ToArray());
        }

        return result;
    }

    private void SimplifySection(Line line, int i, int j, int depth)
    {
        depth++;
        if (i + 1 == j)
        {
            AddToResult(line, i, j);
            return;
        }

        bool valid = true;
        if (line.ResultSize < line.MinimumSize && depth + 1 < line.MinimumSize)
        {
            valid = false; // sonst könnte die Linie unter ihre Mindestgröße fallen
        }

        int furthest = i;
        double maxDistance = -1;
        for (int k = i + 1; k < j; k++)
        {
            double d = Segments.PointDistance(line.Points[k], line.Points[i], line.Points[j]);
            if (d > maxDistance)
            {
                maxDistance = d;
                furthest = k;
            }
        }

        if (maxDistance > tolerance) valid = false;
        if (valid && HasBadIntersection(line, i, j, line.Points[i], line.Points[j])) valid = false;
        if (valid && HasJump(line, i, j, line.Points[i], line.Points[j])) valid = false;

        if (valid)
        {
            for (int k = i; k < j; k++) _input.Remove((line, k));
            AddSegment(line, line.Points[i], line.Points[j]);
            return;
        }

        SimplifySection(line, i, furthest, depth);
        SimplifySection(line, furthest, j, depth);
    }

    private void AddToResult(Line line, int i, int j)
    {
        _input.Remove((line, i));
        AddSegment(line, line.Points[i], line.Points[j]);
    }

    private void AddSegment(Line line, Coordinate a, Coordinate b)
    {
        line.Result.Add((a, b));
        _output.Add((a, b));
    }

    private bool HasBadIntersection(Line line, int i, int j, Coordinate a, Coordinate b)
    {
        foreach (var (p, q) in _output)
        {
            if (IsInteriorIntersection(a, b, p, q)) return true;
        }

        foreach (var (other, k) in _input)
        {
            if (other == line && k >= i && k < j) continue; // der Abschnitt selbst
            if (IsInteriorIntersection(a, b, other.Points[k], other.Points[k + 1])) return true;
        }

        return false;
    }

    /// <summary>
    /// Ob die Ersatzstrecke über eine andere Komponente „springt“ (wie JTS ComponentJumpChecker): für einen Punkt
    /// jeder anderen Linie unterscheidet sich die Parität der Strahlkreuzungen von Originalabschnitt und Strecke.
    /// </summary>
    private bool HasJump(Line line, int i, int j, Coordinate a, Coordinate b)
    {
        foreach (var other in _lines)
        {
            if (other == line || other.Points.Length < 2) continue;
            var p = other.Points[1];
            int section = 0;
            for (int k = i; k < j; k++) section += Crossing(p, line.Points[k], line.Points[k + 1]);
            if (section % 2 != Crossing(p, a, b) % 2) return true;
        }

        return false;
    }

    /// <summary>1, wenn der Strahl von p nach +x die Strecke kreuzt (halboffen in y), sonst 0.</summary>
    private static int Crossing(Coordinate p, Coordinate a, Coordinate b)
    {
        if ((a.Y > p.Y) == (b.Y > p.Y)) return 0;
        var (lo, hi) = a.Y < b.Y ? (a, b) : (b, a);
        return Noding.Orientation.Orient2D(lo, hi, p) > 0 ? 1 : 0; // p links der aufwärts gerichteten Strecke
    }

    /// <summary>Schnitt, der nicht nur in gemeinsamen Endpunkten liegt (wie JTS isInteriorIntersection).</summary>
    private static bool IsInteriorIntersection(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
            || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y))
        {
            return false;
        }

        if (!Segments.Intersects(a, b, c, d)) return false;
        bool sharedEnd(Coordinate p) => Rings.SameXY(p, c) || Rings.SameXY(p, d);
        bool sharedEnd2(Coordinate p) => Rings.SameXY(p, a) || Rings.SameXY(p, b);

        int o1 = Segments.Orient(a, b, c), o2 = Segments.Orient(a, b, d);
        int o3 = Segments.Orient(c, d, a), o4 = Segments.Orient(c, d, b);
        if (o1 * o2 < 0 && o3 * o4 < 0) return true; // echte Kreuzung

        // Berührung/Überlappung: nur dann innen, wenn ein Berührpunkt kein gemeinsamer Endpunkt ist
        foreach (var p in new[] { a, b })
            if (Segments.IsOnSegment(p, c, d) && !sharedEnd(p)) return true;
        foreach (var p in new[] { c, d })
            if (Segments.IsOnSegment(p, a, b) && !sharedEnd2(p)) return true;
        return false;
    }

    private void SimplifyRingEndpoint(Line line)
    {
        if (line.ResultSize <= line.MinimumSize) return;
        var first = line.Result[0];
        var last = line.Result[^1];
        var candidate = (A: last.A, B: first.B);
        if (Segments.PointDistance(first.A, candidate.A, candidate.B) > tolerance) return;
        if (HasJumpRingEndpoint(line, candidate)) return;

        _output.Remove(first);
        _output.Remove(last);
        if (HasBadIntersectionSegment(candidate.A, candidate.B))
        {
            _output.Add(first);
            _output.Add(last);
            return;
        }

        line.Result.RemoveAt(line.Result.Count - 1);
        line.Result.RemoveAt(0);
        line.Result.Add(candidate);
        _output.Add(candidate);
    }

    private bool HasJumpRingEndpoint(Line line, (Coordinate A, Coordinate B) candidate)
    {
        var first = line.Result[0];
        var last = line.Result[^1];
        foreach (var other in _lines)
        {
            if (other == line || other.Points.Length < 2) continue;
            var p = other.Points[1];
            int section = Crossing(p, last.A, last.B) + Crossing(p, first.A, first.B);
            if (section % 2 != Crossing(p, candidate.A, candidate.B) % 2) return true;
        }

        return false;
    }

    private bool HasBadIntersectionSegment(Coordinate a, Coordinate b) =>
        _output.Any(s => IsInteriorIntersection(a, b, s.A, s.B))
        || _input.Any(s => IsInteriorIntersection(a, b, s.Item1.Points[s.Item2], s.Item1.Points[s.Item2 + 1]));
}
