using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.DocSamples;

// Die Rezepte aus docs/en/cookbook.md und docs/de/cookbook.md. Ändert sich ein Beispiel, muss die Dokumentation mitgezogen werden.

[Collection(GlobalStateCollection.Name)]
public sealed class CookbookSamplesTests : IDisposable
{
    private readonly IGeometryFactory? _savedFactory = GeometryFactory.Default;

    public CookbookSamplesTests() => GeometryFactory.Default = ImpGeometryFactory.Instance;

    public void Dispose() => GeometryFactory.Default = _savedFactory;

    [Fact]
    public void WhichParcelIsHere()
    {
        // --- snippet: cookbook-click
        var parcels = new Dictionary<string, IPolygon2D>
        {
            ["A-1"] = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            ["A-2"] = Wkt.Read<IPolygon2D>("POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
        };

        // Build once (the index never changes); the items are the dictionary entries.
        var index = parcels.ToSpatialIndex(p => p.Value.Envelope());

        // Which parcel is under the click? The index returns envelope candidates, Locate decides.
        var click = new Coordinate(14, 3);
        var hit = index.Query(new Envelope2D(click.X, click.Y, click.X, click.Y))
            .First(p => p.Value.Locate(click) != Location.Outside);

        Console.WriteLine(hit.Key); // A-2

        // Everything within 6 map units of a well: widen the envelope, then test exactly.
        var well = Wkt.Read<IPoint2D>("POINT(24 5)");
        var near = index.Query(new Envelope2D(well.X - 6, well.Y - 6, well.X + 6, well.Y + 6))
            .Where(p => p.Value.IsWithinDistance(well, 6))
            .Select(p => p.Key);

        Console.WriteLine(string.Join(",", near)); // A-2
        // --- end

        Assert.Equal("A-2", hit.Key);
        Assert.Equal(["A-2"], near.ToArray());
    }

    [Fact]
    public void ManyPointsAgainstOnePolygon()
    {
        // --- snippet: cookbook-prepared
        var district = Wkt.Read<IPolygon2D>("POLYGON((0 0, 100 0, 100 100, 0 100, 0 0))");
        var points = Enumerable.Range(0, 1000).Select(i => new Coordinate(i % 40 * 5, i / 40 * 5)).ToArray();

        // Prepare once; a PreparedGeometry is thread-safe, so the threads share it.
        PreparedGeometry prepared = district.Prepare();
        int inside = points.AsParallel().Count(p => prepared.Locate(p) == Location.Inside);

        Console.WriteLine(inside); // points strictly inside, not on the boundary
        // --- end

        Assert.Equal(points.Count(p => district.Locate(p) == Location.Inside), inside);
        Assert.True(inside > 0);
    }

    [Fact]
    public void SpatialJoinWithAreaShares()
    {
        // --- snippet: cookbook-join
        var parcels = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
        }.ToSpatialIndex();
        var floodZones = new[]
        {
            Wkt.Read<IPolygon2D>("POLYGON((5 -5, 15 -5, 15 5, 5 5, 5 -5))"),
        }.ToSpatialIndex();

        // Join walks both trees together and gives envelope pairs; the exact test and the overlay are yours.
        var shares = parcels.Join(floodZones)
            .Where(pair => pair.Left.Intersects(pair.Right))
            .Select(pair => (Parcel: pair.Left, Share: pair.Left.Intersection(pair.Right).Area() / pair.Left.Area()))
            .ToList();

        Console.WriteLine(shares.Count);   // 1: the second parcel is nowhere near the zone
        Console.WriteLine(shares[0].Share); // 0.25
        // --- end

        Assert.Single(shares);
        Assert.Equal(0.25, shares[0].Share, 9);
    }

    [Fact]
    public void DissolveByKey()
    {
        // --- snippet: cookbook-dissolve
        var landUse = new (string Use, string Wkt)[]
        {
            ("forest", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
            ("forest", "POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
            ("field", "POLYGON((0 20, 10 20, 10 30, 0 30, 0 20))"),
        };

        // One union per key; neighbours that share an edge become one polygon.
        Dictionary<string, IPolygon2D> dissolved = landUse
            .GroupBy(x => x.Use)
            .ToDictionary(g => g.Key, g => g.Select(x => Wkt.Read<IPolygon2D>(x.Wkt)).UnionAll());

        Console.WriteLine(dissolved["forest"].Area()); // 200
        Console.WriteLine(dissolved["forest"].RingCount); // 1: one outline, the shared edge is gone
        // --- end

        Assert.Equal(200, dissolved["forest"].Area(), 9);
        Assert.Equal(1, dissolved["forest"].RingCount);
        Assert.Equal(100, dissolved["field"].Area(), 9);
    }

    [Fact]
    public void RepairADrawnPolygon()
    {
        // --- snippet: cookbook-repair
        // A user drew a bow tie: the outline crosses itself.
        var drawn = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

        ValidationResult check = drawn.Validate();
        foreach (var issue in check.Issues)
        {
            Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
        }

        // MakeValid resolves it under the even-odd rule: two triangles that touch in a point.
        IPolygon2D repaired = check.IsValid ? drawn : drawn.MakeValid();
        Console.WriteLine(repaired.IsValid()); // True
        Console.WriteLine(repaired.Area());    // 50
        // --- end

        Assert.False(check.IsValid);
        Assert.Equal(ValidationIssueKind.SelfIntersection, check.Issues[0].Kind);
        Assert.True(repaired.IsValid());
        Assert.Equal(50, repaired.Area(), 6);
    }

    [Fact]
    public void SplitAParcel()
    {
        // --- snippet: cookbook-cut
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
        var fence = Wkt.Read<IPolyline2D>("LINESTRING(4 -1, 4 11)");

        // The pieces come as a flat list, in no left/right order.
        IReadOnlyList<IPolygon2D> pieces = parcel.Cut(fence);

        Console.WriteLine(string.Join(" and ", pieces.Select(p => p.Area()).Order())); // 40 and 60
        // --- end

        Assert.Equal([40d, 60d], pieces.Select(p => p.Area()).Order().ToArray());
    }

    [Fact]
    public void LinesToAreas()
    {
        // --- snippet: cookbook-lines
        // A road network digitised in pieces: the pieces only touch at their ends.
        var pieces = Wkt.Read<IPolyline2D>("MULTILINESTRING((0 0, 5 0), (5 0, 10 5), (10 5, 20 5))");
        IPolyline2D road = pieces.LineMerge();
        Console.WriteLine(road.PathCount); // 1: three pieces joined into one path

        // A closed outline with a line across it: the lines enclose two areas.
        var fences = Wkt.Read<IPolyline2D>(
            "MULTILINESTRING((0 0, 10 0, 10 10, 0 10, 0 0), (5 -5, 5 15))");
        IReadOnlyList<IPolygon2D> fields = fences.Polygonize();
        Console.WriteLine(fields.Count); // 2: the ends of the crossing line outside the outline are dropped
        // --- end

        Assert.Equal(1, road.PathCount);
        Assert.Equal(2, fields.Count);
        Assert.All(fields, f => Assert.Equal(50, f.Area(), 9));
    }

    [Fact]
    public void StationingAlongARoad()
    {
        // --- snippet: cookbook-stationing
        var road = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 100 0, 100 50)");

        Coordinate? at = road.Interpolate(120);               // (100 20): 120 along the road
        double chainage = road.Project(new Coordinate(40, 7)); // 40: where the nearest point of the road lies
        IPolyline2D section = road.Substring(50, 120);          // from (50 0) via (100 0) to (100 20)

        Console.WriteLine($"{at!.Value.X} {at.Value.Y}, {chainage}, {section.Length()}"); // 100 20, 40, 70
        // --- end

        Assert.Equal(new Coordinate(100, 20), at!.Value);
        Assert.Equal(40, chainage, 9);
        Assert.Equal(70, section.Length(), 9);
    }

    [Fact]
    public void RoadWithKilometrePosts()
    {
        // --- snippet: cookbook-calibrate
        var road = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 100 0, 100 50)");
        var posts = new[]
        {
            new Coordinate(0, 0, M: 12000), new Coordinate(100, 0, M: 12200), new Coordinate(100, 50, M: 12300),
        };

        // Once, when the road is loaded: the posts become M on the line.
        IPolyline2D route = road.CalibrateM(posts, maxSnapDistance: 0.5);

        double measure = route.ProjectToM(new Coordinate(40, 7));      // 12080: a click becomes a measure
        IMultiPoint2D at = route.LocateAlong(12_150);                   // (75 0): a measure becomes a point
        IPolyline2D section = route.LocateBetween(12_100, 12_260);      // (50 0) (100 0) (100 30): a range becomes a stretch

        Console.WriteLine($"{measure}, {at[0].X} {at[0].Y}, {section.Length()}"); // 12080, 75 0, 80
        // --- end

        Assert.Equal(12080, measure, 9);
        Assert.Equal((75d, 0d), (at[0].X, at[0].Y));
        Assert.Equal(80, section.Length(), 9);
    }

    [Fact]
    public void SpeedAndSurfaceAlongARoute()
    {
        // --- snippet: cookbook-events
        var route = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 12000, 100 0 12200, 100 50 12300)");
        var speed = new[] { new LineEvent<int>(12000, 12150, 50), new LineEvent<int>(12150, 12300, 70) };
        var surface = new[] { new LineEvent<string>(12000, 12100, "asphalt"), new LineEvent<string>(12100, 12300, "gravel") };

        // Where do both hold? Pure interval arithmetic on the measures, no geometry yet.
        var both = LineEvents.Intersect(speed, surface, (s, b) => $"{s} km/h, {b}");

        foreach (var piece in both)
        {
            // Only now the stretch of road: its shape, in the direction of the route.
            IPolyline2D shape = route.LocateBetween(piece.From, piece.To);

            Console.WriteLine($"{piece.Value}: {shape.Length()} m");
            // 50 km/h, asphalt: 50 m / 50 km/h, gravel: 25 m / 70 km/h, gravel: 75 m
        }

        // The way back: which part of the route does a parcel along the road belong to?
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((20 0, 50 0, 50 10, 20 10, 20 0))");
        var (from, to) = route.LocateFeature(parcel, tolerance: 0).Single();
        Console.WriteLine($"{Math.Round(from)} to {Math.Round(to)}"); // 12040 to 12100
        // --- end

        Assert.Equal(
            [("50 km/h, asphalt", 50d), ("50 km/h, gravel", 25d), ("70 km/h, gravel", 75d)],
            both.Select(p => (p.Value, route.LocateBetween(p.From, p.To).Length())));
        Assert.Equal((12040d, 12100d), (Math.Round(from), Math.Round(to)));
    }

    [Fact]
    public void ServiceAreas()
    {
        // --- snippet: cookbook-service-areas
        var hydrants = Wkt.Read<IMultiPoint2D>("MULTIPOINT ((10 20), (35 80), (60 30), (85 70), (50 55))");
        var district = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 100 0, 100 45, 60 45, 60 100, 0 100, 0 0))");

        // One cell per hydrant: where it is the nearest one, cut to the district.
        var cells = hydrants.VoronoiDiagram(district);

        // Which hydrant serves a point? The one whose cell contains it.
        var served = cells.First(c => c.Cell.Locate(new Coordinate(20, 60)) != Location.Outside);
        Console.WriteLine($"{served.Site.X} {served.Site.Y}");   // 35 80

        // How much of the district does each hydrant serve?
        var areas = cells.Select(c => Math.Round(c.Cell.Area())).ToArray();
        // --- end

        Assert.Equal(new Coordinate(35, 80), served.Site);
        Assert.Equal(district.Area(), areas.Sum(), 1);
        Assert.Equal(5, areas.Length);
    }

    [Fact]
    public void HeightAndContours()
    {
        // --- snippet: cookbook-terrain
        var points = Wkt.Read("MULTIPOINT Z ((0 0 10), (100 0 20), (100 100 40), (0 100 30), (50 50 25))");
        var terrain = Triangulation.FromPoints(points);

        Console.WriteLine(terrain.InterpolateZ(new Coordinate(25, 10)));            // 14.5
        foreach (double level in new[] { 20.0, 30.0 })
        {
            Console.WriteLine($"{level}: {terrain.Contour(level).Length():0.0} m");   // 20: 111.8 m, 30: 111.8 m
        }
        // --- end

        Assert.Equal(14.5, terrain.InterpolateZ(new Coordinate(25, 10)), 9);
        Assert.Equal(111.8, terrain.Contour(20).Length(), 1);
        Assert.Equal(111.8, terrain.Contour(30).Length(), 1);
    }

    [Fact]
    public void SmoothContourLines()
    {
        // --- snippet: cookbook-smooth-contours
        var points = Wkt.Read("MULTIPOINT Z ((0 0 10), (100 0 20), (100 100 40), (0 100 30), (50 50 25))");
        var terrain = Triangulation.FromPoints(points);

        // Smooth the surface first: a grid of 10 by natural neighbour. Contour lines of different heights of it cannot cross.
        var smooth = terrain.Resample(10);

        // Then round the last corners of the lines (Chaikin, two iterations); the height stays as Z.
        IPolyline2D line = smooth.Contour(30).Smooth(2);
        IPolyline2D plain = terrain.Contour(30);

        Console.WriteLine($"{plain[0].PointCount} -> {line[0].PointCount} vertices");
        // --- end

        Assert.True(line[0].PointCount > plain[0].PointCount);
        Assert.True(line.Length() > 0);
        Assert.False(smooth.Contour(20).Smooth(2).Intersects(line));
        Assert.All(Enumerable.Range(0, line[0].PointCount), i => Assert.Equal(30, ((IPoint3D)line[0][i]).Z, 6));
    }

    [Fact]
    public void HeightProfileAlongARoad()
    {
        // --- snippet: cookbook-profile
        var terrain = Triangulation.FromPoints(Wkt.Read(
            "MULTIPOINT Z ((0 0 0), (100 0 10), (100 100 30), (0 100 20), (50 50 15), (50 0 5), (50 100 25))"));
        var road = Wkt.Read<IPolyline2D>("LINESTRING (0 50, 100 50)");

        // The road over the terrain: Z is the height, M the distance along the road.
        var draped = terrain.Drape(road);

        var profile = Enumerable.Range(0, draped[0].PointCount)
            .Select(i => (Distance: ((IPointM)draped[0][i]).M, Height: ((IPoint3D)draped[0][i]).Z))
            .ToArray();
        foreach (var (distance, height) in profile)
        {
            Console.WriteLine($"{distance:0} m: {height:0.0} m");   // 0 m: 10.0 m, ..., 100 m: 20.0 m
        }
        // --- end

        Assert.Equal((0d, 10d), (profile[0].Distance, Math.Round(profile[0].Height, 6)));
        Assert.Equal((100d, 20d), (profile[^1].Distance, Math.Round(profile[^1].Height, 6)));
        Assert.True(profile.Length >= 3);
    }

    [Fact]
    public void ForTheMap()
    {
        // --- snippet: cookbook-display
        // A wiggly shore with many vertices.
        var shore = Wkt.Read<IPolygon2D>(
            "POLYGON((0 0, 25 0.2, 50 0, 75 -0.2, 100 0, 100 40, 60 40, 60 100, 40 100, 40 40, 0 40, 0 0))");

        // Fewer vertices for a small scale, without crossings.
        IPolygon2D light = shore.Simplify(1, preserveTopology: true);

        // Only what is on screen.
        IGeometry2D visible = shore.Clip(new Envelope2D(-10, -10, 120, 50));

        // Where to put the name: inside, and as far from the edge as possible.
        Coordinate? label = shore.Polylabel();

        Console.WriteLine(light.AsList().Sum(r => r.PointCount) < shore.AsList().Sum(r => r.PointCount)); // True
        // --- end

        Assert.True(light.AsList().Sum(r => r.PointCount) < shore.AsList().Sum(r => r.PointCount));
        Assert.True(light.IsValid());
        Assert.True(visible.Area() < shore.Area());
        Assert.Equal(Location.Inside, shore.Locate(label!.Value));
    }

    [Fact]
    public void Exchange()
    {
        // --- snippet: cookbook-exchange
        var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

        // Binary for a database column, JSON for a web client; reading takes the factory that creates your classes.
        byte[] wkb = parcel.ToWkb();
        string json = parcel.ToGeoJson();

        IPolygon2D fromDatabase = Wkb.Read<IPolygon2D>(wkb, ImpGeometryFactory.Instance);
        IPolygon2D fromClient = GeoJson.Read<IPolygon2D>(json, ImpGeometryFactory.Instance);

        Console.WriteLine(fromDatabase.EqualsTopo(fromClient)); // True
        // --- end

        Assert.True(fromDatabase.EqualsTopo(fromClient));
        Assert.True(fromDatabase.EqualsTopo(parcel));
    }
}
