using System.Numerics;
using CsCheck;
using NetTopologySuite.Noding;
using NtsSnapRoundingNoder = NetTopologySuite.Noding.Snapround.SnapRoundingNoder;
using SpatialNET.Internal.Noding;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsPrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace SpatialNET.Tests.Noding;

public class OrientationTests
{
    [Fact]
    public void Simple_cases()
    {
        Assert.Equal(1, Orientation.Orient2D(0, 0, 1, 0, 0, 1));
        Assert.Equal(-1, Orientation.Orient2D(0, 0, 1, 0, 0, -1));
        Assert.Equal(0, Orientation.Orient2D(0, 0, 1, 1, 2, 2));
    }

    [Fact]
    public void Near_degenerate_cases_match_exact_arithmetic()
    {
        // Klassischer Fall (Shewchuk/Kettner): Punkte auf einem feinen Gitter um eine fast kollineare Gerade.
        double eps = Math.Pow(2, -53);
        for (int i = 0; i < 64; i++)
        {
            for (int j = 0; j < 64; j++)
            {
                double px = 0.5 + i * eps, py = 0.5 + j * eps;
                int expected = Orientation.Exact(px, py, 12, 12, 24, 24);
                Assert.Equal(expected, Orientation.Orient2D(px, py, 12, 12, 24, 24));
            }
        }
    }

    [Fact]
    public void Random_inputs_match_exact_arithmetic()
    {
        var coordinate = Gen.OneOf(Gen.Double[-1e6, 1e6], Gen.Int[-8, 8].Select(i => (double)i), Gen.Double[-1, 1].Select(d => d * 1e-300));
        Gen.Select(coordinate, coordinate, coordinate, coordinate, Gen.Double[0, 1]).Sample(t =>
        {
            var (ax, ay, bx, by, s) = t;
            // c fast auf der Geraden a–b
            double cx = ax + s * (bx - ax), cy = ay + s * (by - ay);
            Assert.Equal(Orientation.Exact(ax, ay, bx, by, cx, cy), Orientation.Orient2D(ax, ay, bx, by, cx, cy));
        });
    }

    [Fact]
    public void Decompose_is_exact()
    {
        foreach (var v in new[] { 0.1, -3.5, 1e-310, double.MaxValue, 0.0, -0.0 })
        {
            var (m, e) = Orientation.Decompose(v);
            double back = e >= 0 ? (double)(m << e) : (double)m / Math.Pow(2, -e);
            if (e > -1000) Assert.Equal(v, back);
            Assert.Equal(v == 0 ? 0 : Math.Sign(v), m.Sign);
        }
    }
}

[Collection(GlobalStateCollection.Name)]
public sealed class GridModelTests : IDisposable
{
    private readonly double? _saved = SpatialNET.Precision.DefaultGridSize;

    public void Dispose() => SpatialNET.Precision.DefaultGridSize = _saved;

    [Theory]
    [InlineData(1e6, 1e-6)]
    [InlineData(533000, 1e-7)]
    [InlineData(180, 1e-10)]
    [InlineData(1, 1e-12)]
    [InlineData(0, 1e-12)]
    [InlineData(5e15, 1e3)]
    public void Auto_size_is_a_power_of_ten_for_about_12_digits(double max, double expected)
    {
        Assert.Equal(expected, GridModel.AutoSize(max));
    }

    [Fact]
    public void Cascade_explicit_then_default_then_auto()
    {
        var input = SpatialNET.IO.Wkt.Read("POINT (533000 5212000)", Imp.ImpGeometryFactory.Instance);

        SpatialNET.Precision.DefaultGridSize = null;
        Assert.Equal(1e-6, GridModel.Resolve(null, input).Size);

        SpatialNET.Precision.DefaultGridSize = 0.01;
        Assert.Equal(0.01, GridModel.Resolve(null, input).Size);
        Assert.Equal(0.5, GridModel.Resolve(0.5, input).Size);

        Assert.Throws<ArgumentOutOfRangeException>(() => GridModel.Resolve(0, input));
    }

    [Fact]
    public void Round_trip_through_grid_units()
    {
        var grid = new GridModel(1e-3);
        var p = grid.Round(new Coordinate(533000.12345, -0.0005));

        Assert.Equal(new GridPoint(533000123, 0), p); // halbe Einheiten nach oben (wie NTS)
        Assert.Equal(533000.123, grid.ToCoordinate(p).X);
    }

    [Fact]
    public void Too_fine_grid_for_coordinates_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridModel(1e-15).Round(new Coordinate(1e9, 0)));
    }
}

public class SnapRoundingNoderTests
{
    /// <summary>Pfade mit einfachen Werten – erzwingt Gleichstände auf Pixelkanten und -ecken.</summary>
    private static readonly Gen<Coordinate[]> Paths =
        Gen.Select(Gen.Double[-50, 50], Gen.Double[-50, 50]).Array[2, 6]
            .Select(points => points.Select(p => new Coordinate(p.Item1, p.Item2)).ToArray());

    /// <summary>
    /// Pfade in allgemeiner Lage (irrationale Verschiebung): keine Gleichstände auf Pixelkanten, deren
    /// Auflösung zwischen Implementierungen legitim abweichen darf.
    /// </summary>
    private static readonly Gen<Coordinate[]> GeneralPaths =
        Gen.Select(Gen.Int[-50000, 50000], Gen.Int[-50000, 50000]).Array[2, 6]
            .Select(points => points.Select(p => new Coordinate(p.Item1 / 1000.0 + Math.Sqrt(2) * 1e-7, p.Item2 / 1000.0 + Math.PI * 1e-7)).ToArray());

    [Fact]
    public void Matches_the_NTS_snap_rounding_noder()
    {
        Gen.Select(GeneralPaths.Array[1, 6], Gen.OneOfConst(1.0, 0.1, 0.01)).Sample(t =>
        {
            var (paths, gridSize) = t;
            var (nts, ours) = (NtsEdges(paths, gridSize), OurEdges(paths, gridSize));
            Assert.True(nts.SetEquals(ours),
                $"Raster {gridSize}: nur NTS [{string.Join("; ", nts.Except(ours))}], nur SpatialNET [{string.Join("; ", ours.Except(nts))}] "
                + string.Join(" | ", paths.Select(p => string.Join(", ", p.Select(c => $"{c.X:R} {c.Y:R}")))));
        }, iter: 500);
    }

    [Fact]
    public void Output_edges_never_cross_properly()
    {
        Gen.Select(Paths.Array[1, 8], Gen.OneOfConst(1.0, 0.1)).Sample(t =>
        {
            var (paths, gridSize) = t;
            var edges = Node(paths, gridSize).Select(e => (e.A, e.B)).Distinct().ToArray();
            for (int i = 0; i < edges.Length; i++)
            {
                for (int j = i + 1; j < edges.Length; j++)
                {
                    var (a, b) = edges[i];
                    var (c, d) = edges[j];
                    bool proper = GridPoint.Orient(a, b, c) * GridPoint.Orient(a, b, d) < 0
                                  && GridPoint.Orient(c, d, a) * GridPoint.Orient(c, d, b) < 0;
                    Assert.False(proper, $"Kanten {a}-{b} und {c}-{d} kreuzen sich.");
                }
            }
        }, iter: 200);
    }

    [Fact]
    public void A_crossing_within_rounding_error_of_a_pixel_border_routes_both_segments_through_its_hot_pixel()
    {
        // Found by a stress test (seed fIa1vp2Hfn67): the exact crossing lies about 1e-23 below and left of the corner (107.5, 97.5),
        // so its hot pixel is (107, 97), while the floating-point test sees the corner and assigns the point to the next pixel.
        var a = new[] { new Coordinate(43, 6.343E-66), new Coordinate(-7.097E-24, 13) };
        var b = new[] { new Coordinate(-5.616E-44, -1), new Coordinate(42, 41) };

        var edges = Node([a, b], 0.1).Select(e => (e.A, e.B)).Distinct().ToArray();

        for (int i = 0; i < edges.Length; i++)
        {
            for (int j = i + 1; j < edges.Length; j++)
            {
                var (p, q) = edges[i];
                var (r, s) = edges[j];
                bool proper = GridPoint.Orient(p, q, r) * GridPoint.Orient(p, q, s) < 0
                              && GridPoint.Orient(r, s, p) * GridPoint.Orient(r, s, q) < 0;
                Assert.False(proper, $"Edges {p}-{q} and {r}-{s} cross.");
            }
        }

        Assert.Contains(new GridPoint(107, 97), edges.SelectMany(e => new[] { e.A, e.B }));
    }

    [Fact]
    public void Z_is_interpolated_on_the_original_segment()
    {
        var path = new[] { new Coordinate(0, 0, 0), new Coordinate(10, 0, 100) };
        var cross = new[] { new Coordinate(2.5, -5), new Coordinate(2.5, 5) };

        var edges = SnapRoundingNoder.Node([new SegmentString(path, 0, 0), new SegmentString(cross, 1, 1)], new GridModel(1));

        var first = edges.Single(e => e.Source.Operand == 0 && e.A == new GridPoint(0, 0));
        Assert.Equal(new GridPoint(3, 0), first.B); // 2.5 rundet nach oben
        Assert.Equal(30, first.ZB, 9);               // t des Pixelmittelpunkts (3,0) auf dem Original: 0.3
    }

    private static List<NodedEdge> Node(Coordinate[][] paths, double gridSize) =>
        SnapRoundingNoder.Node(paths.Select((p, i) => new SegmentString(p, 0, i)).ToList(), new GridModel(gridSize));

    private static SortedSet<string> OurEdges(Coordinate[][] paths, double gridSize) =>
        new(Node(paths, gridSize).Select(e => Key(e.A.X, e.A.Y, e.B.X, e.B.Y)), StringComparer.Ordinal);

    private static SortedSet<string> NtsEdges(Coordinate[][] paths, double gridSize)
    {
        var noder = new NtsSnapRoundingNoder(new NtsPrecisionModel(1 / gridSize));
        var input = paths.Select(p => (ISegmentString)new NodedSegmentString(p.Select(c => new NtsCoordinate(c.X, c.Y)).ToArray(), null)).ToList();
        noder.ComputeNodes(input);

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in noder.GetNodedSubstrings())
        {
            for (int i = 1; i < s.Count; i++)
            {
                var (a, b) = (s.Coordinates[i - 1], s.Coordinates[i]);
                long ax = (long)Math.Round(a.X / gridSize), ay = (long)Math.Round(a.Y / gridSize);
                long bx = (long)Math.Round(b.X / gridSize), by = (long)Math.Round(b.Y / gridSize);
                if (ax != bx || ay != by) result.Add(Key(ax, ay, bx, by));
            }
        }

        return result;
    }

    private static string Key(long ax, long ay, long bx, long by) =>
        (ax, ay).CompareTo((bx, by)) <= 0 ? $"{ax},{ay}-{bx},{by}" : $"{bx},{by}-{ax},{ay}";
}
