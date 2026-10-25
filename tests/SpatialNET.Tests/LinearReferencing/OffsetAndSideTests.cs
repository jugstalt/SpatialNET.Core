using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>
/// <c>Interpolate</c> and <c>InterpolateByM</c> with an offset, and <c>ProjectDetailed</c> (ADR 0035). NTS has
/// <c>ExtractPoint(index, offset)</c> for the first, and a test compares inside the segments; the other rules of this library are
/// checked by hand and by a round-trip property.
/// </summary>
[Covers("ProjectDetailed")]
public class OffsetAndSideTests
{
    private const double Tolerance = 1e-9;

    private static Coordinate C(double x, double y, double z = double.NaN, double m = double.NaN) => new(x, y, z, m);

    private static MPolyline Line(params Coordinate[] path) => new([path]);

    // ---------- Interpolate with an offset ----------

    [Fact]
    public void A_positive_offset_is_to_the_left_of_the_direction_and_a_negative_one_to_the_right()
    {
        var east = Line(C(0, 0), C(10, 0));

        Assert.Equal(C(5, 2), east.Interpolate(5, 2));
        Assert.Equal(C(5, -2), east.Interpolate(5, -2));
        Assert.Equal(C(5, 0), east.Interpolate(5, 0));

        var north = Line(C(0, 0), C(0, 10));
        Assert.Equal(C(-2, 5), north.Interpolate(5, 2));   // left of north is west
    }

    [Fact]
    public void An_offset_of_0_is_Interpolate()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100), C(10, 10, 20, 200));

        foreach (double d in new[] { 0d, 3, 10, 14, 20, 99 })
        {
            Assert.Equal(line.Interpolate(d), line.Interpolate(d, 0));
        }
    }

    [Fact]
    public void At_a_vertex_the_segment_that_ends_there_gives_the_direction()
    {
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        Assert.Equal(C(10, 1), line.Interpolate(10, 1));                       // eastwards: left is +y
        var after = line.Interpolate(10.000001, 1)!.Value;                      // northwards: left is -x
        Assert.Equal(9, after.X, 1e-5);
        Assert.Equal(0.000001, after.Y, 1e-9);
    }

    [Fact]
    public void At_the_start_the_first_segment_and_beyond_the_ends_the_clamped_segments_give_the_direction()
    {
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        Assert.Equal(C(0, 1), line.Interpolate(-5, 1));
        Assert.Equal(C(9, 10), line.Interpolate(500, 1));    // clamped to the end; the last segment runs north
    }

    [Fact]
    public void Z_and_M_are_those_of_the_point_on_the_line_and_the_paths_are_laid_end_to_end()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100));
        var p = line.Interpolate(5, 3)!.Value;

        Assert.Equal((5d, 3d, 5d, 50d), (p.X, p.Y, p.Z, p.M));

        var paths = new MPolyline([[C(0, 0), C(10, 0)], [C(20, 0), C(20, 10)]]);
        Assert.Equal(C(19, 5), paths.Interpolate(15, 1));
    }

    [Fact]
    public void Segments_without_length_are_skipped_and_a_line_without_direction_gives_null_for_an_offset()
    {
        var withDuplicates = Line(C(0, 0), C(0, 0), C(10, 0), C(10, 0));
        Assert.Equal(C(0, 1), withDuplicates.Interpolate(0, 1));
        Assert.Equal(C(10, 1), withDuplicates.Interpolate(10, 1));

        var point = Line(C(5, 5), C(5, 5));
        Assert.Equal(C(5, 5), point.Interpolate(0, 0));
        Assert.Null(point.Interpolate(0, 1));
        Assert.Null(new MPolyline([]).Interpolate(0, 1));
    }

    [Fact]
    public void InterpolateByM_with_an_offset_moves_the_point_of_the_first_hit()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 100), C(10, 10, m: 200));

        Assert.Equal(C(5, 2, m: 50), line.InterpolateByM(50, 2));
        Assert.Equal(C(9, 5, m: 150), line.InterpolateByM(150, 1));
        Assert.Null(line.InterpolateByM(500, 1));
        Assert.Equal(line.InterpolateByM(50), line.InterpolateByM(50, 0));
    }

    [Fact]
    public void Bad_offsets_throw()
    {
        var line = Line(C(0, 0), C(10, 0));

        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Interpolate(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Interpolate(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Interpolate(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.InterpolateByM(0, double.PositiveInfinity));
    }

    // ---------- ProjectDetailed ----------

    [Fact]
    public void ProjectDetailed_gives_position_point_distance_and_the_signed_offset()
    {
        var line = Line(C(0, 0, 0, 0), C(10, 0, 10, 100));

        var left = line.ProjectDetailed(C(4, 3))!.Value;
        Assert.Equal((4d, 3d, 3d), (left.Position, left.Distance, left.Offset));
        Assert.Equal((4d, 0d, 4d, 40d), (left.Point.X, left.Point.Y, left.Point.Z, left.Point.M));

        var right = line.ProjectDetailed(C(4, -2))!.Value;
        Assert.Equal((4d, 2d, -2d), (right.Position, right.Distance, right.Offset));

        var on = line.ProjectDetailed(C(7, 0))!.Value;
        Assert.Equal((7d, 0d, 0d), (on.Position, on.Distance, on.Offset));
    }

    [Fact]
    public void ProjectDetailed_agrees_with_Project_and_ProjectToM()
    {
        var line = Line(C(0, 0, m: 0), C(10, 0, m: 10), C(10, 10, m: 30));
        var p = C(12, 4);

        var r = line.ProjectDetailed(p)!.Value;

        Assert.Equal(line.Project(p), r.Position);
        Assert.Equal(line.ProjectToM(p), r.Point.M);
    }

    [Fact]
    public void Beyond_the_end_the_side_is_that_of_the_extension_of_the_end_segment()
    {
        var line = Line(C(0, 0), C(10, 0));

        var above = line.ProjectDetailed(C(12, 1))!.Value;
        Assert.Equal((10d, Math.Sqrt(5), Math.Sqrt(5)), (above.Position, above.Distance, above.Offset));

        var below = line.ProjectDetailed(C(12, -1))!.Value;
        Assert.Equal(-Math.Sqrt(5), below.Offset, Tolerance);

        var extension = line.ProjectDetailed(C(12, 0))!.Value;   // exactly on the extension: counts as left
        Assert.Equal((10d, 2d, 2d), (extension.Position, extension.Distance, extension.Offset));
    }

    [Fact]
    public void The_side_is_that_of_the_segment_of_the_nearest_point()
    {
        // a loop: east, then back west along y = 10
        var line = Line(C(0, 0), C(100, 0), C(100, 10), C(0, 10));

        Assert.Equal(3, line.ProjectDetailed(C(40, 3))!.Value.Offset, Tolerance);        // east: y above is left
        Assert.Equal(3, line.ProjectDetailed(C(40, 7))!.Value.Offset, Tolerance);        // west: y below (7) is left of westward travel at y = 10
        Assert.Equal(-3, line.ProjectDetailed(C(40, 13))!.Value.Offset, Tolerance);
    }

    [Fact]
    public void ProjectDetailed_with_a_window_picks_the_pass()
    {
        var line = Line(C(0, 0), C(100, 0), C(100, 1), C(0, 1));   // the two passes are 1 apart; the click is between them, nearer the first
        var click = C(40, 0.4);

        var first = line.ProjectDetailed(click)!.Value;
        var second = line.ProjectDetailed(click, 101, 201)!.Value;

        Assert.Equal((40d, 0.4), (first.Position, Math.Round(first.Distance, 9)));
        Assert.Equal(0.4, first.Offset, Tolerance);                    // left of the eastward pass
        Assert.Equal(161d, second.Position, Tolerance);
        Assert.Equal(0.6, second.Distance, Tolerance);
        Assert.Equal(0.6, second.Offset, Tolerance);                   // below y = 1 on the way west is left
    }

    [Fact]
    public void ProjectDetailed_prefers_a_real_segment_to_a_duplicate_vertex_for_the_side()
    {
        var line = Line(C(0, 0), C(0, 0), C(10, 0));

        Assert.Equal(2, line.ProjectDetailed(C(0, 2))!.Value.Offset, Tolerance);   // not "no direction"
        Assert.Equal(-2, line.ProjectDetailed(C(0, -2))!.Value.Offset, Tolerance);
    }

    [Fact]
    public void ProjectDetailed_null_overloads_and_errors()
    {
        Assert.Null(new MPolyline([]).ProjectDetailed(C(0, 0)));
        Assert.Null(new MPolyline([]).ProjectDetailed(C(0, 0), 0, 1));

        var line = Line(C(0, 0), C(10, 0));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).ProjectDetailed(C(0, 0)));
        Assert.Throws<ArgumentNullException>(() => line.ProjectDetailed((IPoint2D)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.ProjectDetailed(C(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.ProjectDetailed(C(0, 0), double.NaN, 1));
        Assert.Equal(5, line.ProjectDetailed(new MPoint(C(5, 1)))!.Value.Position, Tolerance);
        Assert.Equal(5, line.ProjectDetailed(new MPoint(C(5, 1)), 0, 10)!.Value.Position, Tolerance);
    }

    // ---------- Oracle ----------

    [Fact]
    public void Interpolate_with_an_offset_agrees_with_the_NTS_LengthIndexedLine_inside_a_segment()
    {
        var rng = new Random(2035);
        for (int n = 0; n < 200; n++)
        {
            var path = new List<Coordinate> { C(0, 0) };
            for (int i = 0; i < 5; i++)
            {
                path.Add(C(path[^1].X + 2 + rng.NextDouble() * 6, path[^1].Y + (rng.NextDouble() - 0.5) * 8));
            }

            var line = Line(path.ToArray());
            var nts = Nts.Read("LINESTRING (" + string.Join(", ", path.Select(c => FormattableString.Invariant($"{c.X} {c.Y}"))) + ")");
            double length = line.Length();
            double distance = rng.NextDouble() * length;
            double offset = (rng.NextDouble() - 0.5) * 6;

            var expected = new NetTopologySuite.LinearReferencing.LengthIndexedLine(nts).ExtractPoint(distance, offset);
            var actual = line.Interpolate(distance, offset)!.Value;

            Assert.Equal(expected.X, actual.X, 1e-9);
            Assert.Equal(expected.Y, actual.Y, 1e-9);
        }
    }

    [Fact]
    public void At_a_vertex_this_library_takes_the_earlier_segment_and_NTS_the_following_one()
    {
        // divergence (ADR 0035): every boundary belongs to the end of the earlier part here (ADR 0017); NTS uses the segment that starts there
        var nts = Nts.Read("LINESTRING (0 0, 10 0, 10 10)");
        var line = Line(C(0, 0), C(10, 0), C(10, 10));

        var ntsPoint = new NetTopologySuite.LinearReferencing.LengthIndexedLine(nts).ExtractPoint(10, 1);
        var point = line.Interpolate(10, 1)!.Value;

        Assert.Equal((9d, 0d), (ntsPoint.X, ntsPoint.Y));    // the following segment runs north, left is west
        Assert.Equal((10d, 1d), (point.X, point.Y));         // the earlier segment runs east, left is north
    }

    // ---------- Round trip ----------

    [Fact]
    public void Interpolate_of_position_and_offset_gives_the_point_back_when_it_lies_beside_the_line()
    {
        var rng = new Random(35);
        int checkedPoints = 0;
        for (int n = 0; n < 400; n++)
        {
            var path = new List<Coordinate> { C(0, 0) };
            for (int i = 0; i < 6; i++)
            {
                path.Add(C(path[^1].X + 2 + rng.NextDouble() * 6, path[^1].Y + (rng.NextDouble() - 0.5) * 6));
            }

            var line = Line(path.ToArray());
            var p = C(rng.NextDouble() * 40, rng.NextDouble() * 20 - 10);
            var r = line.ProjectDetailed(p)!.Value;

            // skip points whose nearest point is a vertex or an end: there the perpendicular is not unique
            bool atVertex = path.Any(v => Math.Abs(v.X - r.Point.X) < 1e-9 && Math.Abs(v.Y - r.Point.Y) < 1e-9);
            if (atVertex)
            {
                continue;
            }

            checkedPoints++;
            var back = line.Interpolate(r.Position, r.Offset)!.Value;
            Assert.Equal(p.X, back.X, 1e-7);
            Assert.Equal(p.Y, back.Y, 1e-7);
        }

        Assert.True(checkedPoints > 100, $"only {checkedPoints} points were checked");
    }
}
