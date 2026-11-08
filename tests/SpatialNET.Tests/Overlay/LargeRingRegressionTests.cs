using SpatialNET.Imp;
using SpatialNET.IO;

namespace SpatialNET.Tests.Overlay;

/// <summary>
/// Regression (0.10.0-preview.1): clipping a polygon with a very large ring crashed the process with a stack overflow, because the
/// union-find of the planar graph (and of the validation) was recursive and its chains grow with the number of vertices. The
/// algorithms must run on rings of 200,000 vertices without deep recursion. Each test runs on a thread with a small stack
/// (<see cref="SmallStack"/>), so that recursion over the number of vertices fails here for sure and not only on hosts with less stack.
/// </summary>
public class LargeRingRegressionTests
{
    private const int Vertices = 200_000;
    private static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    /// <summary>Runs <paramref name="action"/> on a thread with a 256 KB stack and rethrows its exception.</summary>
    private static void SmallStack(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        }, 256 * 1024);
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
    }

    /// <summary>A dense circle (closed, counter-clockwise).</summary>
    private static IPolygon2D Circle(double cx, double cy, double r, int n = Vertices)
    {
        var ring = new Coordinate[n + 1];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            ring[i] = new Coordinate(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }

        ring[n] = ring[0];
        return F.CreatePolygon([new PolygonPart(ring)], null);
    }

    /// <summary>A comb: a zig-zag top edge of <paramref name="n"/> teeth over a flat bottom.</summary>
    private static IPolygon2D ZigZag(int n = 20_000)
    {
        var ring = new List<Coordinate> { new(0, 0), new(n, 0) };
        for (int i = n; i >= 0; i--)
        {
            ring.Add(new Coordinate(i, i % 2 == 0 ? 10 : 11));
        }

        ring.Add(new Coordinate(0, 0));
        return F.CreatePolygon([new PolygonPart(ring.ToArray())], null);
    }

    [Fact]
    public void Clip_of_a_ring_with_200000_vertices_does_not_overflow()
    {
        SmallStack(() =>
        {
            var circle = Circle(0, 0, 1000);
            var clipped = circle.Clip(new Envelope2D(-2000, 0, 2000, 2000), F);
            Assert.Equal(circle.Area() / 2, clipped.Area(), circle.Area() * 1e-6);
        });
    }

    [Fact]
    public void Union_of_rings_with_200000_vertices_does_not_overflow()
    {
        SmallStack(() =>
        {
            var a = Circle(0, 0, 1000);
            var b = Circle(500, 0, 1000);
            var union = a.Union(b, factory: F);
            Assert.Equal(1, union.RingCount);
            Assert.True(union.Area() > a.Area() && union.Area() < a.Area() + b.Area());
        });
    }

    [Fact]
    public void Clip_of_a_zig_zag_ring_does_not_overflow()
    {
        SmallStack(() =>
        {
            var comb = ZigZag();
            var clipped = comb.Clip(new Envelope2D(-1, -1, 10_000, 20), F);
            Assert.Equal(10_000 * 10 + 10_000 / 2.0, clipped.Area(), 1);
        });
    }

    [Fact]
    public void Validate_and_topology_preserving_simplify_of_200000_vertices_do_not_overflow()
    {
        SmallStack(() =>
        {
            var circle = Circle(0, 0, 1000);
            Assert.True(circle.IsValid());
            var simplified = circle.Simplify(0.01, preserveTopology: true, F);
            Assert.Equal(circle.Area(), simplified.Area(), circle.Area() * 1e-4);
        });
    }

    [Fact]
    public void Vector_tile_of_a_polygon_with_200000_vertices_does_not_overflow()
    {
        SmallStack(() =>
        {
            // As in gView: a large polygon cut by the tile builder (clip through the overlay), with and without topology preservation.
            var circle = Circle(0, 0, 1000);
            var grid = new TileGrid(-1000, 1000, [1000.0 / 256]);
            foreach (bool preserve in new[] { false, true })
            {
                var builder = new MvtTileBuilder(grid, 0, 0, 0, new MvtTileOptions { PreserveTopology = preserve, SimplifyTolerance = 0.001 });
                Assert.True(builder.AddFeature("big", circle));
                Assert.NotEmpty(Mvt.Decode(builder.ToBytes()).Layers);
            }
        });
    }
}
