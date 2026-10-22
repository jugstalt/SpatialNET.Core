using CsCheck;

namespace SpatialNET.Tests.Core;

/// <summary>The helpers of <see cref="Envelope2D"/> against their brute-force definitions (ADR 0022).</summary>
public class EnvelopeHelpersTests
{
    private static readonly Gen<Envelope2D> Boxes =
        Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], Gen.Int[0, 6], Gen.Int[0, 6], Gen.Int[0, 5])
            .Select(t => t.Item5 == 0 ? Envelope2D.Empty : new Envelope2D(t.Item1, t.Item2, t.Item1 + t.Item3, t.Item2 + t.Item4));

    [Fact]
    public void Intersects_and_Intersection_agree_and_touching_borders_count()
    {
        Gen.Select(Boxes, Boxes).Sample((a, b) =>
        {
            bool expected = !a.IsEmpty && !b.IsEmpty && Math.Max(a.MinX, b.MinX) <= Math.Min(a.MaxX, b.MaxX) && Math.Max(a.MinY, b.MinY) <= Math.Min(a.MaxY, b.MaxY);

            Assert.Equal(expected, a.Intersects(b));
            Assert.Equal(a.Intersects(b), b.Intersects(a));
            Assert.Equal(expected, !a.Intersection(b).IsEmpty);
            if (expected)
            {
                Assert.True(a.Contains(a.Intersection(b)));
                Assert.True(b.Contains(a.Intersection(b)));
            }
        });
    }

    [Fact]
    public void Union_contains_both_and_is_the_smallest_such_envelope()
    {
        Gen.Select(Boxes, Boxes).Sample((a, b) =>
        {
            var union = a.Union(b);

            Assert.Equal(union, b.Union(a));
            Assert.True(a.IsEmpty || union.Contains(a));
            Assert.True(b.IsEmpty || union.Contains(b));
            if (a.IsEmpty) Assert.Equal(b, union);
            if (b.IsEmpty) Assert.Equal(a, union);
            if (!a.IsEmpty && !b.IsEmpty)
            {
                Assert.Equal((Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY)),
                    (union.MinX, union.MinY, union.MaxX, union.MaxY));
            }
        });
    }

    [Fact]
    public void Contains_a_point_includes_the_border_and_excludes_NaN()
    {
        Gen.Select(Boxes, Gen.Int[-8, 14], Gen.Int[-8, 14]).Sample((e, x, y) =>
        {
            bool expected = !e.IsEmpty && x >= e.MinX && x <= e.MaxX && y >= e.MinY && y <= e.MaxY;

            Assert.Equal(expected, e.Contains(new Coordinate(x, y)));
        });

        var box = new Envelope2D(0, 0, 1, 1);
        Assert.False(box.Contains(new Coordinate(double.NaN, 0.5)));
        Assert.False(Envelope2D.Empty.Contains(new Coordinate(0, 0)));
    }

    [Fact]
    public void Expand_widens_and_a_negative_distance_shrinks_until_nothing_is_left()
    {
        var box = new Envelope2D(0, 0, 10, 4);

        Assert.Equal(new Envelope2D(-1, -1, 11, 5), box.Expand(1));
        Assert.Equal(new Envelope2D(1, 1, 9, 3), box.Expand(-1));
        Assert.Equal(new Envelope2D(2, 2, 8, 2), box.Expand(-2));            // a line is left
        Assert.True(box.Expand(-3).IsEmpty);                                 // nothing is left
        Assert.True(Envelope2D.Empty.Expand(5).IsEmpty);
        Assert.True(box.Expand(double.NaN).IsEmpty);
    }

    [Fact]
    public void Center_is_the_middle_and_NaN_for_an_empty_envelope()
    {
        Assert.Equal(new Coordinate(5, 2), new Envelope2D(0, 0, 10, 4).Center);
        Assert.True(double.IsNaN(Envelope2D.Empty.Center.X));
    }

    [Fact]
    public void An_empty_envelope_meets_nothing_contains_nothing_and_lies_in_nothing()
    {
        var box = new Envelope2D(0, 0, 1, 1);

        Assert.False(Envelope2D.Empty.Intersects(box));
        Assert.False(box.Intersects(Envelope2D.Empty));
        Assert.False(box.Contains(Envelope2D.Empty));
        Assert.False(Envelope2D.Empty.Contains(box));
        Assert.False(Envelope2D.Empty.Intersects(Envelope2D.Empty));
    }
}
