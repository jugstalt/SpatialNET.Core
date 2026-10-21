using CsCheck;
using NetTopologySuite.Index.Strtree;
using SpatialNET.Imp;
using SpatialNET.IO;
using SpatialNET.Tests.Fixtures;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;

namespace SpatialNET.Tests.Indexing;

// Orakel: Brute Force (exakt, inkl. Reihenfolge bei Gleichstand) und NTS STRtree (ADR 0014).
[Covers("ToSpatialIndex")]
[Covers("Query")]
public class SpatialIndexTests
{
    private static readonly Gen<Envelope2D> Boxes =
        Gen.Select(Gen.Double[-100, 100], Gen.Double[-100, 100], Gen.Double[0, 30], Gen.Double[0, 30], Gen.Int[0, 3])
            .Select(t =>
            {
                var (x, y, w, h, kind) = t;
                // kind 0 = Punkt, 1 = waagrechte Linie, sonst Rechteck; grobe Rasterung erzeugt viele Duplikate und Gleichstände
                x = Math.Round(x / 10) * 10;
                y = Math.Round(y / 10) * 10;
                return kind switch
                {
                    0 => new Envelope2D(x, y, x, y),
                    1 => new Envelope2D(x, y, x + w, y),
                    _ => new Envelope2D(x, y, x + w, y + h),
                };
            });

    private static readonly Gen<(Envelope2D[] Items, int Capacity)> Inputs =
        Gen.Select(Boxes.Array[0, 300], Gen.Int[2, 20]);

    private static SpatialIndex<int> Build(Envelope2D[] boxes, int capacity) =>
        new(boxes.Select((e, i) => (e, i)), capacity);

    private static bool Meets(Envelope2D a, Envelope2D b) =>
        a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY;

    private static double Dist(Envelope2D e, Coordinate p)
    {
        double dx = Math.Max(Math.Max(e.MinX - p.X, p.X - e.MaxX), 0), dy = Math.Max(Math.Max(e.MinY - p.Y, p.Y - e.MaxY), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void Query_equals_brute_force_and_NTS_STRtree()
    {
        Gen.Select(Inputs, Boxes).Sample(t =>
        {
            var ((boxes, capacity), query) = t;
            var index = Build(boxes, capacity);
            var expected = Enumerable.Range(0, boxes.Length).Where(i => Meets(boxes[i], query)).ToArray();

            Assert.Equal(expected, index.Query(query).Order().ToArray());
            var list = new List<int>();
            index.Query(query, list);
            Assert.Equal(expected, list.Order().ToArray());

            var tree = new STRtree<int>();
            for (int i = 0; i < boxes.Length; i++) tree.Insert(new NtsEnvelope(boxes[i].MinX, boxes[i].MaxX, boxes[i].MinY, boxes[i].MaxY), i);
            Assert.Equal(expected, tree.Query(new NtsEnvelope(query.MinX, query.MaxX, query.MinY, query.MaxY)).Order().ToArray());
        });
    }

    [Fact]
    public void Nearest_equals_brute_force_including_ties()
    {
        Gen.Select(Inputs, Gen.Double[-120, 120], Gen.Double[-120, 120], Gen.Int[1, 12]).Sample(t =>
        {
            var ((boxes, capacity), x, y, k) = t;
            x = Math.Round(x / 5) * 5;
            y = Math.Round(y / 5) * 5; // viele gleiche Abstände
            var p = new Coordinate(x, y);
            var expected = Enumerable.Range(0, boxes.Length).OrderBy(i => Dist(boxes[i], p)).ThenBy(i => i).Take(k).ToArray();

            Assert.Equal(expected, Build(boxes, capacity).Nearest(p, k));
        });
    }

    [Fact]
    public void Nearest_distances_equal_NTS_STRtree()
    {
        Gen.Select(Inputs, Gen.Double[-120, 120], Gen.Double[-120, 120], Gen.Int[1, 8]).Sample(t =>
        {
            var ((boxes, capacity), x, y, k) = t;
            if (boxes.Length == 0) return;
            var p = new Coordinate(x, y);
            var tree = new STRtree<NetTopologySuite.Geometries.Geometry>();
            foreach (var e in boxes)
            {
                var g = NetTopologySuite.Geometries.GeometryFactory.Default.ToGeometry(new NtsEnvelope(e.MinX, e.MaxX, e.MinY, e.MaxY));
                tree.Insert(g.EnvelopeInternal, g);
            }

            tree.Build();
            var point = NetTopologySuite.Geometries.GeometryFactory.Default.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y));
            var nts = tree.NearestNeighbour(point.EnvelopeInternal, point, new GeometryItemDistance(), k);

            var ours = Build(boxes, capacity).Nearest(p, k).Select(i => Dist(boxes[i], p)).ToArray();
            Assert.Equal(nts.Select(g => g.Distance(point)).Order().ToArray(), ours, new DoubleClose());
        });
    }

    private sealed class DoubleClose : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-9;
        public int GetHashCode(double d) => 0;
    }

    [Fact]
    public void Exact_distance_refines_the_envelope_distance()
    {
        // Dreieck-artige Elemente: Envelope-Abstand ist nur eine untere Schranke.
        var boxes = new[] { new Envelope2D(0, 0, 10, 10), new Envelope2D(4, 4, 6, 6), new Envelope2D(20, 0, 21, 1) };
        var exact = new[] { 7.0, 3.0, 20.0 }; // Item 0 liegt in Wahrheit weiter weg als Item 1
        var index = new SpatialIndex<int>(boxes.Select((e, i) => (e, i)));

        Assert.Equal([0, 1, 2], index.Nearest(new Coordinate(5, 5), 3));
        Assert.Equal([1, 0, 2], index.Nearest(new Coordinate(5, 5), 3, (i, _) => exact[i]));
        Assert.Equal([1], index.Nearest(new Coordinate(5, 5), 1, (i, _) => exact[i]));
    }

    [Fact]
    public void Empty_envelopes_are_skipped_and_invalid_ones_throw()
    {
        var index = new SpatialIndex<string>([(Envelope2D.Empty, "none"), (new Envelope2D(0, 0, 1, 1), "box")]);
        Assert.Equal(1, index.Count);
        Assert.Equal(new Envelope2D(0, 0, 1, 1), index.Extent);
        Assert.Empty(index.Query(Envelope2D.Empty));
        Assert.Equal(["box"], index.Query(new Envelope2D(1, 1, 2, 2))); // Berührung zählt

        var nothing = new SpatialIndex<string>([]);
        Assert.Equal(0, nothing.Count);
        Assert.True(nothing.Extent.IsEmpty);
        Assert.Empty(nothing.Query(new Envelope2D(0, 0, 1, 1)));
        Assert.Empty(nothing.Nearest(new Coordinate(0, 0), 3));

        Envelope2D[] invalid =
        [
            new(double.NaN, 0, 1, 1), new(0, 0, double.NaN, 1), new(2, 0, 1, 1), new(0, 2, 1, 1),
            new(0, 0, double.PositiveInfinity, 1), new(double.NegativeInfinity, 0, 1, 1),
        ];
        foreach (var e in invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialIndex<int>([(e, 1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.Query(e));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.Query(e, new List<string>()));
        }
    }

    [Fact]
    public void Arguments_are_checked()
    {
        var index = new SpatialIndex<int>([(new Envelope2D(0, 0, 1, 1), 1)]);
        Assert.Throws<ArgumentNullException>(() => new SpatialIndex<int>(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialIndex<int>([], 1));
        Assert.Throws<ArgumentNullException>(() => index.Query(new Envelope2D(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Nearest(new Coordinate(0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Nearest(new Coordinate(double.NaN, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Nearest(new Coordinate(0, double.PositiveInfinity), 1));
        Assert.Throws<InvalidOperationException>(() => index.Nearest(new Coordinate(0, 0), 1, (_, _) => double.NaN));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<IGeometry2D>)null!).ToSpatialIndex());
        Assert.Throws<ArgumentNullException>(() => Array.Empty<int>().ToSpatialIndex(null!));
        Assert.Throws<ArgumentNullException>(() => ((SpatialIndex<int>)null!).Query(Wkt.Read("POINT(0 0)", ImpGeometryFactory.Instance)));
    }

    [Fact]
    public void Duplicates_are_kept_and_the_input_is_copied()
    {
        var box = new Envelope2D(0, 0, 1, 1);
        var source = new List<(Envelope2D, string)> { (box, "a"), (box, "a"), (box, "b") };
        var index = new SpatialIndex<string>(source);
        source.Clear();

        Assert.Equal(3, index.Count);
        Assert.Equal(["a", "a", "b"], index.Query(box).Order().ToArray());
        Assert.Equal(["a", "a"], index.Nearest(new Coordinate(5, 5), 2)); // Gleichstand: Eingabereihenfolge

        var withNull = new SpatialIndex<string?>([(box, null)]);
        Assert.Equal([null], withNull.Query(box));
    }

    [Fact]
    public void Queries_are_safe_in_parallel()
    {
        var rnd = new Random(7);
        var boxes = Enumerable.Range(0, 5000)
            .Select(_ => { double x = rnd.NextDouble() * 1000, y = rnd.NextDouble() * 1000; return new Envelope2D(x, y, x + rnd.NextDouble() * 20, y + rnd.NextDouble() * 20); })
            .ToArray();
        var index = Build(boxes, 8);
        var queries = Enumerable.Range(0, 400).Select(_ => { double x = rnd.NextDouble() * 1000, y = rnd.NextDouble() * 1000; return new Envelope2D(x, y, x + 60, y + 60); }).ToArray();

        var results = new int[queries.Length][];
        Parallel.For(0, queries.Length, q => results[q] = index.Query(queries[q]).Order().ToArray());

        for (int q = 0; q < queries.Length; q++)
        {
            Assert.Equal(Enumerable.Range(0, boxes.Length).Where(i => Meets(boxes[i], queries[q])).ToArray(), results[q]);
        }
    }

    [Fact]
    public void Geometries_are_indexed_by_their_envelope()
    {
        var f = ImpGeometryFactory.Instance;
        var geometries = new IGeometry2D[]
        {
            Wkt.Read("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", f),
            Wkt.Read("LINESTRING(20 20, 30 30)", f),
            Wkt.Read("POINT(5 5)", f),
            Wkt.Read("MULTIPOINT EMPTY", f),
        };
        var index = geometries.ToSpatialIndex();

        Assert.Equal(3, index.Count);
        Assert.Equal([geometries[0], geometries[2]], index.Query(Wkt.Read("POINT(5 5)", f)).OrderBy(g => Array.IndexOf(geometries, g)));
        Assert.Empty(index.Query(Wkt.Read("MULTIPOINT EMPTY", f)));
        Assert.Equal(geometries[1], index.Nearest(new Coordinate(25, 26), 1)[0]);

        var named = new[] { ("a", geometries[0]), ("b", geometries[1]) }.ToSpatialIndex(x => x.Item2.Envelope());
        Assert.Equal(["b"], named.Query(new Envelope2D(25, 25, 26, 26)).Select(x => x.Item1));
    }
}
