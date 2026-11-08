using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.DocSamples;

// Die Code-Beispiele aus docs/{en,de}/concepts/vector-tiles.md.
// Ändert sich ein Beispiel, muss die Dokumentation mitgezogen werden – und umgekehrt.

[Collection(GlobalStateCollection.Name)]
public sealed class VectorTileSamplesTests : IDisposable
{
    private readonly IGeometryFactory? _savedFactory = GeometryFactory.Default;

    public VectorTileSamplesTests() => GeometryFactory.Default = ImpGeometryFactory.Instance;

    public void Dispose() => GeometryFactory.Default = _savedFactory;

    [Fact]
    public void BuildAndReadATile()
    {
        // --- snippet: vector-tile
        // The grid of the tile cache, in any CRS: here EPSG:31256 with the scales of the service at 96 dpi.
        var grid = TileGrid.FromScales(-115000, 375000, [500000, 250000, 100000, 50000, 10000], dpi: 96);
        Envelope2D tile = grid.TileEnvelope(4, 12, 30);    // level 4, row 12, column 30

        // A parcel of 30 x 20 m near the lower left corner of the tile, in world coordinates.
        var parcel = new Envelope2D(tile.MinX + 10, tile.MinY + 10, tile.MinX + 40, tile.MinY + 30).ToPolygon();

        // One builder per tile; one MVT layer per map layer; the object id becomes the feature id.
        var builder = new MvtTileBuilder(grid, level: 4, row: 12, column: 30);
        builder.AddFeature("parcels", parcel, [new("name", "Parcel 7"), new("area", 600.0)], id: 7);
        byte[] pbf = builder.ToBytes(gzip: true);

        MvtTile read = Mvt.Decode(pbf);                   // gzip is detected
        MvtFeature feature = read["parcels"]!.Features[0];
        Console.WriteLine(feature.Id);                    // 7
        Console.WriteLine(feature.ToGeometry().ToWkt());  // POLYGON ((60 3915, 242 3915, 242 4036, 60 4036, 60 3915))
        // --- end

        Assert.Equal(7UL, feature.Id);
        Assert.Equal("POLYGON ((60 3915, 242 3915, 242 4036, 60 4036, 60 3915))", feature.ToGeometry().ToWkt());
        Assert.Equal(MvtValue.Double(600.0), feature.Attributes[1].Value);
    }

    [Fact]
    public void WebMercatorZoom()
    {
        var webGrid = TileGrid.FromScales(-TileGrid.WebMercatorHalfExtent, TileGrid.WebMercatorHalfExtent,
            Enumerable.Range(0, 20).Select(z => 591658710.90913117 / Math.Pow(2, z)));

        // --- snippet: vector-tile-webmercator
        // Is level 10 of the grid a standard zoom? Then its tiles can be served as z/x/y unchanged.
        bool standard = webGrid.TryGetWebMercatorZoom(10, out int zoom);  // True, zoom = 10

        // The same for a single scale (1:577791.71 at 96 dpi is zoom 10).
        bool fromScale = TileGrid.TryGetWebMercatorZoom(577791.71, out int z);  // True, z = 10
        // --- end

        Assert.True(standard);
        Assert.Equal(10, zoom);
        Assert.True(fromScale);
        Assert.Equal(10, z);
    }
}
