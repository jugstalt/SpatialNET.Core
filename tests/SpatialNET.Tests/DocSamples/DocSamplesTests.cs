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
    public void Polygonize()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: polygonize
        var lines = Wkt.Read<IPolyline2D>("MULTILINESTRING ((0 0, 10 0, 10 10, 0 10, 0 0), (0 0, 10 10), (0 10, 10 0))");

        IReadOnlyList<IPolygon2D> faces = lines.Polygonize();   // four triangles of area 25
        // --- end

        Assert.Equal(4, faces.Count);
        Assert.All(faces, f => Assert.Equal(25, f.Area(), 6));
    }

    [Fact]
    public void TransformationsAndChecks()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: transformations
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var track = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 4 4, 8 0)");

        IPolygon2D moved = parcel.Translate(100, 50);                          // dx, dy
        IPolygon2D turned = parcel.Rotate(Math.PI / 4, parcel.Centroid());     // radians, counter-clockwise
        IPolygon2D mirrored = parcel.Scale(-1, 1);                             // still valid, rings oriented again

        bool simple = track.IsSimple();                                        // true: no self-intersection
        double apart = track.HausdorffDistance(Wkt.Read("LINESTRING(0 0, 8 0)")); // 4: the apex is 4 away
        // --- end

        Assert.Equal(new Envelope2D(100, 50, 110, 60), moved.Envelope());
        Assert.Equal(100, turned.Area(), 6);
        Assert.True(mirrored.IsValid());
        Assert.True(simple);
        Assert.Equal(4, apart, 9);
    }

    [Fact]
    public void BoundingShapesAndLabel()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: shapes
        var lot = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 10, 0 10, 0 0), (6 2, 14 2, 14 8, 6 8, 6 2))");

        IGeometry2D box = lot.MinimumRotatedRectangle();   // the tightest rectangle, at any angle
        Circle? around = lot.MinimumBoundingCircle();      // centre and radius of the smallest circle
        IPolygon2D disc = around!.Value.ToPolygon();       // the same circle as a polygon
        Coordinate? label = lot.Polylabel();               // inside, and as far from the edge as possible: not in the hole
        // --- end

        Assert.Equal(200, box.Area(), 6);
        Assert.Equal(Math.Sqrt(500) / 2, around.Value.Radius, 9);
        Assert.True(disc.IsValid());
        Assert.Equal(Location.Inside, lot.Locate(label!.Value));
    }

    [Fact]
    public void CleanUp()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: cleanup
        var land = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 20, 0 20, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2), (6 6, 12 6, 12 12, 6 12, 6 6))");

        IPolygon2D solid = land.RemoveHoles();                // no holes at all
        IPolygon2D lakes = land.RemoveHoles(minArea: 5);      // the pond of 4 goes, the lake of 36 stays
        IGeometry2D big = land.RemoveSmallParts(100);         // parts of less than 100 are dropped (polygon: area, line: length)
        IGeometry2D rounded = land.SnapToGrid(5);             // only rounds; the result may be invalid
        IGeometry2D safe = land.ReducePrecision(5);           // rounds and keeps it valid
        // --- end

        Assert.Equal(400, solid.Area(), 9);
        Assert.Equal(364, lakes.Area(), 9);
        Assert.Equal(360, big.Area(), 9);
        Assert.NotNull(rounded);
        Assert.True(safe.IsValid());
    }

    [Fact]
    public void TriangulationAndConcaveHull()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: triangulation
        var wells = Wkt.Read("MULTIPOINT ((0 0), (4 0), (8 0), (0 4), (4 4), (8 4), (0 8), (4 8), (8 8), (12 2), (12 6), (13 4))");

        IReadOnlyList<IPolygon2D> triangles = wells.DelaunayTriangles();      // no vertex inside the circumcircle of a triangle
        IGeometry2D around = wells.ConcaveHull(5.5);                          // keeps every edge on the border up to 5.5 long
        IGeometry2D byRatio = wells.ConcaveHullByRatio(0.3);                  // the same, scale-free: 0 = tightest, 1 = convex hull
        // --- end

        Assert.Equal(wells.ConvexHull().Area(), triangles.Sum(t => t.Area()), 6);
        Assert.True(around.IsValid());
        Assert.True(around.Area() <= wells.ConvexHull().Area());
        Assert.True(byRatio.IsValid());
    }

    [Fact]
    public void PredicatesWithATolerance()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // two parcels with a boundary that is 4 mm off, and one that sticks out by 3 mm
        var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var b = Wkt.Read<IPolygon2D>("POLYGON((10.004 0, 20 0, 20 10, 10.004 10, 10.004 0))");

        // --- snippet: tolerance
        double t = 0.01;                                    // the tolerance, in the unit of your coordinates (here 1 cm)

        bool intersects = a.IsWithinDistance(b, t);         // Intersects: closer than t
        bool disjoint = !a.IsWithinDistance(b, t);          // Disjoint: farther than t
        bool contains = a.Buffer(t).Contains(b);            // Contains: b is in a, up to t over the edge
        bool within = b.Buffer(t).Contains(a);              // Within
        bool covers = a.Buffer(t).Covers(b);                // Covers
        bool coveredBy = b.Buffer(t).Covers(a);             // CoveredBy
        bool touches = a.IsWithinDistance(b, t) && !a.Buffer(-t).Intersects(b);          // Touches (areas): they meet, and overlap by less than t
        bool overlaps = a.Buffer(-t).Intersects(b.Buffer(-t))                             // Overlaps (areas): they overlap by more than t,
            && !a.Buffer(t).Covers(b) && !b.Buffer(t).Covers(a);                          // and neither lies in the other
        bool equal = a.HausdorffDistance(b) <= t;           // EqualsTopo: no vertex of one is farther than t from the other
        // --- end

        Assert.False(a.Intersects(b));   // the exact predicate says no ...
        Assert.True(intersects);         // ... the tolerant one yes
        Assert.False(disjoint);
        Assert.True(touches);
        Assert.False(overlaps);
        Assert.False(contains);
        Assert.False(equal);

        var inner = Wkt.Read<IPolygon2D>("POLYGON((5 5, 10.003 5, 10.003 6, 5 6, 5 5))");
        Assert.False(a.Contains(inner));
        Assert.True(a.Buffer(0.01).Contains(inner));
        Assert.True(inner.Buffer(0.01).Contains(Wkt.Read<IPolygon2D>("POLYGON((5.001 5.001, 6 5.001, 6 5.5, 5.001 5.5, 5.001 5.001))")));

        var shifted = Wkt.Read<IPolygon2D>("POLYGON((0.004 0, 10.004 0, 10.004 10, 0.004 10, 0.004 0))");
        Assert.False(a.EqualsTopo(shifted));
        Assert.True(a.HausdorffDistance(shifted) <= 0.01);

        var half = Wkt.Read<IPolygon2D>("POLYGON((5 0, 15 0, 15 10, 5 10, 5 0))");
        Assert.True(a.Buffer(-0.01).Intersects(half.Buffer(-0.01)) && !a.Buffer(0.01).Covers(half) && !half.Buffer(0.01).Covers(a));
        Assert.False(a.Buffer(-0.01).Intersects(b));
    }

    [Fact]
    public void LocateAndClip()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 5 15, 15 15, 15 5, 5 5))");

        // --- snippet: locate
        Location a = parcel.Locate(new Coordinate(2, 2));    // Inside
        Location b = parcel.Locate(new Coordinate(0, 10));   // Boundary
        Location c = parcel.Locate(new Coordinate(10, 10));  // Outside: in the hole
        // --- end

        // --- snippet: clip
        var tile = new Envelope2D(0, 0, 10, 10);

        IPolygon2D inTile = parcel.Clip(tile);    // cut at the border
        // --- end

        Assert.Equal((Location.Inside, Location.Boundary, Location.Outside), (a, b, c));
        Assert.Equal(75, inTile.Area(), 6);   // 10 x 10 minus the part of the hole (5..10 x 5..10)
    }

    [Fact]
    public void SpatialJoin()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;
        var parcelGeometries = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))"),
        };
        var zoneGeometries = new[] { Wkt.Read<IPolygon2D>("POLYGON((12 12, 20 12, 20 20, 12 20, 12 12))") };

        // --- snippet: spatial-join
        var parcels = parcelGeometries.ToSpatialIndex();
        var zones = zoneGeometries.ToSpatialIndex();

        // which parcels meet which flood zone? Envelope candidates first, then the exact test:
        var hits = parcels.Join(zones).Where(p => p.Left.Intersects(p.Right));

        // all overlapping pairs within one layer: every pair once, never a parcel with itself
        var overlaps = parcels.SelfJoin().Where(p => p.First.Overlaps(p.Second));

        // pairs that are close: the envelopes are widened by 5 map units
        var near = parcels.Join(zones, distance: 5);
        // --- end

        Assert.Single(hits);
        Assert.Single(overlaps);
        Assert.Equal(2, near.Count());
    }

    [Fact]
    public void MakeValidBowtie()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: make-valid
        var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

        bool before = bowtie.IsValid();                  // false
        IPolygon2D valid = bowtie.MakeValid();           // two triangles, valid
        // --- end

        Assert.False(before);
        Assert.True(valid.IsValid());
        Assert.Equal(50, valid.Area(), 6);
    }

    [Fact]
    public void MeasuresAndConstructions()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: measures-and-constructions
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var well = Wkt.Read<IPoint2D>("POINT(14 5)");

        var (onParcel, onWell) = parcel.NearestPoints(well)!.Value;  // (10 5) and (14 5)
        bool near = parcel.IsWithinDistance(well, 5);                // true: 4 away, and it stops as soon as it knows
        Coordinate? label = parcel.PointOnSurface();                 // a point inside, for a label
        IPolyline2D outline = parcel.Boundary();                     // the ring as a line
        IPolyline2D dense = outline.Densify(2.5);                    // no segment longer than 2.5
        IPolyline2D back = outline.Reverse();                        // the same line, backwards
        // --- end

        Assert.Equal((new Coordinate(10, 5), new Coordinate(14, 5)), (onParcel, onWell));
        Assert.True(near);
        Assert.True(parcel.Contains(ImpGeometryFactory.Instance.CreatePoint(label!.Value, null)));
        Assert.Equal(40, dense.Length());
        Assert.Equal(17, Internal.Rings.Coordinates(dense[0]).Length);
        Assert.Equal(outline.ToWkt(), back.Reverse().ToWkt());
    }

    [Fact]
    public void UnionAllDissolve()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: union-all
        var parcels = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((5 0, 15 0, 15 10, 5 10, 5 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((50 50, 60 50, 60 60, 50 60, 50 50))"),
        };

        IPolygon2D all = parcels.UnionAll();   // two parts: the merged row and the separate square

        // "Dissolve by key": group, then unite each group.
        var byOwner = parcels.GroupBy(p => p.Envelope().MinX < 20 ? "west" : "east").Select(g => g.UnionAll());
        // --- end

        Assert.Equal(250, all.Area(), 6);
        Assert.Equal(2, byOwner.Count());
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
    public void CalibrateM()
    {
        GeometryFactory.Default = ImpGeometryFactory.Instance;

        // --- snippet: calibrate-m
        var road = Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0, 10 20)");
        var posts = new[] { new Coordinate(5, 0, M: 100), new Coordinate(10, 15, M: 200) };

        IPolyline2D withM = road.CalibrateM(posts, maxSnapDistance: 0.5);
        double m = withM.ProjectToM(new Coordinate(10, 4)); // 145: 100 + (200 - 100) / (25 - 5) * (14 - 5)
        // --- end

        Assert.Equal(145, m, 9);
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
