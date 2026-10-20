namespace SpatialNET.Tests.Core;

[Collection(GlobalStateCollection.Name)]
public sealed class GlobalSettingsTests : IDisposable
{
    private readonly IGeometryFactory? _savedFactory = GeometryFactory.Default;
    private readonly double? _savedGridSize = Precision.DefaultGridSize;

    public void Dispose()
    {
        GeometryFactory.Default = _savedFactory;
        Precision.DefaultGridSize = _savedGridSize;
    }

    [Fact]
    public void Resolve_prefers_the_explicit_factory()
    {
        var explicitFactory = new NullFactory();
        GeometryFactory.Default = new NullFactory();

        Assert.Same(explicitFactory, GeometryFactory.Resolve(explicitFactory));
    }

    [Fact]
    public void Resolve_falls_back_to_the_default()
    {
        var defaultFactory = new NullFactory();
        GeometryFactory.Default = defaultFactory;

        Assert.Same(defaultFactory, GeometryFactory.Resolve(null));
    }

    [Fact]
    public void Resolve_without_any_factory_throws()
    {
        GeometryFactory.Default = null;

        var ex = Assert.Throws<InvalidOperationException>(() => GeometryFactory.Resolve(null));
        Assert.Contains("GeometryFactory.Default", ex.Message);
    }

    [Fact]
    public void Default_grid_size_is_initially_null()
    {
        Assert.Null(_savedGridSize);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_default_grid_size_is_rejected(double gridSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Precision.DefaultGridSize = gridSize);
    }

    [Fact]
    public void Valid_default_grid_size_can_be_set_and_reset()
    {
        Precision.DefaultGridSize = 1e-6;
        Assert.Equal(1e-6, Precision.DefaultGridSize);

        Precision.DefaultGridSize = null;
        Assert.Null(Precision.DefaultGridSize);
    }

    private sealed class NullFactory : IGeometryFactory
    {
        public bool SupportsZ => false;
        public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) => throw new NotSupportedException();
        public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) => throw new NotSupportedException();
        public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) => throw new NotSupportedException();
        public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source) => throw new NotSupportedException();
    }
}
