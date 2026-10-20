using SpatialNET.Imp;
using SpatialNET.Tests.Foreign;

namespace SpatialNET.Tests.Core;

[SpatialNET.Tests.Fixtures.Covers("AsList")]
public class ListViewTests
{
    private static readonly IPolyline2D ImpLine = ImpGeometryFactory.Instance.CreatePolyline(
        new ReadOnlyMemory<Coordinate>[] { new Coordinate[] { new(0, 0), new(1, 2), new(3, 4) } }, null);

    private static readonly IPolyline2D ForeignLine = ForeignGeometryFactory.Instance.CreatePolyline(
        new ReadOnlyMemory<Coordinate>[] { new Coordinate[] { new(0, 0), new(1, 2), new(3, 4) } }, null);

    [Fact]
    public void View_over_geometry_without_list_supports_LINQ()
    {
        var xs = ImpLine[0].AsList().Select(p => p.X);

        Assert.Equal([0d, 1d, 3d], xs);
        Assert.Single(ImpLine.AsList());
    }

    [Fact]
    public void Existing_list_is_passed_through()
    {
        var path = ForeignLine[0];

        Assert.Same(path, path.AsList());
        Assert.Same(ForeignLine, ForeignLine.AsList());
    }

    [Fact]
    public void View_index_out_of_range_throws()
    {
        var view = ImpLine[0].AsList();

        Assert.Throws<ArgumentOutOfRangeException>(() => view[3]);
    }

    [Fact]
    public void Null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).AsList());
    }
}
