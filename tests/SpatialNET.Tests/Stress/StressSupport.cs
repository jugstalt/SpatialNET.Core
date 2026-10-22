using CsCheck;
using SpatialNET.Imp;

namespace SpatialNET.Tests.Stress;

/// <summary>
/// A test that runs only if <c>SPATIALNET_STRESS</c> is set to <c>1</c> (or <c>true</c>). Stress tests hammer the algorithms with
/// many random, deliberately dirty inputs (tiny values, near ties, self-intersections) and take minutes; they are
/// no part of the normal run. See <c>tools/stress.ps1</c> and the workflow <c>stress.yml</c>.
/// </summary>
public sealed class StressFactAttribute : FactAttribute
{
    public StressFactAttribute()
    {
        if (!StressSupport.Enabled)
        {
            Skip = "Stress tests run only with SPATIALNET_STRESS=1 (see tools/stress.ps1).";
        }
    }
}

/// <summary>Settings and generators shared by the stress tests.</summary>
internal static class StressSupport
{
    public static readonly IGeometryFactory F = ImpGeometryFactory.Instance;

    /// <summary>The grid the overlays run on; coarse enough that the tolerances below stay meaningful.</summary>
    public const double Grid = 1e-3;

    public static bool Enabled => Environment.GetEnvironmentVariable("SPATIALNET_STRESS") is "1" or "true";

    /// <summary>The number of random cases per stress test (<c>SPATIALNET_STRESS_ITER</c>, default 20,000).</summary>
    public static int Iterations => int.TryParse(Environment.GetEnvironmentVariable("SPATIALNET_STRESS_ITER"), out int n) && n > 0 ? n : 20_000;

    /// <summary>The time budget of one stress test in seconds (<c>SPATIALNET_STRESS_SECONDS</c>, default 60).</summary>
    public static int Seconds => int.TryParse(Environment.GetEnvironmentVariable("SPATIALNET_STRESS_SECONDS"), out int n) && n > 0 ? n : 60;

    /// <summary>Runs a property with the stress settings. A failing case prints its seed; replay it with <c>CsCheck_Seed</c>.</summary>
    public static void Run<T>(this Gen<T> gen, Action<T> assert) => gen.Sample(assert, iter: Iterations, time: Seconds);

    /// <summary>
    /// A coordinate value: ordinary reals, integers, tenths with and without a tiny error (decimal data that lies on grid
    /// borders), and values far below the grid.
    /// </summary>
    public static readonly Gen<double> Value = Gen.OneOf(
        Gen.Double[-50, 50],
        Gen.Int[-50, 50].Select(i => (double)i),
        Gen.Int[-500, 500].Select(i => i / 10.0),
        Gen.Select(Gen.Int[-500, 500], Gen.Double[-1e-12, 1e-12]).Select((i, e) => i / 10.0 + e),
        Gen.Double[-1e-9, 1e-9]);

    /// <summary>
    /// Values that NTS can still decide (no offsets below its floating-point resolution), for the tests that compare with NTS as an oracle.
    /// </summary>
    public static readonly Gen<double> SaneValue = Gen.OneOf(
        Gen.Double[-50, 50].Select(d => Math.Round(d, 3)),  // rounded: CsCheck also produces values like 1e-46 that NTS cannot resolve
        Gen.Int[-50, 50].Select(i => (double)i),
        Gen.Int[-500, 500].Select(i => i / 10.0));

    public static readonly Gen<Coordinate> SanePoint = Gen.Select(SaneValue, SaneValue).Select((x, y) => new Coordinate(x, y));

    public static Gen<Coordinate[]> SanePoints(int min, int max) => SanePoint.Array[min, max];

    /// <summary>A polygon of one or two arbitrary rings from sane values.</summary>
    public static readonly Gen<IPolygon2D> SaneDirtyPolygon = Gen.Select(SanePoints(3, 8), SanePoints(3, 8).Array[0, 1]).Select((shell, holes) =>
        F.CreatePolygon([new PolygonPart(Closed(shell), holes.Select(h => (ReadOnlyMemory<Coordinate>)Closed(h)).ToArray())], null));

    /// <summary>A valid polygon on the stress grid, built from sane values.</summary>
    public static readonly Gen<IPolygon2D> SaneValidPolygon = Gen.Select(SanePoints(3, 8), SanePoints(3, 8).Array[0, 1]).Select((shell, holes) =>
        F.CreatePolygon([new PolygonPart(Closed(shell), holes.Select(h => (ReadOnlyMemory<Coordinate>)Closed(h)).ToArray())], null)
            .MakeValid(factory: F, gridSize: Grid));

    /// <summary>A valid polyline: a path of identical points is no line (ADR 0012: the predicates need not agree for invalid input).</summary>
    public static readonly Gen<IPolyline2D> SaneLine = SanePoints(2, 7).Array[1, 2].Select(paths =>
        F.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null).MakeValid(F));

    public static readonly Gen<Coordinate> RandomPoint = Gen.Select(Value, Value).Select((x, y) => new Coordinate(x, y));

    /// <summary>The points of one ring or path.</summary>
    public static Gen<Coordinate[]> Points(int min, int max) => RandomPoint.Array[min, max];

    /// <summary>Closes a ring (the closing point equals the first).</summary>
    public static Coordinate[] Closed(Coordinate[] points) => [.. points, points[0]];

    /// <summary>A polygon of one or two arbitrary rings: self-intersections, overlaps and touches are likely (even-odd semantics).</summary>
    public static readonly Gen<IPolygon2D> DirtyPolygon = Gen.Select(Points(3, 8), Points(3, 8).Array[0, 1]).Select((shell, holes) =>
        F.CreatePolygon([new PolygonPart(Closed(shell), holes.Select(h => (ReadOnlyMemory<Coordinate>)Closed(h)).ToArray())], null));

    /// <summary>A valid polygon on the stress grid.</summary>
    public static readonly Gen<IPolygon2D> ValidPolygon = DirtyPolygon.Select(p => p.MakeValid(factory: F, gridSize: Grid));

    public static readonly Gen<IPolyline2D> Line = Points(2, 7).Array[1, 2].Select(paths =>
        F.CreatePolyline(paths.Select(p => (ReadOnlyMemory<Coordinate>)p).ToArray(), null).MakeValid(F));

    /// <summary>The tolerance for area identities: snap rounding moves a vertex by at most the grid size.</summary>
    public static double AreaTolerance(params IGeometry2D[] geometries) => 4 * Grid * (1 + geometries.Sum(g => g.Length())) + 1e-9;
}
