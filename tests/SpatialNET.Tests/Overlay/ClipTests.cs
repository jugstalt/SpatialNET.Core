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
/// <c>Clip</c> against NTS <c>OverlayNG</c> intersecting the same rectangle on the same grid (ADR 0022/0008). Invalid input
/// (tag self-crossing) has no NTS reference; there only a valid result is required.
/// </summary>
[Covers("ToPolygon")]
public class ClipFixtureTests
{
    public static TheoryData<string, string> ClipFixtures => FixtureCatalog.Theory("clip");

    [Theory]
    [MemberData(nameof(ClipFixtures))]
    public void Clip(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var geometry = fixture.ReadA(factory);
        var rect = new Envelope2D(fixture.Param("minX", 0), fixture.Param("minY", 0), fixture.Param("maxX", 1), fixture.Param("maxY", 1));
        var nts = Nts.ToNts(geometry);
        var ntsRect = NetTopologySuite.Geometries.GeometryFactory.Default.ToGeometry(new NetTopologySuite.Geometries.Envelope(rect.MinX, rect.MaxX, rect.MinY, rect.MaxY));
        double max = new[] { rect.MinX, rect.MinY, rect.MaxX, rect.MaxY }.Concat(nts.IsEmpty ? [] : new[]
        {
            nts.EnvelopeInternal.MinX, nts.EnvelopeInternal.MinY, nts.EnvelopeInternal.MaxX, nts.EnvelopeInternal.MaxY,
        }).Max(Math.Abs);
        double gridSize = GridModel.AutoSize(max);

        var actual = geometry.Clip(rect, factory, gridSize);

        if (fixture.Tags.Contains("self-crossing"))
        {
            Assert.True(actual.IsValid(), "The result of a clip must be valid (ADR 0003).");
            return;
        }

        Geometry expected;
        try
        {
            expected = new OverlayNG(nts, ntsRect, new PrecisionModel(1 / gridSize), SpatialFunction.Intersection) { StrictMode = true }.GetResult();
        }
        catch (TopologyException)
        {
            Assert.True(actual.IsValid(), "The result of a clip must be valid (ADR 0003).");
            return;
        }

        // strict semantics (ADR 0012): pure touching and crossing points of lines and areas fall away
        int dimension = nts is IPolygonal ? 2 : nts is ILineal ? 1 : 0;
        if (!expected.IsEmpty && (int)expected.Dimension < dimension)
        {
            expected = expected.Factory.CreateGeometryCollection();
        }

        OracleAssert.SameGeometry(expected, actual, gridSize, fixture);
    }
}

public class ClipTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private static readonly Envelope2D Rect = new(0, 0, 10, 10);

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static (double, double)[] Xy(IGeometry2D g) =>
        Internal.GeometryAccess.AllPoints(g).Select(p => (p.X, p.Y)).Distinct().Order().ToArray();

    [Fact]
    public void A_polygon_is_cut_at_the_border()
    {
        var clipped = ((IPolygon2D)G("POLYGON ((5 5, 20 5, 20 20, 5 20, 5 5))")).Clip(Rect, F);

        Assert.True(clipped.IsValid());
        Assert.Equal(25, clipped.Area(), 6);
        Assert.Equal("POLYGON ((5 5, 10 5, 10 10, 5 10, 5 5))", clipped.ToWkt());
    }

    [Fact]
    public void A_geometry_inside_is_returned_without_rounding_and_one_outside_is_empty()
    {
        const string wkt = "POLYGON ((1.123456789012345 1.5, 8.987654321098765 1.5, 8.5 8.25, 1.123456789012345 1.5))";
        var polygon = (IPolygon2D)G(wkt);

        var inside = polygon.Clip(Rect, F);
        var outside = polygon.Clip(new Envelope2D(50, 50, 60, 60), F);

        Assert.Equal(Xy(polygon), Xy(inside)); // not rounded to the grid
        Assert.Equal(polygon.Area(), inside.Area(), 12);
        Assert.Equal(0, outside.RingCount);
    }

    [Fact]
    public void A_polygon_that_only_touches_the_border_has_no_area_left()
    {
        Assert.Equal(0, ((IPolygon2D)G("POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))")).Clip(Rect, F).RingCount);
    }

    [Fact]
    public void A_polyline_keeps_the_parts_inside_and_on_the_border()
    {
        var through = ((IPolyline2D)G("LINESTRING (-5 5, 15 5)")).Clip(Rect, F);
        var along = ((IPolyline2D)G("LINESTRING (-5 0, 15 0)")).Clip(Rect, F);
        var outside = ((IPolyline2D)G("LINESTRING (20 20, 30 30)")).Clip(Rect, F);

        Assert.Equal("LINESTRING (0 5, 10 5)", through.ToWkt());
        Assert.Equal("LINESTRING (0 0, 10 0)", along.ToWkt());   // on the border: it counts
        Assert.Equal(0, outside.PathCount);
    }

    [Fact]
    public void Points_are_filtered_exactly_including_the_border()
    {
        var points = (IMultiPoint2D)G("MULTIPOINT ((0 0), (10 10), (5 5), (10.0000001 5), (-1 5))");

        var clipped = points.Clip(Rect, F);

        Assert.Equal([(0d, 0d), (5d, 5d), (10d, 10d)], Xy(clipped));
        Assert.IsAssignableFrom<IPoint2D>(G("POINT (5 5)").Clip(Rect, F));
        Assert.IsAssignableFrom<IMultiPoint2D>(G("POINT (50 5)").Clip(Rect, F));  // outside: an empty multipoint
    }

    [Fact]
    public void An_envelope_without_area_clips_areas_and_lines_to_nothing_but_still_filters_points()
    {
        var flat = new Envelope2D(0, 5, 10, 5);

        Assert.Equal(0, ((IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))")).Clip(flat, F).RingCount);
        Assert.Equal(0, ((IPolyline2D)G("LINESTRING (-5 5, 15 5)")).Clip(flat, F).PathCount);
        Assert.Single(Xy(((IMultiPoint2D)G("MULTIPOINT ((3 5), (3 6))")).Clip(flat, F)));
    }

    [Fact]
    public void An_empty_envelope_gives_an_empty_result_and_an_invalid_one_throws()
    {
        var polygon = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        Assert.Equal(0, polygon.Clip(Envelope2D.Empty, F).RingCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.Clip(new Envelope2D(5, 0, 1, 10), F));
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.Clip(new Envelope2D(0, 0, double.PositiveInfinity, 1), F));
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.Clip(new Envelope2D(0, 0, 10, 10), F, gridSize: 0));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Clip(Rect, F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Clip(Rect, F));
    }

    [Fact]
    public void The_general_overload_dispatches_by_kind()
    {
        Assert.IsAssignableFrom<IPolygon2D>(G("POLYGON ((5 5, 20 5, 20 20, 5 5))").Clip(Rect, F));
        Assert.IsAssignableFrom<IPolyline2D>(G("LINESTRING (-5 5, 15 5)").Clip(Rect, F));
        Assert.IsAssignableFrom<IMultiPoint2D>(G("MULTIPOINT ((1 1))").Clip(Rect, F));
        Assert.IsAssignableFrom<IPoint2D>(G("POINT (1 1)").Clip(Rect, F));
    }

    [Fact]
    public void Z_and_M_are_carried_where_the_border_cuts()
    {
        var polygon = MGeo.Polygon(MGeo.Square(5, 5, 20, 20, MGeo.WithM));

        var clipped = MGeo.Coordinates(polygon.Clip(Rect, ZM));
        var dropped = MGeo.Coordinates(polygon.Clip(Rect, new MFactory(supportsZ: false, supportsM: false)));

        Assert.NotEmpty(clipped);
        // the vertices from the edges of the polygon carry the interpolated M; a corner of the rectangle has none
        Assert.Contains(clipped, c => c is { X: 5, Y: 5 } && c.HasM);
        Assert.All(clipped.Where(c => c.HasM), c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, 1e-6));
        Assert.Contains(clipped, c => c is { X: 10, Y: 10 } && !c.HasM);
        Assert.All(dropped, c => Assert.False(c.HasM));
    }

    [Fact]
    public void ToPolygon_creates_a_counter_clockwise_rectangle_and_nothing_for_an_envelope_without_area()
    {
        Assert.Equal("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", Rect.ToPolygon(F).ToWkt());
        Assert.Equal(0, Envelope2D.Empty.ToPolygon(F).RingCount);
        Assert.Equal(0, new Envelope2D(0, 0, 10, 0).ToPolygon(F).RingCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Envelope2D(5, 0, 1, 10).ToPolygon(F));
    }
}

public class ClipPropertyTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private const double Grid = 1e-6;

    private static readonly Gen<Envelope2D> Rectangles =
        Gen.Select(Gen.Int[-20, 20], Gen.Int[-20, 20], Gen.Int[1, 30], Gen.Int[1, 30]).Select(t => new Envelope2D(t.Item1, t.Item2, t.Item1 + t.Item3, t.Item2 + t.Item4));

    private static IPolygon2D Polygon(Envelope2D e) => e.ToPolygon(F);

    [Fact]
    public void Clip_equals_Intersection_with_the_rectangle_and_stays_inside_it()
    {
        Gen.Select(Rectangles, Rectangles).Sample((shape, window) =>
        {
            var polygon = Polygon(shape);

            var clipped = polygon.Clip(window, F, Grid);
            var expected = polygon.Intersection(Polygon(window), factory: F, gridSize: Grid);

            Assert.Equal(expected.Area(), clipped.Area(), 1e-6);
            Assert.True(clipped.IsValid());
            Assert.True(clipped.RingCount == 0 || window.Contains(clipped.Envelope()));
        });
    }

    [Fact]
    public void A_geometry_inside_comes_back_with_identical_coordinates()
    {
        Gen.Select(Rectangles, Gen.Int[1, 10]).Sample((window, margin) =>
        {
            // off-grid coordinates strictly inside the window
            var inner = new Envelope2D(window.MinX + window.Width * 0.123456789, window.MinY + window.Height * 0.234567891 + margin * 1e-3,
                window.MinX + window.Width * 0.6, window.MinY + window.Height * 0.7);
            var polygon = Polygon(window).Clip(window, F); // normalized
            var small = Polygon(inner);

            var clipped = small.Clip(window, F);

            Assert.Equal(Internal.GeometryAccess.AllPoints(small).Select(p => (p.X, p.Y)).Order(), Internal.GeometryAccess.AllPoints(clipped).Select(p => (p.X, p.Y)).Order());
            Assert.Equal(window.ToPolygon(F).Area(), polygon.Area(), 9);
        });
    }
}
