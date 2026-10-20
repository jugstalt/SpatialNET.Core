using SpatialNET.Imp;
using SpatialNET.Internal.Noding;
using SpatialNET.Internal.Overlay;
using SpatialNET.Tests.Oracle;
using Geometry = NetTopologySuite.Geometries.Geometry;
using OverlayNG = NetTopologySuite.Operation.OverlayNG.OverlayNG;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;
using SpatialFunction = NetTopologySuite.Operation.Overlay.SpatialFunction;

namespace SpatialNET.Tests.Overlay;

/// <summary>Envelope-Vorfilter (#38): <see cref="RingClipper"/> erhält Windungszahlen, Fast-Paths ändern keine Ergebnisse.</summary>
public class EnvelopeFilterTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly Envelope2D R = new(0, 0, 10, 10);

    private static Coordinate[] Ring(params (double X, double Y)[] points) =>
        points.Append(points[0]).Select(p => new Coordinate(p.X, p.Y)).ToArray();

    /// <summary>Windungszahl eines geschlossenen Rings an einem Punkt (Testpunkte liegen nie auf dem Ring).</summary>
    private static int Winding(Coordinate[] ring, double x, double y)
    {
        int w = 0;
        for (int i = 0; i + 1 < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[i + 1];
            double cross = (b.X - a.X) * (y - a.Y) - (x - a.X) * (b.Y - a.Y);
            if (a.Y <= y && b.Y > y && cross > 0) w++;
            else if (a.Y > y && b.Y <= y && cross < 0) w--;
        }

        return w;
    }

    private static void AssertWindingPreservedInside(Coordinate[] ring)
    {
        var clipped = RingClipper.Clip(ring, R);
        Assert.Equal(clipped[0], clipped[^1]);
        for (double x = 0.0371; x < 10; x += 0.4137)
        {
            for (double y = 0.0213; y < 10; y += 0.3971)
            {
                Assert.Equal(Winding(ring, x, y), Winding(clipped, x, y));
            }
        }

        // jeder neue Punkt liegt auf dem Rand von R
        var original = ring.ToHashSet();
        foreach (var c in clipped.Where(c => !original.Contains(c)))
        {
            Assert.True(c.X is 0 or 10 || c.Y is 0 or 10, $"{c} liegt nicht auf dem Rand.");
            Assert.InRange(c.X, 0, 10);
            Assert.InRange(c.Y, 0, 10);
        }
    }

    [Fact]
    public void Ring_enclosing_the_rectangle_becomes_its_boundary()
    {
        var ring = Ring((-20, -30), (40, -25), (35, 50), (-15, 45));

        var clipped = RingClipper.Clip(ring, R);

        Assert.Equal(1, Winding(clipped, 5, 5));
        Assert.All(clipped, c => Assert.True(c.X is 0 or 10 && c.Y is 0 or 10, $"{c} ist keine Ecke von R."));
        AssertWindingPreservedInside(ring);
    }

    [Fact]
    public void Clockwise_enclosing_ring_keeps_winding_minus_one()
    {
        var ring = Ring((-20, -30), (-15, 45), (35, 50), (40, -25));

        Assert.Equal(-1, Winding(RingClipper.Clip(ring, R), 5, 5));
        AssertWindingPreservedInside(ring);
    }

    [Fact]
    public void Run_around_a_corner_is_retracted_onto_two_sides()
    {
        // tritt links in R ein, verlässt es unten und läuft außen um die Ecke (0, 0) zurück
        var ring = Ring((5, 5), (-3, 7), (-8, 3), (-6, -6), (4, -9), (6, -2));

        var clipped = RingClipper.Clip(ring, R);

        Assert.Contains(new Coordinate(0, 0), clipped);
        Assert.True(clipped.Length < ring.Length + 4);
        AssertWindingPreservedInside(ring);
    }

    [Fact]
    public void Ring_far_outside_does_not_enclose_anything()
    {
        var ring = Ring((20, 20), (30, 20), (30, 30), (20, 30));

        Assert.Equal(0, Winding(RingClipper.Clip(ring, R), 5, 5));
        AssertWindingPreservedInside(ring);
    }

    [Fact]
    public void Ring_touching_the_rectangle_with_every_segment_is_unchanged()
    {
        var ring = Ring((-5, 5), (5, -5), (15, 5), (5, 15));

        Assert.Same(ring, RingClipper.Clip(ring, R));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_rings_keep_their_winding_inside(int seed)
    {
        var random = new Random(seed);
        for (int k = 0; k < 40; k++)
        {
            // selbstüberschneidende Zufallsringe um R herum, auch mehrfach umlaufend
            int n = random.Next(3, 40);
            double turns = random.Next(1, 3) * (random.Next(2) == 0 ? 1 : -1);
            var points = Enumerable.Range(0, n).Select(i =>
            {
                double angle = 2 * Math.PI * turns * i / n + random.NextDouble() * 0.5;
                double radius = random.NextDouble() * 30;
                return (5 + radius * Math.Cos(angle) + random.NextDouble() * 6 - 3, 5 + radius * Math.Sin(angle) + random.NextDouble() * 6 - 3);
            }).ToArray();

            AssertWindingPreservedInside(Ring(points));
        }
    }

    private static IPolygon2D Polygon(params Coordinate[][] rings) =>
        F.CreatePolygon(rings.Select(r => new PolygonPart(r)).ToArray(), null);

    private static IPolygon2D Circle(double cx, double cy, double r, int n) =>
        Polygon(Enumerable.Range(0, n + 1)
            .Select(i => new Coordinate(cx + r * Math.Cos(2 * Math.PI * (i % n) / n), cy + r * Math.Sin(2 * Math.PI * (i % n) / n)))
            .ToArray());

    /// <summary>Overlay wie <c>OverlayRunner.Areas</c>, aber ohne Vorfilter und Fast-Paths.</summary>
    private static IPolygon2D Unfiltered(OverlayOp op, IPolygon2D a, IPolygon2D b, FillRule fill)
    {
        var engine = new OverlayEngine(a, b, GridModel.Resolve(null, a, b), fill);
        var parts = engine.Areas(h => OverlayEngine.Apply(op, engine.InsideLeft(h, 0), engine.InsideLeft(h, 1)));
        return F.CreatePolygon(parts.ToArray(), a);
    }

    private static readonly string[] Valid = ["Disjoint", "NearlyTouching", "Contained", "Containing", "Edge", "Overlap", "Holes"];

    public static TheoryData<string> ValidCases => [.. Valid];

    /// <summary>Zusätzlich überlappende Schalen in A – ungültig für NTS, daher nur gegen das Ergebnis ohne Vorfilter.</summary>
    public static TheoryData<string> Cases => [.. Valid, "SelfOverlapping"];

    private static (IPolygon2D A, IPolygon2D B) Create(string name) => name switch
    {
        "Disjoint" => (Circle(500_000, 5_200_000, 1000, 200), Circle(505_000, 5_200_000, 1000, 200)),
        // Envelopes nur wenige Gitterzellen auseinander: kein Fast-Path, Vorfilter am Rand
        "NearlyTouching" => (Circle(500_000, 5_200_000, 1000, 200), Circle(502_000.000_001, 5_200_000, 1000, 200)),
        "Contained" => (Circle(500_000, 5_200_000, 1000, 300), Circle(500_100, 5_200_050, 200, 100)),
        "Containing" => (Circle(500_100, 5_200_050, 200, 100), Circle(500_000, 5_200_000, 1000, 300)),
        "Edge" => (Circle(500_000, 5_200_000, 1000, 500), Circle(501_000, 5_200_000, 50, 60)),
        "Overlap" => (Circle(500_000, 5_200_000, 1000, 400), Circle(500_500, 5_200_300, 1000, 400)),
        "Holes" => (Polygon(Ring((0, 0), (100, 0), (100, 100), (0, 100)), Ring((20, 20), (20, 80), (80, 80), (80, 20))),
                    Polygon(Ring((70, 70), (90, 70), (90, 90), (70, 90)))),
        // zwei überlappende Schalen in A (Füllregel entscheidet), B schneidet nur eine Ecke
        _ => (Polygon(Ring((0, 0), (60, 0), (60, 60), (0, 60)), Ring((40, 40), (100, 40), (100, 100), (40, 100))),
              Polygon(Ring((90, 90), (120, 90), (120, 120), (90, 120)))),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Filtered_overlay_equals_the_unfiltered_one(string name)
    {
        var (a, b) = Create(name);
        foreach (var fill in new[] { FillRule.EvenOdd, FillRule.NonZero })
        {
            AssertSame(Unfiltered(OverlayOp.Intersection, a, b, fill), a.Intersection(b, fill, F));
            AssertSame(Unfiltered(OverlayOp.Difference, a, b, fill), a.Difference(b, fill, F));
            AssertSame(Unfiltered(OverlayOp.Difference, b, a, fill), b.Difference(a, fill, F));
        }
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Filtered_overlay_matches_the_oracle(string name)
    {
        var (a, b) = Create(name);
        double grid = GridModel.Resolve(null, a, b).Size;
        var ntsA = Nts.ToNts(a);
        var ntsB = Nts.ToNts(b);
        Geometry Oracle(Geometry x, Geometry y, SpatialFunction op) =>
            new OverlayNG(x, y, new PrecisionModel(1 / grid), op) { StrictMode = true }.GetResult();

        OracleAssert.SameGeometry(Oracle(ntsA, ntsB, SpatialFunction.Intersection), a.Intersection(b, FillRule.NonZero, F), grid);
        OracleAssert.SameGeometry(Oracle(ntsA, ntsB, SpatialFunction.Difference), a.Difference(b, FillRule.NonZero, F), grid);
        OracleAssert.SameGeometry(Oracle(ntsB, ntsA, SpatialFunction.Difference), b.Difference(a, FillRule.NonZero, F), grid);
    }

    [Fact]
    public void Disjoint_intersection_is_empty_and_difference_is_the_first_operand()
    {
        var (a, b) = Create("Disjoint");

        Assert.Equal(0, a.Intersection(b, factory: F).RingCount);
        OracleAssert.Close(a.Area(), a.Difference(b, factory: F).Area());
    }

    /// <summary>Gleiche Teilflächen mit gleichen Koordinaten (Reihenfolge der Ringe und Startpunkte egal).</summary>
    private static void AssertSame(IPolygon2D expected, IPolygon2D actual)
    {
        var e = Nts.ToNts(expected).Normalized();
        var a = Nts.ToNts(actual).Normalized();
        Assert.True(e.EqualsExact(a), $"Soll: {e.AsText()}\nIst:  {a.AsText()}");
    }
}
