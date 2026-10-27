using SpatialNET.Internal;

namespace SpatialNET;

// What can be seen from a point (ADR 0044): the visible part of the surface as a polygon, by a fan of rays.
public sealed partial class Triangulation
{
    private const int MaxRays = 100_000;

    /// <summary>
    /// The part of the surface that can be seen from a point (ADR 0044): a polygon (or several) of everything within <paramref name="maxDistance"/> of
    /// the observer that a target at <paramref name="targetHeight"/> above the ground can be seen at by an eye at <paramref name="observerHeight"/> above the
    /// ground at the observer. It is what <see cref="LineOfSight"/> asks for one target, asked for all of them: the line of sight to a point is free
    /// where the ground along the line does not rise above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fan of <paramref name="rays"/> rays leaves the observer at equal angles. Along every ray the surface is linear between the edges that the
    /// ray crosses, and the horizon (the steepest slope from the eye to the ground so far) is followed exactly: the visible stretches of the ray come
    /// out as exact intervals, not as a sample. The stretches of a ray stand for a slice of the angle around it, and the slices are united. So the
    /// edge of the visible area is exact along the rays and approximate between them: at a distance <c>r</c> it is accurate to about
    /// <c>r · π / rays</c>, so 360 rays give 1 % of the distance and 3600 give 0.1 %. A ridge that is narrower than a slice can slip through between two
    /// rays.
    /// </para>
    /// <para>
    /// A point outside the surface, or at a vertex without height, has nothing to see from; a stretch of a ray that leaves the surface (a notch of a
    /// polygon) has no ground that can be seen, but does not block the view. The cost is that of <paramref name="rays"/> lines over the surface
    /// (<see cref="Drape"/>) and the union of their slices, so it grows with the number of rays and with the distance, not with the size of the surface.
    /// </para>
    /// </remarks>
    /// <param name="observer">The place of the eye.</param>
    /// <param name="maxDistance">How far to look; finite and greater than 0.</param>
    /// <param name="observerHeight">The height of the eye above the ground (a person: 1.7).</param>
    /// <param name="targetHeight">The height of what is to be seen above the ground (0 for the ground itself).</param>
    /// <param name="rays">The number of rays, from 8 to 100,000.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The visible area; an empty polygon if nothing can be seen.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="observer"/>, <paramref name="maxDistance"/> or a height are not finite, <paramref name="maxDistance"/> is not greater than 0, or <paramref name="rays"/> is not from 8 to 100,000.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IPolygon2D Viewshed(Coordinate observer, double maxDistance, double observerHeight = 0, double targetHeight = 0, int rays = 360, IGeometryFactory? factory = null)
    {
        EnsurePoint(observer);
        if (!double.IsFinite(maxDistance) || maxDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "The distance must be finite and greater than 0.");
        }

        if (!double.IsFinite(observerHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(observerHeight), observerHeight, "The height must be finite.");
        }

        if (!double.IsFinite(targetHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), targetHeight, "The height must be finite.");
        }

        if (rays < 8 || rays > MaxRays)
        {
            throw new ArgumentOutOfRangeException(nameof(rays), rays, $"The number of rays must be from 8 to {MaxRays}.");
        }

        var f = GeometryFactory.Resolve(factory);
        double ground = InterpolateZ(observer);
        if (double.IsNaN(ground))
        {
            return f.CreatePolygon([], null);
        }

        double eye = ground + observerHeight;
        double step = 2 * Math.PI / rays;
        var visible = new List<(double From, double To)>[rays];
        Parallel.For(0, rays, k =>
        {
            double angle = k * step;
            var end = new Coordinate(observer.X + maxDistance * Math.Cos(angle), observer.Y + maxDistance * Math.Sin(angle));
            visible[k] = Snapped(VisibleAlong(ProfileOf(observer, end), eye, targetHeight), maxDistance * 1e-9);
        });

        // The slice around a ray stands from the angle half a step before to half a step after, for every visible stretch of the ray. Neighbouring slices
        // meet exactly along the angle between two rays, so no overlay is needed: what is left of the sides of a slice, where the neighbouring ray does
        // not see, and its inner and outer arcs make the boundary of the visible area, which is chained to rings with the area on the left.
        var vertices = new List<Coordinate>();
        var ids = new Dictionary<(int Angle, double Distance), int>();
        int Vertex(int angle, double distance)
        {
            // the observer is one vertex for every angle
            var key = distance == 0 ? (-1, 0d) : (((angle % rays) + rays) % rays, distance);
            if (!ids.TryGetValue(key, out int id))
            {
                id = vertices.Count;
                ids[key] = id;
                vertices.Add(distance == 0 ? observer : Polar(observer, distance, (angle - 0.5) * step));
            }

            return id;
        }

        var from = new List<int>();
        var to = new List<int>();
        void Edge(int u, int v)
        {
            if (u != v)
            {
                from.Add(u);
                to.Add(v);
            }
        }

        for (int k = 0; k < rays; k++)
        {
            var before = visible[(k + rays - 1) % rays];
            var after = visible[(k + 1) % rays];
            foreach (var (a, b) in visible[k])
            {
                // the side towards the smaller angle, outwards (the area is on its left), where the ray before does not see
                foreach (var (p, q) in Uncovered(a, b, before))
                {
                    Edge(Vertex(k, p), Vertex(k, q));
                }

                // the arc at the far end, to the larger angle, and the side towards the larger angle, inwards, where the ray after does not see
                Edge(Vertex(k, b), Vertex(k + 1, b));
                foreach (var (p, q) in Uncovered(a, b, after).AsEnumerable().Reverse())
                {
                    Edge(Vertex(k + 1, q), Vertex(k + 1, p));
                }

                // the arc at the near end, back to the smaller angle
                Edge(Vertex(k + 1, a), Vertex(k, a));
            }
        }

        return from.Count == 0 ? f.CreatePolygon([], null) : AreaFromEdges(vertices, from, to, f, repair: false);
    }

    /// <summary>
    /// The ends of the stretches rounded to a grid of the given size (a billionth of the distance): the same place seen along two rays, say the end of
    /// the distance or the edge of the surface, is the same number then, and not two that differ by a rounding error, which would make a sliver of the
    /// side between two slices.
    /// </summary>
    private static List<(double From, double To)> Snapped(List<(double From, double To)> stretches, double grid)
    {
        var result = new List<(double From, double To)>(stretches.Count);
        foreach (var (from, to) in stretches)
        {
            double a = Math.Round(from / grid) * grid, b = Math.Round(to / grid) * grid;
            if (result.Count > 0 && a <= result[^1].To)
            {
                result[^1] = (result[^1].From, Math.Max(result[^1].To, b));
            }
            else if (b > a)
            {
                result.Add((a, b));
            }
        }

        return result;
    }

    /// <summary>The parts of the stretch from <paramref name="a"/> to <paramref name="b"/> that none of the (sorted, disjoint) stretches covers.</summary>
    private static List<(double From, double To)> Uncovered(double a, double b, List<(double From, double To)> covered)
    {
        var parts = new List<(double From, double To)>();
        double position = a;
        foreach (var (from, to) in covered)
        {
            if (to <= position)
            {
                continue;
            }

            if (from >= b)
            {
                break;
            }

            if (from > position)
            {
                parts.Add((position, from));
            }

            position = Math.Max(position, to);
            if (position >= b)
            {
                break;
            }
        }

        if (position < b)
        {
            parts.Add((position, b));
        }

        return parts;
    }

    private static Coordinate Polar(Coordinate centre, double distance, double angle) =>
        new(centre.X + distance * Math.Cos(angle), centre.Y + distance * Math.Sin(angle));

    /// <summary>
    /// The stretches of a ray (distances from the observer) at which a target can be seen, from the height profile of the ray. The horizon, the
    /// steepest slope from the eye to the ground so far, is followed along the piecewise linear profile, where both the slope to the ground and the
    /// slope to a target above it are monotonic between two vertices, so every boundary of a stretch is the solution of a linear equation.
    /// </summary>
    internal static List<(double From, double To)> VisibleAlong(List<List<Coordinate>> profile, double eye, double targetHeight)
    {
        var stretches = new List<(double From, double To)>();
        double horizon = double.NegativeInfinity;

        void Add(double from, double to)
        {
            if (!(to > from))
            {
                return;
            }

            if (stretches.Count > 0 && from <= stretches[^1].To + 1e-12 * Math.Max(1, to))
            {
                stretches[^1] = (stretches[^1].From, Math.Max(stretches[^1].To, to));
            }
            else
            {
                stretches.Add((from, to));
            }
        }

        foreach (var path in profile)
        {
            for (int j = 0; j + 1 < path.Count; j++)
            {
                double da = path[j].M, za = path[j].Z, db = path[j + 1].M, zb = path[j + 1].Z;
                if (!(db > da) || double.IsNaN(za) || double.IsNaN(zb))
                {
                    continue;
                }

                // along the segment the ground is z = za + s · (d − da); the slope from the eye to it is s + cg / d, to a target above it s + cf / d
                double s = (zb - za) / (db - da);
                double cg = za - s * da - eye, cf = cg + targetHeight;
                double slopeAtStart = da > 0 ? s + cg / da : cg < 0 ? double.NegativeInfinity : cg > 0 ? double.PositiveInfinity : s;
                double slopeAtEnd = s + cg / db;

                // the ground at the start is part of the horizon from here on; where the ground comes out above it again (its slope rises: cg < 0),
                // the ground itself is the new horizon and is seen; before that, and where it falls (cg > 0), it lies below the horizon, and a
                // target above it is seen where its slope is not below the horizon
                double m = Math.Max(horizon, slopeAtStart);
                double belowEnd;
                if (double.IsNegativeInfinity(m))
                {
                    belowEnd = da;
                }
                else if (cg < 0)
                {
                    belowEnd = slopeAtStart >= m ? da : slopeAtEnd >= m ? Math.Clamp(cg / (m - s), da, db) : db;
                }
                else if (cg == 0)
                {
                    belowEnd = s >= m ? da : db;
                }
                else
                {
                    belowEnd = db;
                }

                if (belowEnd > da)
                {
                    double r = m - s;
                    if (r > 0)
                    {
                        Add(da, Math.Min(belowEnd, cf / r));   // the slope to the target falls with the distance: seen up to where it meets the horizon
                    }
                    else if (r < 0)
                    {
                        Add(Math.Max(da, cf / r), belowEnd);
                    }
                    else if (cf >= 0)
                    {
                        Add(da, belowEnd);
                    }
                }

                Add(belowEnd, db);
                horizon = Math.Max(m, slopeAtEnd);
            }
        }

        return stretches;
    }
}
