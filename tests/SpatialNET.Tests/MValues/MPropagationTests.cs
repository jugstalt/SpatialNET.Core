namespace SpatialNET.Tests.MValues;

/// <summary>
/// M is carried and interpolated like Z and never influences a computation (ADR 0015). The NTS oracle does
/// not interpolate M, so the expectations are hand-written; the strongest check is that X/Y are identical
/// with and without M.
/// </summary>
public class MPropagationTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);
    private static readonly MFactory ZOnly = new(supportsZ: true, supportsM: false);
    private static readonly MFactory MOnly = new(supportsZ: false, supportsM: true);

    private const double Tolerance = 1e-6;

    private static MPolygon SquareA(Func<double, double, Coordinate> make) => MGeo.Polygon(MGeo.Square(0, 0, 10, 10, make));

    private static MPolygon TriangleB(Func<double, double, Coordinate> make) =>
        MGeo.Polygon([make(4, -3), make(14, 6), make(2, 12), make(4, -3)]);

    public static TheoryData<string> Operations => new("Intersection", "Union", "Difference", "SymDifference");

    private static IPolygon2D Run(string op, IPolygon2D a, IPolygon2D b, IGeometryFactory f) => op switch
    {
        "Intersection" => a.Intersection(b, factory: f),
        "Union" => a.Union(b, factory: f),
        "Difference" => a.Difference(b, factory: f),
        "SymDifference" => a.SymDifference(b, factory: f),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public void Overlay_keeps_M_at_original_vertices_and_interpolates_it_at_new_ones(string op)
    {
        var result = Run(op, SquareA(MGeo.WithM), TriangleB(MGeo.WithM), ZM);

        var coordinates = MGeo.Coordinates(result);
        Assert.NotEmpty(coordinates);
        // M is a linear function of the position, so every vertex, original or new, must reproduce it.
        Assert.All(coordinates, c =>
        {
            Assert.True(c.HasM);
            Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance);
        });
    }

    [Fact]
    public void Overlay_creates_new_vertices_with_interpolated_M()
    {
        var result = SquareA(MGeo.WithM).Intersection(TriangleB(MGeo.WithM), factory: ZM);

        var inputs = MGeo.Coordinates(SquareA(MGeo.WithM)).Concat(MGeo.Coordinates(TriangleB(MGeo.WithM))).ToArray();
        var created = MGeo.Coordinates(result)
            .Where(c => !inputs.Any(i => Math.Abs(i.X - c.X) < Tolerance && Math.Abs(i.Y - c.Y) < Tolerance))
            .ToArray();

        Assert.NotEmpty(created); // crossings of the two outlines
        Assert.All(created, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void Overlay_xy_is_identical_with_and_without_M(string op)
    {
        var withM = MGeo.Coordinates(Run(op, SquareA(MGeo.WithM), TriangleB(MGeo.WithM), ZM));
        var flat = MGeo.Coordinates(Run(op, SquareA((x, y) => new(x, y)), TriangleB((x, y) => new(x, y)), ZM));

        Assert.Equal(flat.Select(c => (c.X, c.Y)), withM.Select(c => (c.X, c.Y)));
        Assert.All(flat, c => Assert.False(c.HasM));
    }

    [Fact]
    public void At_a_crossing_the_M_of_the_first_operand_applies()
    {
        // A has M = 1 everywhere, B has M = 2 everywhere (like Z in ADR 0005).
        var a = SquareA((x, y) => new(x, y, M: 1));
        var b = MGeo.Polygon(MGeo.Square(5, 5, 15, 15, (x, y) => new(x, y, M: 2)));

        var result = MGeo.Coordinates(a.Intersection(b, factory: ZM));

        var dump = string.Join(" ", result.Select(c => $"({c.X} {c.Y} m{c.M})"));
        Assert.True(result.Any(c => c is { X: 10, Y: 5 }), dump);
        Assert.All(result.Where(c => c is { X: 10, Y: 5 }), c => Assert.Equal(1, c.M)); // crossing: first operand
        Assert.True(result.Any(c => c is { X: 5, Y: 5 }), dump);
        Assert.All(result.Where(c => c is { X: 5, Y: 5 }), c => Assert.Equal(2, c.M)); // vertex of B only
    }

    [Fact]
    public void M_of_zero_is_a_measure()
    {
        var result = SquareA((x, y) => new(x, y, M: 0)).Union(TriangleB((x, y) => new(x, y, M: 0)), factory: ZM);

        Assert.All(MGeo.Coordinates(result), c =>
        {
            Assert.True(c.HasM);
            Assert.Equal(0, c.M);
        });
    }

    [Fact]
    public void Without_M_at_one_end_the_new_vertex_has_no_M()
    {
        var line = MGeo.Polyline(new(0, 0, M: 0), new(10, 0));
        var cutter = MGeo.Polyline(new(5, -1), new(5, 1));

        var pieces = line.Cut(cutter, ZM);

        var all = pieces.SelectMany(p => MGeo.Coordinates(p)).ToArray();
        Assert.Equal(0, all.Single(c => c is { X: 0 }).M);          // original vertex keeps its M
        Assert.All(all.Where(c => c.X > 0), c => Assert.False(c.HasM)); // never guessed, never 0
    }

    [Fact]
    public void Cut_interpolates_M_on_the_original_segment()
    {
        var line = MGeo.Polyline(new(0, 0, M: 0), new(10, 0, M: 100));
        var cutter = MGeo.Polyline(new(4, -1), new(4, 1));

        var pieces = line.Cut(cutter, ZM);

        Assert.Equal(2, pieces.Count);
        var ends = pieces.SelectMany(p => MGeo.Coordinates(p)).Where(c => c.X is > 3.9 and < 4.1).ToArray();
        Assert.Equal(2, ends.Length); // both pieces share the cut vertex
        Assert.All(ends, c => Assert.Equal(40, c.M, Tolerance));
    }

    [Fact]
    public void Cut_polygon_keeps_and_interpolates_M()
    {
        var polygon = SquareA(MGeo.WithM);
        var cutter = MGeo.Polyline(new(5, -5), new(5, 15));

        var pieces = polygon.Cut(cutter, ZM);

        Assert.Equal(2, pieces.Count);
        Assert.All(pieces.SelectMany(p => MGeo.Coordinates(p)), c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance));
    }

    [Fact]
    public void Line_overlay_interpolates_M()
    {
        var line = MGeo.Polyline(MGeo.WithM(0, 0), MGeo.WithM(20, 0));

        var inside = line.Intersection(SquareA(MGeo.WithM), factory: ZM);

        var coordinates = MGeo.Coordinates(inside);
        Assert.Contains(coordinates, c => c is { X: 10 });
        Assert.All(coordinates, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance));
    }

    [Fact]
    public void Point_overlay_keeps_M()
    {
        var points = new MMultiPoint([new(2, 2, M: 7), new(20, 20, M: 9)]);

        var inside = points.Intersection(SquareA(MGeo.WithM), factory: ZM);

        var c = Assert.Single(MGeo.Coordinates(inside));
        Assert.Equal(7, c.M);
    }

    [Fact]
    public void Simplify_keeps_M_of_the_remaining_vertices()
    {
        var line = MGeo.Polyline(MGeo.WithM(0, 0), MGeo.WithM(5, 0.01), MGeo.WithM(10, 0));

        var simplified = line.Simplify(0.1, factory: ZM);

        var coordinates = MGeo.Coordinates(simplified);
        Assert.Equal(2, coordinates.Length);
        Assert.Equal(MGeo.Linear(0, 0), coordinates[0].M);
        Assert.Equal(MGeo.Linear(10, 0), coordinates[1].M);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Simplify_polygon_keeps_M(bool preserveTopology)
    {
        var ring = new[] { MGeo.WithM(0, 0), MGeo.WithM(5, 0.01), MGeo.WithM(10, 0), MGeo.WithM(10, 10), MGeo.WithM(0, 10), MGeo.WithM(0, 0) };

        var simplified = MGeo.Polygon(ring).Simplify(0.1, preserveTopology, ZM);

        var coordinates = MGeo.Coordinates(simplified);
        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance));
    }

    [Fact]
    public void OffsetCurve_interpolates_M_from_the_nearest_source_segment()
    {
        var line = MGeo.Polyline(new(0, 0, M: 0), new(10, 0, M: 100));

        var curve = line.OffsetCurve(1, factory: ZM);

        var coordinates = MGeo.Coordinates(curve);
        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, c => Assert.Equal(c.X * 10, c.M, Tolerance));
    }

    [Fact]
    public void Buffer_and_ConvexHull_produce_no_M()
    {
        var polygon = SquareA(MGeo.WithM);

        Assert.All(MGeo.Coordinates(polygon.Buffer(1, factory: ZM)), c => Assert.False(c.HasM));
        Assert.All(MGeo.Coordinates(polygon.ConvexHull(ZM)), c => Assert.False(c.HasM));
    }

    private static Coordinate WithZAndM(double x, double y) => new(x, y, x - y, MGeo.Linear(x, y));

    [Fact]
    public void Results_have_no_M_if_the_factory_does_not_support_it()
    {
        var result = Run("Union", SquareA(WithZAndM), TriangleB(WithZAndM), ZOnly);

        var coordinates = MGeo.Coordinates(result);
        Assert.All(coordinates, c => Assert.False(c.HasM));
        Assert.All(coordinates, c => Assert.Equal(c.X - c.Y, c.Z, Tolerance)); // Z is unaffected
    }

    [Fact]
    public void Results_have_no_Z_but_keep_M_if_only_M_is_supported()
    {
        var result = Run("Union", SquareA(WithZAndM), TriangleB(WithZAndM), MOnly);

        var coordinates = MGeo.Coordinates(result);
        Assert.All(coordinates, c => Assert.False(c.HasZ));
        Assert.All(coordinates, c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance));
    }

    [Fact]
    public void Z_and_M_are_carried_independently()
    {
        var result = Run("Intersection", SquareA(WithZAndM), TriangleB(WithZAndM), ZM);

        Assert.All(MGeo.Coordinates(result), c =>
        {
            Assert.Equal(c.X - c.Y, c.Z, Tolerance);
            Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, Tolerance);
        });
    }
}
