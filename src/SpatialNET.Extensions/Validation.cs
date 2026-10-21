using SpatialNET.Internal;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

/// <summary>Kind of a validity finding (ADR 0003/0012).</summary>
public enum ValidationIssueKind
{
    /// <summary>Rings cross each other, or a ring crosses or overlaps itself.</summary>
    SelfIntersection,

    /// <summary>A ring touches itself at a point (e.g. an "inverted hole").</summary>
    RingSelfTouch,

    /// <summary>A ring without area (fewer than 3 distinct points or all points collinear).</summary>
    DegenerateRing,

    /// <summary>A coordinate is <c>NaN</c> or infinite.</summary>
    InvalidCoordinate,

    /// <summary>A path with fewer than 2 distinct points.</summary>
    TooFewPoints,

    /// <summary>Two rings overlap along an edge.</summary>
    OverlappingShells,

    /// <summary>Rings touch in such a way that the area falls apart into separate parts (e.g. two touch points of the same ring pair).</summary>
    DisconnectedInterior,
}

/// <summary>A validity finding with its location.</summary>
/// <param name="Kind">The kind of finding.</param>
/// <param name="Location">The location (intersection or touch point, affected vertex).</param>
public readonly record struct ValidationIssue(ValidationIssueKind Kind, Coordinate Location);

/// <summary>The result of a validity check.</summary>
/// <param name="Issues">All findings; empty for a valid geometry.</param>
public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Whether the geometry is valid.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Validity according to ADR 0003: area by even-odd, OGC rules for crossings and touches. A missing
/// closing point and the ring orientation are irrelevant; a ring in a hole is an island, not an error.
/// Decisions are exact (orient2d).
/// </summary>
public static class Validation
{
    /// <summary>Whether the geometry is valid.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static bool IsValid(this IGeometry2D geometry) => geometry.Validate().IsValid;

    /// <summary>Checks validity and returns all findings with their location.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static ValidationResult Validate(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var issues = new List<ValidationIssue>();

        foreach (var p in GeometryAccess.AllPoints(geometry))
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
                issues.Add(new(ValidationIssueKind.InvalidCoordinate, p.ToCoordinate()));
            }
        }

        if (issues.Count > 0)
        {
            return new ValidationResult(issues);
        }

        switch (geometry)
        {
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++)
                {
                    var path = Rings.Coordinates(line[i]);
                    if (Compact(path, closed: false).Length < 2)
                    {
                        issues.Add(new(ValidationIssueKind.TooFewPoints, path.Length > 0 ? path[0] : default));
                    }
                }

                break;

            case IPolygon2D polygon:
                ValidatePolygon(polygon, issues);
                break;
        }

        return new ValidationResult(issues.Distinct().ToList());
    }

    private readonly record struct Seg(int Ring, int Index, Coordinate A, Coordinate B)
    {
        public double MinX => Math.Min(A.X, B.X);
        public double MaxX => Math.Max(A.X, B.X);
        public double MinY => Math.Min(A.Y, B.Y);
        public double MaxY => Math.Max(A.Y, B.Y);
    }

    private static void ValidatePolygon(IPolygon2D polygon, List<ValidationIssue> issues)
    {
        var rings = new List<Coordinate[]>();
        for (int i = 0; i < polygon.RingCount; i++)
        {
            if (polygon[i].PointCount == 0)
            {
                continue; // leerer Ring (WKT „EMPTY“) trägt nichts bei
            }

            var ring = Compact(Rings.Closed(polygon[i]), closed: true);
            if (ring.Length < 4 || IsCollinear(ring))
            {
                issues.Add(new(ValidationIssueKind.DegenerateRing, ring.Length > 0 ? ring[0] : default));
                continue;
            }

            rings.Add(ring);
        }

        var segments = new List<Seg>();
        for (int r = 0; r < rings.Count; r++)
        {
            for (int i = 1; i < rings[r].Length; i++)
            {
                segments.Add(new Seg(r, i - 1, rings[r][i - 1], rings[r][i]));
            }
        }

        var touches = new Dictionary<(int, int), HashSet<(double, double)>>();
        var order = segments.OrderBy(s => s.MinX).ToArray();
        var active = new List<Seg>();
        foreach (var s in order)
        {
            active.RemoveAll(a => a.MaxX < s.MinX);
            foreach (var a in active)
            {
                if (a.MaxY >= s.MinY && s.MaxY >= a.MinY)
                {
                    Check(a, s, rings, issues, touches);
                }
            }

            active.Add(s);
        }

        // Kreuzung in einem gemeinsamen Punkt
        foreach (var ((r1, r2), points) in touches.OrderBy(t => t.Key))
        {
            foreach (var (x, y) in points.Order())
            {
                if (CrossAt(rings[r1], rings[r2], new Coordinate(x, y)))
                {
                    issues.Add(new(ValidationIssueKind.SelfIntersection, new Coordinate(x, y)));
                }
            }
        }

        // Zerfallende Fläche (je Teilfläche): Zyklus im Graph aus Ringen und Berührpunkten. Berührungen zwischen
        // verschiedenen Teilflächen (Multipolygon) oder viele Ringe in einem einzigen Punkt sind zulässig.
        if (issues.Count == 0 && touches.Count > 0)
        {
            var part = PolygonStructure.PartOf(rings);
            var nodes = new Dictionary<object, int>();
            var union = new List<int>();
            int Node(object key)
            {
                if (!nodes.TryGetValue(key, out int id))
                {
                    nodes[key] = id = union.Count;
                    union.Add(id);
                }

                return id;
            }

            int Find(int x) => union[x] == x ? x : union[x] = Find(union[x]);
            var linked = new HashSet<(int Ring, (double, double) Point)>();
            foreach (var ((r1, r2), points) in touches.OrderBy(t => t.Key))
            {
                if (part[r1] != part[r2])
                {
                    continue;
                }

                foreach (var (x, y) in points.Order())
                {
                    int point = Node((x, y));
                    foreach (int ring in new[] { r1, r2 })
                    {
                        if (!linked.Add((ring, (x, y))))
                        {
                            continue; // dieselbe Berührung Ring–Punkt nur einmal
                        }

                        int a = Find(Node(ring)), b = Find(point);
                        if (a == b)
                        {
                            issues.Add(new(ValidationIssueKind.DisconnectedInterior, new Coordinate(x, y)));
                        }
                        else
                        {
                            union[a] = b;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Whether two rings cross at the touch point <paramref name="p"/> (directions alternate by angle).</summary>
    private static bool CrossAt(Coordinate[] ring1, Coordinate[] ring2, Coordinate p)
    {
        if (Directions(ring1, p) is not { } d1 || Directions(ring2, p) is not { } d2)
        {
            return false;
        }

        // Deckungsgleiche Richtungen sind eine Überlappung (bereits als OverlappingShells gemeldet), keine Kreuzung.
        foreach (var u in new[] { d1.Prev, d1.Next })
            foreach (var v in new[] { d2.Prev, d2.Next })
                if (CompareAngle(p, u, v) == 0) return false;

        var all = new[] { (d1.Prev, 1), (d1.Next, 1), (d2.Prev, 2), (d2.Next, 2) };
        Array.Sort(all, (u, v) => CompareAngle(p, u.Item1, v.Item1));
        return all[0].Item2 != all[1].Item2 && all[1].Item2 != all[2].Item2 && all[2].Item2 != all[3].Item2;
    }

    /// <summary>The neighbor points of a closed ring around <paramref name="p"/> (vertex or point on a segment).</summary>
    private static (Coordinate Prev, Coordinate Next)? Directions(Coordinate[] closed, Coordinate p)
    {
        int n = closed.Length - 1;
        for (int i = 0; i < n; i++)
        {
            if (Rings.SameXY(closed[i], p))
            {
                return (closed[(i - 1 + n) % n], closed[i + 1]);
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (Segments.IsOnSegment(p, closed[i], closed[i + 1]))
            {
                return (closed[i], closed[i + 1]);
            }
        }

        return null;
    }

    /// <summary>Exact angle comparison of two directions around <paramref name="o"/> (half-plane, then orient2d).</summary>
    private static int CompareAngle(Coordinate o, Coordinate a, Coordinate b)
    {
        int ha = Half(o, a), hb = Half(o, b);
        if (ha != hb) return ha.CompareTo(hb);
        return -Orientation.Orient2D(o, a, b); // a vor b, wenn b links von o→a liegt
    }

    private static int Half(Coordinate o, Coordinate a) =>
        a.Y > o.Y || (a.Y == o.Y && a.X > o.X) ? 0 : 1;

    private static void Check(Seg a, Seg b, List<Coordinate[]> rings, List<ValidationIssue> issues,
        Dictionary<(int, int), HashSet<(double, double)>> touches)
    {
        int o1 = Orientation.Orient2D(a.A, a.B, b.A), o2 = Orientation.Orient2D(a.A, a.B, b.B);
        int o3 = Orientation.Orient2D(b.A, b.B, a.A), o4 = Orientation.Orient2D(b.A, b.B, a.B);
        bool sameRing = a.Ring == b.Ring;
        int n = rings[a.Ring].Length - 1; // Segmente je Ring
        bool adjacent = sameRing && (Math.Abs(a.Index - b.Index) == 1 || Math.Abs(a.Index - b.Index) == n - 1);

        if (o1 * o2 < 0 && o3 * o4 < 0)
        {
            issues.Add(new(ValidationIssueKind.SelfIntersection, Intersection(a, b)));
            return;
        }

        if (o1 == 0 && o2 == 0)
        {
            // kollinear: Überlappung mit positiver Länge?
            if (OverlapLength(a, b))
            {
                issues.Add(new(sameRing ? ValidationIssueKind.SelfIntersection : ValidationIssueKind.OverlappingShells, Overlap(a, b)));
                return;
            }
        }

        // Berührungen: Endpunkte auf dem anderen Segment
        var points = new List<Coordinate>();
        if (o1 == 0 && Segments.IsOnSegment(b.A, a.A, a.B)) points.Add(b.A);
        if (o2 == 0 && Segments.IsOnSegment(b.B, a.A, a.B)) points.Add(b.B);
        if (o3 == 0 && Segments.IsOnSegment(a.A, b.A, b.B)) points.Add(a.A);
        if (o4 == 0 && Segments.IsOnSegment(a.B, b.A, b.B)) points.Add(a.B);
        if (points.Count == 0)
        {
            return;
        }

        if (sameRing)
        {
            if (adjacent)
            {
                return; // gemeinsamer Vertex benachbarter Segmente
            }

            issues.Add(new(ValidationIssueKind.RingSelfTouch, points[0]));
            return;
        }

        var key = a.Ring < b.Ring ? (a.Ring, b.Ring) : (b.Ring, a.Ring);
        if (!touches.TryGetValue(key, out var set))
        {
            touches[key] = set = [];
        }

        foreach (var p in points)
        {
            set.Add((p.X, p.Y));
        }
    }

    private static bool OverlapLength(Seg a, Seg b)
    {
        // Projektion auf die dominante Achse von a
        bool useX = Math.Abs(a.B.X - a.A.X) >= Math.Abs(a.B.Y - a.A.Y);
        double a0 = useX ? Math.Min(a.A.X, a.B.X) : Math.Min(a.A.Y, a.B.Y), a1 = useX ? Math.Max(a.A.X, a.B.X) : Math.Max(a.A.Y, a.B.Y);
        double b0 = useX ? Math.Min(b.A.X, b.B.X) : Math.Min(b.A.Y, b.B.Y), b1 = useX ? Math.Max(b.A.X, b.B.X) : Math.Max(b.A.Y, b.B.Y);
        return Math.Min(a1, b1) > Math.Max(a0, b0);
    }

    private static Coordinate Overlap(Seg a, Seg b) =>
        Segments.IsOnSegment(b.A, a.A, a.B) ? b.A : Segments.IsOnSegment(b.B, a.A, a.B) ? b.B : a.A;

    private static Coordinate Intersection(Seg a, Seg b)
    {
        double rx = a.B.X - a.A.X, ry = a.B.Y - a.A.Y, sx = b.B.X - b.A.X, sy = b.B.Y - b.A.Y;
        double t = ((b.A.X - a.A.X) * sy - (b.A.Y - a.A.Y) * sx) / (rx * sy - ry * sx);
        return new Coordinate(a.A.X + t * rx, a.A.Y + t * ry);
    }

    /// <summary>Removes consecutive duplicate points (for rings the closing point is kept).</summary>
    private static Coordinate[] Compact(Coordinate[] points, bool closed)
    {
        var result = new List<Coordinate>(points.Length);
        foreach (var p in points)
        {
            if (result.Count == 0 || !Rings.SameXY(result[^1], p))
            {
                result.Add(p);
            }
        }

        if (closed && result.Count == 1)
        {
            result.Add(result[0]);
        }

        return result.ToArray();
    }

    private static bool IsCollinear(Coordinate[] closed)
    {
        for (int i = 2; i < closed.Length - 1; i++)
        {
            if (Orientation.Orient2D(closed[0], closed[1], closed[i]) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
