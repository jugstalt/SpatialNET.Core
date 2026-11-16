using SpatialNET.Internal.Noding;

namespace SpatialNET.Internal.Overlay;

/// <summary>
/// Fast path of <see cref="Overlays.Clip(IPolygon2D, Envelope2D, IGeometryFactory?, double?)"/>: the polygon is cut directly at
/// the rectangle on the grid (chains of the rings inside the rectangle, linked along its border), without the noder and the
/// planar graph of the overlay. The result is the one the overlay builds, vertex for vertex: the same rounding of the crossings
/// (<see cref="GridModel.RoundExactIntersection"/>), the same Z and M, the same orientation, start points and order of the rings.
/// </summary>
/// <remarks>
/// The path only takes inputs where snap rounding cannot change anything: every vertex, crossing and rectangle corner keeps a
/// clearance of <see cref="Clearance"/> grid units from every segment that does not end in it, no vertex lies near the border,
/// and the segments in or near the rectangle do not cross. Then every segment piece of the overlay is the straight piece between
/// its own nodes, and the parity of the even-odd rule inside the rectangle follows from the parity at one corner. Everything else
/// (touching, near-degenerate, self-crossing in the rectangle, many inner rings) returns <c>null</c>, and the overlay runs.
/// <para>
/// Ring order: the overlay reports each ring from its half-edge with the lowest index, and the edge indices follow the noded
/// input: the rings of the polygon in order, each from the first segment that touches the prefilter rectangle
/// (<see cref="RingClipper"/>), then the rectangle from (MinX, MinY) counter-clockwise. Every result edge carries that order as a
/// key, and each ring starts at the origin of its edge with the smallest key.
/// </para>
/// </remarks>
internal static class RectangleClipper
{
    /// <summary>Clearance in grid units (a hot pixel reaches 0.71 units from its centre).</summary>
    private const double Clearance = 2;

    /// <summary>Above this number of inner rings (rings completely in the rectangle), the parity test would grow quadratically.</summary>
    private const int MaxInnerRings = 16;

    private const int RingShift = 40;

    private struct Vertex(GridPoint p, double z, double m, long key)
    {
        public GridPoint P = p;
        public double Z = z, M = m;
        public long Key = key; // key of the outgoing edge
    }

    private struct Event
    {
        public GridPoint P;
        public int Side;
        public long Along;
        public int Chain;
        public bool Entry;
        public int Segment; // index into the relevant segments
        public double Z, M;
    }

    private struct Chain
    {
        public int Start, Count, In, Out;
    }

    private struct Seg
    {
        public int Ring, Index, Count;
        public double X0, Y0, X1, Y1;
        public bool Border; // at least one end point outside the rectangle
    }

    /// <summary>The clipped polygon, or <c>null</c> when the overlay has to decide.</summary>
    public static IPolygon2D? TryClip(IPolygon2D polygon, Envelope2D rect, Envelope2D own, IGeometryFactory f, double? gridSize)
    {
        // ---------- grid (as GridModel.Resolve over the polygon and the rectangle), without copying the rings ----------
        int ringCount = polygon.RingCount;
        double maxAbs = 0;
        for (int i = 0; i < ringCount; i++)
        {
            var points = polygon[i];
            for (int k = 0, count = points.PointCount; k < count; k++)
            {
                var p = points[k];
                double x = p.X, y = p.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
                maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(x), Math.Abs(y)));
            }
        }

        var grid = gridSize is { } size ? new GridModel(size)
            : Precision.DefaultGridSize is { } defaultSize ? new GridModel(defaultSize)
            : new GridModel(GridModel.AutoSize(Math.Max(maxAbs, Math.Max(Math.Max(Math.Abs(rect.MinX), Math.Abs(rect.MinY)),
                Math.Max(Math.Abs(rect.MaxX), Math.Abs(rect.MaxY))))));

        double rx0 = grid.ToUnits(rect.MinX), ry0 = grid.ToUnits(rect.MinY), rx1 = grid.ToUnits(rect.MaxX), ry1 = grid.ToUnits(rect.MaxY);
        if (rx1 - rx0 <= 4 * Clearance || ry1 - ry0 <= 4 * Clearance) return null;
        double ex0 = rx0 - Clearance, ey0 = ry0 - Clearance, ex1 = rx1 + Clearance, ey1 = ry1 + Clearance;
        double sx0 = rx0 + Clearance, sy0 = ry0 + Clearance, sx1 = rx1 - Clearance, sy1 = ry1 - Clearance;

        // a vertex near the border sends the clip to the overlay: test it before anything is copied
        for (int i = 0; i < ringCount; i++)
        {
            var points = polygon[i];
            for (int k = 0, count = points.PointCount; k < count; k++)
            {
                var p = points[k];
                if (InBand(grid.ToUnits(p.X), grid.ToUnits(p.Y))) return null;
            }
        }

        // ---------- rings as the overlay sees them ----------
        var rings = new List<Coordinate[]>(ringCount);
        for (int i = 0; i < ringCount; i++)
        {
            var ring = Rings.Closed(polygon[i]);
            if (ring.Length > 0 && !Rings.IsDegenerate(ring)) rings.Add(ring);
        }

        if (rings.Count == 0) return null;
        if (rings.Count > 1 && PolygonStructure.CancelDuplicates(rings).Count != rings.Count) return null;

        Coordinate[] corner =
        [
            new(rect.MinX, rect.MinY), new(rect.MaxX, rect.MinY), new(rect.MaxX, rect.MaxY), new(rect.MinX, rect.MaxY),
        ];
        GridPoint[] cornerGrid = [grid.Round(corner[0]), grid.Round(corner[1]), grid.Round(corner[2]), grid.Round(corner[3])];
        Span<double> cornerUx = [rx0, rx1, rx1, rx0];
        Span<double> cornerUy = [ry0, ry0, ry1, ry1];

        // prefilter rectangle of the overlay (OverlayRunner.EnvelopeFilter): the ring order starts at its first touching segment
        double scale = Math.Max(Math.Max(Math.Max(Math.Abs(own.MinX), Math.Abs(own.MinY)), Math.Max(Math.Abs(own.MaxX), Math.Abs(own.MaxY))),
            Math.Max(Math.Max(Math.Abs(rect.MinX), Math.Abs(rect.MinY)), Math.Max(Math.Abs(rect.MaxX), Math.Abs(rect.MaxY))));
        double clipMargin = 8 * grid.Size + 1e-9 * scale;
        var region = new Envelope2D(Math.Max(own.MinX, rect.MinX) - clipMargin, Math.Max(own.MinY, rect.MinY) - clipMargin,
            Math.Min(own.MaxX, rect.MaxX) + clipMargin, Math.Min(own.MaxY, rect.MaxY) + clipMargin);

        var segs = new List<Seg>();
        var events = new List<Event>();
        var starts = new int[rings.Count];
        var innerRings = new List<int>();

        // ---------- relevant segments, crossings ----------
        for (int r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];
            int n = ring.Length - 1;
            int start = -1;
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                bool touches = Touches(ring[i], ring[i + 1], region);
                if (touches && start < 0) start = i;
                all &= touches;
            }

            starts[r] = all || start < 0 ? 0 : start;
            int firstSeg = segs.Count, eventsBefore = events.Count;
            bool anyInside = false;

            double px = grid.ToUnits(ring[0].X), py = grid.ToUnits(ring[0].Y);
            for (int i = 0; i < n; i++)
            {
                double qx = grid.ToUnits(ring[i + 1].X), qy = grid.ToUnits(ring[i + 1].Y);
                if (Math.Max(px, qx) < ex0 || Math.Min(px, qx) > ex1 || Math.Max(py, qy) < ey0 || Math.Min(py, qy) > ey1)
                {
                    px = qx;
                    py = qy;
                    continue; // far outside
                }

                if (InBand(px, py) || InBand(qx, qy)) return null;
                double dx = qx - px, dy = qy - py;
                if (dx * dx + dy * dy < Clearance * Clearance) return null;

                bool pIn = px > rx0 && px < rx1 && py > ry0 && py < ry1;
                bool qIn = qx > rx0 && qx < rx1 && qy > ry0 && qy < ry1;
                anyInside |= pIn;
                int segIndex = segs.Count;
                segs.Add(new Seg { Ring = r, Index = i, Count = n, X0 = px, Y0 = py, X1 = qx, Y1 = qy, Border = !pIn || !qIn });

                if (!pIn || !qIn)
                {
                    // Liang–Barsky: entering and leaving parameter with the side (0 bottom, 1 right, 2 top, 3 left)
                    double tIn = 0, tOut = 1;
                    int sideIn = -1, sideOut = -1;
                    bool hit = Bound(dx, px, rx0, rx1, 3, 1, ref tIn, ref tOut, ref sideIn, ref sideOut)
                        && Bound(dy, py, ry0, ry1, 0, 2, ref tIn, ref tOut, ref sideIn, ref sideOut);
                    if (hit && tIn < tOut)
                    {
                        if (!pIn)
                        {
                            if (sideIn < 0 || !AddEvent(ring, i, sideIn, segIndex, true, px, py, dx, dy)) return null;
                        }

                        if (!qIn)
                        {
                            if (sideOut < 0 || !AddEvent(ring, i, sideOut, segIndex, false, px, py, dx, dy)) return null;
                        }
                    }
                    else if (pIn || qIn)
                    {
                        return null;
                    }
                }

                px = qx;
                py = qy;
            }

            if (events.Count == eventsBefore && anyInside)
            {
                if (segs.Count - firstSeg != n) return null;
                innerRings.Add(r);
                if (innerRings.Count > MaxInnerRings) return null;
            }
        }

        // ---------- clearance: segments against segments ----------
        if (!SegmentsClear(segs)) return null;

        // ---------- clearance: crossings and corners against the segments near the border ----------
        for (int s = 0; s < segs.Count; s++)
        {
            var seg = segs[s];
            if (!seg.Border) continue;
            for (int c = 0; c < 4; c++)
            {
                if (Distance2(cornerUx[c], cornerUy[c], seg) < Clearance * Clearance) return null;
            }

            foreach (var e in events)
            {
                if (e.Segment != s && Distance2(e.P.X, e.P.Y, seg) < Clearance * Clearance) return null;
            }
        }

        // ---------- order along the border ----------
        int eventCount = events.Count;
        var order = new int[eventCount];
        for (int i = 0; i < eventCount; i++) order[i] = i;
        var eventArray = events.ToArray();
        Array.Sort(order, (a, b) =>
        {
            var ea = eventArray[a];
            var eb = eventArray[b];
            return ea.Side != eb.Side ? ea.Side.CompareTo(eb.Side) : ea.Along.CompareTo(eb.Along);
        });

        for (int k = 1; k < eventCount; k++)
        {
            var a = eventArray[order[k - 1]];
            var b = eventArray[order[k]];
            if (a.Side == b.Side && b.Along - a.Along < Clearance) return null;
        }

        // border nodes in the order of the noded rectangle: corner j, then the events on side j
        Span<int> perSide = stackalloc int[4];
        foreach (var e in eventArray) perSide[e.Side]++;
        Span<int> cornerOrdinal = stackalloc int[4];
        for (int j = 0, before = 0; j < 4; j++)
        {
            cornerOrdinal[j] = j + before;
            before += perSide[j];
        }

        long rectKey = (long)rings.Count << RingShift;
        var rank = new int[eventCount]; // sorted position of an event
        for (int k = 0; k < eventCount; k++) rank[order[k]] = k;

        // ---------- parity of the even-odd rule ----------
        int parity0 = 0;
        foreach (var ring in rings)
        {
            var location = Rings.Locate(corner[0], ring);
            if (location == Location.Boundary) return null;
            if (location == Location.Inside) parity0 ^= 1;
        }

        // ---------- chains ----------
        var vertices = new List<Vertex>();
        var chains = new List<Chain>();
        BuildChains();
        if (chains.Count * 2 != eventCount) return null;

        // parity just after each event (walking counter-clockwise); a chain runs from the event after which it is even
        var after = new int[eventCount];
        for (int k = 0, p = parity0; k < eventCount; k++)
        {
            p ^= 1;
            after[order[k]] = p;
        }

        var reversed = new bool[chains.Count];
        for (int c = 0; c < chains.Count; c++)
        {
            int ai = after[chains[c].In], ao = after[chains[c].Out];
            if (ai == ao) return null;
            reversed[c] = ai == 1;
        }

        // ---------- loops ----------
        var loops = new List<Vertex[]>();
        var loopVerts = new List<Vertex>();
        var used = new bool[chains.Count];
        for (int c0 = 0; c0 < chains.Count; c0++)
        {
            if (used[c0]) continue;
            loopVerts.Clear();
            int c = c0;
            while (!used[c])
            {
                used[c] = true;
                var chain = chains[c];
                int endEvent = reversed[c] ? chain.In : chain.Out;
                int k = rank[endEvent];
                long endKey = rectKey | (uint)(k + eventArray[endEvent].Side + 1);
                if (!reversed[c])
                {
                    for (int v = 0; v < chain.Count - 1; v++) loopVerts.Add(vertices[chain.Start + v]);
                    var last = vertices[chain.Start + chain.Count - 1];
                    last.Key = endKey;
                    loopVerts.Add(last);
                }
                else
                {
                    for (int v = chain.Count - 1; v >= 1; v--)
                    {
                        var vertex = vertices[chain.Start + v];
                        vertex.Key = vertices[chain.Start + v - 1].Key;
                        loopVerts.Add(vertex);
                    }

                    var first = vertices[chain.Start];
                    first.Key = endKey;
                    loopVerts.Add(first);
                }

                // along the border to the next event, with the corners passed
                int nextK = (k + 1) % eventCount;
                int next = order[nextK];
                int sideA = eventArray[endEvent].Side, sideB = eventArray[next].Side;
                int passes = nextK == 0 ? sideB + 4 - sideA : sideB - sideA;
                for (int j = 1; j <= passes; j++)
                {
                    int cj = (sideA + j) % 4;
                    loopVerts.Add(new Vertex(cornerGrid[cj], double.NaN, double.NaN, rectKey | (uint)cornerOrdinal[cj]));
                }

                c = eventArray[next].Chain;
                bool startsHere = reversed[c] ? next == chains[c].Out : next == chains[c].In;
                if (!startsHere) return null;
            }

            if (c != c0) return null;
            loops.Add(loopVerts.ToArray());
        }

        if (eventCount == 0 && parity0 == 1)
        {
            loops.Add(
            [
                new(cornerGrid[0], double.NaN, double.NaN, rectKey | 0u), new(cornerGrid[1], double.NaN, double.NaN, rectKey | 1u),
                new(cornerGrid[2], double.NaN, double.NaN, rectKey | 2u), new(cornerGrid[3], double.NaN, double.NaN, rectKey | 3u),
            ]);
        }

        foreach (int r in innerRings)
        {
            var ring = rings[r];
            int outside = 0;
            foreach (var other in rings)
            {
                if (ReferenceEquals(other, ring)) continue;
                var location = Rings.Locate(ring[0], other);
                if (location == Location.Boundary) return null;
                if (location == Location.Inside) outside ^= 1;
            }

            int n = ring.Length - 1;
            var loop = new Vertex[n];
            for (int i = 0; i < n; i++)
            {
                loop[i] = new Vertex(grid.Round(ring[i]), ring[i].Z, ring[i].M, Key(r, i, n));
            }

            bool ccw = SignedArea2(loop) > 0;
            if (ccw != (outside == 0)) Reverse(loop);
            loops.Add(loop);
        }

        return Assemble(loops, grid, f, polygon);

        bool InBand(double x, double y) =>
            x >= ex0 && x <= ex1 && y >= ey0 && y <= ey1 && !(x > sx0 && x < sx1 && y > sy0 && y < sy1);

        long Key(int r, int i, int n) => ((long)r << RingShift) | (uint)((i - starts[r] + n) % n);

        bool AddEvent(Coordinate[] ring, int i, int side, int segIndex, bool entry, double px, double py, double dx, double dy)
        {
            var p = ring[i];
            var q = ring[i + 1];
            var pixel = grid.RoundExactIntersection(p, q, corner[side], corner[(side + 1) % 4]);
            bool onSide = side switch
            {
                0 => pixel.Y == cornerGrid[0].Y,
                1 => pixel.X == cornerGrid[1].X,
                2 => pixel.Y == cornerGrid[2].Y,
                _ => pixel.X == cornerGrid[3].X,
            };
            if (!onSide) return false;

            double len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : ((pixel.X - px) * dx + (pixel.Y - py) * dy) / len2;
            double tz = Math.Clamp(t, 0, 1);
            events.Add(new Event
            {
                P = pixel,
                Side = side,
                Along = side switch { 0 => pixel.X, 1 => pixel.Y, 2 => -pixel.X, _ => -pixel.Y },
                Entry = entry,
                Segment = segIndex,
                Chain = -1,
                Z = p.HasZ && q.HasZ ? p.Z + tz * (q.Z - p.Z) : double.NaN,
                M = p.HasM && q.HasM ? p.M + tz * (q.M - p.M) : double.NaN,
            });
            return true;
        }

        void BuildChains()
        {
            // events of a ring are in segment order (an entry before an exit on the same segment)
            int e = 0;
            while (e < eventCount)
            {
                int ringIndex = segs[eventArray[e].Segment].Ring;
                int end = e;
                while (end < eventCount && segs[eventArray[end].Segment].Ring == ringIndex) end++;

                var ring = rings[ringIndex];
                int n = ring.Length - 1;
                int first = e;
                while (first < end && !eventArray[first].Entry) first++;
                if (first == end || (end - e) % 2 != 0)
                {
                    chains.Clear();
                    return;
                }

                // from the first entry, every entry is followed by its exit (cyclic)
                for (int k = 0; k < end - e; k += 2)
                {
                    int inEvent = e + (first - e + k) % (end - e);
                    int outEvent = e + (first - e + k + 1) % (end - e);
                    if (!eventArray[inEvent].Entry || eventArray[outEvent].Entry)
                    {
                        chains.Clear();
                        return;
                    }

                    int startVertex = vertices.Count;
                    int inSeg = segs[eventArray[inEvent].Segment].Index;
                    int outSeg = segs[eventArray[outEvent].Segment].Index;
                    var ev = eventArray[inEvent];
                    vertices.Add(new Vertex(ev.P, ev.Z, ev.M, Key(ringIndex, inSeg, n)));
                    for (int i = inSeg; i != outSeg;)
                    {
                        i = (i + 1) % n;
                        var c = ring[i];
                        vertices.Add(new Vertex(grid.Round(c), c.Z, c.M, Key(ringIndex, i, n)));
                    }

                    var ex = eventArray[outEvent];
                    vertices.Add(new Vertex(ex.P, ex.Z, ex.M, -1));
                    int chain = chains.Count;
                    chains.Add(new Chain { Start = startVertex, Count = vertices.Count - startVertex, In = inEvent, Out = outEvent });
                    eventArray[inEvent].Chain = chain;
                    eventArray[outEvent].Chain = chain;
                }

                e = end;
            }
        }
    }

    /// <summary>Parts as <see cref="OverlayEngine.BuildAreas"/> forms them: rings in key order, holes to the smallest shell around them.</summary>
    private static IPolygon2D Assemble(List<Vertex[]> loops, GridModel grid, IGeometryFactory f, IPolygon2D source)
    {
        var rings = new List<(long Key, GridPoint[] Ring, Coordinate[] Coordinates, Int128 Area2)>(loops.Count);
        foreach (var loop in loops)
        {
            int min = 0;
            for (int i = 1; i < loop.Length; i++)
            {
                if (loop[i].Key < loop[min].Key) min = i;
            }

            var points = new GridPoint[loop.Length + 1];
            var coordinates = new Coordinate[loop.Length + 1];
            for (int i = 0; i < loop.Length; i++)
            {
                var v = loop[(min + i) % loop.Length];
                points[i] = v.P;
                coordinates[i] = grid.ToCoordinate(v.P, v.Z, v.M);
            }

            points[^1] = points[0];
            coordinates[^1] = coordinates[0];
            rings.Add((loop[min].Key, points, coordinates, SignedArea2(points)));
        }

        rings.Sort((a, b) => a.Key.CompareTo(b.Key));

        var shells = new List<int>();
        var holes = new List<int>();
        for (int i = 0; i < rings.Count; i++)
        {
            if (rings[i].Area2 > 0) shells.Add(i);
            else if (rings[i].Area2 < 0) holes.Add(i);
        }

        var assigned = new List<ReadOnlyMemory<Coordinate>>?[shells.Count];
        foreach (int h in holes)
        {
            var hole = rings[h].Ring;
            int best = -1;
            for (int s = 0; s < shells.Count; s++)
            {
                var shell = rings[shells[s]];
                if (ContainsRing(shell.Ring, hole) && (best < 0 || shell.Area2 < rings[shells[best]].Area2)) best = s;
            }

            if (best >= 0) (assigned[best] ??= []).Add(rings[h].Coordinates);
        }

        var parts = new PolygonPart[shells.Count];
        for (int s = 0; s < shells.Count; s++)
        {
            var part = new PolygonPart(rings[shells[s]].Coordinates, assigned[s]?.ToArray() ?? []);
            parts[s] = GeometryAccess.Restrict(part, f);
        }

        return f.CreatePolygon(parts, source);
    }

    private static bool ContainsRing(GridPoint[] outer, GridPoint[] inner)
    {
        foreach (var p in inner)
        {
            int location = OverlayEngine.Locate(p, outer);
            if (location != 1) return location == 2;
        }

        return false;
    }

    private static Int128 SignedArea2(GridPoint[] closed)
    {
        Int128 sum = 0;
        for (int i = 0; i < closed.Length - 1; i++)
        {
            sum += (Int128)closed[i].X * closed[i + 1].Y - (Int128)closed[i + 1].X * closed[i].Y;
        }

        return sum;
    }

    private static Int128 SignedArea2(Vertex[] loop)
    {
        Int128 sum = 0;
        for (int i = 0; i < loop.Length; i++)
        {
            var a = loop[i].P;
            var b = loop[(i + 1) % loop.Length].P;
            sum += (Int128)a.X * b.Y - (Int128)b.X * a.Y;
        }

        return sum;
    }

    /// <summary>Reverses a loop; each vertex takes the key of the edge it now starts (the former incoming edge).</summary>
    private static void Reverse(Vertex[] loop)
    {
        int n = loop.Length;
        var keys = new long[n];
        for (int i = 0; i < n; i++) keys[i] = loop[i].Key;
        Array.Reverse(loop);
        // after reversing, position k holds vertex n-1-k; its outgoing edge runs to vertex n-2-k: the edge (n-2-k) → (n-1-k)
        for (int k = 0; k < n; k++) loop[k].Key = keys[((n - 2 - k) % n + n) % n];
    }

    /// <summary>Liang–Barsky in one axis; tracks the side that sets the entering and the leaving parameter.</summary>
    private static bool Bound(double d, double p, double lo, double hi, int loSide, int hiSide, ref double tIn, ref double tOut, ref int sideIn, ref int sideOut)
    {
        if (d == 0) return p >= lo && p <= hi;
        double tLo = (lo - p) / d, tHi = (hi - p) / d;
        var (tEnter, enterSide, tLeave, leaveSide) = d > 0 ? (tLo, loSide, tHi, hiSide) : (tHi, hiSide, tLo, loSide);
        if (tEnter > tIn)
        {
            tIn = tEnter;
            sideIn = enterSide;
        }

        if (tLeave < tOut)
        {
            tOut = tLeave;
            sideOut = leaveSide;
        }

        return tIn <= tOut;
    }

    /// <summary>
    /// No two relevant segments cross or come closer than the clearance, except at a shared end point (sweep in x).
    /// </summary>
    private static bool SegmentsClear(List<Seg> segs)
    {
        int n = segs.Count;
        if (n < 2) return true;
        var arr = segs.ToArray();
        var keys = new double[n];
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            keys[i] = Math.Min(arr[i].X0, arr[i].X1);
            order[i] = i;
        }

        Array.Sort(keys, order);
        const double c2 = Clearance * Clearance;
        for (int a = 0; a < n; a++)
        {
            var s = arr[order[a]];
            double sMaxX = Math.Max(s.X0, s.X1) + Clearance;
            double sMinY = Math.Min(s.Y0, s.Y1) - Clearance, sMaxY = Math.Max(s.Y0, s.Y1) + Clearance;
            for (int b = a + 1; b < n && keys[b] <= sMaxX; b++)
            {
                var t = arr[order[b]];
                if (Math.Max(t.Y0, t.Y1) < sMinY || Math.Min(t.Y0, t.Y1) > sMaxY) continue;

                if (s.Ring == t.Ring && (s.Index + 1) % s.Count == t.Index)
                {
                    // t follows s: they share s.end = t.start
                    if (Distance2(s.X0, s.Y0, t) < c2 || Distance2(t.X1, t.Y1, s) < c2) return false;
                }
                else if (s.Ring == t.Ring && (t.Index + 1) % t.Count == s.Index)
                {
                    if (Distance2(t.X0, t.Y0, s) < c2 || Distance2(s.X1, s.Y1, t) < c2) return false;
                }
                else
                {
                    if (Cross(s, t)) return false;
                    if (Distance2(s.X0, s.Y0, t) < c2 || Distance2(s.X1, s.Y1, t) < c2 ||
                        Distance2(t.X0, t.Y0, s) < c2 || Distance2(t.X1, t.Y1, s) < c2) return false;
                }
            }
        }

        return true;
    }

    private static bool Cross(Seg s, Seg t)
    {
        double d1 = Side(s.X0, s.Y0, s.X1, s.Y1, t.X0, t.Y0), d2 = Side(s.X0, s.Y0, s.X1, s.Y1, t.X1, t.Y1);
        double d3 = Side(t.X0, t.Y0, t.X1, t.Y1, s.X0, s.Y0), d4 = Side(t.X0, t.Y0, t.X1, t.Y1, s.X1, s.Y1);
        return d1 * d2 <= 0 && d3 * d4 <= 0;
    }

    private static double Side(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static double Distance2(double x, double y, Seg s)
    {
        double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / len2, 0, 1);
        double ex = s.X0 + t * dx - x, ey = s.Y0 + t * dy - y;
        return ex * ex + ey * ey;
    }

    private static bool Touches(Coordinate a, Coordinate b, Envelope2D r) =>
        Math.Max(a.X, b.X) >= r.MinX && Math.Min(a.X, b.X) <= r.MaxX &&
        Math.Max(a.Y, b.Y) >= r.MinY && Math.Min(a.Y, b.Y) <= r.MaxY;
}
