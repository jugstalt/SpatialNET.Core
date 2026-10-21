namespace SpatialNET.Internal;

/// <summary>Immutable copy of a geometry (for <see cref="PreparedGeometry"/>, ADR 0013).</summary>
internal static class Snapshot
{
    public static IGeometry2D Of(IGeometry2D g) => g switch
    {
        IPoint2D p => new SPoint(p.ToCoordinate()),
        IMultiPoint2D mp => new SMultiPoint(Rings.Coordinates(mp)),
        IPolyline2D line => new SPolyline(Enumerable.Range(0, line.PathCount).Select(i => new SPath(Rings.Coordinates(line[i]))).ToArray()),
        IPolygon2D polygon => new SPolygon(Enumerable.Range(0, polygon.RingCount).Select(i => new SPath(Rings.Coordinates(polygon[i]))).ToArray()),
        _ => throw new NotSupportedException(g.GetType().FullName),
    };

    private sealed class SPoint(Coordinate c) : IPoint3D
    {
        public double X => c.X;
        public double Y => c.Y;
        public double Z => c.HasZ ? c.Z : 0;
    }

    private class SPath(Coordinate[] points) : IRing2D
    {
        private readonly IPoint2D[] _points = points.Select(p => (IPoint2D)new SPoint(p)).ToArray();
        public int PointCount => _points.Length;
        public IPoint2D this[int index] => _points[index];
    }

    private sealed class SMultiPoint(Coordinate[] points) : SPath(points), IMultiPoint2D;

    private sealed class SPolyline(SPath[] paths) : IPolyline2D
    {
        public int PathCount => paths.Length;
        public IPath2D this[int index] => paths[index];
    }

    private sealed class SPolygon(SPath[] rings) : IPolygon2D
    {
        public int RingCount => rings.Length;
        public IRing2D this[int index] => rings[index];
    }
}

/// <summary>Grid index over segments for fast range queries.</summary>
internal sealed class SegmentIndex
{
    private readonly (Coordinate A, Coordinate B)[] _segments;
    private readonly List<int>[] _cells;
    private readonly double _minX, _minY, _cellW, _cellH;
    private readonly int _nx, _ny;
    private readonly int[] _stamp;
    private int _query;

    public SegmentIndex(IReadOnlyList<(Coordinate A, Coordinate B)> segments, Envelope2D extent)
    {
        _segments = segments.ToArray();
        _stamp = new int[_segments.Length];
        int n = Math.Max(1, (int)Math.Sqrt(_segments.Length));
        (_nx, _ny) = (n, n);
        (_minX, _minY) = (extent.IsEmpty ? 0 : extent.MinX, extent.IsEmpty ? 0 : extent.MinY);
        _cellW = extent.IsEmpty || extent.Width == 0 ? 1 : extent.Width / _nx;
        _cellH = extent.IsEmpty || extent.Height == 0 ? 1 : extent.Height / _ny;
        _cells = new List<int>[_nx * _ny];
        for (int i = 0; i < _segments.Length; i++)
        {
            var (a, b) = _segments[i];
            ForCells(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y),
                cell => (_cells[cell] ??= []).Add(i));
        }
    }

    public (Coordinate A, Coordinate B) this[int i] => _segments[i];

    /// <summary>Candidates whose cells touch the rectangle (without duplicates; not thread-safe).</summary>
    public List<int> Query(double minX, double minY, double maxX, double maxY)
    {
        var result = new List<int>();
        _query++;
        ForCells(minX, minY, maxX, maxY, cell =>
        {
            if (_cells[cell] is not { } list) return;
            foreach (int i in list)
            {
                if (_stamp[i] == _query) continue;
                _stamp[i] = _query;
                result.Add(i);
            }
        });
        return result;
    }

    private void ForCells(double minX, double minY, double maxX, double maxY, Action<int> action)
    {
        int x0 = Clamp((minX - _minX) / _cellW, _nx), x1 = Clamp((maxX - _minX) / _cellW, _nx);
        int y0 = Clamp((minY - _minY) / _cellH, _ny), y1 = Clamp((maxY - _minY) / _cellH, _ny);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                action(y * _nx + x);
    }

    private static int Clamp(double v, int n) => double.IsNaN(v) ? 0 : (int)Math.Clamp(Math.Floor(v), 0, n - 1);
}
