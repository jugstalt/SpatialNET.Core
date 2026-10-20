using SpatialNET.Imp;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SpatialNET.Benchmarks;

/// <summary>Lage der Operanden zueinander.</summary>
public enum Case
{
    /// <summary>Zwei Kreise mit N Vertices, halb überlappend.</summary>
    Overlap,

    /// <summary>Zwei Kreise, Envelopes disjunkt.</summary>
    Disjoint,

    /// <summary>Kleiner Kreis vollständig im großen.</summary>
    Contained,

    /// <summary>Kleiner Kreis am Rand eines großen Kreises (kleiner Schnitt-Envelope).</summary>
    Edge,

    /// <summary>N/4 kleine Quadrate (Inseln) gegen einen Kreis, der etwa die Hälfte überdeckt.</summary>
    Islands,
}

/// <summary>Eingaben in UTM-artigen Koordinaten (Gitterweite automatisch).</summary>
public static class Inputs
{
    private const double X0 = 500_000, Y0 = 5_200_000;

    public static (IPolygon2D A, IPolygon2D B) Create(Case c, int n) => c switch
    {
        Case.Overlap => (Circle(0, 0, 1000, n), Circle(500, 300, 1000, n)),
        Case.Disjoint => (Circle(0, 0, 1000, n), Circle(5000, 0, 1000, n)),
        Case.Contained => (Circle(0, 0, 1000, n), Circle(100, 50, 200, n)),
        Case.Edge => (Circle(0, 0, 1000, n), Circle(1000, 0, 50, n / 10)),
        _ => (Islands(n / 4), Circle(500, 500, 700, n)),
    };

    private static IPolygon2D Circle(double cx, double cy, double r, int n)
    {
        var ring = new Coordinate[n + 1];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            ring[i] = new Coordinate(X0 + cx + r * Math.Cos(a), Y0 + cy + r * Math.Sin(a));
        }

        ring[n] = ring[0];
        return ImpGeometryFactory.Instance.CreatePolygon([new PolygonPart(ring)], null);
    }

    private static IPolygon2D Islands(int count)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        double step = 1000.0 / side, size = step * 0.6;
        var parts = new List<PolygonPart>();
        for (int i = 0; i < count; i++)
        {
            double x = X0 + (i % side) * step, y = Y0 + (i / side) * step;
            parts.Add(new PolygonPart(new Coordinate[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size), new(x, y) }));
        }

        return ImpGeometryFactory.Instance.CreatePolygon(parts.ToArray(), null);
    }

    public static NtsGeometry ToNts(IPolygon2D polygon)
    {
        var f = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        var rings = Enumerable.Range(0, polygon.RingCount)
            .Select(i => f.CreateLinearRing(Enumerable.Range(0, polygon[i].PointCount)
                .Select(k => new NetTopologySuite.Geometries.Coordinate(polygon[i][k].X, polygon[i][k].Y)).ToArray()))
            .ToList();
        // Teilflächen sind hier Außenringe ohne Löcher
        return rings.Count == 1 ? f.CreatePolygon(rings[0]) : f.CreateMultiPolygon(rings.Select(r => f.CreatePolygon(r)).ToArray());
    }
}
