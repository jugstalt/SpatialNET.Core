using SpatialNET.Internal;

namespace SpatialNET;

/// <summary>What <see cref="LinearReferencing.CalibrateM(IPolyline2D, IEnumerable{Coordinate}, double, MExtrapolation, IGeometryFactory?)"/> gives the line before its first and after its last anchor (ADR 0030).</summary>
public enum MExtrapolation
{
    /// <summary>No M: that stretch cannot answer, as for any line without M (ADR 0017).</summary>
    None,

    /// <summary>The M of the nearest anchor.</summary>
    Clamp,

    /// <summary>M continues with the slope of the first or last interval between two anchors.</summary>
    Linear,
}

/// <summary>
/// Linear referencing on polylines (ADR 0017): a position on a line is addressed by its length along the
/// line or by its M value (ADR 0015). Lengths are planar (like <see cref="Measures.Length(IPolyline2D)"/>); the
/// paths of a polyline are laid end to end in their order. Point-like and scalar results are values, only
/// the pieces of <c>Substring</c> and <c>SubstringByM</c> are created through the factory.
/// </summary>
/// <remarks>
/// The <c>...ByM</c> variants use only segments whose both ends carry M and answer with the first hit along
/// the line, so non-monotonic M is allowed. Empty or degenerate lines never throw; they answer
/// <c>null</c>, <see cref="double.NaN"/> or an empty polyline.
/// </remarks>
public static class LinearReferencing
{
    /// <summary>
    /// The point at <paramref name="distance"/> along the line, with Z and M interpolated on the segment.
    /// The distance is clamped to <c>[0, length]</c>.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="distance">The length from the start of the line.</param>
    /// <returns>The point, or <c>null</c> for a line without points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is <c>NaN</c> or infinite.</exception>
    public static Coordinate? Interpolate(this IPolyline2D line, double distance)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(distance, nameof(distance));
        var index = new LineIndex(line);
        return index.IsEmpty ? null : index.PointAt(Math.Clamp(distance, 0, index.Length));
    }

    /// <summary>
    /// The length along the line of the point nearest to <paramref name="point"/>; among equally near points
    /// the smallest length.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="point">The point.</param>
    /// <returns>The length, or <see cref="double.NaN"/> for a line without points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public static double Project(this IPolyline2D line, Coordinate point)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsurePoint(point);
        var index = new LineIndex(line);
        return index.IsEmpty ? double.NaN : index.Nearest(point).Position;
    }

    /// <inheritdoc cref="Project(IPolyline2D, Coordinate)"/>
    public static double Project(this IPolyline2D line, IPoint2D point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return line.Project(point.ToCoordinate());
    }

    /// <summary>
    /// The length along the line of the point nearest to <paramref name="point"/> on the part of the line between two
    /// lengths (ADR 0031). It tells apart the passes of a line that crosses itself or doubles back: restrict the search
    /// to the stretch where the point is expected. The result is a length on the whole line.
    /// </summary>
    /// <remarks>
    /// Both lengths are clamped to <c>[0, length]</c>; if <paramref name="from"/> is greater than <paramref name="to"/> they are
    /// swapped (the direction does not matter for a search). Among equally near points the smallest length wins. For
    /// "after a position" pass the line's <c>Length()</c> as <paramref name="to"/>.
    /// </remarks>
    /// <param name="line">The polyline.</param>
    /// <param name="point">The point.</param>
    /// <param name="from">The length where the search window starts.</param>
    /// <param name="to">The length where the search window ends.</param>
    /// <returns>The length, or <see cref="double.NaN"/> for a line without points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite, or <paramref name="from"/> or <paramref name="to"/> is <c>NaN</c> or infinite.</exception>
    public static double Project(this IPolyline2D line, Coordinate point, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsurePoint(point);
        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        var index = new LineIndex(line);
        if (index.IsEmpty)
        {
            return double.NaN;
        }

        var (a, b) = Window(index, from, to);
        return index.Nearest(point, a, b).Position;
    }

    /// <inheritdoc cref="Project(IPolyline2D, Coordinate, double, double)"/>
    public static double Project(this IPolyline2D line, IPoint2D point, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(point);
        return line.Project(point.ToCoordinate(), from, to);
    }

    /// <summary>
    /// The M value at the point nearest to <paramref name="point"/> on the part of the line between two lengths: the
    /// M of <c>Project(point, from, to)</c> (ADR 0031).
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="point">The point.</param>
    /// <param name="from">The length where the search window starts.</param>
    /// <param name="to">The length where the search window ends.</param>
    /// <returns>The M value, or <see cref="double.NaN"/> if the line has no M there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite, or <paramref name="from"/> or <paramref name="to"/> is <c>NaN</c> or infinite.</exception>
    public static double ProjectToM(this IPolyline2D line, Coordinate point, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsurePoint(point);
        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        var index = new LineIndex(line);
        if (index.IsEmpty)
        {
            return double.NaN;
        }

        var (a, b) = Window(index, from, to);
        return index.Nearest(point, a, b).Point.M;
    }

    /// <inheritdoc cref="ProjectToM(IPolyline2D, Coordinate, double, double)"/>
    public static double ProjectToM(this IPolyline2D line, IPoint2D point, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(point);
        return line.ProjectToM(point.ToCoordinate(), from, to);
    }

    private static (double From, double To) Window(LineIndex index, double from, double to)
    {
        double a = Math.Clamp(from, 0, index.Length), b = Math.Clamp(to, 0, index.Length);
        return a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// The piece of the line between two lengths, one path per touched path. Both lengths are clamped; if
    /// <paramref name="from"/> is greater than <paramref name="to"/> the piece runs in the opposite direction.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="from">The length where the piece starts.</param>
    /// <param name="to">The length where the piece ends.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The piece; an empty polyline if it has no length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D Substring(this IPolyline2D line, double from, double to, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        var f = GeometryFactory.Resolve(factory);
        return Piece(new LineIndex(line), from, to, f, line);
    }

    /// <summary>The point where the line first reaches the M value, with Z and M interpolated on the segment.</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="m">The M value.</param>
    /// <returns>The point, or <c>null</c> if no segment with M at both ends contains the value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="m"/> is <c>NaN</c> or infinite.</exception>
    public static Coordinate? InterpolateByM(this IPolyline2D line, double m)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(m, nameof(m));
        return new LineIndex(line).FirstHit(m)?.Point;
    }

    /// <summary>The M value at the point of the line nearest to <paramref name="point"/> (the point of <c>Project</c>).</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="point">The point.</param>
    /// <returns>The M value, or <see cref="double.NaN"/> if the line has no M there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public static double ProjectToM(this IPolyline2D line, Coordinate point)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsurePoint(point);
        var index = new LineIndex(line);
        return index.IsEmpty ? double.NaN : index.Nearest(point).Point.M;
    }

    /// <inheritdoc cref="ProjectToM(IPolyline2D, Coordinate)"/>
    public static double ProjectToM(this IPolyline2D line, IPoint2D point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return line.ProjectToM(point.ToCoordinate());
    }

    /// <summary>The piece of the line between the first hits of two M values (see <c>Substring</c>).</summary>
    /// <param name="line">The polyline.</param>
    /// <param name="fromM">The M value where the piece starts.</param>
    /// <param name="toM">The M value where the piece ends.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The piece; an empty polyline if one of the values has no hit or the piece has no length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fromM"/> or <paramref name="toM"/> is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D SubstringByM(this IPolyline2D line, double fromM, double toM, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(fromM, nameof(fromM));
        EnsureFinite(toM, nameof(toM));
        var f = GeometryFactory.Resolve(factory);
        var index = new LineIndex(line);
        return index.FirstHit(fromM) is { } a && index.FirstHit(toM) is { } b
            ? Piece(index, a.Position, b.Position, f, line)
            : f.CreatePolyline([], line);
    }

    /// <summary>
    /// Gives a polyline M values from anchor points that carry M: every anchor is projected onto the line, a vertex
    /// is inserted at its position, and M is interpolated linearly in the length along the line between the anchors
    /// (ADR 0030). With the result, <see cref="ProjectToM(IPolyline2D, Coordinate)"/> answers what interpolating
    /// between the previous and the next anchor would.
    /// </summary>
    /// <remarks>
    /// M of the line itself is ignored, Z is kept (and interpolated at inserted vertices). The anchors are sorted by
    /// their position on the line; M need not be monotonic. Anchors at the same position (within 1e-9 of the line
    /// length) are merged if their M is equal. Where the nearest point of an anchor is not unique (a line that
    /// crosses itself) the smallest length is used, as in <see cref="Project(IPolyline2D, Coordinate)"/>.
    /// </remarks>
    /// <param name="line">The polyline, usually without M.</param>
    /// <param name="anchors">Points with X, Y and M, in any order.</param>
    /// <param name="maxSnapDistance">The largest distance of an anchor from the line, in map units.</param>
    /// <param name="extrapolation">What M the line gets before the first and after the last anchor.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The polyline with M; an empty polyline for a line without points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> or <paramref name="anchors"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSnapDistance"/> is negative, <c>NaN</c> or infinite, or the X or Y of an anchor is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// There is no anchor; an anchor has no finite M or is farther from the line than <paramref name="maxSnapDistance"/>;
    /// two anchors at the same position have different M; <see cref="MExtrapolation.Linear"/> with anchors at fewer than two positions.
    /// </exception>
    /// <exception cref="InvalidOperationException">No factory available, or it does not support M.</exception>
    public static IPolyline2D CalibrateM(this IPolyline2D line, IEnumerable<Coordinate> anchors, double maxSnapDistance,
        MExtrapolation extrapolation = MExtrapolation.None, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(anchors);
        if (!double.IsFinite(maxSnapDistance) || maxSnapDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSnapDistance), maxSnapDistance, "The distance must be finite and not negative.");
        }

        if (!Enum.IsDefined(extrapolation))
        {
            throw new ArgumentOutOfRangeException(nameof(extrapolation), extrapolation, "Unknown extrapolation.");
        }

        var f = GeometryFactory.Resolve(factory);
        if (!f.SupportsM)
        {
            throw new InvalidOperationException("The factory does not support M values.");
        }

        var list = anchors as IReadOnlyList<Coordinate> ?? anchors.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(anchors), a, $"The coordinates of anchor {i} must be finite.");
            }

            if (!double.IsFinite(a.M))
            {
                throw new ArgumentException($"Anchor {i} has no finite M.", nameof(anchors));
            }
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("At least one anchor is needed.", nameof(anchors));
        }

        var index = new LineIndex(line);
        if (index.IsEmpty)
        {
            return f.CreatePolyline([], line);
        }

        double eps = 1e-9 * Math.Max(1, index.Length);
        var located = new List<(double Position, double M)>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var hit = index.Nearest(list[i]);
            double dx = hit.Point.X - list[i].X, dy = hit.Point.Y - list[i].Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > maxSnapDistance)
            {
                throw new ArgumentException($"Anchor {i} is {distance} from the line, more than the allowed {maxSnapDistance}.", nameof(anchors));
            }

            located.Add((index.SnapToVertex(hit.Position, eps), list[i].M));
        }

        located.Sort((a, b) => a.Position.CompareTo(b.Position));
        var stations = new List<(double Position, double M)>();
        foreach (var l in located)
        {
            if (stations.Count > 0 && l.Position - stations[^1].Position <= eps)
            {
                if (stations[^1].M != l.M)
                {
                    throw new ArgumentException($"Two anchors at the length {l.Position} have different M ({stations[^1].M} and {l.M}).", nameof(anchors));
                }

                continue;
            }

            stations.Add(l);
        }

        if (extrapolation == MExtrapolation.Linear && stations.Count < 2)
        {
            throw new ArgumentException("Linear extrapolation needs anchors at two different positions.", nameof(anchors));
        }

        var paths = new List<ReadOnlyMemory<Coordinate>>(index.PathCount);
        for (int p = 0; p < index.PathCount; p++)
        {
            var path = index.PathAt(p);
            var pos = index.PositionsAt(p);
            var result = new List<Coordinate>(path.Length + stations.Count);
            for (int i = 0; i < path.Length; i++)
            {
                if (i > 0)
                {
                    foreach (var s in stations)
                    {
                        if (s.Position > pos[i - 1] && s.Position < pos[i])
                        {
                            result.Add(index.PointAt(p, s.Position) with { M = s.M });
                        }
                    }
                }

                result.Add(path[i] with { M = MAt(stations, pos[i], extrapolation) });
            }

            paths.Add(GeometryAccess.Restrict(result.ToArray(), f));
        }

        return f.CreatePolyline(paths.ToArray(), line);
    }

    /// <inheritdoc cref="CalibrateM(IPolyline2D, IEnumerable{Coordinate}, double, MExtrapolation, IGeometryFactory?)"/>
    /// <remarks>The anchors are read through <c>IPointM</c>; a point that does not implement it has no M, which is an error.</remarks>
    public static IPolyline2D CalibrateM(this IPolyline2D line, IEnumerable<IPoint2D> anchors, double maxSnapDistance,
        MExtrapolation extrapolation = MExtrapolation.None, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        return line.CalibrateM(anchors.Select(a => a.ToCoordinate()), maxSnapDistance, extrapolation, factory);
    }

    /// <summary>M at a length from anchors sorted by distinct position, or <see cref="double.NaN"/> where there is none.</summary>
    private static double MAt(List<(double Position, double M)> stations, double d, MExtrapolation extrapolation)
    {
        var first = stations[0];
        var last = stations[^1];
        if (d < first.Position || d > last.Position)
        {
            return extrapolation switch
            {
                MExtrapolation.Clamp => d < first.Position ? first.M : last.M,
                MExtrapolation.Linear => d < first.Position ? Extend(first, stations[1], d) : Extend(last, stations[^2], d),
                _ => double.NaN,
            };
        }

        for (int i = 1; i < stations.Count; i++)
        {
            if (d <= stations[i].Position)
            {
                var a = stations[i - 1];
                var b = stations[i];
                return d == b.Position ? b.M : a.M + (b.M - a.M) / (b.Position - a.Position) * (d - a.Position);
            }
        }

        return first.M; // a single anchor: only its own position is inside the range
    }

    private static double Extend((double Position, double M) from, (double Position, double M) other, double d) =>
        from.M + (other.M - from.M) / (other.Position - from.Position) * (d - from.Position);

    /// <summary>
    /// All stretches of the line where M lies between two values (closed interval), as one polyline: the stretches
    /// of consecutive segments form one path, a gap starts a new one (ADR 0032). Unlike <see cref="SubstringByM"/>, which
    /// follows the first hit of two values, this finds every stretch, also where M is not monotonic.
    /// </summary>
    /// <remarks>
    /// Only segments with M at both ends take part (as in ADR 0017). M is interpolated linearly on a segment, the cut ends get
    /// exactly the bound as M and an interpolated Z; original vertices keep their own values, and the direction of the
    /// line is kept. Stretches without length (M touches the interval only at a point) are not part of the result;
    /// <see cref="LocateAlong"/> finds such points. If <paramref name="fromM"/> is greater than <paramref name="toM"/> they are swapped.
    /// </remarks>
    /// <param name="line">The polyline.</param>
    /// <param name="fromM">One bound of the M interval.</param>
    /// <param name="toM">The other bound of the M interval.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The stretches; an empty polyline if there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fromM"/> or <paramref name="toM"/> is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IPolyline2D LocateBetween(this IPolyline2D line, double fromM, double toM, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(fromM, nameof(fromM));
        EnsureFinite(toM, nameof(toM));
        var f = GeometryFactory.Resolve(factory);
        double lo = Math.Min(fromM, toM), hi = Math.Max(fromM, toM);
        var index = new LineIndex(line);
        var result = new List<ReadOnlyMemory<Coordinate>>();
        for (int p = 0; p < index.PathCount; p++)
        {
            var path = index.PathAt(p);
            List<Coordinate>? current = null;

            void Close()
            {
                if (current is { Count: >= 2 })
                {
                    result.Add(GeometryAccess.Restrict(current.ToArray(), f));
                }

                current = null;
            }

            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                if (a.X == b.X && a.Y == b.Y)
                {
                    continue; // no length, nothing to locate
                }

                if (!a.HasM || !b.HasM)
                {
                    Close();
                    continue;
                }

                double t0, t1, enterM = lo, exitM = hi;
                if (a.M == b.M)
                {
                    (t0, t1) = a.M < lo || a.M > hi ? (1, 0) : (0, 1);
                }
                else
                {
                    bool rising = b.M > a.M;
                    double tLo = (lo - a.M) / (b.M - a.M), tHi = (hi - a.M) / (b.M - a.M);
                    t0 = Math.Max(0, Math.Min(tLo, tHi));
                    t1 = Math.Min(1, Math.Max(tLo, tHi));
                    (enterM, exitM) = rising ? (lo, hi) : (hi, lo);
                }

                if (t0 >= t1)
                {
                    Close();
                    continue;
                }

                if (current is not null && t0 > 0)
                {
                    Close();
                }

                current ??= [t0 > 0 ? Segments.Lerp(a, b, t0) with { M = enterM } : a];
                current.Add(t1 < 1 ? Segments.Lerp(a, b, t1) with { M = exitM } : b);
                if (t1 < 1)
                {
                    Close();
                }
            }

            Close();
        }

        return f.CreatePolyline(result.ToArray(), line);
    }

    /// <summary>
    /// Every point of the line where M has the value, in the order along the line (ADR 0032): a hit at a vertex shared
    /// by two segments counts once, and a segment with constant M equal to the value answers with its start, as
    /// <see cref="InterpolateByM"/> does. Unlike <c>InterpolateByM</c>, which gives the first hit, this finds them all.
    /// </summary>
    /// <param name="line">The polyline.</param>
    /// <param name="m">The M value.</param>
    /// <param name="factory">The factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <returns>The points with Z and M interpolated on the segment; an empty multipoint if there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="m"/> is <c>NaN</c> or infinite.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public static IMultiPoint2D LocateAlong(this IPolyline2D line, double m, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureFinite(m, nameof(m));
        var f = GeometryFactory.Resolve(factory);
        var index = new LineIndex(line);
        var hits = new List<Coordinate>();
        for (int p = 0; p < index.PathCount; p++)
        {
            var path = index.PathAt(p);
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                if (!a.HasM || !b.HasM || (a.X == b.X && a.Y == b.Y) || m < Math.Min(a.M, b.M) || m > Math.Max(a.M, b.M))
                {
                    continue;
                }

                var hit = (a.M == b.M ? a : Segments.Lerp(a, b, (m - a.M) / (b.M - a.M))) with { M = m };
                if (hits.Count == 0 || hits[^1].X != hit.X || hits[^1].Y != hit.Y)
                {
                    hits.Add(hit);
                }
            }
        }

        return f.CreateMultiPoint(GeometryAccess.Restrict(hits.ToArray(), f), line);
    }

    /// <summary>
    /// The ranges of M along a route where a line or an area lies: the reverse of <see cref="LocateBetween"/>, from a
    /// geometry to what a route system stores (ADR 0034). A parcel next to a road becomes "from 4200 to 4900".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The feature is widened by <paramref name="tolerance"/> (a buffer) and cut with the route; the M values of the pieces of the
    /// route inside it are the result. A piece gives <c>(lowest M, highest M)</c> of its vertices, which is the M that it covers since
    /// M is interpolated linearly on a segment (ADR 0017). Stretches of the route without M cannot answer and are left out; a piece
    /// without length (the feature touches the route at one point) is not a range. Ranges that touch or overlap are joined, so a route
    /// that passes the feature twice gives two ranges only if they are apart in M.
    /// </para>
    /// <para>
    /// The M values at the ends of a range are interpolated on the grid of the overlay (ADR 0005), so they are exact to about its size.
    /// </para>
    /// <para>
    /// A point has an M value, not a range: use <see cref="ProjectToM(IPolyline2D, Coordinate)"/> and <c>IsWithinDistance</c>.
    /// </para>
    /// </remarks>
    /// <param name="route">The calibrated route, a polyline with M.</param>
    /// <param name="feature">A polyline or a polygon.</param>
    /// <param name="tolerance">How far from the feature the route still counts, in map units. For a polygon 0 is allowed (the area itself); for a line it must be greater than 0.</param>
    /// <param name="factory">The factory for the intermediate geometries; <c>null</c> = <see cref="GeometryFactory.Default"/>. It must support M.</param>
    /// <returns>The ranges, sorted; empty if there is none or the route has no M there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> or <paramref name="feature"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative, <c>NaN</c> or infinite, or 0 for a line.</exception>
    /// <exception cref="ArgumentException"><paramref name="feature"/> is a point, a multipoint or not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available, or it does not support M.</exception>
    public static IReadOnlyList<(double From, double To)> LocateFeature(this IPolyline2D route, IGeometry2D feature, double tolerance,
        IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(feature);
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be finite and not negative.");
        }

        var f = GeometryFactory.Resolve(factory);
        if (!f.SupportsM)
        {
            throw new InvalidOperationException("The factory does not support M values.");
        }

        IPolygon2D zone = feature switch
        {
            IPolygon2D polygon => polygon.Buffer(tolerance, null, f),
            IPolyline2D line when tolerance > 0 => line.Buffer(tolerance, null, f),
            IPolyline2D => throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "A line needs a tolerance greater than 0."),
            IPoint2D or IMultiPoint2D => throw new ArgumentException("A point has an M value, not a range: use ProjectToM.", nameof(feature)),
            _ => throw new ArgumentException("The feature is not a known geometry kind.", nameof(feature)),
        };

        var ranges = new List<(double From, double To)>();
        if (zone.RingCount == 0 || new LineIndex(route).IsEmpty)
        {
            return ranges;
        }

        var pieces = route.Intersection(zone, FillRule.EvenOdd, f);
        for (int p = 0; p < pieces.PathCount; p++)
        {
            var path = Rings.Coordinates(pieces[p]);
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            int count = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i < path.Length && path[i].HasM)
                {
                    lo = Math.Min(lo, path[i].M);
                    hi = Math.Max(hi, path[i].M);
                    count++;
                    continue;
                }

                if (count >= 2 && hi > lo)
                {
                    ranges.Add((lo, hi));
                }

                lo = double.PositiveInfinity;
                hi = double.NegativeInfinity;
                count = 0;
            }
        }

        ranges.Sort((a, b) => a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));
        var joined = new List<(double From, double To)>();
        foreach (var r in ranges)
        {
            if (joined.Count > 0 && r.From <= joined[^1].To)
            {
                joined[^1] = (joined[^1].From, Math.Max(joined[^1].To, r.To));
            }
            else
            {
                joined.Add(r);
            }
        }

        return joined;
    }

    private static IPolyline2D Piece(LineIndex index, double from, double to, IGeometryFactory f, IPolyline2D source)
    {
        if (index.IsEmpty)
        {
            return f.CreatePolyline([], source);
        }

        double a = Math.Clamp(from, 0, index.Length), b = Math.Clamp(to, 0, index.Length);
        bool reverse = a > b;
        var paths = index.Piece(Math.Min(a, b), Math.Max(a, b));
        if (reverse)
        {
            paths.Reverse();
            foreach (var path in paths)
            {
                path.Reverse();
            }
        }

        return f.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)GeometryAccess.Restrict(p.ToArray(), f)).ToArray(), source);
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }
    }

    private static void EnsurePoint(Coordinate point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "The coordinates of the point must be finite.");
        }
    }

    /// <summary>The paths of a polyline with the length at every vertex (paths laid end to end).</summary>
    private sealed class LineIndex
    {
        private readonly List<Coordinate[]> _paths = [];
        private readonly List<double[]> _positions = [];

        public LineIndex(IPolyline2D line)
        {
            double offset = 0;
            for (int i = 0; i < line.PathCount; i++)
            {
                var path = Rings.Coordinates(line[i]);
                if (path.Length == 0)
                {
                    continue;
                }

                var positions = new double[path.Length];
                positions[0] = offset;
                for (int k = 1; k < path.Length; k++)
                {
                    double dx = path[k].X - path[k - 1].X, dy = path[k].Y - path[k - 1].Y;
                    positions[k] = positions[k - 1] + Math.Sqrt(dx * dx + dy * dy);
                }

                offset = positions[^1];
                _paths.Add(path);
                _positions.Add(positions);
            }

            Length = offset;
        }

        public bool IsEmpty => _paths.Count == 0;

        public int PathCount => _paths.Count;

        public Coordinate[] PathAt(int p) => _paths[p];

        public double[] PositionsAt(int p) => _positions[p];

        /// <summary>The length of a vertex within <paramref name="eps"/> of <paramref name="position"/>, or the position itself.</summary>
        public double SnapToVertex(double position, double eps)
        {
            foreach (var positions in _positions)
            {
                foreach (var v in positions)
                {
                    if (Math.Abs(v - position) <= eps)
                    {
                        return v;
                    }
                }
            }

            return position;
        }

        public double Length { get; }

        /// <summary>The point at a length in <c>[0, Length]</c>; at the boundary of two paths the end of the earlier one.</summary>
        public Coordinate PointAt(double d)
        {
            for (int p = 0; p < _paths.Count; p++)
            {
                if (d <= _positions[p][^1] || p == _paths.Count - 1)
                {
                    return PointAt(p, d);
                }
            }

            throw new InvalidOperationException();
        }

        /// <summary>The point at a length within one path.</summary>
        public Coordinate PointAt(int p, double d)
        {
            var path = _paths[p];
            var pos = _positions[p];
            if (path.Length == 1 || d <= pos[0])
            {
                return path[0];
            }

            for (int i = 1; i < path.Length; i++)
            {
                if (d <= pos[i])
                {
                    double len = pos[i] - pos[i - 1];
                    return len == 0 ? path[i] : Segments.Lerp(path[i - 1], path[i], (d - pos[i - 1]) / len);
                }
            }

            return path[^1];
        }

        /// <summary>The nearest point on the line to <paramref name="p"/> and its length; the first of equally near points.</summary>
        public (Coordinate Point, double Position) Nearest(Coordinate p) => Nearest(p, 0, Length);

        /// <summary>
        /// The nearest point to <paramref name="p"/> on the part of the line between the lengths <paramref name="from"/> and
        /// <paramref name="to"/> (<paramref name="from"/> &lt;= <paramref name="to"/>, within <c>[0, Length]</c>), and its length on the whole
        /// line; the first of equally near points.
        /// </summary>
        public (Coordinate Point, double Position) Nearest(Coordinate p, double from, double to)
        {
            var best = (Point: PointAt(from), Position: from);
            double bestDistance = double.PositiveInfinity;
            for (int k = 0; k < _paths.Count; k++)
            {
                var path = _paths[k];
                var pos = _positions[k];
                if (path.Length == 1 && pos[0] >= from && pos[0] <= to)
                {
                    double d0 = Squared(p, path[0]);
                    if (d0 < bestDistance)
                    {
                        bestDistance = d0;
                        best = (path[0], pos[0]);
                    }
                }

                for (int i = 1; i < path.Length; i++)
                {
                    if (pos[i] < from || pos[i - 1] > to)
                    {
                        continue;
                    }

                    var a = path[i - 1];
                    var b = path[i];
                    double len = pos[i] - pos[i - 1];
                    double tLo = len == 0 || from <= pos[i - 1] ? 0 : (from - pos[i - 1]) / len;
                    double tHi = len == 0 || to >= pos[i] ? 1 : (to - pos[i - 1]) / len;
                    double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                    double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, tLo, tHi);
                    var q = Segments.Lerp(a, b, t);
                    double distance = Squared(p, q);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (q, pos[i - 1] + t * len);
                    }
                }
            }

            return best;
        }

        /// <summary>The first position along the line where M reaches the value, using only segments with M at both ends.</summary>
        public (Coordinate Point, double Position)? FirstHit(double m)
        {
            for (int k = 0; k < _paths.Count; k++)
            {
                var path = _paths[k];
                var pos = _positions[k];
                for (int i = 1; i < path.Length; i++)
                {
                    var a = path[i - 1];
                    var b = path[i];
                    double len = pos[i] - pos[i - 1];
                    if (!a.HasM || !b.HasM || len == 0 || m < Math.Min(a.M, b.M) || m > Math.Max(a.M, b.M))
                    {
                        continue;
                    }

                    double t = a.M == b.M ? 0 : (m - a.M) / (b.M - a.M);
                    var point = Segments.Lerp(a, b, t) with { M = m };
                    return (point, pos[i - 1] + t * len);
                }
            }

            return null;
        }

        /// <summary>The piece between two lengths (<paramref name="from"/> &lt; <paramref name="to"/>), one path per touched path.</summary>
        public List<List<Coordinate>> Piece(double from, double to)
        {
            var result = new List<List<Coordinate>>();
            for (int p = 0; p < _paths.Count; p++)
            {
                var path = _paths[p];
                var pos = _positions[p];
                double start = Math.Max(from, pos[0]), end = Math.Min(to, pos[^1]);
                if (end <= start)
                {
                    continue;
                }

                var piece = new List<Coordinate> { PointAt(p, start) };
                for (int i = 0; i < path.Length; i++)
                {
                    if (pos[i] > start && pos[i] < end)
                    {
                        piece.Add(path[i]);
                    }
                }

                piece.Add(PointAt(p, end));
                result.Add(piece);
            }

            return result;
        }

        private static double Squared(Coordinate a, Coordinate b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
