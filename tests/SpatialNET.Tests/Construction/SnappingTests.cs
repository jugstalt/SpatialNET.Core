using CsCheck;
using NetTopologySuite.Operation.Overlay.Snap;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;
using SpatialNET.Tests.Oracle;

namespace SpatialNET.Tests.Construction;

/// <summary>Snapping to a target: <c>SnapTo</c> for points, the ends or the vertices of lines, and polygons (ADR 0046).</summary>
[Covers("SnapTo")]
public class SnappingTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private const string Square = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))";

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static IPolyline2D Line(string wkt) => (IPolyline2D)G(wkt);

    private static IPolygon2D Polygon(string wkt) => (IPolygon2D)G(wkt);

    private static Coordinate[] Path(IPolyline2D line, int index = 0) => Internal.Rings.Coordinates(line[index]);

    private static (double, double) XY(Coordinate c) => (c.X, c.Y);

    // ---------- Points ----------

    [Fact]
    public void A_point_goes_to_a_vertex_before_a_nearer_edge()
    {
        var target = G(Square);

        Assert.Equal("POINT (10 0)", ((IPoint2D)G("POINT (10.3 0.4)")).SnapTo(target, 0.6, F).ToWkt());       // the corner is 0.5 away, the edge 0.3
        Assert.Equal("POINT (10 5)", ((IPoint2D)G("POINT (10.3 5)")).SnapTo(target, 0.6, F).ToWkt());         // no corner near: the edge
        Assert.Equal("POINT (10.3 5)", ((IPoint2D)G("POINT (10.3 5)")).SnapTo(target, 0.2, F).ToWkt());       // nothing within the tolerance
        Assert.Equal("POINT (5 5)", ((IPoint2D)G("POINT (5 5)")).SnapTo(target, 1, F).ToWkt());               // inside, but far from the rings
        Assert.Equal("MULTIPOINT ((0 0), (0 0), (20 20))",
            ((IMultiPoint2D)G("MULTIPOINT ((0.1 0), (0 0.1), (20 20))")).SnapTo(target, 0.5, F).ToWkt());     // all points stay
    }

    [Fact]
    public void A_point_snaps_to_points_and_lines_as_targets_and_keeps_Z_and_M()
    {
        Assert.Equal("POINT (3 4)", ((IPoint2D)G("POINT (3.1 4)")).SnapTo(G("MULTIPOINT ((3.5 4), (3 4))"), 0.5, F).ToWkt());   // the nearer one
        Assert.Equal("POINT (5 0)", ((IPoint2D)G("POINT (5 0.2)")).SnapTo(G("LINESTRING (0 0, 10 0)"), 0.5, F).ToWkt());

        var point = ((IPoint2D)Wkt.Read("POINT ZM (5 0.2 7 9)", ZM)).SnapTo(Wkt.Read("LINESTRING Z (0 0 1, 10 0 1)", ZM), 0.5, ZM);
        Assert.Equal((5d, 0d, 7d, 9d), (point.X, point.Y, ((IPoint3D)point).Z, ((IPointM)point).M));
    }

    // ---------- Ends of a line ----------

    [Fact]
    public void An_end_short_of_the_target_moves_onto_it_and_the_inner_vertices_stay()
    {
        var snapped = Line("LINESTRING (-2 5, 5 4.7, 9.98 5.01)").SnapTo(G(Square), 0.05, factory: F);

        var p = Path(snapped);
        Assert.Equal(3, p.Length);
        Assert.Equal((-2d, 5d), XY(p[0]));        // too far: stays
        Assert.Equal((5d, 4.7d), XY(p[1]));       // inner vertex: stays, although it is not near anything
        Assert.Equal(10, p[2].X);                 // the nearest point of the edge
        Assert.Equal(5.01, p[2].Y, 12);
    }

    [Fact]
    public void An_overshoot_is_cut_back_to_where_the_line_meets_the_target()
    {
        var snapped = Line("LINESTRING (5 5, 10.04 5)").SnapTo(G(Square), 0.05, factory: F);

        Assert.Equal("LINESTRING (5 5, 10 5)", snapped.ToWkt());

        // too far over: stays
        Assert.Equal("LINESTRING (5 5, 10.5 5)", Line("LINESTRING (5 5, 10.5 5)").SnapTo(G(Square), 0.05, factory: F).ToWkt());
        // over the target over several short segments, measured along the line
        Assert.Equal("LINESTRING (5 5, 10 5)", Line("LINESTRING (5 5, 10.01 5, 10.02 5.01, 10.03 5)").SnapTo(G(Square), 0.05, factory: F).ToWkt());
    }

    [Fact]
    public void The_way_of_the_ends_moves_extends_or_falls_back()
    {
        var target = G(Square);
        var line = Line("LINESTRING (5 3, 9.95 4.98)");   // runs diagonally (slope 0.4) towards the right edge, 0.05 short of it

        var nearest = Path(line.SnapTo(target, 0.1, SnapMode.Ends, SnapEnds.Nearest, F));
        Assert.Equal((10d, 4.98d), XY(nearest[^1]));

        var extended = Path(line.SnapTo(target, 0.1, SnapMode.Ends, SnapEnds.Extend, F));
        Assert.Equal(10, extended[^1].X, 12);
        Assert.Equal(5, extended[^1].Y, 12);          // in the direction of the last segment (slope 0.4)

        // the extension misses within the tolerance: Extend leaves it, ExtendOrNearest moves it
        var away = Line("LINESTRING (9.96 3, 9.96 4.96)");  // runs along the edge
        Assert.Equal((9.96d, 4.96d), XY(Path(away.SnapTo(target, 0.1, SnapMode.Ends, SnapEnds.Extend, F))[^1]));
        Assert.Equal((10d, 4.96d), XY(Path(away.SnapTo(target, 0.1, SnapMode.Ends, SnapEnds.ExtendOrNearest, F))[^1]));
    }

    [Fact]
    public void A_vertex_of_the_target_wins_at_an_end_and_both_ends_snap()
    {
        var snapped = Line("LINESTRING (0.02 -0.01, 5 5, 9.97 5)").SnapTo(G(Square), 0.05, factory: F);

        Assert.Equal("LINESTRING (0 0, 5 5, 10 5)", snapped.ToWkt());
    }

    [Fact]
    public void A_closed_path_and_a_polygon_have_no_loose_ends()
    {
        var ring = Line("LINESTRING (0.02 0.02, 5 0.02, 5 5, 0.02 0.02)");
        Assert.Equal(ring.ToWkt(), ring.SnapTo(G(Square), 0.05, factory: F).ToWkt());

        var polygon = Polygon("POLYGON ((0.02 0.02, 5 0.02, 5 5, 0.02 5, 0.02 0.02))");
        Assert.Equal(polygon.ToWkt(), polygon.SnapTo(G(Square), 0.05, SnapMode.Ends, F).ToWkt());
    }

    [Fact]
    public void Snapping_the_cutting_line_first_makes_Cut_cut()
    {
        // The case of the playground (ADR 0046): projected coordinates, the end of the cutting line 4 mm short of the edge.
        const double x0 = 512_300.25, y0 = 5_340_110.75;
        var polygon = F.CreatePolygon([new PolygonPart(new Coordinate[]
        {
            new(x0, y0), new(x0 + 500, y0), new(x0 + 500, y0 + 400), new(x0, y0 + 400), new(x0, y0),
        })], null);
        var cutter = F.CreatePolyline([new Coordinate[] { new(x0 + 120, y0 - 30), new(x0 + 380, y0 + 400 - 0.004) }], null);

        Assert.Single(polygon.Cut(cutter, F));                       // a loose end does not cut (ADR 0007)
        Assert.Equal(2, polygon.Cut(cutter.SnapTo(polygon, 0.01, factory: F), F).Count);
    }

    // ---------- Vertices ----------

    [Fact]
    public void Vertices_moves_every_near_vertex_and_inserts_the_vertices_of_the_target()
    {
        var snapped = Line("LINESTRING (-5 0.03, 4 -0.02, 15 0.01)").SnapTo(G("LINESTRING (0 0, 3 0, 7 0, 10 0)"), 0.05, SnapMode.Vertices, factory: F);

        Assert.Equal("LINESTRING (-5 0.03, 0 0, 3 0, 4 0, 7 0, 10 0, 15 0.01)", snapped.ToWkt());
    }

    [Fact]
    public void Neighbouring_areas_share_their_boundary_after_snapping()
    {
        var left = Polygon(Square);
        var right = Polygon("POLYGON ((10.02 0.01, 20 0, 20 10, 10.01 9.98, 10.03 5.02, 10.02 0.01))");
        Assert.False(right.Touches(left));

        var snapped = right.SnapTo(left, 0.05, factory: F);

        Assert.True(snapped.Touches(left));
        Assert.Equal(0, snapped.Intersection(left, factory: F).Area(), 12);
        Assert.Equal(1, left.Union(snapped, factory: F).RingCount);   // one part, no gap, no hole
    }

    [Fact]
    public void Inserted_vertices_interpolate_Z_and_M_and_moved_ones_keep_theirs()
    {
        var line = (IPolyline2D)Wkt.Read("LINESTRING ZM (0 0.01 0 0, 10 0 10 100)", ZM);
        var snapped = line.SnapTo(Wkt.Read("MULTIPOINT ((0 0), (4 0.02))", ZM), 0.05, SnapMode.Vertices, factory: ZM);

        var p = Path(snapped);
        Assert.Equal(3, p.Length);
        Assert.Equal((0d, 0d, 0d, 0d), (p[0].X, p[0].Y, p[0].Z, p[0].M));
        Assert.Equal((4d, 0.02d), XY(p[1]));
        Assert.Equal(4, p[1].Z, 9);
        Assert.Equal(40, p[1].M, 9);
    }

    [Fact]
    public void What_collapses_is_dropped()
    {
        var target = G("MULTIPOINT ((0 0), (10 0))");

        Assert.Equal("LINESTRING EMPTY", Line("LINESTRING (0.01 0, 0 0.01)").SnapTo(target, 0.05, SnapMode.Vertices, factory: F).ToWkt());
        Assert.Equal("POLYGON EMPTY", Polygon("POLYGON ((0 0.01, 10 0.01, 10 0.02, 0 0.01))").SnapTo(G("LINESTRING (0 0, 10 0)"), 0.05, factory: F).ToWkt());
    }

    [Fact]
    public void The_overload_for_any_geometry_takes_the_default_of_the_kind()
    {
        var target = G(Square);

        Assert.Equal("LINESTRING (5 5, 10 5)", G("LINESTRING (5 5, 9.98 5)").SnapTo(target, 0.05, factory: F).ToWkt());
        Assert.Equal("POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))",
            G("POLYGON ((10.01 0, 20 0, 20 10, 10.02 10, 10.01 0))").SnapTo(target, 0.05, factory: F).ToWkt());
        Assert.Equal("POINT (0 0)", G("POINT (0.01 0)").SnapTo(target, 0.05, factory: F).ToWkt());
        Assert.Equal("LINESTRING EMPTY", G("LINESTRING EMPTY").SnapTo(target, 0.05, factory: F).ToWkt());
        Assert.Equal("LINESTRING (5 5, 9.98 5)", G("LINESTRING (5 5, 9.98 5)").SnapTo(G("POLYGON EMPTY"), 0.05, factory: F).ToWkt());
    }

    [Fact]
    public void Errors()
    {
        var target = G(Square);
        var line = Line("LINESTRING (0 0, 1 1)");

        Assert.Throws<ArgumentNullException>(() => line.SnapTo(null!, 1));
        Assert.Throws<ArgumentNullException>(() => ((IPolyline2D)null!).SnapTo(target, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.SnapTo(target, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.SnapTo(target, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.SnapTo(target, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.SnapTo(target, 1, (SnapMode)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.SnapTo(target, 1, SnapMode.Ends, (SnapEnds)7));
    }

    // ---------- Against NTS (ADR 0008) ----------

    [Fact]
    public void Vertices_agrees_with_the_GeometrySnapper_of_NTS_where_both_do_the_same()
    {
        // NTS moves vertices only onto vertices of the target and inserts vertices of the target into segments; with every
        // vertex either near a vertex of the target or far from its edges, SnapTo does exactly that too.
        var source = Polygon(Square);
        var target = Polygon("POLYGON ((10.01 0.004, 20 0, 20 10, 10.008 9.997, 10.006 5.002, 10.01 0.004))");

        var expected = new GeometrySnapper(Nts.ToNts(source)).SnapTo(Nts.ToNts(target), 0.05);
        var actual = Nts.ToNts(source.SnapTo(target, 0.05, factory: F));
        expected.Normalize();
        actual.Normalize();

        Assert.True(expected.EqualsExact(actual, 1e-12), $"NTS {expected} – SpatialNET {actual}");
    }

    [Fact]
    public void Vertices_differs_from_NTS_by_snapping_onto_edges()
    {
        // A vertex 0.02 from an edge of the target and far from its corners: NTS leaves it, SnapTo puts it on the edge.
        var source = Line("LINESTRING (5 -3, 5 -0.02)");
        var target = Polygon(Square);

        var nts = new GeometrySnapper(Nts.ToNts(source)).SnapTo(Nts.ToNts(target), 0.05);
        Assert.Equal(-0.02, nts.Coordinates[^1].Y);
        Assert.Equal((5d, 0d), XY(Path(source.SnapTo(target, 0.05, SnapMode.Vertices, factory: F))[^1]));
    }

    // ---------- Properties ----------

    [Fact]
    public void Every_snapped_vertex_lies_within_the_tolerance_of_where_it_was_and_on_the_target()
    {
        var target = G("POLYGON ((0 0, 40 0, 40 30, 20 45, 0 30, 0 0), (10 10, 30 10, 30 20, 10 20, 10 10))");
        var targetLine = target.Boundary(F);
        Gen.Select(Gen.Double[-5, 45], Gen.Double[-5, 50], Gen.Double[0.01, 2]).Sample((x, y, tolerance) =>
        {
            var point = F.CreatePoint(new Coordinate(x, y), null);
            var snapped = point.SnapTo(target, tolerance, F);
            double moved = point.Distance(snapped);
            double before = point.Distance(targetLine);

            Assert.True(moved <= tolerance * (1 + 1e-12), $"moved {moved} > {tolerance}");
            if (before <= tolerance)
            {
                Assert.True(snapped.Distance(targetLine) <= 1e-12 * 50, "a point within the tolerance must end on the target");
            }
            else
            {
                Assert.Equal((x, y), (snapped.X, snapped.Y));
            }
        });
    }

    [Fact]
    public void A_cutting_line_that_ends_within_the_tolerance_of_the_edges_always_cuts_after_snapping()
    {
        // Both ends of a chord of a convex polygon moved by up to the tolerance, inwards or outwards.
        const double x0 = 480_000, y0 = 5_300_000;
        var polygon = F.CreatePolygon([new PolygonPart(new Coordinate[]
        {
            new(x0, y0), new(x0 + 900, y0 + 50), new(x0 + 1000, y0 + 700), new(x0 + 300, y0 + 950), new(x0 - 100, y0 + 500), new(x0, y0),
        })], null);
        var ring = Internal.Rings.Closed(polygon[0]);
        Gen.Select(Gen.Int[0, 4], Gen.Double[0.1, 0.9], Gen.Int[0, 4], Gen.Double[0.1, 0.9], Gen.Double[-0.009, 0.009], Gen.Double[-0.009, 0.009])
            .Where((e1, _, e2, _, _, _) => e1 != e2)
            .Sample((e1, t1, e2, t2, d1, d2) =>
            {
                Coordinate On(int edge, double t, double offset)
                {
                    Coordinate a = ring[edge], b = ring[edge + 1];
                    double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
                    return new Coordinate(a.X + t * dx - dy / length * offset, a.Y + t * dy + dx / length * offset); // + offset = inwards (CCW)
                }

                var cutter = F.CreatePolyline([new[] { On(e1, t1, d1), On(e2, t2, d2) }], null);
                var pieces = polygon.Cut(cutter.SnapTo(polygon, 0.01, factory: F), F);

                Assert.Equal(2, pieces.Count);
                Assert.True(Math.Abs(polygon.Area() - pieces.Sum(p => p.Area())) < 1e-3);
            });
    }
}
