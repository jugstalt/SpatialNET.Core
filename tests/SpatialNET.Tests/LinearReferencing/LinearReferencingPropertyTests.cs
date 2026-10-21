using CsCheck;
using SpatialNET.Tests.MValues;

namespace SpatialNET.Tests.LinearReferencing;

/// <summary>Property tests for linear referencing (ADR 0017).</summary>
public class LinearReferencingPropertyTests
{
    private static readonly MFactory ZM = new(supportsZ: true, supportsM: true);

    /// <summary>Steps along x, y, M and a flag per vertex whether it carries M.</summary>
    private static readonly Gen<(double[] Dx, double[] Y, double[] Dm, bool[] Has)> Shape =
        Gen.Select(Gen.Double[0.1, 100].Array[2, 12], Gen.Double[-100, 100].Array[12], Gen.Double[0.1, 50].Array[12], Gen.Bool.Array[12]);

    /// <summary>A line that is simple (x strictly increasing) with strictly increasing M at every vertex.</summary>
    private static MPolyline Monotone((double[] Dx, double[] Y, double[] Dm, bool[] Has) s) => Build(s, mask: false, monotone: true);

    private static MPolyline Build((double[] Dx, double[] Y, double[] Dm, bool[] Has) s, bool mask, bool monotone)
    {
        var path = new List<Coordinate>();
        double x = 0, m = 0;
        for (int i = 0; i < s.Dx.Length; i++)
        {
            x += s.Dx[i];
            m = monotone ? m + s.Dm[i] : s.Dm[i] * (s.Has[i] ? 1 : -1) * 3 % 40; // non-monotone M with repeats
            bool has = !mask || s.Has[i];
            path.Add(new Coordinate(x, s.Y[i], i * 0.5, has ? m : double.NaN));
        }

        return new MPolyline([path.ToArray()]);
    }

    private static double Length(IPolyline2D line) => line.Length();

    [Fact]
    public void Project_of_Interpolate_is_the_distance_on_simple_lines()
    {
        Gen.Select(Shape, Gen.Double[0, 1]).Sample((shape, fraction) =>
        {
            var line = Monotone(shape);
            double d = fraction * Length(line);

            double back = line.Project(line.Interpolate(d)!.Value);

            Assert.True(Math.Abs(back - d) <= 1e-6 * Math.Max(1, Length(line)), $"{back} != {d}");
        });
    }

    [Fact]
    public void Substring_has_the_length_of_the_clamped_interval_and_starts_and_ends_where_Interpolate_says()
    {
        Gen.Select(Shape, Gen.Double[-10, 1500], Gen.Double[-10, 1500]).Sample((shape, a, b) =>
        {
            var line = Monotone(shape);
            double length = Length(line);
            double ca = Math.Clamp(a, 0, length), cb = Math.Clamp(b, 0, length);

            var piece = line.Substring(a, b, ZM);

            Assert.Equal(Math.Abs(cb - ca), Length(piece), 1e-6 * Math.Max(1, length));
            if (Math.Abs(cb - ca) > 1e-9)
            {
                var coordinates = MGeo.Coordinates(piece);
                Assert.Equal(line.Interpolate(ca)!.Value.X, coordinates[0].X, 1e-6);
                Assert.Equal(line.Interpolate(cb)!.Value.X, coordinates[^1].X, 1e-6);
            }
        });
    }

    [Fact]
    public void Substring_of_the_whole_line_has_the_same_vertices()
    {
        Shape.Sample(shape =>
        {
            var line = Monotone(shape);

            var piece = line.Substring(0, Length(line), ZM);

            Assert.Equal(MGeo.Coordinates(line).Select(c => (c.X, c.Y)), MGeo.Coordinates(piece).Select(c => (c.X, c.Y)));
        });
    }

    [Fact]
    public void InterpolateByM_inverts_Interpolate_for_strictly_increasing_M()
    {
        Gen.Select(Shape, Gen.Double[0, 1]).Sample((shape, fraction) =>
        {
            var line = Monotone(shape);
            var point = line.Interpolate(fraction * Length(line))!.Value;

            var found = line.InterpolateByM(point.M)!.Value;

            Assert.Equal(point.X, found.X, 1e-6);
            Assert.Equal(point.Y, found.Y, 1e-6);
        });
    }

    [Fact]
    public void ProjectToM_of_a_point_on_the_line_is_its_M()
    {
        Gen.Select(Shape, Gen.Double[0, 1]).Sample((shape, fraction) =>
        {
            var line = Monotone(shape);
            var point = line.Interpolate(fraction * Length(line))!.Value;

            Assert.Equal(point.M, line.ProjectToM(point), 1e-6 * Math.Max(1, Math.Abs(point.M)));
        });
    }

    [Fact]
    public void First_hit_agrees_with_a_brute_force_search_for_non_monotone_M_with_gaps()
    {
        Gen.Select(Shape, Gen.Double[0, 60]).Sample((shape, m) =>
        {
            var line = Build(shape, mask: true, monotone: false);
            var coordinates = MGeo.Coordinates(line);

            // brute force: all hits of all segments with M at both ends, then the one with the smallest position
            double position = 0;
            double best = double.PositiveInfinity;
            double bestX = double.NaN;
            for (int i = 1; i < coordinates.Length; i++)
            {
                var a = coordinates[i - 1];
                var b = coordinates[i];
                double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                if (a.HasM && b.HasM && m >= Math.Min(a.M, b.M) && m <= Math.Max(a.M, b.M))
                {
                    double t = a.M == b.M ? 0 : (m - a.M) / (b.M - a.M);
                    if (position + t * len < best)
                    {
                        best = position + t * len;
                        bestX = a.X + t * (b.X - a.X);
                    }
                }

                position += len;
            }

            var found = line.InterpolateByM(m);

            if (double.IsNaN(bestX))
            {
                Assert.Null(found);
            }
            else
            {
                Assert.NotNull(found);
                Assert.Equal(bestX, found.Value.X, 1e-6);
            }
        });
    }

    [Fact]
    public void M_never_changes_the_X_and_Y_of_any_result()
    {
        Gen.Select(Shape, Gen.Double[0, 1], Gen.Double[0, 1], Gen.Double[-100, 1500], Gen.Double[-100, 100]).Sample((shape, f1, f2, px, py) =>
        {
            var withM = Build(shape, mask: true, monotone: false);
            var flat = (IPolyline2D)MGeo.Flat(withM);
            double length = Length(withM);

            Assert.Equal(length, Length(flat));
            Assert.Equal(Xy(flat.Interpolate(f1 * length)), Xy(withM.Interpolate(f1 * length)));
            Assert.Equal(flat.Project(new Coordinate(px, py)), withM.Project(new Coordinate(px, py)));
            Assert.Equal(
                MGeo.Coordinates(flat.Substring(f1 * length, f2 * length, ZM)).Select(c => (c.X, c.Y)),
                MGeo.Coordinates(withM.Substring(f1 * length, f2 * length, ZM)).Select(c => (c.X, c.Y)));
        });
    }

    private static (double, double)? Xy(Coordinate? c) => c is { } v ? (v.X, v.Y) : null;
}
