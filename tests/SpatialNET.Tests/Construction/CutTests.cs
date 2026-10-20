using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;
using SpatialNET.Internal;
using SpatialNET.Internal.Noding;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>Cut gegen NTS (ADR 0007): Polygonize(Union(Rand, Schnittlinie)), gefiltert auf das Innere.</summary>
public class CutFixtureTests
{
    public static TheoryData<string, string> PolygonFixtures =>
        FixtureCatalog.Theory("cut", f => f.A.Contains("POLYGON", StringComparison.OrdinalIgnoreCase));

    public static TheoryData<string, string> LineFixtures =>
        FixtureCatalog.Theory("cut", f => !f.A.Contains("POLYGON", StringComparison.OrdinalIgnoreCase));

    [Theory, MemberData(nameof(PolygonFixtures))]
    public void Polygon(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var polygon = (IPolygon2D)fixture.ReadA(factory);
        var cutter = (IPolyline2D)fixture.ReadB(factory);
        double grid = GridModel.Resolve(null, polygon, cutter).Size;

        var result = polygon.Cut(cutter, factory);

        Assert.All(result, r => Assert.True(r.IsValid(), "Teile müssen gültig sein."));
        if (fixture.Tags.Contains("self-crossing")) return;

        // Soll je Teilfläche: Polygonize auf genodetem Rand + Schnittlinie, nur Stücke im Inneren
        var pm = new PrecisionModel(1 / grid);
        var cutterNts = Nts.ToNts(cutter);
        var expectedPieces = new List<Geometry>();
        var untouched = new List<Geometry>();
        var parts = Nts.ToNts(polygon);
        for (int i = 0; i < parts.NumGeometries; i++)
        {
            var part = parts.GetGeometryN(i);
            if (part.IsEmpty) continue;
            var noded = new OverlayNG(part.Boundary, cutterNts, pm, NetTopologySuite.Operation.Overlay.SpatialFunction.Union).GetResult();
            var polygonizer = new Polygonizer();
            polygonizer.Add(noded);
            var pieces = polygonizer.GetPolygons().Where(p => part.Contains(p.InteriorPoint)).ToList();
            if (pieces.Count <= 1) untouched.Add(part);
            else expectedPieces.AddRange(pieces);
        }

        if (untouched.Count > 0 || expectedPieces.Count == 0)
        {
            expectedPieces.Add(untouched.Count == 0 ? parts.Factory.CreatePolygon() : parts.Factory.BuildGeometry(untouched));
        }

        Assert.Equal(expectedPieces.Count, result.Count);
        var ordered = expectedPieces.OrderBy(Key).ToList();
        var actual = result.OrderBy(r => Key(Nts.ToNts(r))).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            OracleAssert.SameGeometry(ordered[i], actual[i], grid, fixture);
        }

        // Unberührtes bleibt bitgenau (ADR 0007)
        if (untouched.Count > 0 && expectedPieces.Count == 1 + 0)
        {
            var original = GeometryAccess.AllPoints(polygon).Select(p => (p.X, p.Y)).ToHashSet();
            Assert.All(GeometryAccess.AllPoints(result.Single()), p => Assert.Contains((p.X, p.Y), original));
        }
    }

    [Theory, MemberData(nameof(LineFixtures))]
    public void Line(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var line = (IPolyline2D)fixture.ReadA(factory);
        var cutter = (IPolyline2D)fixture.ReadB(factory);
        double grid = GridModel.Resolve(null, line, cutter).Size;

        var result = line.Cut(cutter, factory);
        var cutterNts = Nts.ToNts(cutter);

        // Vereinigung der Teile = Linie (bis auf das Gitter)
        var union = Nts.ToNts(line).Factory.BuildGeometry(result.Select(Nts.ToNts).ToList());
        Assert.True(union.SymmetricDifference(Nts.ToNts(line)).Length <= 1e-9 * Math.Max(1, Nts.ToNts(line).Length));

        // Zerschnittene Stücke: Endpunkte sind Pfadenden oder liegen auf der Schnittlinie; keine inneren Kontakte
        foreach (var piece in result.Where(r => r.PathCount == 1 && result.Count > 1))
        {
            var path = Rings.Coordinates(piece[0]);
            if (path.Length < 2) continue;
            foreach (var end in new[] { path[0], path[^1] })
            {
                bool onCutter = cutterNts.Distance(new NetTopologySuite.Geometries.Point(end.X, end.Y)) <= grid * Math.Sqrt(2);
                bool isPathEnd = Enumerable.Range(0, line.PathCount).Any(i =>
                    Math.Abs(line[i][0].X - end.X) <= grid && Math.Abs(line[i][0].Y - end.Y) <= grid
                    || Math.Abs(line[i][line[i].PointCount - 1].X - end.X) <= grid && Math.Abs(line[i][line[i].PointCount - 1].Y - end.Y) <= grid);
                Assert.True(onCutter || isPathEnd, $"Stückende {end} liegt weder auf der Schnittlinie noch am Pfadende.");
            }
        }
    }

    private static (double, double, double) Key(Geometry g) =>
        g.IsEmpty ? (0, 0, 0) : (Math.Round(g.Area, 6), Math.Round(g.Centroid.X, 6), Math.Round(g.Centroid.Y, 6));
}

[Covers("Cut")]
public class CutContractTests
{
    private static readonly IGeometryFactory F = Imp.ImpGeometryFactory.Instance;

    [Fact]
    public void Untouched_parts_form_one_element_with_original_coordinates()
    {
        var polygon = SpatialNET.IO.Wkt.Read<IPolygon2D>(
            "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0)), ((20.123456789 0, 30 0, 30 10, 20.123456789 10, 20.123456789 0)), ((40 0, 50 0, 50 10, 40 10, 40 0)))", F);
        var cutter = SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (-1 5, 11 5)", F);

        var result = polygon.Cut(cutter, F, gridSize: 1);

        Assert.Equal(3, result.Count); // zwei Stücke + ein Element für beide unberührten Teilflächen
        var rest = result.Single(r => r.RingCount == 2);
        Assert.Contains(GeometryAccess.AllPoints(rest), p => p.X == 20.123456789); // nicht aufs Gitter gerundet
    }

    [Fact]
    public void Nothing_cut_returns_exactly_one_element()
    {
        var polygon = SpatialNET.IO.Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", F);

        Assert.Single(polygon.Cut(SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (20 0, 30 0)", F), F));
        Assert.Single(polygon.Cut(SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (-1 5, 5 5)", F), F)); // loses Ende
    }

    [Fact]
    public void Line_overlap_is_its_own_piece()
    {
        var line = SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0)", F);

        var result = line.Cut(SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (3 0, 6 0)", F), F);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r[0][0].X == 3 && r[0][r[0].PointCount - 1].X == 6);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var polygon = SpatialNET.IO.Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", F);
        Assert.Throws<ArgumentNullException>(() => polygon.Cut(null!, F));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Cut(SpatialNET.IO.Wkt.Read<IPolyline2D>("LINESTRING (0 0, 1 1)", F), F));
    }
}
