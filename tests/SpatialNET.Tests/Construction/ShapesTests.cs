using CsCheck;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Construct;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>MinimumRotatedRectangle, MinimumBoundingCircle and Polylabel (ADR 0026), against NTS and against brute force.</summary>
public class ShapesTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Int[-40, 40], Gen.Int[-40, 40], Gen.Double[0, 1]).Select((x, y, e) => new Coordinate(x + e, y - e / 3));

    private static IGeometry2D Make(int kind, Coordinate[] pts) => kind switch
    {
        0 => F.CreatePolygon([new PolygonPart(pts.Append(pts[0]).ToArray())], null).MakeValid(factory: F, gridSize: 1e-3),
        1 => F.CreatePolyline([pts], null).MakeValid(F),
        _ => F.CreateMultiPoint(pts, null),
    };

    // ---------- MinimumRotatedRectangle ----------

    [Fact]
    [Covers("MinimumRotatedRectangle")]
    public void MinimumRotatedRectangle_of_cases()
    {
        Assert.Equal("POLYGON ((0 0, 4 0, 4 2, 0 2, 0 0))", G("POLYGON ((0 0, 4 0, 4 2, 0 2, 0 0))").MinimumRotatedRectangle(F).ToWkt());

        // a diamond: the rectangle is the diamond itself, turned by 45 degrees (area 2 * 2 = 4 for the unit diamond with diagonal 2)
        var diamond = G("POLYGON ((2 0, 4 2, 2 4, 0 2, 2 0))").MinimumRotatedRectangle(F);
        Assert.Equal(8, diamond.Area(), 9);

        Assert.Equal("POINT (1 1)", G("MULTIPOINT ((1 1), (1 1))").MinimumRotatedRectangle(F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 4 4)", G("MULTIPOINT ((0 0), (2 2), (4 4))").MinimumRotatedRectangle(F).ToWkt());
        Assert.Equal("POLYGON EMPTY", G("POLYGON EMPTY").MinimumRotatedRectangle(F).ToWkt());
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).MinimumRotatedRectangle(F));
    }

    private static double BruteForceArea(IReadOnlyList<Coordinate> hull)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < hull.Count; i++)
        {
            var (a, b) = (hull[i], hull[(i + 1) % hull.Count]);
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity, minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            foreach (var p in hull)
            {
                double u = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, v = (dx * (p.Y - a.Y) - dy * (p.X - a.X)) / length;
                (minU, maxU, minV, maxV) = (Math.Min(minU, u), Math.Max(maxU, u), Math.Min(minV, v), Math.Max(maxV, v));
            }

            best = Math.Min(best, (maxU - minU) * (maxV - minV));
        }

        return best;
    }

    [Fact]
    [Covers("MinimumRotatedRectangle")]
    public void MinimumRotatedRectangle_has_the_least_area_and_contains_the_geometry()
    {
        Gen.Select(Gen.Int[0, 2], Pt.Array[1, 14]).Sample((kind, pts) =>
        {
            var geometry = Make(kind, pts);
            if (Internal.GeometryAccess.IsEmpty(geometry)) return;
            var rectangle = geometry.MinimumRotatedRectangle(F);
            if (rectangle is not IPolygon2D polygon) return;

            var hull = (IPolygon2D)geometry.ConvexHull(F) is { } h ? Internal.Rings.Closed(h[0])[..^1] : [];
            double expected = BruteForceArea(hull);
            double ntsArea = NetTopologySuite.Algorithm.MinimumDiameter.GetMinimumRectangle(Nts.ToNts(geometry)).Area;

            Assert.True(Math.Abs(polygon.Area() - expected) <= 1e-9 * (1 + expected), $"{polygon.Area():R} vs brute force {expected:R}: {geometry.ToWkt()}");
            // NTS offers the rectangle of least WIDTH, which cannot be smaller than the one of least area
            Assert.True(polygon.Area() <= ntsArea * (1 + 1e-9), $"{polygon.Area():R} vs NTS minimum-width rectangle {ntsArea:R}");
            Assert.True(polygon.Covers(geometry) || polygon.Buffer(1e-7, factory: F).Covers(geometry), "the rectangle must contain the geometry: " + geometry.ToWkt());
        });
    }

    [Fact]
    [Covers("MinimumRotatedRectangle")]
    public void MinimumRotatedRectangle_of_large_hulls_equals_the_brute_force_area()
    {
        Gen.Select(Gen.Int[20, 400], Gen.Int[0, 1_000_000]).Sample((count, seed) =>
        {
            var random = new Random(seed);
            // points on and near a rotated ellipse: a long hull with many nearly parallel edges
            var points = Enumerable.Range(0, count).Select(_ =>
            {
                double t = random.NextDouble() * 2 * Math.PI, r = 0.9 + 0.1 * random.NextDouble();
                double x = 30 * r * Math.Cos(t), y = 10 * r * Math.Sin(t);
                return new Coordinate(x * 0.8 - y * 0.6, x * 0.6 + y * 0.8);
            }).ToArray();
            var multiPoint = F.CreateMultiPoint(points, null);

            var rectangle = (IPolygon2D)multiPoint.MinimumRotatedRectangle(F);

            var hull = (IPolygon2D)multiPoint.ConvexHull(F);
            double expected = BruteForceArea(Internal.Rings.Closed(hull[0])[..^1]);
            Assert.True(Math.Abs(rectangle.Area() - expected) <= 1e-9 * (1 + expected), $"{rectangle.Area():R} vs {expected:R}, seed {seed}, count {count}");
        }, iter: 300);
    }

    // ---------- MinimumBoundingCircle ----------

    [Fact]
    [Covers("MinimumBoundingCircle")]
    public void MinimumBoundingCircle_of_cases()
    {
        var two = G("MULTIPOINT ((0 0), (6 8))").MinimumBoundingCircle()!.Value;
        Assert.Equal(new Coordinate(3, 4), two.Centre);
        Assert.Equal(5, two.Radius, 12);

        var triangle = G("POLYGON ((0 0, 4 0, 0 3, 0 0))").MinimumBoundingCircle()!.Value; // right angle: the hypotenuse is the diameter
        Assert.Equal(new Coordinate(2, 1.5), triangle.Centre);
        Assert.Equal(2.5, triangle.Radius, 12);

        var obtuse = G("MULTIPOINT ((0 0), (10 0), (5 1))").MinimumBoundingCircle()!.Value;
        Assert.Equal(5, obtuse.Radius, 12);

        Assert.Equal(0, G("POINT (7 7)").MinimumBoundingCircle()!.Value.Radius);
        Assert.Null(G("POLYGON EMPTY").MinimumBoundingCircle());
        Assert.Throws<ArgumentNullException>(() => ((IGeometry2D)null!).MinimumBoundingCircle());
    }

    [Fact]
    [Covers("MinimumBoundingCircle")]
    public void MinimumBoundingCircle_agrees_with_NTS_and_contains_every_vertex()
    {
        Gen.Select(Gen.Int[0, 2], Pt.Array[1, 20]).Sample((kind, pts) =>
        {
            var geometry = Make(kind, pts);
            if (Internal.GeometryAccess.IsEmpty(geometry)) return;
            var circle = geometry.MinimumBoundingCircle()!.Value;

            var nts = new MinimumBoundingCircle(Nts.ToNts(geometry));

            Assert.True(Math.Abs(circle.Radius - nts.GetRadius()) <= 1e-9 * (1 + circle.Radius), $"{circle.Radius:R} vs NTS {nts.GetRadius():R}");
            Assert.All(Internal.GeometryAccess.AllPoints(geometry), p =>
                Assert.True(Internal.Rings.Distance(circle.Centre, new Coordinate(p.X, p.Y)) <= circle.Radius * (1 + 1e-12) + 1e-12));
        });
    }

    [Fact]
    [Covers("MinimumBoundingCircle")]
    public void Circle_ToPolygon_is_an_inscribed_polygon()
    {
        var circle = new Circle(new Coordinate(10, 20), 5);

        var polygon = circle.ToPolygon(F, 8);

        Assert.Equal(32, Internal.Rings.Closed(polygon[0]).Length - 1);
        Assert.True(polygon.IsValid());
        Assert.True(polygon.Area() < Math.PI * 25 && polygon.Area() > 0.98 * Math.PI * 25);
        Assert.Equal(new Coordinate(15, 20), Internal.Rings.Closed(polygon[0])[0]);
        Assert.Equal("POLYGON EMPTY", new Circle(new Coordinate(0, 0), 0).ToPolygon(F).ToWkt());
        Assert.Throws<ArgumentOutOfRangeException>(() => circle.ToPolygon(F, 0));
    }

    // ---------- Polylabel ----------

    [Fact]
    [Covers("Polylabel")]
    public void Polylabel_of_cases()
    {
        var square = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
        var pole = square.Polylabel(0.001)!.Value;
        Assert.Equal(5, pole.X, 1);
        Assert.Equal(5, pole.Y, 1);

        // a hole in the middle moves the label out of it
        var ring = (IPolygon2D)G("POLYGON ((0 0, 20 0, 20 10, 0 10, 0 0), (6 2, 14 2, 14 8, 6 8, 6 2))");
        var label = ring.Polylabel(0.001)!.Value;
        Assert.Equal(Location.Inside, ((IGeometry2D)ring).Locate(label));
        Assert.True(label.X < 6 || label.X > 14, "the label must not be in the hole: " + label);

        // an L shape: the pole is in the thick corner
        var l = (IPolygon2D)G("POLYGON ((0 0, 10 0, 10 2, 2 2, 2 10, 0 10, 0 0))");
        Assert.Equal(Location.Inside, ((IGeometry2D)l).Locate(l.Polylabel(0.001)!.Value));

        // several parts: the larger one
        var parts = (IPolygon2D)G("MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 10, 30 10, 30 30, 10 30, 10 10)))");
        Assert.True(parts.Polylabel(0.001)!.Value.X > 10);

        Assert.Null(((IPolygon2D)G("POLYGON EMPTY")).Polylabel());
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Polylabel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Polylabel(double.NaN));
        Assert.Throws<ArgumentNullException>(() => ((IPolygon2D)null!).Polylabel());
    }

    [Fact]
    [Covers("Polylabel")]
    public void Polylabel_is_inside_and_within_the_tolerance_of_the_NTS_maximum_inscribed_circle()
    {
        Gen.Select(Pt.Array[3, 10], Pt.Array[0, 6]).Sample((shell, holeSeed) =>
        {
            var polygon = Make(0, shell) as IPolygon2D;
            if (polygon is null || Internal.GeometryAccess.IsEmpty(polygon)) return;
            const double Tolerance = 0.01;

            var pole = polygon.Polylabel(Tolerance)!.Value;
            var ours = ((IGeometry2D)polygon).Locate(pole);
            var ntsPolygon = Nts.ToNts(polygon);
            var mic = new MaximumInscribedCircle(ntsPolygon, Tolerance / 10);
            double expected = mic.GetRadiusLine().Length;
            double radius = ntsPolygon.Boundary.Distance(new NetTopologySuite.Geometries.Point(pole.X, pole.Y));

            Assert.Equal(Location.Inside, ours);
            Assert.True(radius >= expected - 2 * Tolerance, $"radius {radius} vs NTS {expected}: {polygon.ToWkt()}");
        });
    }
}
