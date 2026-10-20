using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using SpatialNET.Imp;
using SpatialNET.Internal;
using SpatialNET.IO;
using SpatialNET.Tests.Oracle;
using ImpPolygon = SpatialNET.Imp.Polygon;
using ImpRing = SpatialNET.Imp.Ring;

namespace SpatialNET.Tests.Overlay;

/// <summary>Füllregeln, ungültige Eingaben und Ausgabeform des Polygon-Overlays (ADR 0003/0005).</summary>
public class PolygonOverlayTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    /// <summary>Flache Ringliste mit Ringen genau wie angegeben (Orientierung bleibt erhalten).</summary>
    private static IPolygon2D Rings(params string[] rings) =>
        new ImpPolygon(rings.Select(r => (IRing2D)new ImpRing(Wkt.Read<IPolyline2D>("LINESTRING " + r, F)[0].AsList().ToArray())).ToArray());

    private static Geometry NtsUnionOfRings(params string[] rings) =>
        UnaryUnionOp.Union((Geometry)new GeometryCollection(rings.Select(r => Nts.Read("POLYGON (" + r + ")")).ToArray()));

    private static readonly string A = "(0 0, 10 0, 10 10, 0 10, 0 0)";            // gegen den Uhrzeigersinn
    private static readonly string B = "(5 5, 15 5, 15 15, 5 15, 5 5)";            // gegen den Uhrzeigersinn
    private static readonly string BClockwise = "(5 5, 5 15, 15 15, 15 5, 5 5)";

    [Fact]
    public void EvenOdd_makes_the_overlap_of_two_shells_a_hole()
    {
        var polygon = Rings(A, B);

        var result = polygon.Union(Rings(), FillRule.EvenOdd, F);

        Assert.Equal(200 - 2 * 25, result.Area(), 9);
    }

    [Fact]
    public void NonZero_merges_overlapping_shells()
    {
        var result = Rings(A, B).Union(Rings(), FillRule.NonZero, F);

        OracleAssert.SameGeometry(NtsUnionOfRings(A, B), result, 1e-12);
    }

    [Fact]
    public void Positive_keeps_only_counterclockwise_coverage()
    {
        // B im Uhrzeigersinn: Windungszahl −1 außerhalb von A, 0 in der Überlappung
        var result = Rings(A, BClockwise).Union(Rings(), FillRule.Positive, F);

        OracleAssert.SameGeometry(Nts.Read("POLYGON ((0 0, 10 0, 10 5, 5 5, 5 10, 0 10, 0 0))"), result, 1e-12);
    }

    [Fact]
    public void Negative_keeps_only_clockwise_coverage()
    {
        var result = Rings(A, BClockwise).Union(Rings(), FillRule.Negative, F);

        OracleAssert.SameGeometry(Nts.Read("POLYGON ((10 5, 15 5, 15 15, 5 15, 5 10, 10 10, 10 5))"), result, 1e-12);
    }

    [Fact]
    public void Self_crossing_ring_is_two_triangles_under_EvenOdd()
    {
        var bowtie = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))", F);

        var result = bowtie.Union(Rings(), factory: F);

        OracleAssert.SameGeometry(Nts.Read("MULTIPOLYGON (((0 0, 5 5, 0 10, 0 0)), ((10 0, 10 10, 5 5, 10 0)))"), result, 1e-12);
    }

    [Fact]
    public void Results_are_closed_with_counterclockwise_shells_and_clockwise_holes()
    {
        var a = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 0 10, 10 10, 10 0, 0 0))", F); // Eingabe im Uhrzeigersinn
        var b = Wkt.Read<IPolygon2D>("POLYGON ((3 3, 7 3, 7 7, 3 7, 3 3))", F);

        var result = a.Difference(b, factory: F);

        Assert.Equal(2, result.RingCount);
        var shell = Internal.Rings.Coordinates(result[0]);
        var hole = Internal.Rings.Coordinates(result[1]);
        Assert.Equal(shell[0], shell[^1]);
        Assert.True(Internal.Rings.SignedArea(shell) > 0);
        Assert.True(Internal.Rings.SignedArea(hole) < 0);
    }

    [Fact]
    public void Z_of_the_first_operand_wins_and_is_dropped_without_Z_support()
    {
        var a = Wkt.Read<IPolygon2D>("POLYGON Z ((0 0 1, 10 0 1, 10 10 1, 0 10 1, 0 0 1))", F);
        var b = Wkt.Read<IPolygon2D>("POLYGON Z ((5 5 9, 15 5 9, 15 15 9, 5 15 9, 5 5 9))", F);

        var withZ = a.Intersection(b, factory: F);
        var zs = Internal.Rings.Coordinates(withZ[0]).Where(c => c.HasZ).Select(c => c.Z).Distinct().ToArray();
        Assert.Contains(1d, zs);

        var withoutZ = a.Intersection(b, factory: new ImpGeometryFactory(supportsZ: false));
        Assert.All(Internal.Rings.Coordinates(withoutZ[0]), c => Assert.False(c.HasZ));
    }

    [Fact]
    public void Grid_size_is_symmetric_and_rounds_the_output()
    {
        var a = Wkt.Read<IPolygon2D>("POLYGON ((0.123 0, 10 0, 10 10, 0 10, 0.123 0))", F);
        var b = Wkt.Read<IPolygon2D>("POLYGON ((5 5, 15 5, 15 15, 5 15, 5 5))", F);

        var ab = a.Union(b, factory: F, gridSize: 0.1);
        var ba = b.Union(a, factory: F, gridSize: 0.1);

        Assert.Equal(ab.Area(), ba.Area(), 12);
        Assert.All(GeometryAccess.AllPoints(ab), p => Assert.Equal(Math.Round(p.X, 1), p.X, 12));
    }
}
