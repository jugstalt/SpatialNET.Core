using CsCheck;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using SpatialNET.Internal.Noding;
using SpatialNET.Tests.Oracle;
using SpatialNET.Tests.Validation;
using static SpatialNET.Tests.Stress.StressSupport;
using NtsLocation = NetTopologySuite.Geometries.Location;

namespace SpatialNET.Tests.Stress;

/// <summary>
/// Stress tests (see <see cref="StressFactAttribute"/>): the algorithms on many random, dirty inputs, checked against invariants
/// and, where NTS has the same meaning, against NTS. A failing case prints a CsCheck seed; replay it with <c>CsCheck_Seed</c>.
/// </summary>
[Trait("Category", "Stress")]
public class AlgorithmStressTests
{
    /// <summary>The rings exactly as given as WKT (no normalization), so that a failing case can be pasted into a test.</summary>
    private static string RawWkt(IGeometry2D g) => g switch
    {
        IPolygon2D p => "POLYGON (" + string.Join(", ", Enumerable.Range(0, p.RingCount).Select(i => Sequence(Internal.Rings.Closed(p[i])))) + ")",
        IPolyline2D l => "MULTILINESTRING (" + string.Join(", ", Enumerable.Range(0, l.PathCount).Select(i => Sequence(Internal.Rings.Coordinates(l[i])))) + ")",
        _ => "MULTIPOINT (" + string.Join(", ", Internal.GeometryAccess.AllPoints(g).Select(q => $"({q.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} {q.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})")) + ")",
    };

    private static string Sequence(Coordinate[] c) =>
        "(" + string.Join(", ", c.Select(p => $"{p.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} {p.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}")) + ")";

    /// <summary>The rings exactly as given (no normalization), for the even-odd oracle.</summary>
    private static Geometry RawNts(IPolygon2D polygon)
    {
        var f = NetTopologySuite.Geometries.GeometryFactory.Default;
        var rings = Enumerable.Range(0, polygon.RingCount)
            .Select(i => f.CreateLinearRing(Internal.Rings.Closed(polygon[i]).Select(c => new NetTopologySuite.Geometries.Coordinate(c.X, c.Y)).ToArray()))
            .ToArray();
        return f.CreatePolygon(rings[0], rings.Skip(1).ToArray());
    }

    [StressFact]
    public void Noder_output_edges_never_cross_properly()
    {
        Gen.Select(Points(2, 6).Array[1, 8], Gen.OneOfConst(1.0, 0.1, 0.01)).Run(t =>
        {
            var (paths, gridSize) = t;
            var edges = SnapRoundingNoder.Node(paths.Select((p, i) => new SegmentString(p, 0, i)).ToList(), new GridModel(gridSize))
                .Select(e => (e.A, e.B)).Distinct().ToArray();
            for (int i = 0; i < edges.Length; i++)
            {
                for (int j = i + 1; j < edges.Length; j++)
                {
                    var (a, b) = edges[i];
                    var (c, d) = edges[j];
                    bool proper = GridPoint.Orient(a, b, c) * GridPoint.Orient(a, b, d) < 0
                                  && GridPoint.Orient(c, d, a) * GridPoint.Orient(c, d, b) < 0;
                    Assert.False(proper, $"Edges {a}-{b} and {c}-{d} cross (grid {gridSize}).");
                }
            }
        });
    }

    [StressFact]
    public void Overlays_are_valid_and_satisfy_the_area_identities()
    {
        Gen.Select(ValidPolygon, ValidPolygon).Run(t =>
        {
            var (a, b) = t;
            double tol = AreaTolerance(a, b);

            var intersection = a.Intersection(b, factory: F, gridSize: Grid);
            var union = a.Union(b, factory: F, gridSize: Grid);
            var difference = a.Difference(b, factory: F, gridSize: Grid);
            var symDifference = a.SymDifference(b, factory: F, gridSize: Grid);

            Assert.All(new[] { intersection, union, difference, symDifference }, r => Assert.True(r.IsValid(), "an overlay result must be valid"));
            Assert.InRange(Math.Abs(union.Area() + intersection.Area() - a.Area() - b.Area()), 0, tol);   // |A∪B| + |A∩B| = |A| + |B|
            Assert.InRange(Math.Abs(difference.Area() + intersection.Area() - a.Area()), 0, tol);         // |A\B| + |A∩B| = |A|
            Assert.InRange(Math.Abs(symDifference.Area() - (union.Area() - intersection.Area())), 0, tol);
            Assert.InRange(Math.Abs(b.Intersection(a, factory: F, gridSize: Grid).Area() - intersection.Area()), 0, tol); // commutative
            Assert.InRange(intersection.Area() - a.Intersection(intersection, factory: F, gridSize: Grid).Area(), -tol, tol); // A∩B ⊆ A
        });
    }

    [StressFact]
    public void MakeValid_gives_a_valid_polygon_with_the_even_odd_area()
    {
        DirtyPolygon.Run(polygon =>
        {
            var valid = polygon.MakeValid(factory: F, gridSize: Grid);

            Assert.True(valid.IsValid(), RawWkt(polygon));
            // Idempotent only up to the grid: snap rounding again can bend an edge through the hot pixel of a nearby vertex.
            Assert.True(Math.Abs(valid.MakeValid(factory: F, gridSize: Grid).Area() - valid.Area()) <= AreaTolerance(valid), RawWkt(valid));
        });
    }

    [StressFact]
    public void MakeValid_area_equals_the_independent_even_odd_area()
    {
        // sane values only: NTS polygonizes slivers of 1e-15 width wrongly, so it is no oracle for them
        SaneDirtyPolygon.Run(polygon =>
        {
            var valid = polygon.MakeValid(factory: F, gridSize: Grid);

            Assert.True(Math.Abs(valid.Area() - EvenOdd.Area(RawNts(polygon))) <= AreaTolerance(polygon),
                $"MakeValid area {valid.Area():R}, even-odd area {EvenOdd.Area(RawNts(polygon)):R}, tolerance {AreaTolerance(polygon):R}: {RawWkt(polygon)}");
        });
    }

    [StressFact]
    public void Relate_agrees_with_NTS_on_valid_geometries()
    {
        Gen.Select(SaneValidPolygon, SaneValidPolygon, SaneLine, SanePoints(1, 3), Gen.Int[0, 5]).Run(t =>
        {
            var (p1, p2, line, points, kind) = t;
            IGeometry2D a = kind % 2 == 0 ? p1 : line;
            IGeometry2D b = kind switch { 0 or 1 => p2, 2 or 3 => line, _ => F.CreateMultiPoint(points, null) };

            string expected;
            try
            {
                expected = Nts.ToNts(a).Relate(Nts.ToNts(b)).ToString();
            }
            catch (Exception)
            {
                return; // NTS itself gives up on this input (its topology exceptions), so it is no oracle here
            }

            Assert.True(expected == a.Relate(b).ToString(), $"Relate differs from NTS: {expected} versus {a.Relate(b)}. A: {RawWkt(a)} B: {RawWkt(b)}");
        });
    }

    [StressFact]
    public void Locate_agrees_with_the_NTS_PointLocator()
    {
        Gen.Select(SaneValidPolygon, SanePoint, Gen.Int[0, 2]).Run(t =>
        {
            var (polygon, probe, mode) = t;
            var vertices = Internal.GeometryAccess.AllPoints(polygon).Select(p => Internal.GeometryAccess.ToCoordinate(p)).ToArray();
            var at = mode == 0 || vertices.Length == 0 ? probe : vertices[(int)(Math.Abs(probe.X * 1000) % vertices.Length)];
            if (mode == 2 && vertices.Length > 1)
            {
                at = new Coordinate((vertices[0].X + vertices[1].X) / 2, (vertices[0].Y + vertices[1].Y) / 2); // a segment midpoint
            }

            var expected = new PointLocator().Locate(new NetTopologySuite.Geometries.Coordinate(at.X, at.Y), Nts.ToNts(polygon));
            var actual = polygon.Locate(at);

            var mapped = expected switch { NtsLocation.Interior => Location.Inside, NtsLocation.Boundary => Location.Boundary, _ => Location.Outside };
            Assert.True(mapped == actual, $"Locate: NTS says {mapped}, SpatialNET {actual} at ({at.X:R} {at.Y:R}) in {RawWkt(polygon)}");
        });
    }

    [StressFact]
    public void Locate_is_exact_the_prepared_and_the_unprepared_geometry_agree()
    {
        // NTS is no oracle for offsets of 1e-80; the prepared geometry decides with the exact orientation test, the unprepared one must too
        Gen.Select(DirtyPolygon, RandomPoint, Gen.Int[0, 2]).Run(t =>
        {
            var (polygon, probe, mode) = t;
            var vertices = Internal.GeometryAccess.AllPoints(polygon).Select(p => Internal.GeometryAccess.ToCoordinate(p)).ToArray();
            var at = mode == 0 ? probe : new Coordinate(vertices[(int)(Math.Abs(probe.X * 1000) % vertices.Length)].X, probe.Y); // same x as a vertex

            Assert.True(polygon.Locate(at) == polygon.Prepare().Locate(at), $"Locate at ({at.X:R} {at.Y:R}) in {RawWkt(polygon)}");
        });
    }

    [StressFact]
    public void Relate_is_exact_transposed_and_consistent_with_the_predicates()
    {
        Gen.Select(DirtyPolygon.Select(p => p.MakeValid(factory: F, gridSize: Grid)), DirtyPolygon.Select(p => p.MakeValid(factory: F, gridSize: Grid)), Line, Gen.Int[0, 3]).Run(t =>
        {
            var (p1, p2, line, kind) = t;
            IGeometry2D a = kind % 2 == 0 ? p1 : line;
            IGeometry2D b = kind < 2 ? p2 : p1;

            string ab = a.Relate(b).ToString(), ba = b.Relate(a).ToString();
            string transposed = new string(new[] { ab[0], ab[3], ab[6], ab[1], ab[4], ab[7], ab[2], ab[5], ab[8] });

            Assert.True(ba == transposed, $"Relate is not symmetric: {ab} and {ba}. A: {RawWkt(a)} B: {RawWkt(b)}");
            string where = $" ab={ab} A: {RawWkt(a)} B: {RawWkt(b)}";
            Assert.True(a.Intersects(b) == !a.Disjoint(b), "Intersects/Disjoint" + where);
            Assert.True(a.Contains(b) == b.Within(a), "Contains/Within" + where);
            Assert.True(a.Intersects(b) == (ab[0] != 'F' || ab[1] != 'F' || ab[3] != 'F' || ab[4] != 'F'), "Intersects/matrix" + where);
        });
    }

    [StressFact]
    public void Clip_equals_Intersection_with_the_rectangle()
    {
        Gen.Select(ValidPolygon, RandomPoint, RandomPoint).Run(t =>
        {
            var (polygon, c1, c2) = t;
            var rect = new Envelope2D(Math.Min(c1.X, c2.X), Math.Min(c1.Y, c2.Y), Math.Max(c1.X, c2.X), Math.Max(c1.Y, c2.Y));

            var clipped = polygon.Clip(rect, F, Grid);
            var expected = polygon.Intersection(rect.ToPolygon(F), factory: F, gridSize: Grid);

            Assert.True(clipped.IsValid());
            Assert.InRange(Math.Abs(clipped.Area() - expected.Area()), 0, AreaTolerance(polygon, rect.ToPolygon(F)));
        });
    }

    [StressFact]
    public void UnionAll_equals_the_fold_of_Union()
    {
        ValidPolygon.Array[2, 14].Run(polygons =>
        {
            var cascade = polygons.UnionAll(factory: F, gridSize: Grid);
            var fold = polygons.Aggregate((acc, p) => acc.Union(p, factory: F, gridSize: Grid));

            Assert.True(cascade.IsValid());
            Assert.InRange(Math.Abs(cascade.Area() - fold.Area()), 0, AreaTolerance(polygons) * polygons.Length);
        });
    }

    [StressFact]
    public void Constructions_keep_their_guarantees()
    {
        Gen.Select(ValidPolygon, Gen.Double[0.01, 5], Gen.Double[0.01, 3], Line).Run(t =>
        {
            var (polygon, distance, tolerance, line) = t;

            var buffered = polygon.Buffer(distance, factory: F, gridSize: Grid);
            var simplified = polygon.Simplify(tolerance, preserveTopology: true, factory: F, gridSize: Grid);
            var pieces = polygon.Cut(line, F, Grid);

            Assert.True(buffered.IsValid());
            Assert.True(simplified.IsValid());
            Assert.True(buffered.Area() + AreaTolerance(polygon) >= polygon.Area(), "a positive buffer never shrinks the polygon");
            Assert.All(pieces, piece => Assert.True(piece.IsValid()));
            Assert.InRange(Math.Abs(pieces.Sum(p => p.Area()) - polygon.Area()), 0, AreaTolerance(polygon, line) * (1 + pieces.Count));
        });
    }
}
