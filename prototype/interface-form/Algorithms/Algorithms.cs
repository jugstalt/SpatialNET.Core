// PROTOTYPE – ein paar Algorithmen, nur um zu prüfen, dass die Interface-Form zum Rechnen reicht.

using SpatialNET.Core;

namespace SpatialNET.Extensions;

public static class Iteration
{
    /// <summary>Ersatz für IEnumerable auf den Interfaces: Iteration als Extension statt als Interface-Member.</summary>
    public static IEnumerable<IPoint2D> Points(this IPointCollection2D c)
    {
        for (int i = 0; i < c.PointCount; i++) yield return c[i];
    }
}

public static class Measures
{
    public static double Length(this IPath2D path)
    {
        double len = 0;
        for (int i = 1; i < path.PointCount; i++)
        {
            var a = path[i - 1]; var b = path[i];
            len += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        return len;
    }

    public static double Length(this IPolyline2D line)
    {
        double len = 0;
        for (int i = 0; i < line.PathCount; i++) len += line[i].Length();
        return len;
    }

    static double SignedArea(IRing2D r)
    {
        double s = 0;
        int n = r.PointCount;
        for (int i = 0; i < n; i++)
        {
            var a = r[i]; var b = r[(i + 1) % n]; // funktioniert mit und ohne wiederholten Schlusspunkt
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }

    /// <summary>Flache Ringliste: ein Ring ist Loch, wenn er in einer ungeraden Zahl anderer Ringe liegt (Host-Typ egal).</summary>
    public static double Area(this IPolygon2D poly)
    {
        double area = 0;
        for (int i = 0; i < poly.RingCount; i++)
        {
            int depth = 0;
            for (int j = 0; j < poly.RingCount; j++)
                if (i != j && Contains(poly[j], poly[i][0])) depth++;
            area += (depth % 2 == 0 ? 1 : -1) * Math.Abs(SignedArea(poly[i]));
        }
        return area;
    }

    static bool Contains(IRing2D r, IPoint2D p)
    {
        bool inside = false;
        int n = r.PointCount;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = r[i]; var b = r[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    public static Envelope2D Envelope(this IGeometry2D g) => g switch
    {
        IPoint2D p => new(p.X, p.Y, p.X, p.Y),
        IMultiPoint2D mp => Of(mp.Points()),
        IPolyline2D l => Of(Enumerable.Range(0, l.PathCount).SelectMany(i => l[i].Points())),
        IPolygon2D a => Of(Enumerable.Range(0, a.RingCount).SelectMany(i => a[i].Points())),
        _ => throw new NotSupportedException(g.GetType().Name)
    };

    static Envelope2D Of(IEnumerable<IPoint2D> pts)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return new(x0, y0, x1, y1);
    }

    /// <summary>Z-Interpolation an neuem Punkt auf Segment a-b (Parameter t). null = kein Z.</summary>
    public static double? InterpolateZ(IPoint2D a, IPoint2D b, double t)
        => a is IPoint3D za && b is IPoint3D zb ? za.Z + t * (zb.Z - za.Z) : null;
}

/// <summary>Alternative: C# 14 Extension-Properties statt Methoden – zeigt das Namens-Shadowing durch Host-Properties.</summary>
public static class AsProperties
{
    extension(IPolygon2D poly)
    {
        public double AreaProp => poly.Area();
    }
}

/// <summary>"Beides": Interface bleibt Count+Indexer, SpatialNET liefert eine IReadOnlyList-Sicht für LINQ/foreach.
/// Ein Host, der IReadOnlyList&lt;IPoint2D&gt; selbst implementiert, wird direkt durchgereicht.</summary>
public static class ListViews
{
    public static IReadOnlyList<IPoint2D> AsList(this IPointCollection2D c) => c as IReadOnlyList<IPoint2D> ?? new PointView(c);
    public static IReadOnlyList<IPath2D> AsList(this IPolyline2D l) => l as IReadOnlyList<IPath2D> ?? new View<IPath2D>(l.PathCount, i => l[i]);
    public static IReadOnlyList<IRing2D> AsList(this IPolygon2D p) => p as IReadOnlyList<IRing2D> ?? new View<IRing2D>(p.RingCount, i => p[i]);

    sealed class PointView(IPointCollection2D c) : View<IPoint2D>(c.PointCount, i => c[i]);

    class View<T>(int count, Func<int, T> get) : IReadOnlyList<T>
    {
        public int Count => count;
        public T this[int index] => get(index);
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < count; i++) yield return get(i); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
