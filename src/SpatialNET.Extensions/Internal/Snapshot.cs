namespace SpatialNET.Internal;

/// <summary>Immutable copy of a geometry (for <see cref="PreparedGeometry"/>, ADR 0013).</summary>
internal static class Snapshot
{
    public static IGeometry2D Of(IGeometry2D g) => g switch
    {
        IPoint2D p => GeometryAccess.PointOf(p.ToCoordinate()),
        IMultiPoint2D mp => new SMultiPoint(Rings.Coordinates(mp)),
        IPolyline2D line => new SPolyline(Enumerable.Range(0, line.PathCount).Select(i => new SPath(Rings.Coordinates(line[i]))).ToArray()),
        IPolygon2D polygon => new SPolygon(Enumerable.Range(0, polygon.RingCount).Select(i => new SPath(Rings.Coordinates(polygon[i]))).ToArray()),
        _ => throw new NotSupportedException(g.GetType().FullName),
    };

    private class SPath(Coordinate[] points) : IRing2D
    {
        private readonly IPoint2D[] _points = points.Select(GeometryAccess.PointOf).ToArray();
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
