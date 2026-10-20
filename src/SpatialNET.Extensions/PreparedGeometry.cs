using SpatialNET.Internal;
using SpatialNET.Internal.Noding;

namespace SpatialNET;

/// <summary>
/// Eine für wiederholte Prädikate vorbereitete Geometrie (ADR 0013): Schnappschuss der Koordinaten plus
/// Segment-Index. Spätere Änderungen am Host-Objekt wirken nicht. Die Ergebnisse sind identisch mit den
/// nicht vorbereiteten Prädikaten; schnelle Wege gibt es für <see cref="Intersects"/>/<see cref="Disjoint"/>
/// und Punkt-in-Fläche, alles andere läuft über das exakte <see cref="Relations.Relate(IGeometry2D, IGeometry2D)"/>.
/// </summary>
/// <remarks>Nicht thread-sicher; je Thread eine eigene Instanz vorbereiten.</remarks>
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

    /// <summary>Der Schnappschuss der vorbereiteten Geometrie.</summary>
    public IGeometry2D Geometry { get; }

    /// <summary>Wie <see cref="Relations.Intersects"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
    public bool Intersects(IGeometry2D other) => !Disjoint(other);

    /// <summary>Wie <see cref="Relations.Disjoint"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
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

    /// <summary>Wie <see cref="Relations.Contains"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
    public bool Contains(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Includes(other.Envelope())) return false;
        if (_isArea && other is IPoint2D p) return LocateInArea(p.ToCoordinate()) == Location.Inside;
        return Geometry.Contains(other);
    }

    /// <summary>Wie <see cref="Relations.Covers"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
    public bool Covers(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !Includes(other.Envelope())) return false;
        if (_isArea && other is IPoint2D p) return LocateInArea(p.ToCoordinate()) != Location.Outside;
        return Geometry.Covers(other);
    }

    /// <summary>Wie <see cref="Relations.Within"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
    public bool Within(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !IsIncludedIn(other.Envelope())) return false;
        return Geometry.Within(other);
    }

    /// <summary>Wie <see cref="Relations.CoveredBy"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
    public bool CoveredBy(IGeometry2D other)
    {
        GeometryAccess.EnsureKnown(other, nameof(other));
        if (_isEmpty || GeometryAccess.IsEmpty(other) || !IsIncludedIn(other.Envelope())) return false;
        return Geometry.CoveredBy(other);
    }

    /// <summary>Wie <see cref="Relations.Touches"/>.</summary>
    /// <param name="other">Die andere Geometrie.</param>
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

    /// <summary>Je Ring, Pfad bzw. Punkt der vorbereiteten Geometrie ein Punkt.</summary>
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

    /// <summary>Punkt in Fläche (Even-Odd) über den Index: Strahl nach links, halboffene Regel, exakt.</summary>
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

/// <summary>Vorbereitung für wiederholte Prädikate (ADR 0013).</summary>
public static class Preparation
{
    /// <summary>Bereitet die Geometrie für wiederholte Prädikate vor; die Koordinaten werden kopiert.</summary>
    /// <param name="geometry">Die Geometrie.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> ist <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> ist keine bekannte Geometrie-Art.</exception>
    public static PreparedGeometry Prepare(this IGeometry2D geometry)
    {
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        return new PreparedGeometry(geometry);
    }
}
