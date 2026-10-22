using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.Predicates;

/// <summary>
/// <see cref="PreparedGeometry"/>: Distance, IsWithinDistance, NearestPoints and thread safety (ADR 0024). The prepared answers equal the
/// unprepared ones; only among equally near pairs the nearest points may differ.
/// </summary>
public class PreparedDistanceTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static Coordinate C(double x, double y) => new(x, y);

    [Fact]
    public void Distance_and_IsWithinDistance_of_simple_cases()
    {
        var prepared = G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))").Prepare();

        Assert.Equal(4, prepared.Distance(G("POINT (14 5)")), 1e-12);
        Assert.Equal(0, prepared.Distance(G("POINT (5 5)")));                                  // inside the area
        Assert.Equal(0, prepared.Distance(G("LINESTRING (-5 5, 15 5)")));                      // crossing
        Assert.Equal(Math.Sqrt(2), prepared.Distance(G("POINT (11 11)")), 1e-12);              // to a vertex
        Assert.Equal(5, prepared.Distance(G("POLYGON ((15 0, 20 0, 20 10, 15 10, 15 0))")), 1e-12);
        Assert.True(prepared.IsWithinDistance(G("POINT (14 5)"), 4));
        Assert.False(prepared.IsWithinDistance(G("POINT (14 5)"), 3.999));
        Assert.True(prepared.IsWithinDistance(G("POINT (5 5)"), 0));
    }

    [Fact]
    public void NearestPoints_lie_on_the_geometries_and_realise_the_distance()
    {
        var polygon = G("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))");
        var prepared = polygon.Prepare();
        var well = G("POINT (7 10)");   // in the hole

        var pair = prepared.NearestPoints(well)!.Value;

        Assert.Equal(C(5, 10), pair.OnPrepared);
        Assert.Equal(C(7, 10), pair.OnOther);
        Assert.Equal(polygon.Distance(well), prepared.Distance(well), 1e-12);
    }

    [Fact]
    public void Touching_and_intersecting_geometries_give_the_same_point()
    {
        var prepared = G("LINESTRING (0 0, 10 10)").Prepare();

        var pair = prepared.NearestPoints(G("LINESTRING (0 10, 10 0)"))!.Value;

        Assert.Equal(5, pair.OnPrepared.X, 1e-12);
        Assert.Equal(pair.OnPrepared.X, pair.OnOther.X, 1e-12);
        Assert.Equal(0, prepared.Distance(G("LINESTRING (0 10, 10 0)")));
    }

    [Fact]
    public void Empty_geometries_invalid_arguments_and_null_are_handled_like_the_unprepared_methods()
    {
        var prepared = G("POLYGON ((0 0, 1 0, 1 1, 0 0))").Prepare();
        var empty = G("POLYGON EMPTY").Prepare();

        Assert.True(double.IsNaN(prepared.Distance(G("POINT EMPTY"))));
        Assert.True(double.IsNaN(empty.Distance(G("POINT (1 1)"))));
        Assert.Null(prepared.NearestPoints(G("POINT EMPTY")));
        Assert.Null(empty.NearestPoints(G("POINT (1 1)")));
        Assert.False(prepared.IsWithinDistance(G("POINT EMPTY"), 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.IsWithinDistance(G("POINT (1 1)"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.IsWithinDistance(G("POINT (1 1)"), double.NaN));
        Assert.Throws<ArgumentNullException>(() => prepared.Distance(null!));
        Assert.Throws<ArgumentNullException>(() => prepared.NearestPoints(null!));
        Assert.Throws<ArgumentNullException>(() => prepared.IsWithinDistance(null!, 1));
    }

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-30, 30], Gen.Int[-30, 30], Gen.Double[0, 1]).Select((x, y, e) => new Coordinate(x + e, y - e / 3));

    private static IGeometry2D Polygon(Coordinate[] shell) => F.CreatePolygon([new PolygonPart(shell.Append(shell[0]).ToArray())], null).MakeValid(factory: F, gridSize: 1e-3);

    private static IGeometry2D Line(Coordinate[] pts) => F.CreatePolyline([pts], null).MakeValid(F);

    private static IGeometry2D Make(int kind, Coordinate[] pts) => kind switch
    {
        0 => Polygon(pts),
        1 => Line(pts),
        _ => F.CreateMultiPoint(pts, null),
    };

    [Fact]
    public void The_prepared_answers_equal_the_unprepared_ones()
    {
        Gen.Select(Gen.Int[0, 2], Pt.Array[3, 9], Gen.Int[0, 2], Pt.Array[1, 6], Gen.Double[0, 3]).Sample((k1, p1, k2, p2, factor) =>
        {
            var (own, other) = (Make(k1, p1), Make(k2, p2));
            if (GeometryAccessIsEmpty(own) || GeometryAccessIsEmpty(other)) return;
            var prepared = own.Prepare();

            double expected = own.Distance(other);

            Assert.Equal(expected, prepared.Distance(other), 1e-9);
            var pair = prepared.NearestPoints(other)!.Value;
            Assert.Equal(expected, Internal.Rings.Distance(pair.OnPrepared, pair.OnOther), 1e-9);
            Assert.True(F.CreatePoint(pair.OnPrepared, null).Distance(own) <= 1e-9, "the first point must lie on the prepared geometry");
            Assert.True(F.CreatePoint(pair.OnOther, null).Distance(other) <= 1e-9, "the second point must lie on the other geometry");
            double d = expected * factor;
            if (Math.Abs(d - expected) > 1e-6)
            {
                Assert.Equal(expected <= d, prepared.IsWithinDistance(other, d));
                Assert.Equal(own.IsWithinDistance(other, d), prepared.IsWithinDistance(other, d));
            }
        });
    }

    private static bool GeometryAccessIsEmpty(IGeometry2D g) => !Internal.GeometryAccess.AllPoints(g).Any();

    [Fact]
    public void A_prepared_geometry_can_be_used_by_many_threads_at_once()
    {
        // a polygon of 2,000 vertices, so that the index is deep; each thread asks many questions and must get the sequential answers
        var ring = Enumerable.Range(0, 2000).Select(i => new Coordinate(
            1000 * (0.8 + 0.2 * Math.Sin(i * 0.37)) * Math.Cos(2 * Math.PI * i / 2000), 1000 * (0.8 + 0.2 * Math.Sin(i * 0.37)) * Math.Sin(2 * Math.PI * i / 2000))).ToList();
        ring.Add(ring[0]);
        var polygon = F.CreatePolygon([new PolygonPart(ring.ToArray())], null);
        var prepared = polygon.Prepare();
        var rng = new Random(11);
        var points = Enumerable.Range(0, 3000).Select(_ => new Coordinate(rng.NextDouble() * 2400 - 1200, rng.NextDouble() * 2400 - 1200)).ToArray();
        var probes = points.Take(400).Select(p => (IGeometry2D)F.CreatePolyline([new[] { p, new Coordinate(p.X + 40, p.Y + 25) }], null)).ToArray();

        var expectedLocate = points.Select(prepared.Locate).ToArray();
        var expectedIntersects = probes.Select(prepared.Intersects).ToArray();
        var expectedDistance = probes.Take(100).Select(prepared.Distance).ToArray();

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (prepared.Locate(points[i]) != expectedLocate[i]) failures.Add($"Locate {i}");
            }

            for (int i = 0; i < probes.Length; i++)
            {
                if (prepared.Intersects(probes[i]) != expectedIntersects[i]) failures.Add($"Intersects {i}");
            }

            for (int i = 0; i < expectedDistance.Length; i++)
            {
                if (prepared.Distance(probes[i]) != expectedDistance[i]) failures.Add($"Distance {i}");
            }
        });

        Assert.Empty(failures);
    }
}
