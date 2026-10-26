using CsCheck;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.Construction;

/// <summary>Terrain analysis on a triangulation (ADR 0039): gradient, slope, aspect, surface area, volume and draping a line.</summary>
public class TerrainTests
{
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    private static IGeometry2D G(string wkt) => Wkt.Read(wkt, F);

    private static IPolyline2D Line(string wkt) => (IPolyline2D)G(wkt);

    private static readonly Gen<Coordinate> Pt = Gen.Select(Gen.Double[0, 100], Gen.Double[0, 100]).Select((x, y) => new Coordinate(x, y));

    /// <summary>Points on a plane z = ax + by + c, with the four corners of the square (0 0)-(100 100), so that the hull is the square.</summary>
    private static Triangulation Plane(Coordinate[] scattered, double a, double b, double c)
    {
        var all = scattered.Concat([new(0, 0), new(100, 0), new(100, 100), new(0, 100)])
            .Select(p => p with { Z = a * p.X + b * p.Y + c }).ToArray();
        return Triangulation.FromPoints(F.CreateMultiPoint(all, null));
    }

    private static Triangulation OfPlane(double a, double b, double c) =>
        Plane([new(30, 20), new(70, 60), new(20, 80)], a, b, c);

    // ---------- gradient, slope, aspect ----------

    [Fact]
    [Covers("Gradient")]
    public void The_gradient_of_a_plane_is_its_coefficients_in_every_triangle()
    {
        var t = OfPlane(0.5, -2, 7);

        for (int i = 0; i < t.TriangleCount; i++)
        {
            var (dx, dy) = t.Gradient(i);
            Assert.Equal(0.5, dx, 9);
            Assert.Equal(-2, dy, 9);
        }
    }

    [Fact]
    [Covers("Slope")]
    public void Slope_is_the_angle_of_the_plane_to_the_horizontal()
    {
        Assert.Equal(45, OfPlane(1, 0, 0).Slope(0), 9);
        Assert.Equal(45, OfPlane(0, -1, 0).Slope(0), 9);
        Assert.Equal(Math.Atan(0.5) * 180 / Math.PI, OfPlane(0.5, 0, 3).Slope(0), 9);
        Assert.Equal(Math.Atan(Math.Sqrt(2)) * 180 / Math.PI, OfPlane(1, 1, 0).Slope(0), 9);
        Assert.Equal(0, OfPlane(0, 0, 12).Slope(0), 9);
    }

    [Fact]
    [Covers("Aspect")]
    public void Aspect_is_the_compass_direction_of_steepest_descent()
    {
        Assert.Equal(270, OfPlane(1, 0, 0).Aspect(0), 9);     // rises to the east, so it faces west
        Assert.Equal(90, OfPlane(-1, 0, 0).Aspect(0), 9);     // faces east
        Assert.Equal(180, OfPlane(0, 1, 0).Aspect(0), 9);     // rises to the north: faces south
        Assert.Equal(0, OfPlane(0, -1, 0).Aspect(0), 9);      // faces north
        Assert.Equal(225, OfPlane(1, 1, 0).Aspect(0), 9);     // rises to the north east: faces south west
        Assert.True(double.IsNaN(OfPlane(0, 0, 5).Aspect(0)));   // flat: faces nowhere
    }

    [Fact]
    [Covers("Slope")]
    public void Triangles_without_Z_have_no_gradient_slope_aspect_or_area_and_arguments_are_checked()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10))"));

        Assert.True(double.IsNaN(t.Slope(0)));
        Assert.True(double.IsNaN(t.Aspect(0)));
        Assert.True(double.IsNaN(t.Gradient(0).Dx));
        Assert.True(double.IsNaN(t.SurfaceArea(0)));
        Assert.Equal(0, t.SurfaceArea());
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Slope(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Aspect(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Gradient(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.SurfaceArea(1));
    }

    // ---------- surface area ----------

    [Fact]
    [Covers("SurfaceArea")]
    public void The_surface_area_of_a_plane_is_the_map_area_over_the_cosine_of_the_slope()
    {
        Assert.Equal(100 * 100, OfPlane(0, 0, 3).SurfaceArea(), 6);
        Assert.Equal(100 * 100 * Math.Sqrt(2), OfPlane(1, 0, 0).SurfaceArea(), 4);               // 45 degrees
        Assert.Equal(100 * 100 * Math.Sqrt(1 + 0.25 + 4), OfPlane(0.5, -2, 9).SurfaceArea(), 3);   // sqrt(1 + a² + b²)
    }

    [Fact]
    [Covers("SurfaceArea")]
    public void Triangles_without_Z_are_not_counted_in_the_total()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (0 10 0), (10 10 0))"));
        double full = t.SurfaceArea();

        var partial = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 0), (0 10 0), (10 10 0), (20 20 0))")).SurfaceArea();

        Assert.Equal(100, full, 9);
        Assert.True(partial > full);
    }

    // ---------- volume ----------

    [Fact]
    [Covers("Volume")]
    public void The_volume_over_a_flat_surface_and_a_tilted_plane_is_exact()
    {
        var flat = OfPlane(0, 0, 3).Volume(0);
        Assert.Equal(30000, flat.Above, 6);
        Assert.Equal(0, flat.Below, 9);

        var below = OfPlane(0, 0, 3).Volume(10);
        Assert.Equal(0, below.Above, 9);
        Assert.Equal(70000, below.Below, 6);

        // z = x, cut at x = 40: above 100 * ∫(x - 40) from 40 to 100, below 100 * ∫(40 - x) from 0 to 40
        var tilted = OfPlane(1, 0, 0).Volume(40);
        Assert.Equal(100 * 60 * 60 / 2.0, tilted.Above, 4);
        Assert.Equal(100 * 40 * 40 / 2.0, tilted.Below, 4);
        Assert.Equal(100 * 100 * (50 - 40.0), tilted.Above - tilted.Below, 4);   // the net volume is the area times the mean height
    }

    [Fact]
    [Covers("Volume")]
    public void The_volume_of_a_random_terrain_agrees_with_numerical_integration()
    {
        Gen.Select(Pt.Array[20, 60], Gen.Double[-20, 120].Array[60], Gen.Double[0, 100]).Sample((scattered, heights, reference) =>
        {
            var all = scattered.Select((p, i) => p with { Z = heights[i % heights.Length] })
                .Concat([new Coordinate(0, 0, 40), new Coordinate(100, 0, 40), new Coordinate(100, 100, 40), new Coordinate(0, 100, 40)]).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(all, null));

            var (above, below) = t.Volume(reference);

            // midpoint rule on a fine grid over the square, which the hull covers completely
            const int n = 300;
            double step = 100.0 / n, sumAbove = 0, sumBelow = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double z = t.InterpolateZ(new Coordinate((i + 0.5) * step, (j + 0.5) * step)) - reference;
                    if (z > 0) sumAbove += z * step * step; else sumBelow -= z * step * step;
                }
            }

            Assert.Equal(sumAbove, above, 0.01 * (above + below) + 5);
            Assert.Equal(sumBelow, below, 0.01 * (above + below) + 5);
            Assert.True(above >= 0 && below >= 0);
        }, iter: 30);
    }

    [Fact]
    [Covers("Volume")]
    public void Volume_ignores_triangles_without_Z_and_checks_the_height()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10))"));

        Assert.Equal((0d, 0d), t.Volume(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Volume(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Volume(double.PositiveInfinity));
    }

    // ---------- draping ----------

    [Fact]
    [Covers("Drape")]
    public void A_draped_line_follows_the_plane_and_keeps_its_shape_and_length()
    {
        Gen.Select(Pt.Array[5, 40], Pt, Pt, Pt).Sample((scattered, p, q, r) =>
        {
            var t = Plane(scattered, 2, -1, 3);
            var line = F.CreatePolyline([new[] { p, q, r }], null);

            var draped = t.Drape(line, F);

            Assert.Equal(1, draped.PathCount);                          // everything is inside the square
            var path = Enumerable.Range(0, draped[0].PointCount).Select(i => (IPoint3D)draped[0][i]).ToArray();
            Assert.All(path, v => Assert.Equal(2 * v.X - v.Y + 3, v.Z, 1e-7));
            Assert.Equal(line.Length(), draped.Length(), 1e-7);          // the same line in the map
            Assert.Equal((p.X, p.Y), (path[0].X, path[0].Y));
            Assert.Equal((r.X, r.Y), (path[^1].X, path[^1].Y));
            Assert.True(path.Length >= 3);

            // M is the distance along the input line, rising to its length
            var ms = Enumerable.Range(0, draped[0].PointCount).Select(i => ((IPointM)draped[0][i]).M).ToArray();
            Assert.Equal(0, ms[0], 1e-9);
            Assert.Equal(line.Length(), ms[^1], 1e-7);
            for (int i = 1; i < ms.Length; i++)
            {
                Assert.True(ms[i] >= ms[i - 1]);
            }
        }, iter: 150);
    }

    [Fact]
    [Covers("Drape")]
    public void A_line_gets_a_vertex_at_every_edge_it_crosses_and_the_height_between_is_linear()
    {
        // the square 0..10 with its centre: four triangles around (5 5); not a plane, so the height must follow the triangles
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 40), (0 10 10), (5 5 20))"));

        var draped = t.Drape(Line("LINESTRING (0 5, 10 5)"), ZM);

        var v = Enumerable.Range(0, draped[0].PointCount).Select(i => (X: draped[0][i].X, Z: ((IPoint3D)draped[0][i]).Z)).ToArray();
        Assert.Equal(3, v.Length);                                     // the start on the edge (0 0)-(0 10), the vertex (5 5), the end on the edge (10 0)-(10 10)
        Assert.Equal((0d, 5d), (v[0].X, v[0].Z));                      // halfway between 0 and 10
        Assert.Equal((5d, 20d), (v[1].X, v[1].Z));
        Assert.Equal((10d, 25d), (v[2].X, v[2].Z));                    // halfway between 10 and 40
    }

    [Fact]
    [Covers("Drape")]
    public void A_line_that_runs_almost_along_an_edge_keeps_the_height_of_the_plane()
    {
        // found by a stress test: the position of the crossing on the edge came from the cross product of two almost parallel directions
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 7), (10 0 27), (10 10 57), (0 10 37))"));

        var draped = t.Drape(Line("LINESTRING (1 1.000000000000002, 9 8.999999999999998)"), ZM);

        Assert.Equal(1, draped.PathCount);
        for (int i = 0; i < draped[0].PointCount; i++)
        {
            var v = (IPoint3D)draped[0][i];
            Assert.Equal(2 * v.X + 3 * v.Y + 7, v.Z, 1e-9);
        }
    }

    [Fact]
    [Covers("Drape")]
    public void Parts_outside_the_surface_are_cut_off_and_the_rest_splits_into_paths()
    {
        var t = Plane([new(30, 20), new(70, 60)], 1, 0, 0);

        var across = t.Drape(Line("LINESTRING (-50 50, 150 50)"), ZM);
        Assert.Equal(1, across.PathCount);
        Assert.Equal(0, across[0][0].X, 1e-6);
        Assert.Equal(100, across[0][across[0].PointCount - 1].X, 1e-6);
        Assert.Equal(50, ((IPointM)across[0][0]).M, 1e-6);             // 50 along the input line

        var twice = t.Drape(Line("LINESTRING (20 50, 120 50, 120 60, 80 60, 80 70, 20 70)"), ZM);
        Assert.Equal(2, twice.PathCount);                              // the first stretch ends at the border; the line comes back in at (100 60) and stays

        Assert.Equal(0, t.Drape(Line("LINESTRING (200 200, 300 300)"), ZM).PathCount);
        Assert.Equal(0, t.Drape(Line("LINESTRING EMPTY"), ZM).PathCount);
    }

    [Fact]
    [Covers("Drape")]
    public void A_line_along_an_edge_or_through_vertices_is_handled()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT Z ((0 0 0), (10 0 10), (10 10 20), (0 10 10))"));

        var onHullEdge = t.Drape(Line("LINESTRING (0 0, 10 0)"), ZM);
        Assert.Equal(1, onHullEdge.PathCount);
        Assert.Equal(10, ((IPoint3D)onHullEdge[0][onHullEdge[0].PointCount - 1]).Z, 1e-9);

        var through = t.Drape(Line("LINESTRING (0 0, 10 10)"), ZM);
        Assert.Equal(1, through.PathCount);
        Assert.Equal(20, ((IPoint3D)through[0][through[0].PointCount - 1]).Z, 1e-9);
        Assert.Equal(Math.Sqrt(200), ((IPointM)through[0][through[0].PointCount - 1]).M, 1e-9);
    }

    [Fact]
    [Covers("Drape")]
    public void Draping_works_with_UTM_sized_coordinates_also_along_the_border()
    {
        var corners = new[] { new Coordinate(500000, 5300000, 100), new Coordinate(501000, 5300000, 110), new Coordinate(501000, 5301000, 130), new Coordinate(500000, 5301000, 120) };
        var t = Triangulation.FromPoints(F.CreateMultiPoint(corners.Append(new Coordinate(500400, 5300600, 112)).ToArray(), null));

        var along = t.Drape(F.CreatePolyline([new Coordinate[] { new(500000, 5300000), new(501000, 5300000) }], null), ZM);   // on the border of the surface
        var across = t.Drape(F.CreatePolyline([new Coordinate[] { new(499000, 5300500), new(502000, 5300500) }], null), ZM);

        Assert.Equal(1, along.PathCount);
        Assert.Equal(110, ((IPoint3D)along[0][along[0].PointCount - 1]).Z, 6);
        Assert.Equal(1, across.PathCount);
        Assert.Equal(500000, across[0][0].X, 3);
        Assert.Equal(501000, across[0][across[0].PointCount - 1].X, 3);
        Assert.Equal(1000, ((IPointM)across[0][0]).M, 3);              // 1000 along the input line to the border
    }

    [Fact]
    [Covers("Drape")]
    public void M_continues_over_the_paths_of_the_line_and_a_factory_without_M_drops_it()
    {
        var t = Plane([new(30, 20)], 1, 0, 0);
        var twoPaths = F.CreatePolyline([new Coordinate[] { new(10, 10), new(20, 10) }, new Coordinate[] { new(30, 30), new(40, 30) }], null);

        var withM = t.Drape(twoPaths, ZM);
        var withoutM = t.Drape(twoPaths, new MFactory(supportsZ: true, supportsM: false));
        var withoutZ = t.Drape(twoPaths, new MFactory(supportsZ: false, supportsM: true));

        Assert.Equal(2, withM.PathCount);
        Assert.Equal(10, ((IPointM)withM[1][0]).M, 9);                 // the second path starts where the first one ended (10 long)
        Assert.Equal(20, ((IPointM)withM[1][withM[1].PointCount - 1]).M, 9);   // the stretch has more vertices between: where it crosses edges
        // the points of the host factory always have both properties; "not supported" is NaN
        Assert.True(double.IsNaN(((IPointM)withoutM[0][0]).M));
        Assert.False(double.IsNaN(((IPoint3D)withoutM[0][0]).Z));
        Assert.True(double.IsNaN(((IPoint3D)withoutZ[0][0]).Z));
        Assert.False(double.IsNaN(((IPointM)withoutZ[0][0]).M));
    }

    [Fact]
    [Covers("Drape")]
    public void Triangles_without_Z_are_left_out_and_arguments_are_checked()
    {
        var t = Triangulation.FromPoints(G("MULTIPOINT ((0 0), (10 0), (0 10), (10 10))"));

        Assert.Equal(0, t.Drape(Line("LINESTRING (1 1, 9 9)"), ZM).PathCount);
        Assert.Equal(0, Triangulation.FromPoints(G("POINT EMPTY")).Drape(Line("LINESTRING (1 1, 9 9)"), ZM).PathCount);
        Assert.Throws<ArgumentNullException>(() => t.Drape(null!, ZM));
    }
}
