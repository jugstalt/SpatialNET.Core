namespace SpatialNET.Internal.Overlay;

/// <summary>
/// Envelope-Vorfilter für Overlays (#38, Muster JTS OverlayNG RingClipper): Ringteile weit außerhalb eines
/// Rechtecks R werden durch ihre Projektion auf den Rand von R ersetzt. Anders als beim Abschneiden bleiben alle
/// Segmente, die R berühren, unverändert erhalten (keine neuen Schnittpunkte), und nur Läufe von Segmenten
/// komplett außerhalb werden ersetzt: P → r(P) → r(Lauf) → r(Q) → Q mit r = Klemmen auf R.
/// </summary>
/// <remarks>
/// Die Windungszahl jedes Punkts im Inneren von R bleibt exakt erhalten: Lauf und Ersatz sind über
/// t ↦ (1 − t)·γ + t·r(γ) homotop, und diese Homotopie verlässt das Komplement des offenen R nie
/// (die Strecke von einem Punkt außerhalb zu seiner Projektion auf ein konvexes R berührt dessen Inneres nicht).
/// Außerhalb von R können Artefakt-Maschen entstehen; der Aufrufer wählt deshalb nur Kanten im Ergebnisbereich.
/// </remarks>
internal static class RingClipper
{
    /// <summary>Der Ring (geschlossen) mit ersetzten Außenläufen; unverändert, wenn jedes Segment R berührt.</summary>
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

    /// <summary>Ob das Segment das abgeschlossene Rechteck berühren kann (Envelope-Test, konservativ).</summary>
    private static bool Touches(Coordinate a, Coordinate b, Envelope2D r) =>
        Math.Max(a.X, b.X) >= r.MinX && Math.Min(a.X, b.X) <= r.MaxX &&
        Math.Max(a.Y, b.Y) >= r.MinY && Math.Min(a.Y, b.Y) <= r.MaxY;

    private static Coordinate Clamp(Coordinate c, Envelope2D r) =>
        new(Math.Clamp(c.X, r.MinX, r.MaxX), Math.Clamp(c.Y, r.MinY, r.MaxY));

    /// <summary>
    /// Projektion r(a→b) eines Segments komplett außerhalb: Das Segment liegt ganz jenseits einer Rechteckkante
    /// (Envelope-Test), also liegt r(a→b) auf deren Gerade, und die Strecke r(a)→r(b) ist genau r(a→b) –
    /// Knicke an den Ecken fallen mit den Endpunkten zusammen oder sind kollinear. Beide Punkte liegen exakt auf dem Rand.
    /// <c>run</c> ist der Index des ersten Punkts des Außenlaufs im Ergebnis (davor stehen Originalpunkte).
    /// </summary>
    private static void Retract(Coordinate a, Coordinate b, Envelope2D r, List<Coordinate> result, int run)
    {
        AddOnBoundary(result, Clamp(a, r), run);
        AddOnBoundary(result, Clamp(b, r), run);
    }

    /// <summary>
    /// Wie <see cref="Add"/>, entfernt aber einen geklemmten Vorgänger, der auf derselben achsparallelen Geraden
    /// zwischen seinen Nachbarn liegt (gleicher Weg). Sonst ergäben lange Außenläufe tausende kollineare
    /// Randsegmente beider Operanden auf denselben Linien, die das Noding teuer machen.
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
