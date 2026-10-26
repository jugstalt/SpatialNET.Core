using CsCheck;
using NetTopologySuite.Triangulate;
using SpatialNET.Internal.Noding;
using SpatialNET.IO;
using SpatialNET.Tests.Construction;
using static SpatialNET.Tests.Stress.StressSupport;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace SpatialNET.Tests.Stress;

/// <summary>Runs a property over a tuple of generated values with one parameter per value.</summary>
internal static class StressTupleExtensions
{
    public static void Run<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> body) => gen.Run(t => body(t.Item1, t.Item2));

    public static void Run<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> body) => gen.Run(t => body(t.Item1, t.Item2, t.Item3));

    public static void Run<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> body) => gen.Run(t => body(t.Item1, t.Item2, t.Item3, t.Item4));

    public static void Run<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> body) =>
        gen.Run(t => body(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5));
}

/// <summary>
/// Stress tests of the Voronoi diagram, the triangulation and the terrain analysis (ADR 0036 to 0039; see <see cref="StressFactAttribute"/>): the
/// algorithms on many random, dirty inputs (tiny values, near ties, collinear and cocircular points, thin triangles), checked against invariants and,
/// where NTS means the same, against NTS. A failing case prints a CsCheck seed; replay it with <c>CsCheck_Seed</c>.
/// </summary>
[Trait("Category", "Stress")]
public class TriangulationStressTests
{
    private static string Raw(IEnumerable<Coordinate> points)
    {
        string R(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return "MULTIPOINT Z (" + string.Join(", ", points.Select(p => $"({R(p.X)} {R(p.Y)}{(p.HasZ ? " " + R(p.Z) : "")})")) + ")";
    }

    private static Coordinate[] Distinct(IEnumerable<Coordinate> points) =>
        points.Where(c => double.IsFinite(c.X) && double.IsFinite(c.Y)).GroupBy(c => (c.X, c.Y)).Select(g => g.First()).ToArray();

    /// <summary>The clip rectangle that <c>VoronoiDiagram</c> takes without a clip area: the envelope widened by half of its larger side.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) DefaultRectangle(Coordinate[] sites)
    {
        double minX = sites.Min(p => p.X), maxX = sites.Max(p => p.X), minY = sites.Min(p => p.Y), maxY = sites.Max(p => p.Y);
        double margin = Math.Max(maxX - minX, maxY - minY) / 2;
        if (!(margin > 0))
        {
            margin = 1;
        }

        return (minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    // ---------- Voronoi ----------

    [StressFact]
    public void Voronoi_cells_are_valid_and_tile_the_clip_rectangle_for_dirty_sites()
    {
        Points(1, 30).Run(points =>
        {
            var sites = Distinct(points);
            if (sites.Length == 0)
            {
                return;
            }

            var cells = F.CreateMultiPoint(points, null).VoronoiDiagram(factory: F);
            Assert.Equal(sites.Length, cells.Count);

            var (minX, minY, maxX, maxY) = DefaultRectangle(sites);
            double area = (maxX - minX) * (maxY - minY);
            double scale = 1 + new[] { minX, minY, maxX, maxY }.Max(Math.Abs);
            Assert.True(Math.Abs(cells.Sum(c => c.Cell.Area()) - area) <= 1e-7 * area + 1e-9 * scale * scale,
                $"area {cells.Sum(c => c.Cell.Area())} instead of {area} ({string.Join(" ", cells.Select(c => c.Cell.Area().ToString("R")))}; reference {string.Join(" ", cells.Select(c => VoronoiTests.ReferenceArea(sites, c.Site, minX, minY, maxX, maxY).ToString("R")))}): {Raw(sites)}");
            Assert.All(cells, c => Assert.True(c.Cell.IsValid(), $"{c.Cell.ToWkt()}: {Raw(sites)}"));
            // sites that lie closer together than the grid have cells of the size of the grid: only the others have to be in their cells
            Assert.All(cells.Where(c => c.Cell.RingCount > 0 && sites.All(o => o == c.Site || Math.Max(Math.Abs(o.X - c.Site.X), Math.Abs(o.Y - c.Site.Y)) > 1e-6 * scale)), c =>
                Assert.True(c.Cell.Distance(F.CreatePoint(c.Site, null)) <= 1e-9 * scale, $"the site {c.Site} is not in its cell: {Raw(sites)}"));
        });
    }

    [StressFact]
    public void Voronoi_cells_have_the_areas_of_the_NTS_diagram()
    {
        SanePoints(4, 30).Run(points =>
        {
            var sites = Distinct(points);
            var geometry = F.CreateMultiPoint(sites, null);
            if (sites.Length < 4 || geometry.ConvexHull(F).Area() <= 0)
            {
                return;   // collinear sites: NTS does not build a diagram
            }

            var (minX, minY, maxX, maxY) = DefaultRectangle(sites);
            var ours = geometry.VoronoiDiagram(factory: F);

            // the reference: the rectangle cut with the bisector to every other site, independent of the library
            foreach (var c in ours)
            {
                double reference = VoronoiTests.ReferenceArea(sites, c.Site, minX, minY, maxX, maxY);
                Assert.True(Math.Abs(reference - c.Cell.Area()) <= 1e-6 * (1 + reference), $"{c.Cell.Area()} against the reference {reference} at {c.Site}: {Raw(sites)}");
            }

            // NTS, where it can do it (its builder throws for some inputs of its own)
            NetTopologySuite.Geometries.GeometryCollection diagram;
            try
            {
                var builder = new VoronoiDiagramBuilder();
                builder.SetSites(sites.Select(s => new NtsCoordinate(s.X, s.Y)).ToList());
                builder.ClipEnvelope = new NetTopologySuite.Geometries.Envelope(minX, maxX, minY, maxY);
                diagram = (NetTopologySuite.Geometries.GeometryCollection)builder.GetDiagram(NetTopologySuite.Geometries.GeometryFactory.Default);
            }
            catch (Exception)
            {
                return;
            }

            foreach (var cell in diagram.Geometries)
            {
                var site = (NtsCoordinate)cell.UserData;
                var mine = ours.Single(c => c.Site.X == site.X && c.Site.Y == site.Y).Cell;
                Assert.True(Math.Abs(cell.Area - mine.Area()) <= 1e-6 * (1 + cell.Area), $"{cell.Area} against NTS {mine.Area()} at {site}: {Raw(sites)}");
            }
        });
    }

    [StressFact]
    public void Voronoi_with_a_clip_polygon_tiles_the_polygon()
    {
        Gen.Select(SanePoints(2, 20), SaneValidPolygon).Run((points, clip) =>
        {
            if (clip.RingCount == 0 || clip.Area() <= 0)
            {
                return;
            }

            var cells = F.CreateMultiPoint(points, null).VoronoiDiagram(clip, F);

            Assert.True(Math.Abs(cells.Sum(c => c.Cell.Area()) - clip.Area()) <= 1e-6 * (1 + clip.Area()) + 1e-6, $"{cells.Sum(c => c.Cell.Area())} instead of {clip.Area()}: {Raw(points)} in {clip.ToWkt()}");
            Assert.All(cells, c => Assert.True(c.Cell.IsValid(), c.Cell.ToWkt()));
        });
    }

    // ---------- the triangulation ----------

    [StressFact]
    public void Triangulation_is_consistent_locates_every_vertex_and_covers_the_hull()
    {
        Points(3, 30).Run(points =>
        {
            var geometry = F.CreateMultiPoint(points, null);
            var t = Triangulation.FromPoints(geometry);
            if (t.TriangleCount == 0)
            {
                return;
            }

            TriangulationTests.AssertStructure(t);
            double hull = geometry.ConvexHull(F).Area();
            Assert.True(Math.Abs(TriangulationTests.Area(t) - hull) <= 1e-9 * (1 + hull) + 1e-12, $"{TriangulationTests.Area(t)} against {hull}: {Raw(points)}");
            Assert.All(t.Vertices, v => Assert.True(t.Locate(v) >= 0, $"{v} is not in a triangle: {Raw(points)}"));
        });
    }

    [StressFact]
    public void Locate_agrees_with_a_brute_force_search_and_InterpolateZ_is_exact_on_planes()
    {
        Gen.Select(SanePoints(4, 30), SanePoint, Gen.Double[-3, 3], Gen.Double[-3, 3], Gen.Double[-100, 100]).Run((points, query, a, b, c) =>
        {
            double Plane(Coordinate p) => a * p.X + b * p.Y + c;
            var withZ = Distinct(points).Select(p => p with { Z = Plane(p) }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            if (t.TriangleCount == 0)
            {
                return;
            }

            int expected = -1;
            for (int i = 0; i < t.TriangleCount && expected < 0; i++)
            {
                var k = TriangulationTests.Corners(t, i);
                if (Orientation.Orient2D(k[0], k[1], query) >= 0 && Orientation.Orient2D(k[1], k[2], query) >= 0 && Orientation.Orient2D(k[2], k[0], query) >= 0)
                {
                    expected = i;
                }
            }

            Assert.Equal(expected, t.Locate(query));
            double z = t.InterpolateZ(query);
            if (expected >= 0)
            {
                Assert.True(Math.Abs(z - Plane(query)) <= 1e-6 * (1 + Math.Abs(Plane(query))), $"{z} against {Plane(query)} at {query}: {Raw(withZ)}");
            }
            else
            {
                Assert.True(double.IsNaN(z));
            }
        });
    }

    // ---------- break lines and polygons ----------

    [StressFact]
    public void Constrained_triangulation_keeps_non_crossing_constraints_and_is_locally_Delaunay()
    {
        Gen.Select(SanePoints(6, 25), Gen.Int[0, 100000].Array[2, 10]).Run((points, picks) =>
        {
            var distinct = Distinct(points);
            if (distinct.Length < 4)
            {
                return;
            }

            var segments = new List<(Coordinate A, Coordinate B)>();
            for (int i = 0; i + 1 < picks.Length; i += 2)
            {
                var a = distinct[picks[i] % distinct.Length];
                var b = distinct[picks[i + 1] % distinct.Length];
                var candidate = (a, b);
                if (a != b && !Internal.Triangulation.ConstraintSet.HasProperCrossing([.. segments, candidate]))
                {
                    segments.Add(candidate);
                }
            }

            var lines = F.CreatePolyline(segments.Select(s => (ReadOnlyMemory<Coordinate>)new[] { s.A, s.B }).ToArray(), null);
            var t = Triangulation.FromPointsAndLines(F.CreateMultiPoint(distinct, null), lines, F);
            if (t.TriangleCount == 0)
            {
                return;
            }

            TriangulationTests.AssertStructure(t);
            TriangulationTests.AssertConstrainedDelaunay(t);
            double hull = F.CreateMultiPoint(distinct, null).ConvexHull(F).Area();
            Assert.True(Math.Abs(TriangulationTests.Area(t) - hull) <= 1e-9 * (1 + hull), Raw(distinct));

            var constrained = t.ConstrainedEdges(F);
            var edges = Enumerable.Range(0, constrained.PathCount).Select(i => (A: new Coordinate(constrained[i][0].X, constrained[i][0].Y), B: new Coordinate(constrained[i][1].X, constrained[i][1].Y))).ToList();
            foreach (var (a, b) in segments)
            {
                for (int s = 1; s < 10; s++)
                {
                    var p = new Coordinate(a.X + (b.X - a.X) * s / 10, a.Y + (b.Y - a.Y) * s / 10);
                    Assert.True(edges.Any(e => TriangulationTests.OnSegment(p, e.A, e.B)), $"the constraint {a}-{b} is not kept: {Raw(distinct)}");
                }
            }
        });
    }

    [StressFact]
    public void Crossing_constraints_give_a_valid_triangulation_that_is_locally_Delaunay()
    {
        Gen.Select(SanePoints(4, 20), SanePoints(2, 6).Array[1, 4]).Run((points, lines) =>
        {
            var polyline = F.CreatePolyline(lines.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null);
            var t = Triangulation.FromPointsAndLines(F.CreateMultiPoint(points, null), polyline, F, gridSize: 1e-3);

            if (t.TriangleCount > 0)
            {
                TriangulationTests.AssertStructure(t);
                TriangulationTests.AssertConstrainedDelaunay(t);
            }
        });
    }

    [StressFact]
    public void The_fast_and_the_general_polygon_triangulation_cover_the_polygon_and_are_locally_Delaunay()
    {
        SaneValidPolygon.Run(polygon =>
        {
            if (polygon.RingCount == 0 || polygon.Area() <= 0)
            {
                return;
            }

            var fast = Triangulation.FromPolygon(polygon, F);
            var general = Triangulation.FromPolygonGeneral(polygon, F);

            foreach (var t in new[] { fast, general })
            {
                if (t.TriangleCount == 0)
                {
                    Assert.True(polygon.Area() < 1e-9, polygon.ToWkt());
                    continue;
                }

                TriangulationTests.AssertStructure(t);
                TriangulationTests.AssertConstrainedDelaunay(t);
                Assert.True(Math.Abs(TriangulationTests.Area(t) - polygon.Area()) <= 1e-9 * (1 + polygon.Area()), $"{TriangulationTests.Area(t)} against {polygon.Area()}: {polygon.ToWkt()}");
                for (int i = 0; i < t.TriangleCount; i++)
                {
                    var k = TriangulationTests.Corners(t, i);
                    var centroid = new Coordinate((k[0].X + k[1].X + k[2].X) / 3, (k[0].Y + k[1].Y + k[2].Y) / 3);
                    Assert.True(polygon.Locate(centroid) != Location.Outside, $"the triangle {i} is outside: {polygon.ToWkt()}");
                }
            }
        });
    }

    // ---------- terrain ----------

    [StressFact]
    public void Volume_satisfies_the_net_identity_and_slope_aspect_and_area_are_in_range()
    {
        Gen.Select(SanePoints(4, 30), Gen.Double[-100, 100].Array[30], Gen.Double[-120, 120]).Run((points, heights, reference) =>
        {
            var withZ = Distinct(points).Select((p, i) => p with { Z = heights[i % heights.Length] }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            if (t.TriangleCount == 0)
            {
                return;
            }

            var (above, below) = t.Volume(reference);
            double net = 0, map = 0, surface = 0;
            for (int i = 0; i < t.TriangleCount; i++)
            {
                var k = TriangulationTests.Corners(t, i);
                double area = ((k[1].X - k[0].X) * (k[2].Y - k[0].Y) - (k[2].X - k[0].X) * (k[1].Y - k[0].Y)) / 2;
                net += area * ((k[0].Z + k[1].Z + k[2].Z) / 3 - reference);
                map += area;
                surface += t.SurfaceArea(i);

                double slope = t.Slope(i), aspect = t.Aspect(i);
                Assert.InRange(slope, 0, 90);
                Assert.True(double.IsNaN(aspect) || (aspect >= 0 && aspect < 360), $"aspect {aspect}");
            }

            Assert.True(above >= 0 && below >= 0);
            Assert.True(Math.Abs((above - below) - net) <= 1e-9 * (1 + above + below), $"{above - below} against {net}: {Raw(withZ)}");
            Assert.True(surface >= map - 1e-9 * (1 + map), $"surface {surface} smaller than map {map}");
            Assert.Equal(surface, t.SurfaceArea(), 1e-9 * (1 + surface));
        });
    }

    [StressFact]
    public void Drape_follows_the_surface_and_keeps_the_line()
    {
        Gen.Select(SanePoints(5, 30), Gen.Double[-3, 3], Gen.Double[-3, 3], Gen.Double[0, 1].Array[6]).Run((points, a, b, weights) =>
        {
            double Plane(double x, double y) => a * x + b * y + 7;
            var withZ = Distinct(points).Select(p => p with { Z = Plane(p.X, p.Y) }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            if (t.TriangleCount == 0 || withZ.Length < 3)
            {
                return;
            }

            // three points of the line inside the hull: convex combinations of the sites
            Coordinate Inside(int i)
            {
                double w0 = weights[i % 6] + 0.01, w1 = weights[(i + 1) % 6] + 0.01, w2 = weights[(i + 2) % 6] + 0.01, s = w0 + w1 + w2;
                var p0 = withZ[i % withZ.Length];
                var p1 = withZ[(i + 1) % withZ.Length];
                var p2 = withZ[(i + 2) % withZ.Length];
                return new Coordinate((w0 * p0.X + w1 * p1.X + w2 * p2.X) / s, (w0 * p0.Y + w1 * p1.Y + w2 * p2.Y) / s);
            }

            var line = F.CreatePolyline([new[] { Inside(0), Inside(1), Inside(2) }], null);
            var draped = t.Drape(line, F);

            if (draped.PathCount == 0)
            {
                return;   // a line along a border may be judged outside by the tolerance; nothing to check
            }

            double length = 0;
            for (int p = 0; p < draped.PathCount; p++)
            {
                for (int i = 0; i < draped[p].PointCount; i++)
                {
                    var v = (IPoint3D)draped[p][i];
                    Assert.True(Math.Abs(v.Z - Plane(v.X, v.Y)) <= 1e-6 * (1 + Math.Abs(v.Z)), $"{v.Z} against {Plane(v.X, v.Y)}: {Raw(withZ)}");
                    if (i > 0)
                    {
                        length += Math.Sqrt((draped[p][i].X - draped[p][i - 1].X) * (draped[p][i].X - draped[p][i - 1].X) + (draped[p][i].Y - draped[p][i - 1].Y) * (draped[p][i].Y - draped[p][i - 1].Y));
                    }
                }
            }

            // everything inside the hull: the same line in the map
            Assert.True(Math.Abs(length - line.Length()) <= 1e-7 * (1 + line.Length()), $"{length} against {line.Length()}: {Raw(withZ)}");
        });
    }

    [StressFact]
    public void Contour_points_lie_on_edges_of_the_triangulation_at_the_height_of_the_contour()
    {
        // The guarantee is along an edge: the point is where the linear height on an edge reaches the level. It is not asked of InterpolateZ,
        // because three almost collinear points make a sliver triangle whose plane is a wall, and a point on its edge can be located in the
        // neighbour (found by this test).
        Gen.Select(SanePoints(5, 30), Gen.Double[-100, 100].Array[30], Gen.Double[-100, 100]).Run((points, heights, level) =>
        {
            var withZ = Distinct(points).Select((p, i) => p with { Z = heights[i % heights.Length] }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            var contour = t.Contour(level, F);
            double scale = 1 + withZ.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));

            bool OnAnEdgeAtTheLevel(double x, double y)
            {
                for (int i = 0; i < t.TriangleCount; i++)
                {
                    var k = TriangulationTests.Corners(t, i);
                    for (int e = 0; e < 3; e++)
                    {
                        var u = k[e];
                        var v = k[(e + 1) % 3];
                        double dx = v.X - u.X, dy = v.Y - u.Y, len2 = dx * dx + dy * dy;
                        double s = Math.Clamp(((x - u.X) * dx + (y - u.Y) * dy) / len2, 0, 1);
                        double px = u.X + s * dx - x, py = u.Y + s * dy - y;
                        if (Math.Sqrt(px * px + py * py) <= 1e-9 * scale && Math.Abs(u.Z + s * (v.Z - u.Z) - level) <= 1e-7 * (1 + Math.Abs(level)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            for (int p = 0; p < contour.PathCount; p++)
            {
                for (int i = 0; i < contour[p].PointCount; i++)
                {
                    Assert.Equal(level, ((IPoint3D)contour[p][i]).Z, 1e-9 * (1 + Math.Abs(level)));
                    Assert.True(OnAnEdgeAtTheLevel(contour[p][i].X, contour[p][i].Y), $"({contour[p][i].X:R} {contour[p][i].Y:R}) is on no edge at the height {level:R}: {Raw(withZ)}");
                }
            }
        });
    }

    // ---------- natural neighbour and refinement (ADR 0041) ----------

    [StressFact]
    public void Natural_neighbour_reproduces_a_plane_and_stays_within_the_heights_for_dirty_sites()
    {
        Gen.Select(SanePoints(4, 30), Gen.Double[-3, 3], Gen.Double[-3, 3], SanePoints(1, 6)).Run((points, a, b, queries) =>
        {
            var withZ = Distinct(points).Select(p => p with { Z = a * p.X + b * p.Y + 7 }).ToArray();
            if (withZ.Length < 3)
            {
                return;
            }

            var t = Triangulation.FromPoints(F.CreateMultiPoint(withZ, null));
            if (t.TriangleCount == 0)
            {
                return;
            }

            double scale = 1 + withZ.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            foreach (var q in queries)
            {
                double z = t.InterpolateNaturalNeighbour(q);
                if (!double.IsNaN(z))
                {
                    Assert.True(Math.Abs(z - (a * q.X + b * q.Y + 7)) <= 1e-6 * scale * (1 + Math.Abs(a) + Math.Abs(b)), $"{z:R} against {a * q.X + b * q.Y + 7:R} at ({q.X:R} {q.Y:R}): {Raw(withZ)}");
                }
            }
        });
    }

    [StressFact]
    public void Refine_gives_a_mesh_without_small_angles_that_tiles_the_square_for_dirty_sites()
    {
        SanePoints(1, 25).Run(points =>
        {
            var inner = Distinct(points).Where(p => Math.Abs(p.X) < 50 && Math.Abs(p.Y) < 50).ToArray();
            var corners = new Coordinate[] { new(-60, -60), new(60, -60), new(60, 60), new(-60, 60) };
            var all = inner.Concat(corners).GroupBy(p => (p.X, p.Y)).Select(g => g.First()).Select(p => p with { Z = p.X + 2 * p.Y }).ToArray();
            var t = Triangulation.FromPoints(F.CreateMultiPoint(all, null));

            var fine = t.Refine(20, maxSteinerPoints: 5000);

            double area = fine.ToPolygons(F).Sum(p => p.Area());
            Assert.True(Math.Abs(area - 14400) <= 1e-6 * 14400, $"area {area:R}: {Raw(all)}");
            if (fine.Vertices.Count - t.Vertices.Count < 5000)
            {
                Assert.True(fine.MinAngle() >= 20 - 1e-6, $"{fine.MinAngle():R}: {Raw(all)}");
            }

            Assert.All(fine.Vertices, v => Assert.True(Math.Abs(v.Z - (v.X + 2 * v.Y)) <= 1e-6 * 200, $"({v.X:R} {v.Y:R} {v.Z:R}): {Raw(all)}"));
        });
    }
}
