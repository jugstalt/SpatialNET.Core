using SpatialNET.Internal;

namespace SpatialNET;

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

        private Coordinate PointAt(int p, double d)
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
                    return len == 0 ? path[i] : Lerp(path[i - 1], path[i], (d - pos[i - 1]) / len);
                }
            }

            return path[^1];
        }

        /// <summary>The nearest point on the line to <paramref name="p"/> and its length; the first of equally near points.</summary>
        public (Coordinate Point, double Position) Nearest(Coordinate p)
        {
            var best = (Point: _paths[0][0], Position: _positions[0][0]);
            double bestDistance = double.PositiveInfinity;
            for (int k = 0; k < _paths.Count; k++)
            {
                var path = _paths[k];
                var pos = _positions[k];
                if (path.Length == 1)
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
                    var a = path[i - 1];
                    var b = path[i];
                    double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                    double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
                    var q = Lerp(a, b, t);
                    double distance = Squared(p, q);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (q, pos[i - 1] + t * (pos[i] - pos[i - 1]));
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
                    var point = Lerp(a, b, t) with { M = m };
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

        /// <summary>
        /// The point at parameter <paramref name="t"/> on a segment. The end points keep their own values; inside,
        /// Z and M are interpolated only if both ends carry them (ADR 0015).
        /// </summary>
        private static Coordinate Lerp(Coordinate a, Coordinate b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return new Coordinate(
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y),
                a.HasZ && b.HasZ ? a.Z + t * (b.Z - a.Z) : double.NaN,
                a.HasM && b.HasM ? a.M + t * (b.M - a.M) : double.NaN);
        }
    }
}
