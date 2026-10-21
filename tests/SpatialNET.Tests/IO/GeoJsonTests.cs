using System.Text;
using System.Text.Json;
using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.IO;

public class GeoJsonMTests
{
    [Fact]
    public void GeoJson_has_no_M_on_writing()
    {
        var polygon = Wkt.Read("POLYGON ZM ((0 0 1 10, 10 0 2 20, 10 10 3 30, 0 0 1 10))", ImpGeometryFactory.Instance);

        var json = polygon.ToGeoJson();

        Assert.Equal("""{"type":"Polygon","coordinates":[[[0,0,1],[10,0,2],[10,10,3],[0,0,1]]]}""", json);
    }

    [Fact]
    public void A_fourth_position_element_is_not_read_as_M()
    {
        var p = GeoJson.Read("""{"type":"Point","coordinates":[1,2,3,4]}""", ImpGeometryFactory.Instance);

        Assert.IsNotAssignableFrom<IPointM>(p);
    }
}

public class GeoJsonReadTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Theory]
    [InlineData("""{"type":"Point","coordinates":[1,2]}""", "POINT (1 2)")]
    [InlineData("""{"type":"Point","coordinates":[1,2,3]}""", "POINT Z (1 2 3)")]
    [InlineData("""{"type":"Point","coordinates":[1,2,3,4]}""", "POINT Z (1 2 3)")]
    [InlineData("""{"type":"Point","coordinates":[]}""", "MULTIPOINT EMPTY")]
    [InlineData("""{"type":"MultiPoint","coordinates":[[1,2],[3,4]]}""", "MULTIPOINT ((1 2), (3 4))")]
    [InlineData("""{"type":"LineString","coordinates":[[0,0],[1,1]]}""", "LINESTRING (0 0, 1 1)")]
    [InlineData("""{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3]]]}""", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[2,2],[2,8],[8,8],[8,2],[2,2]]]}""", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))")]
    [InlineData("""{"type":"Feature","geometry":{"type":"Point","coordinates":[5,6]},"properties":{"name":"x"}}""", "POINT (5 6)")]
    [InlineData("""{"type":"Point","coordinates":[1,2],"crs":{"type":"name","properties":{"name":"EPSG:4326"}}}""", "POINT (1 2)")]
    public void Reads_geometries_like_their_WKT(string json, string wkt)
    {
        Assert.Equal(wkt, GeoJson.Read(json, F).ToWkt());
    }

    [Fact]
    public void ReadAll_reads_feature_collections_and_flattens_geometry_collections()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]},"properties":null},
              {"type":"Feature","geometry":null,"properties":{}},
              {"type":"Feature","geometry":{"type":"GeometryCollection","geometries":[
                {"type":"LineString","coordinates":[[0,0],[1,1]]},
                {"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}]},"properties":{}}]}
            """;

        var all = GeoJson.ReadAll(json, F);

        Assert.Equal(["POINT (1 2)", "LINESTRING (0 0, 1 1)", "POLYGON ((0 0, 1 0, 1 1, 0 0))"], all.Select(g => g.ToWkt()));
        Assert.Throws<NotSupportedException>(() => GeoJson.Read(json, F));
    }

    [Theory]
    [InlineData("""{"type":"Point"}""", "$")]
    [InlineData("""{"type":"Point","coordinates":[1]}""", "$.coordinates")]
    [InlineData("""{"type":"LineString","coordinates":[[0,0],[1,"a"]]}""", "$.coordinates[1][1]")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Polygonn","coordinates":[]}}]}""", "$.features[0].geometry.type")]
    [InlineData("""{"type":""", "JSON")]
    public void Errors_report_the_JSON_path(string json, string path)
    {
        var ex = Assert.Throws<FormatException>(() => GeoJson.ReadAll(json, F));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Reads_from_bytes_and_streams()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"type":"Point","coordinates":[1,2]}""");

        Assert.Equal("POINT (1 2)", GeoJson.Read(bytes, F).ToWkt());
        Assert.Equal("POINT (1 2)", GeoJson.Read(new MemoryStream(bytes), F).ToWkt());
        Assert.Single(GeoJson.ReadAll(new MemoryStream(bytes), F));
    }
}

public class GeoJsonWriteTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    [Fact]
    public void Writes_RFC7946_orientation_and_slimmest_type()
    {
        var polygon = Wkt.Read("MULTIPOLYGON (((0 0, 0 10, 10 10, 10 0, 0 0), (2 2, 8 2, 8 8, 2 8, 2 2)))", F);

        Assert.Equal("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[2,2],[2,8],[8,8],[8,2],[2,2]]]}""", polygon.ToGeoJson());
    }

    [Fact]
    public void Z_mode_auto_omits_zero_heights_of_host_geometries()
    {
        var line = Wkt.Read("LINESTRING (0 0, 1 1)", Foreign.ForeignGeometryFactory.Instance);

        Assert.Equal("""{"type":"LineString","coordinates":[[0,0],[1,1]]}""", line.ToGeoJson());
        Assert.Equal("""{"type":"LineString","coordinates":[[0,0,0],[1,1,0]]}""", line.ToGeoJson(new GeoJsonOptions { ZMode = ZMode.Always }));
    }

    [Fact]
    public void Feature_collection_has_empty_properties()
    {
        var json = GeoJson.ToFeatureCollection([Wkt.Read("POINT (1 2)", F)]);

        using var doc = JsonDocument.Parse(json);
        var feature = doc.RootElement.GetProperty("features")[0];
        Assert.Equal(JsonValueKind.Object, feature.GetProperty("properties").ValueKind);
        Assert.Single(GeoJson.ReadAll(json, F));
    }

    [Fact]
    public void NaN_is_not_representable()
    {
        Assert.Throws<NotSupportedException>(() => Wkt.Read("POINT (NaN 1)", F).ToGeoJson());
    }

    [Fact]
    public void Coordinates_round_trip_bit_exact()
    {
        var finite = Gen.Double.Where(double.IsFinite);
        Gen.Select(finite, finite, finite).Array[2, 20].Sample(points =>
        {
            var line = F.CreatePolyline([points.Select(p => new Coordinate(p.Item1, p.Item2, p.Item3)).ToArray()], null);

            var reread = GeoJson.Read<IPolyline2D>(line.ToGeoJson(new GeoJsonOptions { ZMode = ZMode.Always }), F);

            for (int i = 0; i < points.Length; i++)
            {
                var p = Assert.IsAssignableFrom<IPoint3D>(reread[0][i]);
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item1), BitConverter.DoubleToInt64Bits(p.X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item2), BitConverter.DoubleToInt64Bits(p.Y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(points[i].Item3), BitConverter.DoubleToInt64Bits(p.Z));
            }
        });
    }

    [Fact]
    public void All_area_fixtures_round_trip_with_equal_measures()
    {
        foreach (var fixture in FixtureCatalog.For("area"))
        {
            var g = fixture.ReadA(F);
            if (Internal.GeometryAccess.AllPoints(g).Any(p => !double.IsFinite(p.X))) continue;
            var reread = GeoJson.Read(g.ToGeoJson(), F);
            OracleAssert.Close(g.Area(), reread.Area());
        }
    }
}
