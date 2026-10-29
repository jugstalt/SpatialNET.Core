using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.Predicates;

/// <summary>
/// The fast path of <see cref="PreparedGeometry.Contains"/> for an area: the position of the vertices and the segments of the area that
/// the other geometry meets decide, the DE-9IM matrix only where it touches the boundary without a witness. The answer is always
/// the one of the matrix, also for invalid input (overlapping and self-crossing rings, rings without area).
/// </summary>
public class PreparedContainsTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    [Theory]
    [InlineData("LINESTRING (1 1, 3 3)", true)]                          // inside
    [InlineData("LINESTRING (2 2, 12 8)", false)]                        // a vertex outside
    [InlineData("LINESTRING (0 2, 3 2)", true)]                          // an end on the boundary
    [InlineData("LINESTRING (0 0, 10 0)", false)]                        // on the boundary only
    [InlineData("LINESTRING (2 2, 8 8)", false)]                         // across the hole, both ends inside
    [InlineData("LINESTRING (2 4, 8 4)", true)]                          // along an edge of the hole
    [InlineData("LINESTRING (2 5, 5 5)", false)]                         // into the hole
    [InlineData("POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))", true)]
    [InlineData("POLYGON ((1 1, 9 1, 9 9, 1 9, 1 1))", false)]           // the hole lies in it
    [InlineData("POLYGON ((0 0, 3 0, 3 3, 0 3, 0 0))", true)]            // shares a corner and two edges
    [InlineData("POLYGON ((-1 1, 3 1, 3 3, -1 3, -1 1))", false)]        // reaches out
    [InlineData("MULTIPOINT ((1 1), (9 9))", true)]
    [InlineData("MULTIPOINT ((0 0), (10 10))", false)]                   // on the boundary only
    [InlineData("MULTIPOINT ((1 1), (5 5))", false)]                     // one in the hole
    public void Simple_cases_equal_the_matrix(string other, bool expected)
    {
        var area = G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))");
        var b = G(other);

        Assert.Equal(expected, area.Contains(b));
        Assert.Equal(expected, area.Prepare().Contains(b));
    }

    [Fact]
    public void A_line_that_crosses_many_spikes_of_a_star_is_decided_without_the_matrix_and_equals_it()
    {
        // a star with thin spikes: a short line between two spikes crosses both sides of one, with both ends inside
        int n = 400;
        var random = new Random(3);
        var ring = Enumerable.Range(0, n).Select(i =>
        {
            double a = 2 * Math.PI * i / n, r = i % 2 == 0 ? 100 : 60 + 10 * random.NextDouble();
            return new Coordinate(r * Math.Cos(a), r * Math.Sin(a));
        }).ToList();
        ring.Add(ring[0]);
        var star = F.CreatePolygon([new PolygonPart(ring.ToArray())], null);
        var prepared = star.Prepare();
        var lines = Enumerable.Range(0, 300).Select(_ =>
        {
            double a = random.NextDouble() * 2 * Math.PI, r = 40 + 50 * random.NextDouble(), da = (random.NextDouble() - 0.5) * 0.2;
            return F.CreatePolyline([new[] { new Coordinate(r * Math.Cos(a), r * Math.Sin(a)), new Coordinate(r * Math.Cos(a + da), r * Math.Sin(a + da)) }], null);
        }).ToArray();

        foreach (var line in lines)
        {
            Assert.Equal(star.Contains(line), prepared.Contains(line));
        }
    }

    // coordinates on a coarse grid: vertices on edges, shared edges and collinear runs are frequent
    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[0, 8], Gen.Int[0, 8]).Select((x, y) => new Coordinate(x, y));

    private static readonly Gen<Coordinate[]> Ring = Pt.Array[3, 6].Select(r => r.Append(r[0]).ToArray());

    private static readonly Gen<IGeometry2D> Area = Gen.OneOf(
        Gen.Select(Gen.Int[0, 4], Gen.Int[0, 4], Gen.Int[3, 4], Gen.Int[3, 4]).Select((x, y, w, h) => Rectangle(x, y, w, h)),
        Ring.Array[1, 3].Select(rings => (IGeometry2D)F.CreatePolygon(rings.Select(r => new PolygonPart(r)).ToArray(), null)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Ring).Select((x, y, hole) =>
            (IGeometry2D)F.CreatePolygon([new PolygonPart(new Coordinate[] { new(x, y), new(x + 8, y), new(x + 8, y + 8), new(x, y + 8), new(x, y) }, new ReadOnlyMemory<Coordinate>[] { hole })], null)));

    private static readonly Gen<IGeometry2D> Other = Gen.OneOf(
        Pt.Array[1, 4].Select(p => (IGeometry2D)F.CreateMultiPoint(p, null)),
        Pt.Array[2, 5].Array[1, 2].Select(paths => (IGeometry2D)F.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null)),
        Ring.Array[1, 2].Select(rings => (IGeometry2D)F.CreatePolygon(rings.Select(r => new PolygonPart(r)).ToArray(), null)),
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 6], Gen.Int[1, 3], Gen.Int[1, 3]).Select((x, y, w, h) => Rectangle(x, y, w, h)));

    private static IGeometry2D Rectangle(int x, int y, int w, int h) =>
        F.CreatePolygon([new PolygonPart(new Coordinate[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x, y) })], null);

    /// <summary>
    /// The answer of the matrix behind the envelope test that the prepared geometry has always made first: for invalid input, a
    /// spike that cancels itself (even-odd) may reach out of the envelope of the area, then the prepared answer is false (ADR 0013).
    /// </summary>
    private static bool Unprepared(IGeometry2D a, IGeometry2D b)
    {
        var (ea, eb) = (a.Envelope(), b.Envelope());
        return eb.MinX >= ea.MinX && eb.MaxX <= ea.MaxX && eb.MinY >= ea.MinY && eb.MaxY <= ea.MaxY && a.Contains(b);
    }

    private static string Print((IGeometry2D A, IGeometry2D B) t) => $"{t.A.ToWkt()} / {t.B.ToWkt()}";

    [Fact]
    public void Prepared_Contains_equals_the_matrix_for_random_input_on_a_coarse_grid()
    {
        Gen.Select(Area, Other).Sample((a, b) => a.Prepare().Contains(b) == Unprepared(a, b), iter: 20_000, print: Print);
    }

    [Fact]
    public void Prepared_Contains_equals_the_matrix_for_shifted_and_scaled_input()
    {
        // the same shapes in UTM metres with a fine step: the exact crossing test works on large coordinates too
        static IGeometry2D Move(IGeometry2D g) => g.Transform(c => new Coordinate(533_000 + c.X * 0.37, 5_212_000 + c.Y * 0.37), F);

        Gen.Select(Area, Other).Sample((a, b) => Move(a).Prepare().Contains(Move(b)) == Unprepared(Move(a), Move(b)), iter: 5_000, print: Print);
    }
}
