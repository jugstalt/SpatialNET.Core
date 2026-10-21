using CsCheck;

namespace SpatialNET.Tests.MValues;

/// <summary>
/// The invariant of ADR 0015: M never influences a computation. The same operation with and without M
/// yields identical X/Y (and Z), for random inputs with random and partly missing M.
/// </summary>
public class MInvariantPropertyTests
{
    private static readonly MFactory Factory = new(supportsZ: true, supportsM: true);

    private static readonly Gen<(int X, int Y, int W, int H)> Rect =
        Gen.Select(Gen.Int[0, 15], Gen.Int[0, 15], Gen.Int[1, 12], Gen.Int[1, 12]);

    private static MPolygon Rectangle((int X, int Y, int W, int H) r, double[] m, bool[] has, int offset)
    {
        var corners = new (int X, int Y)[] { (r.X, r.Y), (r.X + r.W, r.Y), (r.X + r.W, r.Y + r.H), (r.X, r.Y + r.H) };
        var ring = corners.Select((c, i) => new Coordinate(c.X, c.Y, M: has[offset + i] ? m[offset + i] : double.NaN)).ToList();
        ring.Add(ring[0]);
        return MGeo.Polygon(ring.ToArray());
    }

    [Fact]
    public void Polygon_overlays_have_identical_xy_with_and_without_M()
    {
        Gen.Select(Rect, Rect, Gen.Double[-100, 100].Array[8], Gen.Bool.Array[8]).Sample((a, b, m, has) =>
        {
            var withM = (A: Rectangle(a, m, has, 0), B: Rectangle(b, m, has, 4));
            var flat = (A: (IPolygon2D)MGeo.Flat(withM.A), B: (IPolygon2D)MGeo.Flat(withM.B));

            foreach (var op in new[] { "Intersection", "Union", "Difference", "SymDifference" })
            {
                var x = MGeo.Coordinates(Run(op, withM.A, withM.B));
                var y = MGeo.Coordinates(Run(op, flat.A, flat.B));
                Assert.Equal(y.Select(c => (c.X, c.Y)), x.Select(c => (c.X, c.Y)));
            }
        });
    }

    [Fact]
    public void Cut_and_Simplify_have_identical_xy_with_and_without_M()
    {
        Gen.Select(Rect, Gen.Int[1, 14], Gen.Double[-100, 100].Array[8], Gen.Bool.Array[8]).Sample((r, cutX, m, has) =>
        {
            var polygon = Rectangle(r, m, has, 0);
            var cutter = MGeo.Polyline(new Coordinate(cutX, -1), new Coordinate(cutX, 40));

            var withM = polygon.Cut(cutter, Factory).SelectMany(p => MGeo.Coordinates(p)).Select(c => (c.X, c.Y));
            var flat = ((IPolygon2D)MGeo.Flat(polygon)).Cut(cutter, Factory).SelectMany(p => MGeo.Coordinates(p)).Select(c => (c.X, c.Y));
            Assert.Equal(flat, withM);

            var simplified = MGeo.Coordinates(polygon.Simplify(1, factory: Factory)).Select(c => (c.X, c.Y));
            var simplifiedFlat = MGeo.Coordinates(((IPolygon2D)MGeo.Flat(polygon)).Simplify(1, factory: Factory)).Select(c => (c.X, c.Y));
            Assert.Equal(simplifiedFlat, simplified);
        });
    }

    private static IPolygon2D Run(string op, IPolygon2D a, IPolygon2D b) => op switch
    {
        "Intersection" => a.Intersection(b, factory: Factory),
        "Union" => a.Union(b, factory: Factory),
        "Difference" => a.Difference(b, factory: Factory),
        _ => a.SymDifference(b, factory: Factory),
    };
}
