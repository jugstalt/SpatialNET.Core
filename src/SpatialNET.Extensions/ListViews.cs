namespace SpatialNET;

/// <summary>
/// Listen-Sichten auf Punktfolgen, Polylinien und Polygone für LINQ und <c>foreach</c> (ADR 0002).
/// </summary>
/// <remarks>
/// Implementiert die Geometrie bereits die passende <see cref="IReadOnlyList{T}"/>, wird sie direkt
/// zurückgegeben; sonst entsteht eine schlanke Sicht ohne Kopie, die auf den Indexer zugreift.
/// </remarks>
public static class ListViews
{
    /// <summary>Die Punkte als Liste.</summary>
    /// <param name="points">Ein Pfad, Ring oder Multipunkt.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> ist <c>null</c>.</exception>
    public static IReadOnlyList<IPoint2D> AsList(this IPointCollection2D points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points as IReadOnlyList<IPoint2D> ?? new View<IPoint2D>(points.PointCount, i => points[i]);
    }

    /// <summary>Die Pfade als Liste.</summary>
    /// <param name="polyline">Die Polylinie.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> ist <c>null</c>.</exception>
    public static IReadOnlyList<IPath2D> AsList(this IPolyline2D polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        return polyline as IReadOnlyList<IPath2D> ?? new View<IPath2D>(polyline.PathCount, i => polyline[i]);
    }

    /// <summary>Die Ringe als Liste.</summary>
    /// <param name="polygon">Das Polygon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> ist <c>null</c>.</exception>
    public static IReadOnlyList<IRing2D> AsList(this IPolygon2D polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        return polygon as IReadOnlyList<IRing2D> ?? new View<IRing2D>(polygon.RingCount, i => polygon[i]);
    }

    private sealed class View<T>(int count, Func<int, T> get) : IReadOnlyList<T>
    {
        public int Count => count;

        public T this[int index] => (uint)index < (uint)count ? get(index) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
            {
                yield return get(i);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
