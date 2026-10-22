using SpatialNET.Internal;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

/// <summary>
/// A geometry prepared for repeated predicates (ADR 0013): a snapshot of the coordinates plus a
/// segment index. Later changes to the host object have no effect. Results are identical to the
/// unprepared predicates; fast paths exist for <see cref="Intersects"/>/<see cref="Disjoint"/>
/// and point-in-area, everything else goes through the exact <see cref="Relations.Relate(IGeometry2D, IGeometry2D)"/>.
/// </summary>
/// <remarks>Not thread-safe; prepare one instance per thread.</remarks>
public sealed class PreparedGeometry
{
    private readonly Envelope2D _envelope;
    private readonly bool _isArea;
    private readonly bool _isEmpty;
    private readonly SegmentIndex _index;
    private readonly List<Coordinate[]> _rings = [];

    internal PreparedGeometry(IGeometry2D geometry)
    {
        Geometry = Snapshot.Of(geometry);
        _envelope = Geometry.Envelope();
        _isEmpty = GeometryAccess.IsEmpty(Geometry);
        _isArea = Geometry is IPolygon2D;
        if (Geometry is IPolygon2D polygon)
        {
            for (int i = 0; i < polygon.RingCount; i++)
            {
                if (polygon[i].PointCount > 0) _rings.Add(Rings.Closed(polygon[i]));
            }
        }

        // Flächenlose Ringe tragen nach ADR 0003 nichts bei und kommen nicht in den Index.
        var segments = Geometry is IPolygon2D
            ? _rings.Where(r => !Rings.IsDegenerate(r)).SelectMany(r => r.Zip(r.Skip(1))).ToList()
            : GeometryAccess.Segments(Geometry).ToList();
        _index = new SegmentIndex(segments, _envelope);
    }

    /// <summary>The snapshot of the prepared geometry.</summary>
    public IGeometry2D Geometry { get; }

    /// <summary>
    /// Like <see cref="Locations.Locate(IGeometry2D, Coordinate)"/>, but for many points against the same geometry: a polygon uses the
    /// segment index (ADR 0022).
    /// </summary>
    /// <param name="point">The point; its coordinates must be finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates of <paramref name="point"/> are not finite.</exception>
    public Location Locate(Coordinate point)
    {
        Locations.EnsureFinite(point);
        if (_isEmpty || !_envelope.Contains(point))
        {
            return Location.Outside;
        }

        return _isArea ? LocateInArea(point) : Locations.LocateCore(Geometry, point);
    }

    /// <summary>Like <see cref="Relations.Intersects"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Intersects(IGeometry2D other) => !Disjoint(other);

    /// <summary>Like <see cref="Relations.Disjoint"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Disjoint(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Overlaps(other.Envelope()))
        {
            return true;
        }

        // Schneiden sich Segmente (inkl. Punkte als Segmente der Länge 0), ist das Ergebnis sicher.
        var otherSegments = other is IPolygon2D poly
            ? Enumerable.Range(0, poly.RingCount).Select(i => Rings.Closed(poly[i]))
                .Where(r => r.Length > 0 && !Rings.IsDegenerate(r)).SelectMany(r => r.Zip(r.Skip(1)))
            : GeometryAccess.Segments(other);
        foreach (var (a, b) in otherSegments)
        {
            foreach (int i in _index.Query(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)))
            {
                var (p, q) = _index[i];
                if (Segments.Intersects(p, q, a, b))
                {
                    return false;
                }
            }
        }

        // Keine Randberührung: dann nur noch Enthaltensein eines ganzen Teils.
        if (_isArea && OtherComponentPoints(other).Any(p => LocateInArea(p) != Location.Outside))
        {
            return false;
        }

        // Liegt die vorbereitete Geometrie ganz in der Fläche der anderen? Ohne Randberührung genügt je Teil ein Punkt.
        if (other is IPolygon2D otherPolygon)
        {
            var otherRings = Enumerable.Range(0, otherPolygon.RingCount)
                .Where(i => otherPolygon[i].PointCount > 0)
                .Select(i => Rings.Closed(otherPolygon[i]))
                .ToList();
            foreach (var p in ComponentPoints())
            {
                if (Rings.LocateInPolygon(p, otherRings) != Location.Outside) return false;
            }
        }

        return true;
    }

    /// <summary>Like <see cref="Relations.Contains"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Contains(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Includes(other.Envelope())) return false;
        if (_isArea && other is IPoint2D p) return LocateInArea(p.ToCoordinate()) == Location.Inside;
        return Geometry.Contains(other);
    }

    /// <summary>Like <see cref="Relations.Covers"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Covers(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Includes(other.Envelope())) return false;
        if (_isArea && other is IPoint2D p) return LocateInArea(p.ToCoordinate()) != Location.Outside;
        return Geometry.Covers(other);
    }

    /// <summary>Like <see cref="Relations.Within"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Within(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !IsIncludedIn(other.Envelope())) return false;
        return Geometry.Within(other);
    }

    /// <summary>Like <see cref="Relations.CoveredBy"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool CoveredBy(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !IsIncludedIn(other.Envelope())) return false;
        return Geometry.CoveredBy(other);
    }

    /// <summary>Like <see cref="Relations.Touches"/>.</summary>
    /// <param name="other">The other geometry.</param>
    public bool Touches(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Overlaps(other.Envelope())) return false;
        return Geometry.Touches(other);
    }

    private static IEnumerable<Coordinate> OtherComponentPoints(IGeometry2D g) => g switch
    {
        IPoint2D p => [p.ToCoordinate()],
        IMultiPoint2D mp => Rings.Coordinates(mp),
        IPolyline2D line => Enumerable.Range(0, line.PathCount).Where(i => line[i].PointCount > 0).Select(i => line[i][0].ToCoordinate()),
        IPolygon2D polygon => Enumerable.Range(0, polygon.RingCount).Select(i => Rings.Closed(polygon[i]))
            .Where(r => r.Length > 0 && !Rings.IsDegenerate(r)).Select(r => r[0]),
        _ => [],
    };

    /// <summary>One point per ring, path or point of the prepared geometry.</summary>
    private IEnumerable<Coordinate> ComponentPoints() => Geometry switch
    {
        IPoint2D p => [p.ToCoordinate()],
        IMultiPoint2D mp => Rings.Coordinates(mp),
        IPolyline2D line => Enumerable.Range(0, line.PathCount).Where(i => line[i].PointCount > 0).Select(i => line[i][0].ToCoordinate()),
        _ => _rings.Where(r => !Rings.IsDegenerate(r)).Select(r => r[0]),
    };

    private bool Overlaps(Envelope2D e) =>
        !(e.MaxX < _envelope.MinX || _envelope.MaxX < e.MinX || e.MaxY < _envelope.MinY || _envelope.MaxY < e.MinY);

    private bool Includes(Envelope2D e) =>
        e.MinX >= _envelope.MinX && e.MaxX <= _envelope.MaxX && e.MinY >= _envelope.MinY && e.MaxY <= _envelope.MaxY;

    private bool IsIncludedIn(Envelope2D e) =>
        _envelope.MinX >= e.MinX && _envelope.MaxX <= e.MaxX && _envelope.MinY >= e.MinY && _envelope.MaxY <= e.MaxY;

    /// <summary>Point in area (even-odd) via the index: ray to the left, half-open rule, exact.</summary>
    private Location LocateInArea(Coordinate p)
    {
        bool inside = false;
        foreach (int i in _index.Query(_envelope.MinX, p.Y, p.X, p.Y))
        {
            var (a, b) = _index[i];
            if (Segments.IsOnSegment(p, a, b)) return Location.Boundary;
            var (lo, hi) = a.Y <= b.Y ? (a, b) : (b, a);
            if (lo.Y <= p.Y && hi.Y > p.Y && Orientation.Orient2D(lo, hi, p) < 0)
            {
                inside = !inside;
            }
        }

        return inside ? Location.Inside : Location.Outside;
    }
}

/// <summary>Preparation for repeated predicates (ADR 0013).</summary>
public static class Preparation
{
    /// <summary>Prepares the geometry for repeated predicates; the coordinates are copied.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public static PreparedGeometry Prepare(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return new PreparedGeometry(geometry);
    }
}
