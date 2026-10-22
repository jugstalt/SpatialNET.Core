using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>A circle: a centre and a radius (ADR 0026).</summary>
/// <param name="Centre">The centre.</param>
/// <param name="Radius">The radius; not negative.</param>
public readonly record struct Circle(Coordinate Centre, double Radius)
{
    /// <summary>
    /// The circle as a polygon: <c>4 * quadrantSegments</c> vertices on the circle, counter-clockwise, the first one east of the
    /// centre. The polygon is inscribed, so it lies inside the circle. A circle without radius has no area and gives an empty polygon.
    /// </summary>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <param name="quadrantSegments">Segments per quarter circle; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quadrantSegments"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolygon2D ToPolygon(IGeometryFactory? factory = null, int quadrantSegments = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quadrantSegments, 1);
        var f = GeometryFactory.Resolve(factory);
        if (!(Radius > 0))
        {
            return f.CreatePolygon([], null);
        }

        int n = 4 * quadrantSegments;
        var ring = new Coordinate[n + 1];
        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * i / n;
            ring[i] = new Coordinate(Centre.X + Radius * Math.Cos(angle), Centre.Y + Radius * Math.Sin(angle));
        }

        ring[n] = ring[0];
        return f.CreatePolygon([new PolygonPart(ring)], null);
    }
}

/// <summary>
/// Bounding shapes and label points (ADR 0026): <c>MinimumRotatedRectangle</c>, <c>MinimumBoundingCircle</c> and <c>Polylabel</c>.
/// </summary>
public static partial class Shapes
{
    // ---------- MinimumRotatedRectangle ----------

    /// <summary>
    /// The rectangle of least area that contains the geometry, turned to any angle (ADR 0026). A side of it lies on a side of the
    /// convex hull. Among rectangles of equal area the one of the first hull edge wins. Counter-clockwise, without Z and M. For a
    /// degenerate input the result is, as for <see cref="Hull.ConvexHull"/>, a point or the line between the two extreme points,
    /// and an empty polygon for an empty geometry.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IGeometry2D MinimumRotatedRectangle(this IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = GeometryFactory.Resolve(factory);
        var hull = geometry.ConvexHull(f);
        if (hull is not IPolygon2D polygon || GeometryAccess.IsEmpty(hull))
        {
            return hull; // a point, the line between the extreme points, or empty
        }

        var ring = Rings.Closed(polygon[0]);
        var points = ring[..^1];
        var rectangle = SmallestRectangle(points);
        return f.CreatePolygon([new PolygonPart(GeometryAccess.Restrict([.. rectangle, rectangle[0]], f))], geometry);
    }

    /// <summary>The corners (counter-clockwise) of the smallest rectangle with a side on an edge of a convex counter-clockwise polygon (rotating calipers).</summary>
    internal static Coordinate[] SmallestRectangle(Coordinate[] hull)
    {
        int h = hull.Length;
        double bestArea = double.PositiveInfinity;
        Coordinate[]? best = null;
        int farthest = 1, highest = 1, lowest = 1;

        for (int i = 0; i < h; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % h];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length2 = dx * dx + dy * dy;
            if (length2 == 0)
            {
                continue;
            }

            double Along(int k) => (hull[k % h].X - a.X) * dx + (hull[k % h].Y - a.Y) * dy;
            double Across(int k) => dx * (hull[k % h].Y - a.Y) - dy * (hull[k % h].X - a.X);

            if (i == 0)
            {
                farthest = highest = lowest = 1;
            }

            // the three calipers only move forwards; a step is bounded by h so that rounding can never loop
            for (int steps = 0; steps < h && Along(farthest + 1) > Along(farthest); steps++) farthest++;
            if (i == 0) highest = farthest;
            for (int steps = 0; steps < h && Across(highest + 1) > Across(highest); steps++) highest++;
            if (i == 0) lowest = highest;
            for (int steps = 0; steps < h && Along(lowest + 1) < Along(lowest); steps++) lowest++;

            double min = Along(lowest), max = Along(farthest), height = Across(highest);
            double area = (max - min) * height / length2;
            if (area < bestArea)
            {
                bestArea = area;
                double length = Math.Sqrt(length2);
                double ux = dx / length, uy = dy / length;      // along the edge
                double vx = -uy, vy = ux;                       // to the left, into the hull
                double lo = min / length, hi = max / length, up = height / length;
                var c0 = new Coordinate(a.X + ux * lo, a.Y + uy * lo);
                var c1 = new Coordinate(a.X + ux * hi, a.Y + uy * hi);
                best = [c0, c1, new Coordinate(c1.X + vx * up, c1.Y + vy * up), new Coordinate(c0.X + vx * up, c0.Y + vy * up)];
            }
        }

        return best ?? hull;
    }

    // ---------- MinimumBoundingCircle ----------

    /// <summary>
    /// The smallest circle that contains the geometry (ADR 0026), found with the algorithm of Welzl on the convex hull, in a
    /// fixed order, so that the result is deterministic. <c>null</c> for an empty geometry; a single point gives radius 0.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The circle, or <c>null</c> if the geometry is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static Circle? MinimumBoundingCircle(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var points = GeometryAccess.AllPoints(geometry).Select(p => new Coordinate(p.X, p.Y)).Distinct().ToArray();
        if (points.Length == 0)
        {
            return null;
        }

        // the hull needs no factory of its own: only its vertices matter
        var hullPoints = points.Length < 4 ? points : HullVertices(points);
        var random = new Random(12345);
        for (int i = hullPoints.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (hullPoints[i], hullPoints[j]) = (hullPoints[j], hullPoints[i]);
        }

        var circle = new Circle(hullPoints[0], 0);
        for (int i = 1; i < hullPoints.Length; i++)
        {
            if (Contains(circle, hullPoints[i])) continue;
            circle = new Circle(hullPoints[i], 0);
            for (int j = 0; j < i; j++)
            {
                if (Contains(circle, hullPoints[j])) continue;
                circle = FromTwo(hullPoints[i], hullPoints[j]);
                for (int k = 0; k < j; k++)
                {
                    if (Contains(circle, hullPoints[k])) continue;
                    circle = FromThree(hullPoints[i], hullPoints[j], hullPoints[k]);
                }
            }
        }

        // the radius that really contains every vertex, whatever rounding did to the circumcentre
        double radius = points.Max(p => Rings.Distance(circle.Centre, p));
        return new Circle(circle.Centre, radius);
    }

    private static Coordinate[] HullVertices(Coordinate[] points)
    {
        var sorted = points.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
        var hull = new Coordinate[2 * sorted.Length];
        int k = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            while (k >= 2 && Predicates.Orient(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }

        for (int i = sorted.Length - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Predicates.Orient(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }

        return k <= 3 ? [sorted[0], sorted[^1]] : hull[..(k - 1)];
    }

    private static bool Contains(Circle circle, Coordinate p) =>
        Rings.Distance(circle.Centre, p) <= circle.Radius * (1 + 1e-14);

    private static Circle FromTwo(Coordinate a, Coordinate b) =>
        new(new Coordinate(a.X / 2 + b.X / 2, a.Y / 2 + b.Y / 2), Rings.Distance(a, b) / 2);

    private static Circle FromThree(Coordinate a, Coordinate b, Coordinate c)
    {
        double bx = b.X - a.X, by = b.Y - a.Y, cx = c.X - a.X, cy = c.Y - a.Y;
        double d = 2 * (bx * cy - by * cx);
        if (d == 0)
        {
            // collinear: the two farthest apart
            var pairs = new[] { FromTwo(a, b), FromTwo(a, c), FromTwo(b, c) };
            return pairs.MaxBy(p => p.Radius);
        }

        double b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
        double ux = (cy * b2 - by * c2) / d, uy = (bx * c2 - cx * b2) / d;
        var centre = new Coordinate(a.X + ux, a.Y + uy);
        return new Circle(centre, Math.Sqrt(ux * ux + uy * uy));
    }

    // ---------- Polylabel ----------

    /// <summary>
    /// The pole of inaccessibility (ADR 0026): the point of the polygon that is farthest from its boundary (the holes included),
    /// found by the polylabel algorithm of Mapbox. It is a good place for a label. For a polygon of several parts it is the pole
    /// of the part with the largest circle. The point is within <paramref name="tolerance"/> of the optimum in its distance to the
    /// boundary. Without Z and M. <c>null</c> for an empty polygon.
    /// </summary>
    /// <param name="polygon">The polygon.</param>
    /// <param name="tolerance">
    /// The accuracy of the distance to the boundary, in the unit of the coordinates; finite and greater than 0.
    /// <c>null</c> = a thousandth of the larger side of the envelope.
    /// </param>
    /// <returns>The point, or <c>null</c> if the polygon is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is not finite or not greater than 0.</exception>
    public static Coordinate? Polylabel(this IPolygon2D polygon, double? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (tolerance is { } t && !(double.IsFinite(t) && t > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and greater than 0.");
        }

        var parts = PolygonStructure.Build(polygon);
        if (parts.Count == 0)
        {
            return null;
        }

        Coordinate? best = null;
        double bestDistance = double.NegativeInfinity;
        foreach (var part in parts)
        {
            var rings = part.Holes.Prepend(part.Shell).ToArray();
            var (point, distance) = PoleOf(rings, tolerance);
            if (distance > bestDistance)
            {
                (best, bestDistance) = (point, distance);
            }
        }

        // a sliver without an inside point of its own: fall back to a point on the surface
        return bestDistance > 0 ? best : polygon.PointOnSurface() is { } onSurface ? new Coordinate(onSurface.X, onSurface.Y) : best;
    }

    private readonly record struct Cell(double X, double Y, double Half, double Distance)
    {
        public double Potential => Distance + Half * Math.Sqrt(2);
    }

    private static (Coordinate Point, double Distance) PoleOf(Coordinate[][] rings, double? tolerance)
    {
        var segments = new List<(Coordinate A, Coordinate B)>();
        foreach (var ring in rings)
        {
            for (int i = 1; i < ring.Length; i++) segments.Add((ring[i - 1], ring[i]));
        }

        var index = new SpatialIndex<int>(segments.Select((s, i) => (
            new Envelope2D(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y)), i)));
        var shell = rings[0];
        var envelope = new Envelope2D(shell.Min(c => c.X), shell.Min(c => c.Y), shell.Max(c => c.X), shell.Max(c => c.Y));
        double precision = tolerance ?? Math.Max(envelope.Width, envelope.Height) / 1000;

        // the signed distance to the boundary: positive inside, negative outside
        double Signed(double x, double y)
        {
            var p = new Coordinate(x, y);
            var nearest = index.Nearest(p, 1, (i, q) => Segments.PointDistance(q, segments[i].A, segments[i].B))[0];
            double d = Segments.PointDistance(p, segments[nearest].A, segments[nearest].B);
            return Rings.LocateInPolygon(p, rings) switch
            {
                Location.Inside => d,
                Location.Boundary => 0,
                _ => -d,
            };
        }

        Cell Make(double x, double y, double half) => new(x, y, half, Signed(x, y));

        double size = Math.Min(envelope.Width, envelope.Height);
        if (size == 0)
        {
            size = Math.Max(envelope.Width, envelope.Height);
        }

        double halfSize = size / 2;
        var queue = new PriorityQueue<Cell, double>();
        for (double x = envelope.MinX; x < envelope.MaxX; x += size)
        {
            for (double y = envelope.MinY; y < envelope.MaxY; y += size)
            {
                var cell = Make(x + halfSize, y + halfSize, halfSize);
                queue.Enqueue(cell, -cell.Potential);
            }
        }

        var centre = envelope.Center;
        var best = Make(centre.X, centre.Y, 0);
        int budget = 5_000_000;
        while (queue.TryDequeue(out var cell, out _) && budget-- > 0)
        {
            if (cell.Distance > best.Distance)
            {
                best = cell;
            }

            if (cell.Potential - best.Distance <= precision)
            {
                continue;
            }

            double h = cell.Half / 2;
            foreach (var (dx, dy) in new[] { (-h, -h), (h, -h), (-h, h), (h, h) })
            {
                var child = Make(cell.X + dx, cell.Y + dy, h);
                queue.Enqueue(child, -child.Potential);
            }
        }

        return (new Coordinate(best.X, best.Y), best.Distance);
    }
}
