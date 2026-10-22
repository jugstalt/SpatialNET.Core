using CsCheck;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>RemoveHoles, RemoveSmallParts, SnapToGrid and ReducePrecision (ADR 0027).</summary>
public class CleaningTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private const string Holey = "POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2), (6 6, 12 6, 12 12, 6 12, 6 6))"; // holes of area 4 and 36

    // ---------- RemoveHoles ----------

    [Fact]
    [Covers("RemoveHoles")]
    public void RemoveHoles_removes_the_small_ones_or_all()
    {
        var polygon = (IPolygon2D)G(Holey);

        Assert.Equal(400, polygon.RemoveHoles(factory: F).Area(), 9);                  // all
        Assert.Equal(1, polygon.RemoveHoles(factory: F).RingCount);
        Assert.Equal(364, polygon.RemoveHoles(5, F).Area(), 9);                         // only the hole of area 4
        Assert.Equal(2, polygon.RemoveHoles(5, F).RingCount);
        Assert.Equal(360, polygon.RemoveHoles(0, F).Area(), 9);                         // none
        Assert.Equal(3, polygon.RemoveHoles(0, F).RingCount);
        Assert.Equal(400, polygon.RemoveHoles(36.0000001, F).Area(), 9);                // both: 36 is smaller than the limit
        Assert.Equal(364, polygon.RemoveHoles(36, F).Area(), 9);                        // equal to the limit stays

        Assert.Equal("POLYGON EMPTY", ((IPolygon2D)G("POLYGON EMPTY")).RemoveHoles(factory: F).ToWkt());
    }

    [Fact]
    [Covers("RemoveHoles")]
    public void RemoveHoles_rejects_bad_limits_and_null()
    {
        var polygon = (IPolygon2D)G(Holey);

        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.RemoveHoles(-1, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.RemoveHoles(double.NaN, F));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).RemoveHoles(factory: F));
    }

    // ---------- RemoveSmallParts ----------

    [Fact]
    [Covers("RemoveSmallParts")]
    public void RemoveSmallParts_of_polygons_and_lines()
    {
        var parts = (IPolygon2D)G("MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2)), ((20 0, 22 0, 22 2, 20 2, 20 0)), ((30 0, 40 0, 40 10, 30 10, 30 0)))"); // net areas 64, 4, 100

        Assert.Equal(164, parts.RemoveSmallParts(10, F).Area(), 9);
        Assert.Equal(100, parts.RemoveSmallParts(65, F).Area(), 9);
        Assert.Equal(168, parts.RemoveSmallParts(0, F).Area(), 9);
        Assert.Equal(168, parts.RemoveSmallParts(4, F).Area(), 9); // equal to the limit stays

        var lines = (IPolyline2D)G("MULTILINESTRING ((0 0, 3 4), (0 0, 1 0), (10 10, 10 20, 20 20))");   // lengths 5, 1, 20
        Assert.Equal(25, lines.RemoveSmallParts(2, F).Length(), 9);
        Assert.Equal(2, lines.RemoveSmallParts(2, F).PathCount);
        Assert.Equal(20, ((IGeometry2D)lines).RemoveSmallParts(6, F).Length(), 9);

        Assert.Equal("MULTIPOINT ((1 1), (2 2))", G("MULTIPOINT ((1 1), (2 2))").RemoveSmallParts(100, F).ToWkt());
        Assert.Equal("POINT (1 1)", G("POINT (1 1)").RemoveSmallParts(100, F).ToWkt());
        Assert.Throws<ArgumentOutOfRangeException>(() => parts.RemoveSmallParts(-1, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.RemoveSmallParts(double.NaN, F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).RemoveSmallParts(1, F));
    }

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-40, 40], Gen.Int[-40, 40], Gen.Double[0, 1]).Select((x, y, e) => new Coordinate(x + e, y - e / 3));

    private static IGeometry2D ValidPolygon(Coordinate[] shell) =>
        F.CreatePolygon([new PolygonPart(shell.Append(shell[0]).ToArray())], null).MakeValid(factory: F, gridSize: 1e-3);

    private static IEnumerable<NetTopologySuite.Geometries.Polygon> NtsParts(IGeometry2D g) =>
        Nts.ToNts(g) switch
        {
            NetTopologySuite.Geometries.Polygon p => p.IsEmpty ? [] : [p],
            MultiPolygon m => m.Geometries.Cast<NetTopologySuite.Geometries.Polygon>(),
            _ => [],
        };

    private static double RingArea(LineString ring) => new NetTopologySuite.Geometries.Polygon((LinearRing)ring).Area;

    [Fact]
    [Covers("RemoveHoles")]
    [Covers("RemoveSmallParts")]
    public void Both_agree_with_an_independent_computation_on_the_NTS_parts()
    {
        Gen.Select(Pt.Array[3, 12], Gen.Double[0, 400]).Sample((shell, limit) =>
        {
            var polygon = (IPolygon2D)ValidPolygon(shell);
            var parts = NtsParts(polygon).ToList();
            if (parts.Count == 0) return;

            // area of every part with the holes of at least `limit` kept
            double expectedHoles = parts.Sum(p => RingArea(p.ExteriorRing)
                - Enumerable.Range(0, p.NumInteriorRings).Select(i => RingArea(p.GetInteriorRingN(i))).Where(a => a >= limit).Sum());
            var noHoles = polygon.RemoveHoles(limit, F);
            Assert.True(Math.Abs(noHoles.Area() - expectedHoles) <= 1e-9 * (1 + expectedHoles), $"{noHoles.Area():R} vs {expectedHoles:R}");
            Assert.True(noHoles.IsValid());

            double expectedParts = parts.Where(p => p.Area >= limit).Sum(p => p.Area);
            var big = polygon.RemoveSmallParts(limit, F);
            Assert.True(Math.Abs(big.Area() - expectedParts) <= 1e-9 * (1 + expectedParts), $"{big.Area():R} vs {expectedParts:R}");
            Assert.True(big.IsValid());
        });
    }

    // ---------- SnapToGrid ----------

    [Fact]
    [Covers("SnapToGrid")]
    public void SnapToGrid_rounds_merges_and_drops_what_collapses()
    {
        Assert.Equal("POINT (1.5 -2)", G("POINT (1.4 -2.2)").SnapToGrid(0.5, F).ToWkt());
        Assert.Equal("POINT (2 -2)", G("POINT (1.5 -1.5)").SnapToGrid(1, F).ToWkt());           // halves away from zero
        Assert.Equal("LINESTRING (0 0, 10 0)", G("LINESTRING (0.1 0.1, 3.9 0.2, 4.1 -0.1, 10 0.3)").SnapToGrid(10, F).ToWkt());   // 3 repeats merge
        Assert.Equal("LINESTRING EMPTY", G("LINESTRING (0.1 0.1, 0.2 0.3)").SnapToGrid(1, F).ToWkt());                              // collapsed
        Assert.Equal("MULTIPOINT ((1 1), (1 1))", G("MULTIPOINT ((0.9 1.1), (1.1 0.9))").SnapToGrid(1, F).ToWkt());               // duplicates stay
        Assert.Equal("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", G("POLYGON ((0.2 0.1, 9.9 0.2, 10.1 9.8, 0.3 10.2, 0.2 0.1))").SnapToGrid(1, F).ToWkt());
        Assert.Equal("POLYGON EMPTY", G("POLYGON ((0 0, 0.3 0, 0.3 0.3, 0 0))").SnapToGrid(1, F).ToWkt());                         // shell collapses
        // a hole that collapses is dropped, the shell stays
        var dropped = G("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5.2 5, 5.2 5.2, 5 5))").SnapToGrid(1, F);
        Assert.Equal(1, ((IPolygon2D)dropped).RingCount);
    }

    [Fact]
    [Covers("SnapToGrid")]
    public void SnapToGrid_rejects_bad_grids_and_null()
    {
        var polygon = G(Holey);

        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.SnapToGrid(0, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.SnapToGrid(-1, F));
        Assert.Throws<ArgumentOutOfRangeException>(() => polygon.SnapToGrid(double.NaN, F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).SnapToGrid(1, F));
        Assert.Throws<InvalidOperationException>(() => G("POINT (1 1)").SnapToGrid(double.Epsilon, F));
    }

    private static bool OnGrid(double value, double grid)
    {
        double q = value / grid;
        return Math.Abs(q - Math.Round(q)) <= 1e-9;
    }

    [Fact]
    [Covers("SnapToGrid")]
    public void SnapToGrid_puts_every_vertex_on_the_grid_near_the_original_and_is_idempotent()
    {
        Gen.Select(Pt.Array[3, 10], Gen.Int[0, 3]).Sample((points, k) =>
        {
            double grid = new[] { 0.25, 1, 2.5, 7 }[k];
            var geometry = F.CreatePolyline([points], null);

            var snapped = geometry.SnapToGrid(grid, F);

            foreach (var p in Internal.GeometryAccess.AllPoints(snapped))
            {
                Assert.True(OnGrid(p.X, grid) && OnGrid(p.Y, grid), $"({p.X:R} {p.Y:R}) not on grid {grid}");
                // every snapped vertex is within half a cell diagonal of a vertex of the input
                double near = points.Min(c => Internal.Rings.Distance(c, new Coordinate(p.X, p.Y)));
                Assert.True(near <= grid * Math.Sqrt(2) / 2 + 1e-9, $"{near}");
            }

            Assert.True(snapped.SnapToGrid(grid, F).EqualsExact(snapped), "idempotent");
        });
    }

    // ---------- ReducePrecision ----------

    [Fact]
    [Covers("ReducePrecision")]
    public void ReducePrecision_of_cases()
    {
        // the hole of width 0.1 is a sliver and may collapse; the result is valid either way
        var sliver = G("POLYGON ((0 0, 10 0, 10 10.2, 0 10.2, 0 0), (3 3, 3.1 3, 3.1 6, 3 6, 3 3))").ReducePrecision(1, F);
        Assert.True(sliver.IsValid());
        Assert.InRange(sliver.Area(), 96, 100.0001);

        // lines: nodes at crossings, duplicates collapse
        var line = (IPolyline2D)G("MULTILINESTRING ((0 0, 10 10), (0 10, 10 0), (0 0, 10 10))").ReducePrecision(1, F);
        Assert.Equal(4, line.PathCount);
        Assert.Equal(4 * Math.Sqrt(50), line.Length(), 9);

        Assert.Equal("MULTIPOINT ((1 1))", G("MULTIPOINT ((0.9 1.1), (1.1 0.9))").ReducePrecision(1, F).ToWkt());
        Assert.Equal("POINT (3 -4)", G("POINT (3.2 -3.6)").ReducePrecision(1, F).ToWkt());
        Assert.Throws<ArgumentOutOfRangeException>(() => G(Holey).ReducePrecision(0, F));
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).ReducePrecision(1, F));
    }

    [Fact]
    [Covers("ReducePrecision")]
    public void ReducePrecision_gives_valid_polygons_on_the_grid_like_NTS()
    {
        Gen.Select(Pt.Array[3, 10], Gen.Int[0, 2]).Sample((points, k) =>
        {
            double grid = new[] { 0.5, 1, 4 }[k];
            var polygon = ValidPolygon(points);
            if (Internal.GeometryAccess.IsEmpty(polygon)) return;

            var reduced = polygon.ReducePrecision(grid, F);

            Assert.True(reduced.IsValid(), polygon.ToWkt());
            foreach (var p in Internal.GeometryAccess.AllPoints(reduced))
            {
                Assert.True(OnGrid(p.X, grid) && OnGrid(p.Y, grid));
            }

            var nts = GeometryPrecisionReducer.Reduce(Nts.ToNts(polygon), new PrecisionModel(1 / grid));
            double tolerance = 4 * grid * (1 + polygon.Length());
            Assert.True(Math.Abs(reduced.Area() - nts.Area) <= tolerance, $"{reduced.Area():R} vs NTS {nts.Area:R}");
        });
    }

    [Fact]
    [Covers("ReducePrecision")]
    public void ReducePrecision_of_lines_stays_close_to_the_original()
    {
        Gen.Select(Pt.Array[2, 8], Gen.Int[0, 2]).Sample((points, k) =>
        {
            double grid = new[] { 0.5, 1, 4 }[k];
            var line = F.CreatePolyline([points], null).MakeValid(F);
            if (Internal.GeometryAccess.IsEmpty(line)) return;

            var reduced = line.ReducePrecision(grid, F);

            foreach (var p in Internal.GeometryAccess.AllPoints(reduced))
            {
                Assert.True(OnGrid(p.X, grid) && OnGrid(p.Y, grid));
            }

            // the reduced line stays within a cell diagonal of the original
            Assert.True(reduced.HausdorffDistance(line) <= grid * 2 + 1e-9, $"{reduced.HausdorffDistance(line)}: {line.ToWkt()}");
        });
    }
}
