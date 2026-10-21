namespace SpatialNET;

/// <summary>
/// List views of point sequences, polylines and polygons for LINQ and <c>foreach</c> (ADR 0002).
/// </summary>
/// <remarks>
/// If the geometry already implements the matching <see cref="IReadOnlyList{T}"/>, it is returned
/// directly; otherwise a lean, copy-free view that accesses the indexer is created.
/// </remarks>
public static class ListViews
{
    /// <summary>The points as a list.</summary>
    /// <param name="points">A path, ring or multipoint.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <c>null</c>.</exception>
    public static IReadOnlyList<IPoint2D> AsList(this IPointCollection2D points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points as IReadOnlyList<IPoint2D> ?? new View<IPoint2D>(points.PointCount, i => points[i]);
    }

    /// <summary>The paths as a list.</summary>
    /// <param name="polyline">The polyline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polyline"/> is <c>null</c>.</exception>
    public static IReadOnlyList<IPath2D> AsList(this IPolyline2D polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        return polyline as IReadOnlyList<IPath2D> ?? new View<IPath2D>(polyline.PathCount, i => polyline[i]);
    }

    /// <summary>The rings as a list.</summary>
    /// <param name="polygon">The polygon.</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <c>null</c>.</exception>
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
