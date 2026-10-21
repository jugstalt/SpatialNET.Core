namespace SpatialNET.Tests.MValues;

// Host-Geometrien für die M-Tests (ADR 0015): Punkte implementieren IPoint3D und IPointM und melden
// „kein Z“ bzw. „kein M“ als NaN. Die Fremd-Implementierung aus Foreign/ bleibt unverändert, weil ihre
// Punkte immer Z = 0 melden und nach ADR 0015 deshalb kein IPointM haben dürfen.

public sealed class MPoint(Coordinate c) : IPoint3D, IPointM
{
    public double X => c.X;
    public double Y => c.Y;
    public double Z => c.Z;
    public double M => c.M;
}

public class MPoints(IEnumerable<Coordinate> coordinates) : IPointCollection2D
{
    private readonly IPoint2D[] _points = coordinates.Select(c => (IPoint2D)new MPoint(c)).ToArray();
    public int PointCount => _points.Length;
    public IPoint2D this[int index] => _points[index];
}

public sealed class MPath(IEnumerable<Coordinate> coordinates) : MPoints(coordinates), IPath2D;

public sealed class MRing(IEnumerable<Coordinate> coordinates) : MPoints(coordinates), IRing2D;

public sealed class MMultiPoint(IEnumerable<Coordinate> coordinates) : MPoints(coordinates), IMultiPoint2D;

public sealed class MPolyline(IEnumerable<Coordinate[]> paths) : IPolyline2D
{
    private readonly MPath[] _paths = paths.Select(p => new MPath(p)).ToArray();
    public int PathCount => _paths.Length;
    public IPath2D this[int index] => _paths[index];
}

public sealed class MPolygon(IEnumerable<Coordinate[]> rings) : IPolygon2D
{
    private readonly MRing[] _rings = rings.Select(r => new MRing(r)).ToArray();
    public int RingCount => _rings.Length;
    public IRing2D this[int index] => _rings[index];
}

public sealed class MFactory(bool supportsZ, bool supportsM) : IGeometryFactory
{
    public bool SupportsZ => supportsZ;
    public bool SupportsM => supportsM;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) => new MPoint(coordinate);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        new MMultiPoint(coordinates.ToArray());

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source)
    {
        var list = new List<Coordinate[]>();
        foreach (var p in paths) list.Add(p.ToArray());
        return new MPolyline(list);
    }

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var list = new List<Coordinate[]>();
        foreach (var part in parts)
        {
            list.Add(part.Shell.ToArray());
            foreach (var h in part.Holes.Span) list.Add(h.ToArray());
        }

        return new MPolygon(list);
    }
}

public static class MGeo
{
    public static MPolygon Polygon(params Coordinate[][] rings) => new(rings);

    public static MPolyline Polyline(params Coordinate[] path) => new([path]);

    /// <summary>All coordinates of a geometry (Z and M as reported by the points).</summary>
    public static Coordinate[] Coordinates(IGeometry2D g)
    {
        var result = new List<Coordinate>();
        switch (g)
        {
            case IPoint2D p:
                result.Add(Of(p));
                break;
            case IMultiPoint2D mp:
                Add(mp);
                break;
            case IPolyline2D line:
                for (int i = 0; i < line.PathCount; i++) Add(line[i]);
                break;
            case IPolygon2D polygon:
                for (int i = 0; i < polygon.RingCount; i++) Add(polygon[i]);
                break;
        }

        return result.ToArray();

        void Add(IPointCollection2D points)
        {
            for (int i = 0; i < points.PointCount; i++) result.Add(Of(points[i]));
        }
    }

    public static Coordinate Of(IPoint2D p) =>
        new(p.X, p.Y, p is IPoint3D p3 ? p3.Z : double.NaN, p is IPointM pm ? pm.M : double.NaN);

    /// <summary>The same geometry without any Z and M.</summary>
    public static IGeometry2D Flat(IGeometry2D g) => g switch
    {
        IPoint2D p => new MPoint(new Coordinate(p.X, p.Y)),
        IMultiPoint2D => new MMultiPoint(Coordinates(g).Select(Strip)),
        IPolyline2D line => new MPolyline(Enumerable.Range(0, line.PathCount).Select(i => Coordinates(new MPolyline([PathOf(line[i])])).Select(Strip).ToArray())),
        IPolygon2D polygon => new MPolygon(Enumerable.Range(0, polygon.RingCount).Select(i => PathOf(polygon[i]).Select(Strip).ToArray())),
        _ => throw new NotSupportedException(),
    };

    public static Coordinate Strip(Coordinate c) => new(c.X, c.Y);

    private static Coordinate[] PathOf(IPointCollection2D points) =>
        Enumerable.Range(0, points.PointCount).Select(i => Of(points[i])).ToArray();

    /// <summary>A linear function of the position: interpolation along any segment reproduces it exactly.</summary>
    public static double Linear(double x, double y) => x + 2 * y;

    public static Coordinate WithM(double x, double y) => new(x, y, M: Linear(x, y));

    public static Coordinate[] Square(double x0, double y0, double x1, double y1, Func<double, double, Coordinate> make) =>
        [make(x0, y0), make(x1, y0), make(x1, y1), make(x0, y1), make(x0, y0)];
}
