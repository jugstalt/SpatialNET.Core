using CsCheck;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using SpatialNET.Imp;
using SpatialNET.Internal.Noding;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Overlay;

/// <summary>
/// <c>UnionAll</c> against NTS <c>OverlayNG</c> folded over the inputs with the same grid (ADR 0018/0008). The inputs of a
/// fixture are the elements of one GEOMETRYCOLLECTION in <c>a</c>. Invalid inputs (tag self-crossing) have no
/// NTS reference; there only a valid result is required.
/// </summary>
public class UnionAllFixtureTests
{
    public static TheoryData<string, string> UnionAllFixtures => FixtureCatalog.Theory("unionAll");

    [Theory]
    [MemberData(nameof(UnionAllFixtures))]
    public void UnionAll(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var inputs = Wkt.ReadAll(fixture.A, factory);
        var ntsInputs = Wkt.ReadAll(fixture.A, ImpGeometryFactory.Instance).Select(Nts.ToNts).ToList();
        double gridSize = fixture.Params.ContainsKey("gridSize")
            ? fixture.Param("gridSize", 0)
            : GridModel.AutoSize(MaxAbs(ntsInputs));

        var dimensions = inputs.Where(g => !Internal.GeometryAccess.IsEmpty(g)).Select(g => Dim(Nts.ToNts(g))).Distinct().ToList();
        if (dimensions.Count > 1)
        {
            Assert.Throws<NotSupportedException>(() => inputs.UnionAll(factory: factory, gridSize: gridSize));
            return;
        }

        var actual = inputs.UnionAll(factory: factory, gridSize: gridSize);

        if (fixture.Tags.Contains("self-crossing"))
        {
            Assert.True(actual.IsValid(), "The result of a union must be valid (ADR 0003).");
            return;
        }

        var pm = new PrecisionModel(1 / gridSize);
        Geometry expected;
        try
        {
            expected = ntsInputs.Skip(1).Aggregate(ntsInputs[0], (acc, g) =>
                new OverlayNG(acc, g, pm, SpatialFunction.Union) { StrictMode = true }.GetResult());
        }
        catch (TopologyException)
        {
            Assert.True(actual.IsValid(), "The result of a union must be valid (ADR 0003).");
            return;
        }

        OracleAssert.SameGeometry(expected, actual, gridSize, fixture);
    }

    private static int Dim(Geometry g) => g is IPolygonal ? 2 : g is ILineal ? 1 : 0;

    private static double MaxAbs(IEnumerable<Geometry> geometries) =>
        geometries.Where(g => !g.IsEmpty).Select(g => g.EnvelopeInternal)
            .Select(e => Math.Max(Math.Max(Math.Abs(e.MinX), Math.Abs(e.MaxX)), Math.Max(Math.Abs(e.MinY), Math.Abs(e.MaxY))))
            .DefaultIfEmpty(0).Max();
}

public class UnionAllTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IPolygon2D Square(double x, double y, double size = 10, bool clockwise = false)
    {
        var ring = new Coordinate[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size), new(x, y) };
        return F.CreatePolygon([new PolygonPart(clockwise ? ring.Reverse().ToArray() : ring)], null);
    }

    private static IPolygon2D RawSquare(double x, double y, double size, bool clockwise)
    {
        var ring = new Coordinate[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size), new(x, y) };
        if (clockwise) Array.Reverse(ring);
        return new Imp.Polygon(new Imp.Ring(ring.Select(c => (IPoint2D)new Imp.Point(c.X, c.Y)).ToArray()));
    }

    [Fact]
    public void Unites_overlapping_touching_and_separate_polygons()
    {
        // A row 0..20 x 0..10 from three overlapping squares, a square on top of its left half (sharing the edge y = 10),
        // and one far away: 200 + 100 + 100.
        var result = new[] { Square(0, 0), Square(5, 0), Square(10, 0), Square(0, 10), Square(50, 50) }.UnionAll(factory: F);

        Assert.True(result.IsValid());
        Assert.Equal(400, result.Area(), 6);
        Assert.Equal(2, Internal.PolygonStructure.Build(result).Count);
    }

    [Fact]
    public void Empty_sequences_give_empty_results_of_the_matching_kind()
    {
        Assert.Equal(0, Array.Empty<IPolygon2D>().UnionAll(factory: F).RingCount);
        Assert.Equal(0, Array.Empty<IPolyline2D>().UnionAll(factory: F).PathCount);
        Assert.Equal(0, Array.Empty<IMultiPoint2D>().UnionAll(factory: F).PointCount);
        Assert.Equal(0, Assert.IsAssignableFrom<IPolygon2D>(Array.Empty<IGeometry2D>().UnionAll(factory: F)).RingCount);
    }

    [Fact]
    public void Empty_geometries_are_skipped()
    {
        var empty = F.CreatePolygon([], null);

        var result = new[] { empty, Square(0, 0), empty }.UnionAll(factory: F);

        Assert.Equal(100, result.Area());
        Assert.Equal(0, new[] { empty, empty }.UnionAll(factory: F).RingCount);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<IPolygon2D>)null!).UnionAll(factory: F));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<IGeometry2D>)null!).UnionAll(factory: F));
        var ex = Assert.Throws<ArgumentNullException>(() => new IPolygon2D[] { Square(0, 0), null! }.UnionAll(factory: F));
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void Mixed_dimensions_are_not_supported_but_empty_ones_are_skipped()
    {
        var line = F.CreatePolyline([new Coordinate[] { new(0, 0), new(5, 5) }], null);

        Assert.Throws<NotSupportedException>(() => new IGeometry2D[] { Square(0, 0), line }.UnionAll(factory: F));
        Assert.Equal(100, Assert.IsAssignableFrom<IPolygon2D>(new IGeometry2D[] { Square(0, 0), F.CreatePolyline([], null) }.UnionAll(factory: F)).Area());
    }

    [Fact]
    public void Unites_lines_and_points()
    {
        var lines = new[]
        {
            F.CreatePolyline([new Coordinate[] { new(0, 0), new(5, 0) }], null),
            F.CreatePolyline([new Coordinate[] { new(5, 0), new(10, 0) }], null),
            F.CreatePolyline([new Coordinate[] { new(20, 0), new(30, 0) }], null),
        };
        var points = new[]
        {
            F.CreateMultiPoint([new Coordinate(0, 0), new Coordinate(1, 1)], null),
            F.CreateMultiPoint([new Coordinate(1, 1), new Coordinate(2, 2)], null),
        };

        var united = lines.UnionAll(factory: F);
        var set = points.UnionAll(factory: F);

        Assert.Equal(lines[0].Union(lines[1], factory: F).Union(lines[2], factory: F).PathCount, united.PathCount); // as the fold
        Assert.Equal(20, united.Length());
        Assert.Equal(3, set.PointCount);
    }

    [Fact]
    public void A_single_input_is_normalized()
    {
        var bowtie = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))", F);

        var result = new[] { bowtie }.UnionAll(factory: F);

        Assert.True(result.IsValid());
        Assert.True(result.Area() > 0);
    }

    [Fact]
    public void Fill_rules_apply_to_every_input()
    {
        var clockwise = new[] { RawSquare(0, 0, 10, true), RawSquare(5, 0, 10, true) };

        Assert.Equal(150, clockwise.UnionAll(FillRule.Negative, F).Area(), 6);   // clockwise shells have winding -1
        Assert.Equal(150, clockwise.UnionAll(FillRule.EvenOdd, F).Area(), 6);
        Assert.Equal(0, clockwise.UnionAll(FillRule.Positive, F).Area(), 6);
        Assert.Equal(150, clockwise.UnionAll(FillRule.NonZero, F).Area(), 6);
    }

    [Fact]
    public void An_invalid_grid_size_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new[] { Square(0, 0), Square(5, 5) }.UnionAll(factory: F, gridSize: 0));
    }

    [Fact]
    public void The_automatic_grid_is_the_one_of_all_inputs()
    {
        var inputs = new[] { Square(0.123456, 0.5), Square(5.1, 5.2), Square(1_000_000.5, 3, 7) };
        double auto = GridModel.AutoSize(1_000_007.5);

        Assert.Equal(inputs.UnionAll(factory: F, gridSize: auto).ToWkt(), inputs.UnionAll(factory: F).ToWkt());
    }

    [Fact]
    public void The_same_sequence_gives_the_identical_result()
    {
        var inputs = Enumerable.Range(0, 30).Select(i => Square(i * 3 % 17, i * 7 % 13, 6)).ToArray();

        Assert.Equal(inputs.UnionAll(factory: F).ToWkt(), inputs.UnionAll(factory: F).ToWkt());
    }

    [Fact]
    public void Parallel_calls_on_the_same_inputs_agree()
    {
        var inputs = Enumerable.Range(0, 40).Select(i => Square(i * 3 % 23, i * 5 % 19, 6)).ToArray();
        string expected = inputs.UnionAll(factory: F).ToWkt();

        var results = new string[16];
        Parallel.For(0, results.Length, i => results[i] = inputs.UnionAll(factory: F).ToWkt());

        Assert.All(results, r => Assert.Equal(expected, r));
    }

    [Fact]
    public void Z_and_M_are_carried_through_the_cascade()
    {
        var zm = new MFactory(supportsZ: true, supportsM: true);
        var squares = Enumerable.Range(0, 6)
            .Select(i => MGeo.Polygon(MGeo.Square(i * 7, 0, i * 7 + 10, 10, MGeo.WithM)))
            .Cast<IPolygon2D>().ToArray();

        var result = squares.UnionAll(factory: zm);

        var coordinates = MGeo.Coordinates(result);
        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, 1e-6)); // M is a linear function of the position
    }

    [Fact]
    public void Results_have_no_M_if_the_factory_does_not_support_it()
    {
        var squares = new[] { MGeo.Polygon(MGeo.Square(0, 0, 10, 10, MGeo.WithM)), MGeo.Polygon(MGeo.Square(5, 5, 15, 15, MGeo.WithM)) };

        var result = squares.UnionAll(factory: new MFactory(supportsZ: true, supportsM: false));

        Assert.All(MGeo.Coordinates(result), c => Assert.False(c.HasM));
    }
}

/// <summary>Property tests for <c>UnionAll</c> (ADR 0018): the cascade agrees with the fold of <c>Union</c>.</summary>
public class UnionAllPropertyTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private const double Grid = 1e-6;

    private static readonly Gen<(int X, int Y, int W, int H)[]> Rectangles =
        Gen.Select(Gen.Int[0, 40], Gen.Int[0, 40], Gen.Int[1, 15], Gen.Int[1, 15]).Array[1, 25];

    private static IPolygon2D Rect((int X, int Y, int W, int H) r) =>
        F.CreatePolygon([new PolygonPart(new Coordinate[] { new(r.X, r.Y), new(r.X + r.W, r.Y), new(r.X + r.W, r.Y + r.H), new(r.X, r.Y + r.H), new(r.X, r.Y) })], null);

    private static IPolygon2D Fold(IEnumerable<IPolygon2D> polygons) =>
        polygons.Aggregate((acc, p) => acc.Union(p, factory: F, gridSize: Grid));

    [Fact]
    public void The_cascade_equals_the_fold_of_Union()
    {
        Rectangles.Sample(rects =>
        {
            var polygons = rects.Select(Rect).ToArray();

            var cascade = polygons.UnionAll(factory: F, gridSize: Grid);
            var fold = Fold(polygons);

            Assert.True(cascade.IsValid());
            Assert.Equal(fold.Area(), cascade.Area(), 1e-6);
            Assert.True(cascade.SymDifference(fold, factory: F, gridSize: Grid).Area() <= 1e-6);
        });
    }

    [Fact]
    public void The_order_of_the_inputs_does_not_change_the_area()
    {
        Gen.Select(Rectangles, Gen.Int[0, 1000]).Sample((rects, rotation) =>
        {
            var polygons = rects.Select(Rect).ToArray();
            int k = rotation % polygons.Length;
            var reordered = polygons.Skip(k).Concat(polygons.Take(k)).Reverse().ToArray();

            var a = polygons.UnionAll(factory: F, gridSize: Grid);
            var b = reordered.UnionAll(factory: F, gridSize: Grid);

            Assert.Equal(a.Area(), b.Area(), 1e-6);
            Assert.True(a.SymDifference(b, factory: F, gridSize: Grid).Area() <= 1e-6);
        });
    }

    [Fact]
    public void The_same_sequence_gives_bit_identical_output()
    {
        Rectangles.Sample(rects =>
        {
            var polygons = rects.Select(Rect).ToArray();

            Assert.Equal(polygons.UnionAll(factory: F).ToWkt(), polygons.UnionAll(factory: F).ToWkt());
        });
    }

    [Fact]
    public void The_union_contains_every_input()
    {
        Rectangles.Sample(rects =>
        {
            var polygons = rects.Select(Rect).ToArray();

            var union = polygons.UnionAll(factory: F, gridSize: Grid);

            Assert.All(polygons, p => Assert.True(union.Covers(p)));
        });
    }
}
