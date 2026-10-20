using SpatialNET.Imp;
using SpatialNET.Internal;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Measures;

public class MeasuresContractTests
{
    private sealed class UnknownGeometry : IGeometry2D;

    private static readonly IGeometry2D Square = Nts.ToSpatial(Nts.Read("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))"), ImpGeometryFactory.Instance);

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Area());
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Envelope());
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).Centroid());
        Assert.Throws<ArgumentNullException>(() => Square.Distance(null!));
    }

    [Fact]
    public void Unknown_geometry_kinds_are_not_supported()
    {
        var unknown = new UnknownGeometry();

        Assert.Throws<NotSupportedException>(() => unknown.Area());
        Assert.Throws<NotSupportedException>(() => unknown.Length());
        Assert.Throws<NotSupportedException>(() => unknown.Envelope());
        Assert.Throws<NotSupportedException>(() => unknown.Centroid());
        Assert.Throws<NotSupportedException>(() => Square.Distance(unknown));
    }

    [Fact]
    public void Lower_dimensions_have_no_area_and_points_no_length()
    {
        var line = Nts.ToSpatial(Nts.Read("LINESTRING (0 0, 3 4)"), ImpGeometryFactory.Instance);
        var point = Nts.ToSpatial(Nts.Read("POINT (1 1)"), ImpGeometryFactory.Instance);

        Assert.Equal(0, line.Area());
        Assert.Equal(0, point.Area());
        Assert.Equal(0, point.Length());
    }

    [Fact]
    public void Self_crossing_input_does_not_throw_and_is_deterministic()
    {
        // Ungültige Eingabe (ADR 0003): Ergebnis nicht zugesichert, aber stabil und ohne Exception.
        var bowtie = Nts.ToSpatial(Nts.Read("POLYGON ((0 0, 10 10, 10 0, 0 10, 0 0))"), ImpGeometryFactory.Instance);

        Assert.Equal(bowtie.Area(), bowtie.Area());
        Assert.Equal(bowtie.Centroid(), bowtie.Centroid());
    }

    [Fact]
    public void Polygon_structure_assigns_holes_and_islands_with_normalized_orientation()
    {
        var polygon = (IPolygon2D)Nts.ToSpatial(Nts.Read(
            "MULTIPOLYGON (((0 0, 0 10, 10 10, 10 0, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2)), ((4 4, 6 4, 6 6, 4 6, 4 4)))"),
            ImpGeometryFactory.Instance);

        var parts = PolygonStructure.Build(polygon);

        Assert.Equal(2, parts.Count);
        Assert.Single(parts[0].Holes);
        Assert.Empty(parts[1].Holes);
        Assert.All(parts, p => Assert.True(Rings.SignedArea(p.Shell) > 0, "Außenring gegen den Uhrzeigersinn"));
        Assert.True(Rings.SignedArea(parts[0].Holes[0]) < 0, "Loch im Uhrzeigersinn");
        Assert.All(parts, p => Assert.True(Rings.SameXY(p.Shell[0], p.Shell[^1]), "Ring geschlossen"));
    }
}
