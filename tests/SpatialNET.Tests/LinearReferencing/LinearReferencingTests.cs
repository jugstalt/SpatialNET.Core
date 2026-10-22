using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// Hand-written cases for linear referencing (ADR 0017). NTS has no M-based counterpart, so the
/// <c>...ByM</c> variants are covered here and by the property tests; the length variants also run against the
/// NTS oracle in <see cref="LinearReferencingFixtureTests"/>.
/// </summary>
[Covers("InterpolateByM")]
[Covers("ProjectToM")]
[Covers("SubstringByM")]
public class LinearReferencingTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private const double Tolerance = 1e-9;

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    private static MPolyline Lines(params Coordinate[][] paths) => new(paths);

    // ---------- Interpolate ----------

    [Fact]
    public void Interpolate_walks_along_the_line()
    {
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        Assert.Equal(C(0, 0), line.Interpolate(0));
        Assert.Equal(C(5, 0), line.Interpolate(5));
        Assert.Equal(C(10, 0), line.Interpolate(10));
        Assert.Equal(C(10, 5), line.Interpolate(15));
        Assert.Equal(C(10, 10), line.Interpolate(20));
    }

    [Fact]
    public void Interpolate_clamps_the_distance()
    {
        var line = Line(C(0, 0), C(10, 0));

        Assert.Equal(C(0, 0), line.Interpolate(-5)); // not "from the end" as in JTS
        Assert.Equal(C(10, 0), line.Interpolate(1000));
    }

    [Fact]
    public void Interpolate_interpolates_Z_and_M_only_if_both_ends_have_them()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100), C(20, 0, double.NaN, double.NaN));

        var first = line.Interpolate(5)!.Value;
        var second = line.Interpolate(15)!.Value;

        Assert.Equal((5d, 50d), (first.Z, first.M));
        Assert.False(second.HasZ);
        Assert.False(second.HasM);
    }

    [Fact]
    public void Interpolate_at_a_vertex_returns_the_values_of_that_vertex()
    {
        var line = Line(C(0, 0, double.NaN, 1), C(10, 0), C(20, 0, double.NaN, 3));

        var vertex = line.Interpolate(10)!.Value;
        var start = line.Interpolate(0)!.Value;

        Assert.False(vertex.HasM);  // the vertex has no M, and nothing is guessed
        Assert.Equal(1, start.M);   // the vertex keeps its own M although its neighbour has none
    }

    [Fact]
    public void Interpolate_lays_the_paths_end_to_end()
    {
        var line = Lines([C(0, 0), C(10, 0)], [C(20, 0), C(20, 10)]);

        Assert.Equal(C(20, 5), line.Interpolate(15));
        Assert.Equal(C(20, 10), line.Interpolate(20));
    }

    [Fact]
    public void Interpolate_on_the_boundary_of_two_paths_returns_the_end_of_the_earlier_path()
    {
        var line = Lines([C(0, 0), C(10, 0)], [C(20, 0), C(20, 10)]);

        Assert.Equal(C(10, 0), line.Interpolate(10));
        Assert.Equal(20, line.Interpolate(10.001)!.Value.X, 1e-9); // just after: the next path
        Assert.Equal(10, line.Project(C(10, 0)), 1e-12);
    }

    [Fact]
    public void Interpolate_of_empty_and_degenerate_lines()
    {
        Assert.Null(new MPolyline([]).Interpolate(1));
        Assert.Null(Lines([]).Interpolate(1));
        Assert.Equal(C(3, 4), Line(C(3, 4), C(3, 4)).Interpolate(10));
        Assert.Equal(C(3, 4), Line(C(3, 4)).Interpolate(0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Interpolate_rejects_a_non_finite_distance(double distance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(C(0, 0), C(1, 0)).Interpolate(distance));
    }

    [Fact]
    public void Null_lines_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Interpolate(1));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Project(C(0, 0)));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Substring(0, 1, ZM));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).InterpolateByM(1));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).ProjectToM(C(0, 0)));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).SubstringByM(0, 1, ZM));
    }

    // ---------- Project ----------

    [Fact]
    public void Project_returns_the_length_of_the_nearest_point()
    {
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        Assert.Equal(5, line.Project(C(5, 3)), Tolerance);
        Assert.Equal(15, line.Project(C(13, 5)), Tolerance);
        Assert.Equal(0, line.Project(C(-5, -5)), Tolerance);  // before the start
        Assert.Equal(20, line.Project(C(10, 50)), Tolerance); // behind the end
    }

    [Fact]
    public void Project_returns_the_smallest_length_among_equally_near_points()
    {
        var line = Line(C(0, 0), C(10, 0), C(5, 0)); // doubles back over (5 0)

        Assert.Equal(5, line.Project(C(5, 0)), Tolerance);
        Assert.Equal(0, Line(C(0, 0), C(10, 0), C(10, 10), C(0, 0)).Project(C(0, 0)), Tolerance);
    }

    [Fact]
    public void Project_accepts_a_point_geometry_and_rejects_a_non_finite_point()
    {
        var line = Line(C(0, 0), C(10, 0));

        Assert.Equal(4, line.Project(new MPoint(C(4, 7))), Tolerance);
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Project(C(double.NaN, 0)));
        Assert.Throws<ArgumentNullException>(() => line.Project((IPoint2D)null!));
    }

    [Fact]
    public void Project_of_empty_and_degenerate_lines()
    {
        Assert.True(double.IsNaN(new MPolyline([]).Project(C(1, 1))));
        Assert.Equal(0, Line(C(3, 4)).Project(C(9, 9)));
        Assert.Equal(0, Line(C(3, 4), C(3, 4)).Project(C(9, 9)));
    }

    [Fact]
    public void Project_counts_the_paths_end_to_end()
    {
        var line = Lines([C(0, 0), C(10, 0)], [C(20, 0), C(20, 10)]);

        Assert.Equal(15, line.Project(C(25, 5)), Tolerance);
    }

    // ---------- Substring ----------

    [Fact]
    public void Substring_cuts_a_piece_with_interpolated_ends_and_original_vertices_inside()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100), C(10, 10, 30, 250));

        var piece = line.Substring(5, 15, ZM);

        var coordinates = MGeo.Coordinates(piece);
        Assert.Equal(new[] { C(5, 0, 5, 50), C(10, 0, 10, 100), C(10, 5, 20, 175) }, coordinates);
    }

    [Fact]
    public void Substring_reversed_runs_in_the_opposite_direction()
    {
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        var piece = line.Substring(15, 5, ZM);

        Assert.Equal(new[] { C(10, 5), C(10, 0), C(5, 0) }, MGeo.Coordinates(piece));
    }

    [Fact]
    public void Substring_clamps_both_lengths()
    {
        var line = Line(C(0, 0), C(10, 0));

        Assert.Equal(new[] { C(0, 0), C(10, 0) }, MGeo.Coordinates(line.Substring(-5, 1000, ZM)));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(-3, 0)]
    [InlineData(10, 1000)]
    public void Substring_without_length_is_an_empty_polyline(double from, double to)
    {
        Assert.Equal(0, Line(C(0, 0), C(10, 0)).Substring(from, to, ZM).PathCount);
    }

    [Fact]
    public void Substring_has_one_path_per_touched_path()
    {
        var line = Lines([C(0, 0), C(10, 0)], [C(20, 0), C(20, 10)], [C(30, 0), C(30, 10)]);

        var piece = line.Substring(5, 15, ZM);

        Assert.Equal(2, piece.PathCount);
        Assert.Equal(new[] { C(5, 0), C(10, 0) }, MGeo.Coordinates(new MPolyline([Path(piece, 0)])));
        Assert.Equal(new[] { C(20, 0), C(20, 5) }, MGeo.Coordinates(new MPolyline([Path(piece, 1)])));
    }

    private static Coordinate[] Path(IPolyline2D line, int index) =>
        Enumerable.Range(0, line[index].PointCount).Select(i => MGeo.Of(line[index][i])).ToArray();

    [Fact]
    public void Substring_is_restricted_to_what_the_factory_supports()
    {
        var line = Line(C(0, 0, 1, 10), C(10, 0, 2, 20));

        var withoutM = MGeo.Coordinates(line.Substring(0, 10, new MFactory(supportsZ: true, supportsM: false)));
        var withoutZ = MGeo.Coordinates(line.Substring(0, 10, new MFactory(supportsZ: false, supportsM: true)));

        Assert.All(withoutM, c => Assert.False(c.HasM));
        Assert.All(withoutM, c => Assert.True(c.HasZ));
        Assert.All(withoutZ, c => Assert.False(c.HasZ));
        Assert.All(withoutZ, c => Assert.True(c.HasM));
    }

    [Fact]
    public void Substring_of_an_empty_line_is_empty()
    {
        Assert.Equal(0, new MPolyline([]).Substring(0, 10, ZM).PathCount);
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0, double.PositiveInfinity)]
    public void Substring_rejects_non_finite_lengths(double from, double to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(C(0, 0), C(1, 0)).Substring(from, to, ZM));
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(C(0, 0), C(1, 0)).SubstringByM(from, to, ZM));
    }

    // ---------- InterpolateByM ----------

    [Fact]
    public void InterpolateByM_finds_the_point_and_interpolates_Z()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100), C(10, 10, 30, 250));

        var point = line.InterpolateByM(40)!.Value;
        var onSecond = line.InterpolateByM(175)!.Value;

        Assert.Equal(C(4, 0, 4, 40), point);
        Assert.Equal(C(10, 5, 20, 175), onSecond);
    }

    [Fact]
    public void InterpolateByM_returns_the_first_hit_of_a_value_that_occurs_several_times()
    {
        // M rises to 10, falls back to 0 and rises again: 5 is reached at x = 5, 15 and 25
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10), C(20, 0, m: 0), C(30, 0, m: 10));

        Assert.Equal(5, line.InterpolateByM(5)!.Value.X, Tolerance);
        Assert.Equal(10, line.InterpolateByM(10)!.Value.X, Tolerance); // reached at a vertex: the end of the first segment
    }

    [Fact]
    public void InterpolateByM_works_with_falling_M_and_with_M_of_zero()
    {
        var line = Line(C(0, 0, m: 100), C(10, 0, m: 0));

        Assert.Equal(C(10, 0, m: 0), line.InterpolateByM(0));
        Assert.Equal(2.5, line.InterpolateByM(75)!.Value.X, Tolerance);
    }

    [Fact]
    public void InterpolateByM_on_a_segment_with_constant_M_answers_with_its_start()
    {
        var line = Line(C(0, 0, m: 7), C(10, 0, m: 7), C(20, 0, m: 9));

        Assert.Equal(C(0, 0, m: 7), line.InterpolateByM(7));
    }

    [Fact]
    public void InterpolateByM_uses_only_segments_with_M_at_both_ends_and_never_bridges_gaps()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10), C(20, 0), C(30, 0, m: 30), C(40, 0, m: 40));

        Assert.Null(line.InterpolateByM(20));  // would lie in the gap between 10 and 30
        Assert.Equal(35, line.InterpolateByM(35)!.Value.X, Tolerance);
    }

    [Fact]
    public void InterpolateByM_is_null_outside_the_range_or_without_M()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10));

        Assert.Null(line.InterpolateByM(-1));
        Assert.Null(line.InterpolateByM(11));
        Assert.Null(Line(C(0, 0), C(10, 0)).InterpolateByM(0));
        Assert.Null(new MPolyline([]).InterpolateByM(0));
    }

    [Fact]
    public void InterpolateByM_looks_through_all_paths_in_order()
    {
        var line = Lines([C(0, 0, m: 0), C(10, 0, m: 10)], [C(20, 0, m: 5), C(30, 0, m: 15)]);

        Assert.Equal(5, line.InterpolateByM(5)!.Value.X, Tolerance);   // first path wins
        Assert.Equal(27.5, line.InterpolateByM(12.5)!.Value.X, Tolerance);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InterpolateByM_rejects_a_non_finite_value(double m)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(C(0, 0, m: 0), C(1, 0, m: 1)).InterpolateByM(m));
    }

    // ---------- ProjectToM ----------

    [Fact]
    public void ProjectToM_returns_the_interpolated_M_at_the_nearest_point()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100));

        Assert.Equal(30, line.ProjectToM(C(3, 5)), Tolerance);
        Assert.Equal(0, line.ProjectToM(C(-4, 1)), Tolerance);   // before the start: M of the start
        Assert.Equal(100, line.ProjectToM(C(40, 1)), Tolerance); // behind the end
        Assert.Equal(60, line.ProjectToM(new MPoint(C(6, 1))), Tolerance);
    }

    [Fact]
    public void ProjectToM_is_NaN_if_the_line_has_no_M_there()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0), C(20, 0, m: 20));

        Assert.True(double.IsNaN(line.ProjectToM(C(5, 1))));
        Assert.True(double.IsNaN(Line(C(0, 0), C(10, 0)).ProjectToM(C(5, 1))));
        Assert.True(double.IsNaN(new MPolyline([]).ProjectToM(C(5, 1))));
        Assert.Equal(0, line.ProjectToM(C(0, 3))); // exactly at a vertex with M of zero
    }

    // ---------- SubstringByM ----------

    [Fact]
    public void SubstringByM_cuts_between_the_first_hits()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(10, 10, m: 200));

        var piece = line.SubstringByM(50, 150, ZM);

        Assert.Equal(new[] { C(5, 0, m: 50), C(10, 0, m: 100), C(10, 5, m: 150) }, MGeo.Coordinates(piece));
    }

    [Fact]
    public void SubstringByM_with_falling_order_reverses_the_piece()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100));

        var piece = line.SubstringByM(80, 20, ZM);

        Assert.Equal(new[] { C(8, 0, m: 80), C(2, 0, m: 20) }, MGeo.Coordinates(piece));
    }

    [Fact]
    public void SubstringByM_is_empty_if_a_value_has_no_hit_or_the_piece_has_no_length()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100));

        Assert.Equal(0, line.SubstringByM(20, 500, ZM).PathCount);
        Assert.Equal(0, line.SubstringByM(-10, 20, ZM).PathCount);
        Assert.Equal(0, line.SubstringByM(30, 30, ZM).PathCount);
        Assert.Equal(0, Line(C(0, 0), C(10, 0)).SubstringByM(0, 1, ZM).PathCount);
    }
}

[Collection(GlobalStateCollection.Name)]
public sealed class LinearReferencingDefaultFactoryTests : IDisposable
{
    private readonly IGeometryFactory? _saved = GeometryFactory.Default;

    public void Dispose() => GeometryFactory.Default = _saved;

    [Fact]
    public void Substring_uses_the_default_factory_and_fails_clearly_without_one()
    {
        var line = new MPolyline([[new Coordinate(0, 0), new Coordinate(10, 0)]]);

        GeometryFactory.Default = new MFactory(true, true);
        Assert.Equal(1, line.Substring(0, 5).PathCount);
        Assert.Equal(0, line.SubstringByM(0, 5).PathCount); // no M: empty, but no exception

        GeometryFactory.Default = null;
        Assert.Throws<InvalidOperationException>(() => line.Substring(0, 5));
        Assert.Throws<InvalidOperationException>(() => line.SubstringByM(0, 5));
    }
}
