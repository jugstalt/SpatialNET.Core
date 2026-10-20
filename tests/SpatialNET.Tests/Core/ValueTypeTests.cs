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
