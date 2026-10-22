using CsCheck;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;
using SpatialNET.Imp;
using SpatialNET.Internal.Noding;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace SpatialNET.Tests.Construction;

/// <summary>Canonical forms of lines, so that sets of lines can be compared whatever their direction or the start of a ring (ADR 0023).</summary>
internal static class LineForms
{
    public static string Canonical(IReadOnlyList<(double X, double Y)> points)
    {
        var list = points.ToList();
        bool closed = list.Count >= 3 && list[0] == list[^1];
        if (closed)
        {
            list.RemoveAt(list.Count - 1);
            int start = list.IndexOf(list.Min());
            list = [.. list.Skip(start), .. list.Take(start)];
            var reversed = new[] { list[0] }.Concat(Enumerable.Reverse(list).Take(list.Count - 1)).ToList();
            if (Compare(reversed, list) < 0) list = reversed;
            list.Add(list[0]);
        }
        else
        {
            var reversed = Enumerable.Reverse(list).ToList();
            if (Compare(reversed, list) < 0) list = reversed;
        }

        return string.Join(" ", list.Select(p => $"{p.X:R},{p.Y:R}"));
    }

    private static int Compare(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }

        return a.Count.CompareTo(b.Count);
    }

    public static string[] Of(IPolyline2D line) => Enumerable.Range(0, line.PathCount)
        .Select(i => Canonical(Internal.Rings.Coordinates(line[i]).Select(c => (c.X, c.Y)).ToList())).Order(StringComparer.Ordinal).ToArray();

    public static string[] Of(IEnumerable<LineString> lines) => lines
        .Select(l => Canonical(l.Coordinates.Select(c => (c.X, c.Y)).ToList())).Order(StringComparer.Ordinal).ToArray();
}

/// <summary><c>LineMerge</c> against the NTS <c>LineMerger</c> (ADR 0023/0008); the directed fixture has a hand-derived expected value.</summary>
public class LineMergeFixtureTests
{
    public static TheoryData<string, string> LineMergeFixtures => FixtureCatalog.Theory("lineMerge");

    [Theory]
    [MemberData(nameof(LineMergeFixtures))]
    public void LineMerge(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var line = (IPolyline2D)fixture.ReadA(factory);
        bool directed = fixture.Params.TryGetValue("directed", out var d) && d.GetBoolean();

        var actual = line.LineMerge(directed, factory);

        string[] expected;
        if (fixture.Expected is not null)
        {
            expected = LineForms.Of((IPolyline2D)Wkt.Read(fixture.Expected, ImpGeometryFactory.Instance));
        }
        else
        {
            var merger = new LineMerger();
            merger.Add(Nts.Read(fixture.A));
            expected = LineForms.Of(merger.GetMergedLineStrings().Cast<LineString>());
        }

        Assert.Equal(expected, LineForms.Of(actual));
    }
}

/// <summary>
/// <c>Polygonize</c> against the NTS <c>Polygonizer</c> on the same lines, noded by NTS snap rounding on the same grid (ADR 0023/0008):
/// the same number of faces, and every face matches an NTS face up to the grid.
/// </summary>
public class PolygonizeFixtureTests
{
    public static TheoryData<string, string> PolygonizeFixtures => FixtureCatalog.Theory("polygonize");

    internal static IReadOnlyList<NtsPolygon> NtsFaces(Geometry lines, double gridSize)
    {
        if (lines.IsEmpty) return [];
        var noded = UnaryUnionNG.Union(lines, new PrecisionModel(1 / gridSize));
        var polygonizer = new Polygonizer();
        polygonizer.Add(noded);
        return polygonizer.GetPolygons().Cast<NtsPolygon>().ToList();
    }

    internal static void AssertSameFaces(IReadOnlyList<IPolygon2D> actual, IReadOnlyList<NtsPolygon> expected, double gridSize)
    {
        Assert.Equal(expected.Count, actual.Count);
        var remaining = expected.ToList();
        foreach (var face in actual)
        {
            var ours = Nts.ToNts(face);
            var best = remaining.OrderBy(e => e.SymmetricDifference(ours).Area).First();
            Assert.True(best.SymmetricDifference(ours).Area <= 4 * gridSize * (1 + ours.Length) + 1e-9,
                $"No NTS face matches {ours.AsText()}; nearest {best.AsText()}.");
            remaining.Remove(best);
        }
    }

    [Theory]
    [MemberData(nameof(PolygonizeFixtures))]
    public void Polygonize(string id, string implementation)
    {
        var fixture = FixtureCatalog.Get(id);
        var factory = Nts.Factory(implementation);
        var line = (IPolyline2D)fixture.ReadA(factory);
        var nts = Nts.Read(fixture.A);
        double max = nts.IsEmpty ? 0 : new[] { nts.EnvelopeInternal.MinX, nts.EnvelopeInternal.MaxX, nts.EnvelopeInternal.MinY, nts.EnvelopeInternal.MaxY }.Max(Math.Abs);
        double gridSize = GridModel.AutoSize(max);

        var actual = line.Polygonize(factory, gridSize);

        Assert.All(actual, face => Assert.True(face.IsValid(), "every face must be a valid polygon"));
        AssertSameFaces(actual, NtsFaces(nts, gridSize), gridSize);
    }
}

public class LineMergeTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IPolyline2D L(string wkt) => (IPolyline2D)Wkt.Read(wkt, F);

    [Fact]
    public void Paths_are_joined_at_nodes_of_two_ends_and_reversed_where_needed()
    {
        Assert.Equal("LINESTRING (0 0, 1 1, 2 0, 3 1)", L("MULTILINESTRING ((0 0, 1 1), (1 1, 2 0), (2 0, 3 1))").LineMerge(factory: F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 1 1, 2 0)", L("MULTILINESTRING ((0 0, 1 1), (2 0, 1 1))").LineMerge(factory: F).ToWkt());   // the direction of the first path prevails
        Assert.Equal("LINESTRING (2 0, 1 1, 0 0)", L("MULTILINESTRING ((2 0, 1 1), (0 0, 1 1))").LineMerge(factory: F).ToWkt());
        Assert.Equal("LINESTRING (1 1, 2 0)", L("MULTILINESTRING ((1 1, 2 0))").LineMerge(factory: F).ToWkt());
    }

    [Fact]
    public void A_node_of_three_or_more_ends_keeps_the_paths_apart()
    {
        var merged = L("MULTILINESTRING ((0 0, 1 1), (2 0, 1 1), (1 2, 1 1))").LineMerge(factory: F);

        Assert.Equal(3, merged.PathCount);
    }

    [Fact]
    public void A_cycle_becomes_one_closed_path()
    {
        var merged = L("MULTILINESTRING ((0 0, 1 0), (1 0, 1 1), (1 1, 0 0))").LineMerge(factory: F);

        Assert.Equal("LINESTRING (0 0, 1 0, 1 1, 0 0)", merged.ToWkt());
    }

    [Fact]
    public void A_closed_path_stays_alone_unless_another_path_ends_at_its_start()
    {
        Assert.Equal("LINESTRING (0 0, 1 0, 1 1, 0 0)", L("LINESTRING (0 0, 1 0, 1 1, 0 0)").LineMerge(factory: F).ToWkt());
        Assert.Equal(2, L("MULTILINESTRING ((0 0, 1 0, 1 1, 0 0), (0 0, -1 -1))").LineMerge(factory: F).PathCount);   // three ends meet at (0 0)
    }

    [Fact]
    public void Directed_merging_joins_only_end_to_start()
    {
        var line = L("MULTILINESTRING ((0 0, 1 1), (1 1, 2 0), (3 3, 2 0))");

        Assert.Equal("MULTILINESTRING ((0 0, 1 1, 2 0), (3 3, 2 0))", line.LineMerge(directed: true, factory: F).ToWkt());
        Assert.Equal("LINESTRING (0 0, 1 1, 2 0, 3 3)", line.LineMerge(directed: false, factory: F).ToWkt());
    }

    [Fact]
    public void It_does_not_node_and_does_not_snap()
    {
        Assert.Equal(2, L("MULTILINESTRING ((0 0, 10 10), (0 10, 10 0))").LineMerge(factory: F).PathCount);                 // crossing, left alone
        Assert.Equal(2, L("MULTILINESTRING ((0 0, 1 1), (1.000000000001 1, 2 0))").LineMerge(factory: F).PathCount);        // nearly meeting: not joined
    }

    [Fact]
    public void Degenerate_paths_are_dropped_and_empty_input_gives_an_empty_polyline()
    {
        var paths = F.CreatePolyline([new Coordinate[] { new(0, 0), new(0, 0) }, new Coordinate[] { new(0, 0), new(1, 1) }], null);

        Assert.Equal("LINESTRING (0 0, 1 1)", paths.LineMerge(factory: F).ToWkt());
        Assert.Equal(0, L("MULTILINESTRING EMPTY").LineMerge(factory: F).PathCount);
    }

    [Fact]
    public void Z_and_M_are_kept_and_a_joined_vertex_keeps_the_values_of_the_earlier_path()
    {
        var zm = new MFactory(supportsZ: true, supportsM: true);
        var line = new MPolyline([new[] { new Coordinate(0, 0, 1, 10), new Coordinate(1, 1, 2, 20) }, new[] { new Coordinate(1, 1, 3, 30), new Coordinate(2, 0, 4, 40) }]);

        var merged = MGeo.Coordinates(line.LineMerge(factory: zm));

        Assert.Equal(new[] { new Coordinate(0, 0, 1, 10), new Coordinate(1, 1, 2, 20), new Coordinate(2, 0, 4, 40) }, merged);
        Assert.All(MGeo.Coordinates(line.LineMerge(factory: new MFactory(false, false))), c => Assert.False(c.HasZ || c.HasM));
    }

    [Fact]
    public void Null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).LineMerge(factory: F));
    }
}

public class PolygonizeTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static IPolyline2D L(string wkt) => (IPolyline2D)Wkt.Read(wkt, F);

    [Fact]
    public void A_ring_gives_one_face_and_lines_that_enclose_nothing_give_none()
    {
        var faces = L("LINESTRING (0 0, 10 0, 10 10, 0 10, 0 0)").Polygonize(F);

        Assert.Equal(100, Assert.Single(faces).Area());
        Assert.Empty(L("LINESTRING (0 0, 10 0, 10 10)").Polygonize(F));
        Assert.Empty(L("MULTILINESTRING EMPTY").Polygonize(F));
    }

    [Fact]
    public void Crossing_lines_are_noded_and_every_face_is_returned_separately()
    {
        var faces = L("MULTILINESTRING ((0 0, 10 0, 10 10, 0 10, 0 0), (0 0, 10 10), (0 10, 10 0))").Polygonize(F);

        Assert.Equal(4, faces.Count);
        Assert.All(faces, f => Assert.Equal(25, f.Area(), 6));
    }

    [Fact]
    public void A_ring_in_a_ring_gives_a_face_with_a_hole_and_an_island()
    {
        var faces = L("MULTILINESTRING ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 15 5, 15 15, 5 15, 5 5))").Polygonize(F);

        Assert.Equal(2, faces.Count);
        Assert.Equal(new[] { 100.0, 300.0 }, faces.Select(f => f.Area()).Order().ToArray());
        Assert.Contains(faces, f => f.RingCount == 2); // the annulus has a hole
    }

    [Fact]
    public void Dangles_and_bridges_enclose_nothing_and_do_not_cut_a_face()
    {
        var dangles = L("MULTILINESTRING ((0 0, 10 0, 10 10, 0 10, 0 0), (10 5, 15 5), (0 5, 3 5))").Polygonize(F);
        var bridge = L("MULTILINESTRING ((0 0, 20 0, 20 20, 0 20, 0 0), (5 5, 15 5, 15 15, 5 15, 5 5), (0 0, 5 5))").Polygonize(F);

        Assert.Equal(100, Assert.Single(dangles).Area(), 6);
        Assert.Equal(new[] { 100.0, 300.0 }, bridge.Select(f => f.Area()).Order().ToArray());
        Assert.All(dangles.Concat(bridge), f => Assert.True(f.IsValid()));
    }

    [Fact]
    public void A_ring_that_touches_itself_gives_a_polygon_per_lobe()
    {
        var faces = L("LINESTRING (0 0, 10 0, 5 5, 10 10, 0 10, 5 5, 0 0)").Polygonize(F);

        Assert.Equal(2, faces.Count);
        Assert.All(faces, f => Assert.True(f.IsValid()));
        Assert.Equal(50, faces.Sum(f => f.Area()), 6); // two triangles of 25: the lobes of the figure eight
    }

    [Fact]
    public void The_result_is_deterministic_and_validates_the_grid_size()
    {
        var line = L("MULTILINESTRING ((0 0, 20 0, 20 20, 0 20, 0 0), (10 0, 10 20), (0 10, 20 10))");

        Assert.Equal(line.Polygonize(F).Select(f => f.ToWkt()), line.Polygonize(F).Select(f => f.ToWkt()));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Polygonize(F, gridSize: 0));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).Polygonize(F));
    }

    [Fact]
    public void Z_and_M_are_carried()
    {
        var zm = new MFactory(supportsZ: true, supportsM: true);
        var ring = new MPolyline([MGeo.Square(0, 0, 10, 10, MGeo.WithM)]);

        var face = Assert.Single(ring.Polygonize(zm));

        Assert.All(MGeo.Coordinates(face), c => Assert.Equal(MGeo.Linear(c.X, c.Y), c.M, 1e-6));
    }
}

/// <summary>Random line sets on a coarse integer grid (many crossings and cycles), against NTS and the invariants of ADR 0023.</summary>
public class LineMergePolygonizePropertyTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    private static readonly Gen<Coordinate[][]> Segments =
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 6], Gen.Int[0, 6], Gen.Int[0, 6])
            .Where(t => (t.Item1, t.Item2) != (t.Item3, t.Item4))
            .Select(t => new[] { new Coordinate(t.Item1, t.Item2), new Coordinate(t.Item3, t.Item4) })
            .Array[1, 14];

    private static IPolyline2D Line(Coordinate[][] paths) => F.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null);

    [Fact]
    public void LineMerge_agrees_with_the_NTS_LineMerger_and_is_idempotent()
    {
        Segments.Sample(paths =>
        {
            var line = Line(paths);
            var merged = line.LineMerge(factory: F);

            var merger = new LineMerger();
            merger.Add(Nts.ToNts(line));

            Assert.Equal(LineForms.Of(merger.GetMergedLineStrings().Cast<LineString>()), LineForms.Of(merged));
            Assert.Equal(LineForms.Of(merged), LineForms.Of(merged.LineMerge(factory: F)));
            Assert.Equal(line.Length(), merged.Length(), 1e-9);
        });
    }

    [Fact]
    public void Polygonize_agrees_with_the_NTS_Polygonizer_and_its_faces_are_valid_and_do_not_overlap()
    {
        const double Grid = 1e-6;
        Segments.Sample(paths =>
        {
            var line = Line(paths);

            var faces = line.Polygonize(F, Grid);

            Assert.All(faces, f => Assert.True(f.IsValid()));
            PolygonizeFixtureTests.AssertSameFaces(faces, PolygonizeFixtureTests.NtsFaces(Nts.ToNts(line), Grid), Grid);
            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = i + 1; j < faces.Count; j++)
                {
                    Assert.True(faces[i].Intersection(faces[j], factory: F, gridSize: Grid).Area() <= 1e-6, "faces must not overlap");
                }
            }
        });
    }
}
