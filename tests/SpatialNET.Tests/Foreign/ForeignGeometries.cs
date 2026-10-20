using System.Collections;

namespace SpatialNET.Tests.Foreign;

// Fremd-Implementierung (ADR 0008): absichtlich „unbequem“ und WebGIS-artig, um Annahmen der
// Algorithmen über konkrete Typen aufzudecken:
// - veränderliche, klassenbasierte Geometrien mit eigenem Indexer-Typ (explizite Interface-Indexer)
// - Ringe ohne Schlusspunkt, Außenringe im Uhrzeigersinn, Löcher gegen den Uhrzeigersinn
// - zusätzlich IReadOnlyList<T>
// - Punkte sind immer IPoint3D, ohne Höhe mit Z = 0

public abstract class Shape : IGeometry2D
{
    /// <summary>Host-Metadatum, das die Factory aus der Quell-Geometrie übernimmt.</summary>
    public int SrsId { get; set; }
}

public sealed class Point(double x, double y, double z = 0) : Shape, IPoint3D
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Z { get; set; } = z;
}

public class PointCollection : Shape, IPointCollection2D, IReadOnlyList<IPoint2D>
{
    protected readonly List<Point> Points = [];

    public PointCollection(IEnumerable<Point> points) => Points.AddRange(points);

    public int PointCount => Points.Count;

    public Point this[int pointIndex] => Points[pointIndex];

    public void AddPoint(Point point) => Points.Add(point);

    IPoint2D IPointCollection2D.this[int index] => Points[index];

    int IReadOnlyCollection<IPoint2D>.Count => Points.Count;

    IPoint2D IReadOnlyList<IPoint2D>.this[int index] => Points[index];

    IEnumerator<IPoint2D> IEnumerable<IPoint2D>.GetEnumerator() => Points.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Points.GetEnumerator();
}

public class Path(IEnumerable<Point> points) : PointCollection(points), IPath2D;

public class Ring(IEnumerable<Point> points) : Path(points), IRing2D;

public sealed class Hole(IEnumerable<Point> points) : Ring(points);

public sealed class MultiPoint(IEnumerable<Point> points) : PointCollection(points), IMultiPoint2D;

public sealed class Polyline : Shape, IPolyline2D, IReadOnlyList<IPath2D>
{
    private readonly List<Path> _paths = [];

    public Polyline(IEnumerable<Path> paths) => _paths.AddRange(paths);

    public int PathCount => _paths.Count;

    public Path this[int pathIndex] => _paths[pathIndex];

    IPath2D IPolyline2D.this[int index] => _paths[index];

    int IReadOnlyCollection<IPath2D>.Count => _paths.Count;

    IPath2D IReadOnlyList<IPath2D>.this[int index] => _paths[index];

    IEnumerator<IPath2D> IEnumerable<IPath2D>.GetEnumerator() => _paths.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _paths.GetEnumerator();
}

public sealed class Polygon : Shape, IPolygon2D, IReadOnlyList<IRing2D>
{
    private readonly List<Ring> _rings = [];

    public Polygon(IEnumerable<Ring> rings) => _rings.AddRange(rings);

    public int RingCount => _rings.Count;

    public Ring this[int ringIndex] => _rings[ringIndex];

    IRing2D IPolygon2D.this[int index] => _rings[index];

    int IReadOnlyCollection<IRing2D>.Count => _rings.Count;

    IRing2D IReadOnlyList<IRing2D>.this[int index] => _rings[index];

    IEnumerator<IRing2D> IEnumerable<IRing2D>.GetEnumerator() => _rings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _rings.GetEnumerator();
}

public sealed class ForeignGeometryFactory : IGeometryFactory
{
    public static ForeignGeometryFactory Instance { get; } = new();

    public bool SupportsZ => true;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) =>
        WithSrs(ToPoint(coordinate), source);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        WithSrs(new MultiPoint(ToPoints(coordinates)), source);

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source)
    {
        var result = new List<Path>();
        foreach (var path in paths)
        {
            result.Add(new Path(ToPoints(path.Span)));
        }

        return WithSrs(new Polyline(result), source);
    }

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<Ring>();
        foreach (var part in parts)
        {
            // Außenringe kommen gegen den Uhrzeigersinn – der Host speichert sie im Uhrzeigersinn.
            rings.Add(new Ring(OpenAndReversed(part.Shell.Span)));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(new Hole(OpenAndReversed(hole.Span)));
            }
        }

        return WithSrs(new Polygon(rings), source);
    }

    private static T WithSrs<T>(T shape, IGeometry2D? source) where T : Shape
    {
        if (source is Shape s)
        {
            shape.SrsId = s.SrsId;
        }

        return shape;
    }

    private static Point ToPoint(Coordinate c) => new(c.X, c.Y, c.HasZ ? c.Z : 0);

    private static List<Point> ToPoints(ReadOnlySpan<Coordinate> coordinates)
    {
        var points = new List<Point>(coordinates.Length);
        foreach (var c in coordinates)
        {
            points.Add(ToPoint(c));
        }

        return points;
    }

    private static List<Point> OpenAndReversed(ReadOnlySpan<Coordinate> ring)
    {
        var points = ToPoints(ring);
        if (points.Count > 1 && points[0].X == points[^1].X && points[0].Y == points[^1].Y)
        {
            points.RemoveAt(points.Count - 1);
        }

        points.Reverse();
        return points;
    }
}
