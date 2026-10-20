using System.Diagnostics;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;
using Xunit.Abstractions;

namespace SpatialNET.Tests.Predicates;

[Covers("Prepare")]
public class PreparedTests(ITestOutputHelper output)
{
    private static readonly string[] Algorithms = ["intersects", "disjoint", "contains", "within", "touches", "covers", "coveredBy"];

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var row in FixtureCatalog.Theory("relate")) data.Add((string)row[0], (string)row[1]);
        foreach (var row in FixtureCatalog.Theory("intersects")) data.Add((string)row[0], (string)row[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Prepared_predicates_equal_unprepared(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var (a, b) = (fixture.ReadA(factory), fixture.ReadB(factory));
        if (fixture.Tags.Contains("self-crossing"))
        {
            return; // ungültige Eingabe: keine Zusage gegenseitiger Konsistenz (ADR 0013)
        }

        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            var prepared = x.Prepare();
            Assert.Equal(x.Intersects(y), prepared.Intersects(y));
            Assert.Equal(x.Disjoint(y), prepared.Disjoint(y));
            Assert.Equal(x.Contains(y), prepared.Contains(y));
            Assert.Equal(x.Within(y), prepared.Within(y));
            Assert.Equal(x.Touches(y), prepared.Touches(y));
            Assert.Equal(x.Covers(y), prepared.Covers(y));
            Assert.Equal(x.CoveredBy(y), prepared.CoveredBy(y));
        }
    }

    [Fact]
    public void Snapshot_ignores_later_changes_of_the_host_geometry()
    {
        var factory = Foreign.ForeignGeometryFactory.Instance;
        var polygon = (Foreign.Polygon)SpatialNET.IO.Wkt.Read("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", factory);
        var point = SpatialNET.IO.Wkt.Read("POINT (15 5)", factory);

        var prepared = polygon.Prepare();
        foreach (var p in Enumerable.Range(0, polygon[0].PointCount).Select(i => polygon[0][i]))
        {
            p.X *= 2; // Host verändert sein Objekt nach dem Vorbereiten
        }

        Assert.True(polygon.Contains(point));
        Assert.False(prepared.Contains(point));
    }

    [Fact]
    public void Prepared_point_in_polygon_matches_and_reports_timing()
    {
        var factory = Imp.ImpGeometryFactory.Instance;
        int n = 2000;
        var ring = Enumerable.Range(0, n + 1).Select(i => new Coordinate(100 * Math.Cos(2 * Math.PI * i / n), 100 * Math.Sin(2 * Math.PI * i / n))).ToArray();
        ring[^1] = ring[0];
        var polygon = factory.CreatePolygon([new PolygonPart(ring)], null);
        var random = new Random(1);
        var points = Enumerable.Range(0, 300).Select(_ => factory.CreatePoint(new Coordinate(random.NextDouble() * 240 - 120, random.NextDouble() * 240 - 120), null)).ToArray();

        var watch = Stopwatch.StartNew();
        int unprepared = points.Count(p => polygon.Contains(p));
        var tUnprepared = watch.Elapsed;

        watch.Restart();
        var prepared = polygon.Prepare();
        int fast = points.Count(p => prepared.Contains(p));
        var tPrepared = watch.Elapsed;

        Assert.Equal(unprepared, fast);
        output.WriteLine($"300 Punkte gegen Polygon mit {n} Ecken: unvorbereitet {tUnprepared.TotalMilliseconds:F0} ms, vorbereitet {tPrepared.TotalMilliseconds:F0} ms");
    }
}
