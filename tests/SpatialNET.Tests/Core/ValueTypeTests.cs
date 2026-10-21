namespace SpatialNET.Tests.Core;

public class ValueTypeTests
{
    [Fact]
    public void Coordinate_without_Z_has_no_Z()
    {
        var c = new Coordinate(1, 2);

        Assert.False(c.HasZ);
        Assert.True(double.IsNaN(c.Z));
    }

    [Fact]
    public void Coordinate_with_Z_has_Z()
    {
        Assert.True(new Coordinate(1, 2, 0).HasZ);
    }

    [Fact]
    public void Coordinate_without_M_has_no_M()
    {
        var c = new Coordinate(1, 2, 3);

        Assert.False(c.HasM);
        Assert.True(double.IsNaN(c.M));
    }

    [Fact]
    public void Coordinate_with_M_has_M_even_if_M_is_zero()
    {
        Assert.True(new Coordinate(1, 2, M: 0).HasM);
        Assert.False(new Coordinate(1, 2, M: 0).HasZ);
        Assert.True(new Coordinate(1, 2, 3, 4).HasM);
    }

    [Fact]
    public void Coordinates_differ_by_M_and_equal_without_M()
    {
        Assert.Equal(new Coordinate(1, 2, 3, 4), new Coordinate(1, 2, 3, 4));
        Assert.NotEqual(new Coordinate(1, 2, 3, 4), new Coordinate(1, 2, 3, 5));
        Assert.NotEqual(new Coordinate(1, 2), new Coordinate(1, 2, M: 0));
    }

    [Fact]
    public void Factory_does_not_support_M_by_default()
    {
        IGeometryFactory legacy = new LegacyFactory();

        Assert.False(legacy.SupportsM);
    }

    private sealed class LegacyFactory : IGeometryFactory
    {
        public bool SupportsZ => false;
        public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) => throw new NotSupportedException();
        public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) => throw new NotSupportedException();
        public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) => throw new NotSupportedException();
        public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source) => throw new NotSupportedException();
    }

    [Fact]
    public void Coordinates_without_Z_are_equal()
    {
        Assert.Equal(new Coordinate(1, 2), new Coordinate(1, 2));
    }

    [Fact]
    public void Empty_envelope_is_empty_and_equal_to_itself()
    {
        Assert.True(Envelope2D.Empty.IsEmpty);
        Assert.Equal(Envelope2D.Empty, new Envelope2D(double.NaN, double.NaN, double.NaN, double.NaN));
        Assert.True(double.IsNaN(Envelope2D.Empty.Width));
    }

    [Fact]
    public void Envelope_has_width_and_height()
    {
        var e = new Envelope2D(1, 2, 4, 8);

        Assert.False(e.IsEmpty);
        Assert.Equal(3, e.Width);
        Assert.Equal(6, e.Height);
    }

    [Fact]
    public void Polygon_part_without_holes_has_no_holes()
    {
        var part = new PolygonPart(new Coordinate[] { new(0, 0), new(1, 0), new(1, 1), new(0, 0) });

        Assert.Equal(4, part.Shell.Length);
        Assert.True(part.Holes.IsEmpty);
    }
}
