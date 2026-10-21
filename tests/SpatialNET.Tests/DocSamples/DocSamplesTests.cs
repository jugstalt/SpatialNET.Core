using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.DocSamples;

// Die Code-Beispiele aus README.md und docs/{en,de}/ (getting-started, host-integration).
// Ändert sich ein Beispiel, muss die Dokumentation mitgezogen werden – und umgekehrt.

[Collection(GlobalStateCollection.Name)]
public sealed class DocSamplesTests : IDisposable
{
    private readonly IGeometryFactory? _savedFactory = GeometryFactory.Default;
    private readonly double? _savedGridSize = Precision.DefaultGridSize;

    public void Dispose()
    {
        GeometryFactory.Default = _savedFactory;
        Precision.DefaultGridSize = _savedGridSize;
    }

    [Fact]
    public void QuickStart()
    {
        // --- snippet: quickstart
        // Once at startup: the factory that algorithms use to create their results.
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

        IPolygon2D overlap = a.Intersection(b);

        Console.WriteLine(overlap.ToWkt());          // POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))
        Console.WriteLine(overlap.Area());           // 25
        Console.WriteLine(a.Intersects(b));          // True
        Console.WriteLine(a.Buffer(1).Area() > 100); // True
        // --- end

        Assert.Equal("POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))", overlap.ToWkt());
        Assert.Equal(25, overlap.Area());
        Assert.True(a.Intersects(b));
        Assert.True(a.Buffer(1).Area() > 100);
    }

    [Fact]
    public void FillRuleAndGrid()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

        // --- snippet: overlay-options
        // Grid size in map units (default: derived automatically from the coordinates).
        IPolygon2D union = a.Union(b, FillRule.NonZero, gridSize: 0.001);

        // Several expensive predicates against the same geometry: prepare it once.
        PreparedGeometry prepared = a.Prepare();
        bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

        // Full DE-9IM: matrix and pattern matching.
        string matrix = a.Relate(b).ToString(); // "212101212"
        // --- end

        Assert.Equal(175, union.Area());
        Assert.True(inside);
        Assert.Equal("212101212", matrix);
        Assert.True(a.Relate(b, "T*T***T**"));
    }

    [Fact]
    public void LinearReferencing()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: linear-referencing
        var road = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 100, 10 0 200, 10 10 250)");

        Coordinate? at15 = road.Interpolate(15);        // (10 5), M = 225: M is interpolated, too
        double position = road.Project(new Coordinate(4, 3)); // 4: the nearest point is (4 0)
        Coordinate? km = road.InterpolateByM(150);      // (5 0): where the line first reaches M = 150
        IPolyline2D piece = road.SubstringByM(150, 225); // from (5 0) via (10 0) to (10 5)
        // --- end

        Assert.Equal(new Coordinate(10, 5, M: 225), at15);
        Assert.Equal(4, position);
        Assert.Equal(new Coordinate(5, 0, M: 150), km);
        Assert.Equal("LINESTRING M (5 0 150, 10 0 200, 10 5 225)", piece.ToWkt());
    }

    [Fact]
    public void ReadingAndWriting()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: io
        IGeometry2D g = Wkt.Read("LINESTRING(0 0, 10 10)");
        string json = g.ToGeoJson();
        IReadOnlyList<IGeometry2D> all = GeoJson.ReadAll(json); // also FeatureCollection

        byte[] wkb = g.ToWkb();                                 // ISO WKB, little endian
        IGeometry2D fromWkb = Wkb.Read(wkb);                    // ISO and EWKB
        // --- end

        Assert.Single(all);
        Assert.IsAssignableFrom<IPolyline2D>(all[0]);
        Assert.Equal("LINESTRING (0 0, 10 10)", fromWkb.ToWkt());
    }

    [Fact]
    public void ConceptFillRule()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-fillrule
        // Two partially overlapping shells in one polygon: invalid by the OGC rules.
        var overlapping = Wkt.Read<IPolygon2D>(
            "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0),(5 5, 15 5, 15 15, 5 15, 5 5))");

        Console.WriteLine(overlapping.IsValid());                            // False
        Console.WriteLine(overlapping.Union(overlapping).Area());            // 150 - even-odd: the overlap becomes a hole
        Console.WriteLine(overlapping.Union(overlapping, FillRule.NonZero).Area()); // 175 - the overlap is merged
        // --- end

        Assert.False(overlapping.IsValid());
        Assert.Equal(150, overlapping.Union(overlapping).Area());
        Assert.Equal(175, overlapping.Union(overlapping, FillRule.NonZero).Area());
    }

    [Fact]
    public void ConceptGrid()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-grid
        var strip = Wkt.Read<IPolygon2D>("POLYGON((0 1, 10 1, 10 5, 0 5, 0 1))");
        var triangle = Wkt.Read<IPolygon2D>("POLYGON((0 0, 1 3, -1 3, 0 0))");

        // Automatic grid (here 1e-11): the intersection point (1/3, 1) is rounded very finely.
        Console.WriteLine(strip.Intersection(triangle).ToWkt());                  // POLYGON ((0 1, 0.33333333333 1, 1 3, 0 3, 0 1))

        // Explicit grid of 0.01 map units.
        Console.WriteLine(strip.Intersection(triangle, gridSize: 0.01).ToWkt());  // POLYGON ((0 1, 0.33 1, 1 3, 0 3, 0 1))

        // Global default for all overlays that do not pass a grid size.
        Precision.DefaultGridSize = 0.01;
        // --- end

        Precision.DefaultGridSize = null;
        Assert.Equal("POLYGON ((0 1, 0.33333333333 1, 1 3, 0 3, 0 1))", strip.Intersection(triangle).ToWkt());
        Assert.Equal("POLYGON ((0 1, 0.33 1, 1 3, 0 3, 0 1))", strip.Intersection(triangle, gridSize: 0.01).ToWkt());
    }

    [Fact]
    public void ConceptPredicates()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-predicates
        var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var edge = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)"); // lies on the boundary of the square

        Console.WriteLine(square.Contains(edge)); // False - DE-9IM: no point of the edge lies in the interior
        Console.WriteLine(square.Covers(edge));   // True  - boundary points count
        Console.WriteLine(square.Touches(edge));  // True
        Console.WriteLine(square.Relate(edge));   // FF2101FF2
        // --- end

        Assert.False(square.Contains(edge));
        Assert.True(square.Covers(edge));
        Assert.True(square.Touches(edge));
        Assert.Equal("FF2101FF2", square.Relate(edge).ToString());
    }

    [Fact]
    public void ConceptSpatialIndex()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-index
        var parcels = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((8 8, 22 8, 8 12, 8 8))"),
        };

        // Build once; the index is immutable, so any number of threads may query it.
        SpatialIndex<IPolygon2D> index = parcels.ToSpatialIndex();

        // Range query: envelope candidates first, then the exact test on the real geometry.
        var probe = Wkt.Read<IPolygon2D>("POLYGON((16 10, 18 10, 18 12, 16 12, 16 10))");
        Console.WriteLine(index.Query(probe).Count());                         // 1 - the envelope of the triangle meets the probe's
        Console.WriteLine(index.Query(probe).Count(p => p.Intersects(probe))); // 0 - but the triangle itself does not

        // "Within 5 map units of a point": query with the point's envelope widened by 5.
        var here = new Coordinate(15, 5);
        Console.WriteLine(index.Query(new Envelope2D(here.X - 5, here.Y - 5, here.X + 5, here.Y + 5)).Count()); // 3

        // The nearest parcel by exact distance (without the delegate, the envelope distance is used).
        var hereGeometry = Wkt.Read<IGeometry2D>("POINT(15 5)");
        IReadOnlyList<IPolygon2D> nearest = index.Nearest(here, 1, (parcel, _) => parcel.Distance(hereGeometry));
        Console.WriteLine(nearest[0].ToWkt()); // POLYGON ((8 8, 22 8, 8 12, 8 8))
        // --- end

        Assert.Single(index.Query(probe));
        Assert.Equal(0, index.Query(probe).Count(p => p.Intersects(probe)));
        Assert.Equal(3, index.Query(new Envelope2D(here.X - 5, here.Y - 5, here.X + 5, here.Y + 5)).Count());
        Assert.Equal("POLYGON ((8 8, 22 8, 8 12, 8 8))", nearest[0].ToWkt());
    }

    [Fact]
    public void ConceptBufferAndOffset()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-buffer
        var line = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 10 0)");
        var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

        // A buffer around a line with flat ends: a 10 x 4 rectangle.
        Console.WriteLine(line.Buffer(2, new BufferOptions { CapStyle = CapStyle.Flat }).Area()); // 40

        // Round corners (default) versus mitre corners.
        Console.WriteLine(Math.Round(square.Buffer(1).Area(), 2));                                         // 143.12
        Console.WriteLine(square.Buffer(1, new BufferOptions { JoinStyle = JoinStyle.Mitre }).Area());      // 144

        // A negative distance shrinks a polygon.
        Console.WriteLine(square.Buffer(-2).ToWkt()); // POLYGON ((2 2, 8 2, 8 8, 2 8, 2 2))

        // Offset curves: positive distance to the left, negative to the right of the direction of travel.
        Console.WriteLine(line.OffsetCurve(2).ToWkt());  // LINESTRING (0 2, 10 2)
        Console.WriteLine(line.OffsetCurve(-2).ToWkt()); // LINESTRING (0 -2, 10 -2)
        // --- end

        Assert.Equal(40, line.Buffer(2, new BufferOptions { CapStyle = CapStyle.Flat }).Area());
        Assert.Equal(143.12, Math.Round(square.Buffer(1).Area(), 2));
        Assert.Equal(144, square.Buffer(1, new BufferOptions { JoinStyle = JoinStyle.Mitre }).Area());
        Assert.Equal("POLYGON ((2 2, 8 2, 8 8, 2 8, 2 2))", square.Buffer(-2).ToWkt());
        Assert.Equal("LINESTRING (0 2, 10 2)", line.OffsetCurve(2).ToWkt());
        Assert.Equal("LINESTRING (0 -2, 10 -2)", line.OffsetCurve(-2).ToWkt());
    }

    [Fact]
    public void ConceptSimplifyValidateCut()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: concept-simplify
        var noisy = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 1 0.1, 2 -0.1, 3 0.05, 4 0)");
        Console.WriteLine(noisy.Simplify(0.5).ToWkt()); // LINESTRING (0 0, 4 0)
        // --- end

        // --- snippet: concept-validate
        var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");
        Console.WriteLine(bowtie.IsValid()); // False

        foreach (var issue in bowtie.Validate().Issues)
        {
            Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
        }
        // --- end

        // --- snippet: concept-cut
        var square = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var cutter = Wkt.Read<IPolyline2D>("LINESTRING(5 -1, 5 11)");

        IReadOnlyList<IPolygon2D> pieces = square.Cut(cutter);

        Console.WriteLine(pieces.Count);      // 2
        Console.WriteLine(pieces[0].Area());  // 50
        // --- end

        Assert.Equal("LINESTRING (0 0, 4 0)", noisy.Simplify(0.5).ToWkt());
        Assert.False(bowtie.IsValid());
        var found = Assert.Single(bowtie.Validate().Issues);
        Assert.Equal(ValidationIssueKind.SelfIntersection, found.Kind);
        Assert.Equal((5, 5), (found.Location.X, found.Location.Y));
        Assert.Equal(2, pieces.Count);
        Assert.Equal(50, pieces[0].Area());
        Assert.Equal(50, pieces[1].Area());
    }

    [Fact]
    public void HostIntegration()
    {
        // --- snippet: host-integration
        // 1. Your own geometry classes implement the read-only interfaces ...
        var square = new HostPolygon(
            new HostRing(new HostPoint(0, 0), new HostPoint(10, 0), new HostPoint(10, 10), new HostPoint(0, 10)));
        var other = new HostPolygon(
            new HostRing(new HostPoint(5, 5), new HostPoint(15, 5), new HostPoint(15, 15), new HostPoint(5, 15)));

        // 2. ... and a factory creates the results as your own classes.
        GeometryFactory.Default = new HostGeometryFactory();

        IPolygon2D result = square.Intersection(other);

        Console.WriteLine(result is HostPolygon);                            // True
        Console.WriteLine(result.AsList().Count);                            // 1 ring
        // --- end

        Assert.IsType<HostPolygon>(result);
        Assert.Single(result.AsList());
        Assert.Equal(25, result.Area());
    }
}

// --- snippet: host-classes
// A minimal host: mutable classes that implement the SpatialNET interfaces.
public sealed class HostPoint(double x, double y) : IPoint3D
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Z { get; set; } // hosts that always carry a height simply return 0
}

public sealed class HostRing(params HostPoint[] points) : IRing2D
{
    public int PointCount => points.Length;

    public IPoint2D this[int index] => points[index];
}

public sealed class HostPolygon(params HostRing[] rings) : IPolygon2D
{
    public int RingCount => rings.Length;

    public IRing2D this[int index] => rings[index];
}

// The factory receives coordinates only and creates the host classes from them.
public sealed class HostGeometryFactory : IGeometryFactory
{
    public bool SupportsZ => false;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) =>
        new HostPoint(coordinate.X, coordinate.Y);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no multipoint class.");

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no polyline class.");

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<HostRing>();
        foreach (var part in parts)
        {
            rings.Add(ToRing(part.Shell.Span));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(ToRing(hole.Span));
            }
        }

        return new HostPolygon(rings.ToArray());
    }

    private static HostRing ToRing(ReadOnlySpan<Coordinate> coordinates)
    {
        var points = new HostPoint[coordinates.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new HostPoint(coordinates[i].X, coordinates[i].Y);
        }

        return new HostRing(points);
    }
}
// --- end
