namespace SpatialNET.Internal;

/// <summary>A read-only polygon over coordinate rings, for algorithms that clean their input first (ADR 0020).</summary>
internal sealed class CoordinatePolygon(Coordinate[][] rings) : IPolygon2D
{
    private readonly IRing2D[] _rings = rings.Select(r => (IRing2D)new CoordinateRing(r)).ToArray();

    public int RingCount => _rings.Length;

    public IRing2D this[int index] => _rings[index];

    private sealed class CoordinateRing(Coordinate[] points) : IRing2D
    {
        public int PointCount => points.Length;

        public IPoint2D this[int index] => GeometryAccess.PointOf(points[index]);
    }
}
