using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;

namespace SpatialNET.Tests.Construction;

/// <summary>FastPolygonLabel and FastAlternativePolygonLabelsLazy: interior points for map labels, cheap and lazy.</summary>
public class FastLabelTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IPolygon2D P(string wkt) => (IPolygon2D)Wkt.Read(wkt, F);

    private const string Square = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))";
    private const string LShape = "POLYGON ((0 0, 10 0, 10 1, 1 1, 1 10, 0 10, 0 0))";
    private const string RingWithHole = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2))";
    private const string TwoParts = "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 0, 12 0, 12 2, 10 2, 10 0)))";

    private static void AssertInterior(IPolygon2D polygon, Coordinate p) =>
        Assert.Equal(Location.Inside, polygon.Locate(p));

    [Theory]
    [Covers("FastPolygonLabel")]
    [InlineData(Square)]
    [InlineData(LShape)]
    [InlineData(RingWithHole)]
    [InlineData(TwoParts)]
    public void FastPolygonLabel_lies_inside(string wkt)
    {
        var polygon = P(wkt);
        var p = polygon.FastPolygonLabel();
        Assert.NotNull(p);
        AssertInterior(polygon, p.Value);
    }

    [Fact]
    [Covers("FastPolygonLabel")]
    public void FastPolygonLabel_takes_the_centroid_when_it_is_inside()
    {
        Assert.Equal(new Coordinate(5, 5), P(Square).FastPolygonLabel());
        // with the hole the centroid (5 5) is in the hole: another point is taken
        Assert.NotEqual(new Coordinate(5, 5), P(RingWithHole).FastPolygonLabel());
    }

    [Fact]
    [Covers("FastPolygonLabel")]
    public void FastPolygonLabel_reads_untyped_rings_by_even_odd()
    {
        // the hole stored with the same orientation as the shell
        var polygon = F.CreatePolygon([new PolygonPart(
            new Coordinate[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0) },
            new ReadOnlyMemory<Coordinate>[] { new Coordinate[] { new(2, 2), new(8, 2), new(8, 8), new(2, 8), new(2, 2) } })], null);
        var p = polygon.FastPolygonLabel()!.Value;
        Assert.False(p.X is > 2 and < 8 && p.Y is > 2 and < 8);
        AssertInterior(polygon, p);
    }

    [Fact]
    [Covers("FastPolygonLabel")]
    public void FastPolygonLabel_of_empty_or_degenerate_is_null()
    {
        Assert.Null(P("POLYGON EMPTY").FastPolygonLabel());
        Assert.Null(F.CreatePolygon([new PolygonPart(new Coordinate[] { new(0, 0), new(5, 0), new(10, 0), new(0, 0) })], null).FastPolygonLabel());
        Assert.Throws<ArgumentNullException>(() => Shapes.FastPolygonLabel(null!));
    }

    [Theory]
    [Covers("FastAlternativePolygonLabelsLazy")]
    [InlineData(Square)]
    [InlineData(LShape)]
    [InlineData(RingWithHole)]
    [InlineData(TwoParts)]
    public void Alternatives_lie_inside_and_keep_their_distances(string wkt)
    {
        var polygon = P(wkt);
        var seed = polygon.FastPolygonLabel()!.Value;
        var options = new AlternativeLabelOptions { Separation = 0.5, MinClearance = 0.1, MaxCount = 30 };
        var points = polygon.FastAlternativePolygonLabelsLazy(seed, options).ToList();
        Assert.NotEmpty(points);
        Assert.True(points.Count <= 30);
        for (int i = 0; i < points.Count; i++)
        {
            AssertInterior(polygon, points[i]);
            Assert.True(Dist(points[i], seed) >= 0.5);
            Assert.True(polygon.Boundary(F).Distance(F.CreatePoint(points[i], null)) >= 0.1 - 1e-12);
            for (int j = 0; j < i; j++) Assert.True(Dist(points[i], points[j]) >= 0.5);
        }
    }

    [Fact]
    [Covers("FastAlternativePolygonLabelsLazy")]
    public void Alternatives_are_deterministic_and_start_near_the_seed()
    {
        var polygon = P(LShape);
        var seed = new Coordinate(5, 0.5);
        var options = new AlternativeLabelOptions { Separation = 1 };
        var a = polygon.FastAlternativePolygonLabelsLazy(seed, options).ToList();
        var b = polygon.FastAlternativePolygonLabelsLazy(seed, options).ToList();
        Assert.Equal(a, b);
        // the first line is the one through the seed: the first point is on it
        Assert.Equal(0.5, a[0].Y);
        Assert.InRange(a.Count, 2, 16); // at most the default MaxCount, within the scanline budget
    }

    [Fact]
    [Covers("FastAlternativePolygonLabelsLazy")]
    public void Alternatives_reach_both_parts()
    {
        var polygon = P(TwoParts);
        var points = polygon.FastAlternativePolygonLabelsLazy(new Coordinate(1, 1), new AlternativeLabelOptions { Separation = 0.2, MaxCount = 100 }).ToList();
        Assert.Contains(points, p => p.X < 2);
        Assert.Contains(points, p => p.X > 10);
    }

    [Fact]
    [Covers("FastAlternativePolygonLabelsLazy")]
    public void Alternatives_of_empty_input_are_empty_and_bad_options_throw_at_once()
    {
        Assert.Empty(P("POLYGON EMPTY").FastAlternativePolygonLabelsLazy(new Coordinate(0, 0)));
        Assert.Empty(F.CreatePolygon([new PolygonPart(new Coordinate[] { new(0, 0), new(5, 0), new(10, 0), new(0, 0) })], null)
            .FastAlternativePolygonLabelsLazy(new Coordinate(0, 0)));
        // a clearance larger than the polygon allows: nothing, no exception
        Assert.Empty(P(Square).FastAlternativePolygonLabelsLazy(new Coordinate(5, 5), new AlternativeLabelOptions { MinClearance = 6 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => P(Square).FastAlternativePolygonLabelsLazy(new Coordinate(5, 5), new AlternativeLabelOptions { Separation = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => P(Square).FastAlternativePolygonLabelsLazy(new Coordinate(5, 5), new AlternativeLabelOptions { MaxCount = -1 }));
    }

    [Fact]
    [Covers("FastAlternativePolygonLabelsLazy")]
    public void Alternatives_are_lazy()
    {
        var polygon = P(RingWithHole);
        var seed = polygon.FastPolygonLabel()!.Value;
        var options = new AlternativeLabelOptions { Separation = 0.3, MaxCount = 50 };

        long before = Shapes_Lines();
        var sequence = polygon.FastAlternativePolygonLabelsLazy(seed, options);
        Assert.Equal(before, Shapes_Lines()); // nothing computed before enumeration
        _ = sequence.First();
        long first = Shapes_Lines() - before;

        before = Shapes_Lines();
        Assert.Equal(50, sequence.Take(50).Count());
        long fifty = Shapes_Lines() - before;
        Assert.True(first < fifty, $"First() computed {first} scanlines, Take(50) {fifty}");
    }

    [Fact]
    [Covers("FastAlternativePolygonLabelsLazy")]
    public void Alternatives_stop_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Empty(P(Square).FastAlternativePolygonLabelsLazy(new Coordinate(5, 5), new AlternativeLabelOptions { CancellationToken = cts.Token }));
    }

    private static long Shapes_Lines() => LabelScan.Lines;

    private static double Dist(Coordinate a, Coordinate b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
