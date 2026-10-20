using CsCheck;
using NetTopologySuite.Algorithm;
using SpatialNET.Tests.Oracle;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace SpatialNET.Tests.Measures;

public class MeasuresPropertyTests
{
    /// <summary>
    /// Zufällige sternförmige (damit einfache) Polygone mit beliebiger Orientierung, beliebigem Startpunkt
    /// und Verschiebung bis in UTM-Größenordnung.
    /// </summary>
    private static readonly Gen<(NtsCoordinate[] Ring, double Dx, double Dy)> StarPolygons =
        Gen.Int[3, 40].SelectMany(n =>
            Gen.Select(Gen.Double[0.5, 100].Array[n], Gen.Bool, Gen.Int[0, n - 1], Gen.Double[-1e6, 1e6], Gen.Double[-6e6, 6e6]))
        .Select(t =>
        {
            var (radii, clockwise, start, dx, dy) = t;
            int n = radii.Length;
            var points = Enumerable.Range(0, n)
                .Select(i => (start + (clockwise ? -i : i) + n) % n)
                .Select(i => new NtsCoordinate(dx + radii[i] * Math.Cos(2 * Math.PI * i / n), dy + radii[i] * Math.Sin(2 * Math.PI * i / n)))
                .ToList();
            points.Add(points[0].Copy());
            return (points.ToArray(), dx, dy);
        });

    [Fact]
    public void Area_and_centroid_of_simple_polygons_match_NTS()
    {
        StarPolygons.Sample(t =>
        {
            var nts = NtsGeometryFactory.Default.CreatePolygon(t.Ring);
            foreach (var (_, factory) in Nts.Implementations)
            {
                var polygon = Nts.ToSpatial(nts, factory);
                OracleAssert.Close(nts.Area, polygon.Area(), 1e-8);

                var expected = Centroid.GetCentroid(nts);
                var actual = polygon.Centroid()!.Value;
                OracleAssert.Close(expected.X, actual.X, 1e-12);
                OracleAssert.Close(expected.Y, actual.Y, 1e-12);
            }
        });
    }

    [Fact]
    public void Area_and_length_are_translation_invariant()
    {
        StarPolygons.Sample(t =>
        {
            var factory = Nts.Implementations[0].Factory;
            var moved = Nts.ToSpatial(NtsGeometryFactory.Default.CreatePolygon(t.Ring), factory);
            var atOrigin = Nts.ToSpatial(NtsGeometryFactory.Default.CreatePolygon(
                t.Ring.Select(c => new NtsCoordinate(c.X - t.Dx, c.Y - t.Dy)).ToArray()), factory);

            OracleAssert.Close(atOrigin.Area(), moved.Area(), 1e-8);
            OracleAssert.Close(atOrigin.Length(), moved.Length(), 1e-8);
        });
    }
}
