using SpatialNET.Imp;
using SpatialNET.Internal.Overlay;
using SpatialNET.IO;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.Overlay;

/// <summary>
/// The fast path of <c>Clip</c> (<see cref="RectangleClipper"/>) gives exactly the result of the overlay: the same vertices with
/// Z and M, the same orientation, start points and order of rings and parts.
/// </summary>
public class RectangleClipperTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static string Dump(IPolygon2D p)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < p.RingCount; i++)
        {
            sb.Append('[');
            foreach (var c in Internal.Rings.Coordinates(p[i]))
            {
                sb.Append($"({c.X:R} {c.Y:R} {c.Z:R} {c.M:R})");
            }

            sb.Append(']');
        }

        return sb.ToString();
    }

    private static void Same(IPolygon2D polygon, Envelope2D box, IGeometryFactory f, ref int fast)
    {
        if (box.Contains(polygon.Envelope())) return; // the copy without rounding, not the overlay
        var expected = Overlays.ClipByOverlay(polygon, box, f);
        var actual = polygon.Clip(box, f);
        Assert.Equal(Dump(expected), Dump(actual));
        Assert.Equal(expected.ToWkt(), actual.ToWkt());
        if (RectangleClipper.TryClip(polygon, box, polygon.Envelope(), f, null) is not null) fast++;
    }

    private static Coordinate[] Star(Random rnd, double cx, double cy, double radius, int n, double jag, bool ccw = true)
    {
        var pts = new Coordinate[n + 1];
        double phase = rnd.NextDouble() * Math.PI;
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n * (ccw ? 1 : -1) + phase;
            double r = radius * (1 + jag * (rnd.NextDouble() - 0.5));
            pts[i] = new Coordinate(cx + r * Math.Cos(t), cy + r * Math.Sin(t), 100 + i, 10 * i);
        }

        pts[n] = pts[0];
        return pts;
    }

    [Theory]
    [InlineData(8)]
    [InlineData(50)]
    [InlineData(200)]
    [InlineData(3000)]
    public void Random_polygons_give_the_overlay_result(int vertices)
    {
        var rnd = new Random(vertices);
        int fast = 0, runs = vertices > 1000 ? 30 : 400;
        for (int run = 0; run < runs; run++)
        {
            double cx = 533_000 + rnd.NextDouble() * 100, cy = 5_212_000 + rnd.NextDouble() * 100;
            var shell = Star(rnd, cx, cy, 1000, vertices, run % 3 == 0 ? 0.9 : 0.2, ccw: run % 2 == 0);
            ReadOnlyMemory<Coordinate>[] holes = run % 4 == 1
                ? [Star(rnd, cx + 200, cy, 150, 12, 0.3, ccw: false), Star(rnd, cx - 250, cy + 100, 120, 9, 0.3)]
                : [];
            var parts = new List<PolygonPart> { new(shell, holes) };
            if (run % 5 == 2) parts.Add(new PolygonPart(Star(rnd, cx + 2600, cy + 100, 900, vertices, 0.4)));
            var polygon = ZM.CreatePolygon(parts.ToArray(), null);

            double w = 200 + rnd.NextDouble() * 2500, h = 200 + rnd.NextDouble() * 2500;
            double x = cx - 1500 + rnd.NextDouble() * 3000 - w / 2, y = cy - 1500 + rnd.NextDouble() * 3000 - h / 2;
            var box = new Envelope2D(x, y, x + w, y + h);
            Same(polygon, box, ZM, ref fast);
            Same(polygon, box, F, ref fast);
        }

        Assert.True(fast > runs / 2, $"the fast path ran only {fast} times");
    }

    [Theory]
    [InlineData("POLYGON ((5 5, 20 5, 20 20, 5 20, 5 5))")]
    [InlineData("POLYGON ((-5 -5, 15 -5, 15 15, -5 15, -5 -5))")]                                    // rectangle inside the polygon
    [InlineData("POLYGON ((-5 -5, 15 -5, 15 15, -5 15, -5 -5), (2 2, 2 8, 8 8, 8 2, 2 2))")]          // with a hole inside the window
    [InlineData("POLYGON ((-5 -5, 15 -5, 15 15, -5 15, -5 -5), (2 2, 2 8, 18 8, 18 2, 2 2))")]        // hole cut by the border
    [InlineData("POLYGON ((-5 2, 15 2, 15 4, -3 4, -3 6, 15 6, 15 8, -5 8, -5 2))")]                  // two strips: two parts
    [InlineData("POLYGON ((-5 5, 5 -5, 15 5, 5 15, -5 5))")]                                          // diamond cutting all corners
    [InlineData("POLYGON ((3 -5, 7 -5, 7 15, 3 15, 3 -5))")]                                          // band through the window
    [InlineData("MULTIPOLYGON (((1 1, 4 1, 4 4, 1 4, 1 1)), ((6 6, 12 6, 12 12, 6 12, 6 6)))")]       // one part inside, one cut
    [InlineData("POLYGON ((-20 -20, 30 -20, 30 30, -20 30, -20 -20), (-10 -10, -10 20, 20 20, 20 -10, -10 -10))")] // window in a hole
    [InlineData("POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))")]                                        // touches the border only
    [InlineData("POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0))")]                                           // shares two sides
    [InlineData("POLYGON ((5 -5, 15 5, 5 15, -5 5, 5 -5), (5 0, 0 5, 5 10, 10 5, 5 0))")]             // hole touching the border
    [InlineData("POLYGON ((-5 2, 15 2, 15 8, -5 8, 5 5, -5 2))")]                                    // a vertex inside
    public void Special_cases_give_the_overlay_result(string wkt)
    {
        int fast = 0;
        var g = Wkt.Read(wkt, F);
        foreach (var box in new[] { new Envelope2D(0, 0, 10, 10), new Envelope2D(0.3, 0.7, 9.1, 9.9) })
        {
            Same((IPolygon2D)g, box, F, ref fast);
        }
    }

    [Fact]
    public void The_fast_path_carries_Z_and_M_and_falls_back_near_the_border()
    {
        var polygon = MGeo.Polygon(MGeo.Square(5, 5, 20, 20, MGeo.WithM));
        Assert.NotNull(RectangleClipper.TryClip(polygon, new Envelope2D(0, 0, 10, 10), polygon.Envelope(), ZM, null));

        // a vertex exactly on the border: the overlay decides
        var onBorder = (IPolygon2D)Wkt.Read("POLYGON ((5 5, 10 7, 20 5, 20 20, 5 20, 5 5))", F);
        Assert.Null(RectangleClipper.TryClip(onBorder, new Envelope2D(0, 0, 10, 10), onBorder.Envelope(), F, null));
    }
}
