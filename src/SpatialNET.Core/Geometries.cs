namespace SpatialNET;

/// <summary>
/// Common base type of all geometries: point, multipoint, polyline and polygon.
/// Paths and rings are parts of a geometry and are not geometries themselves (ADR 0002).
/// </summary>
/// <remarks>
/// All geometry interfaces are read-only. Further members such as envelope, area or
/// IsEmpty are provided as extension methods so that they do not collide with members
/// of the same name on host geometries.
/// </remarks>
public interface IGeometry2D { }

/// <summary>A point in the plane.</summary>
public interface IPoint2D : IGeometry2D
{
    /// <summary>The X coordinate (easting).</summary>
    double X { get; }

    /// <summary>The Y coordinate (northing).</summary>
    double Y { get; }
}

/// <summary>
/// A point with a height. Algorithms interpolate Z at new vertices if the points involved
/// are <see cref="IPoint3D"/>; 3D exists only on points (ADR 0002).
/// </summary>
public interface IPoint3D : IPoint2D
{
    /// <summary>The Z coordinate (height).</summary>
    double Z { get; }
}

/// <summary>
/// An ordered sequence of points; common base of <see cref="IPath2D"/>,
/// <see cref="IRing2D"/> and <see cref="IMultiPoint2D"/>.
/// </summary>
/// <remarks>
/// Access is through <see cref="PointCount"/> and the indexer. For LINQ and
/// <c>foreach</c>, SpatialNET.Extensions offers a list view (<c>AsList()</c>);
/// an implementation may additionally implement <see cref="IReadOnlyList{T}"/> (ADR 0002).
/// </remarks>
public interface IPointCollection2D
{
    /// <summary>The number of points.</summary>
    int PointCount { get; }

    /// <summary>The point at position <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based position, less than <see cref="PointCount"/>.</param>
    IPoint2D this[int index] { get; }
}

/// <summary>A path: a line string as part of an <see cref="IPolyline2D"/>.</summary>
public interface IPath2D : IPointCollection2D { }

/// <summary>
/// A ring: a closed path as part of an <see cref="IPolygon2D"/>.
/// </summary>
/// <remarks>
/// The closing point (equal to the start point) may be repeated or omitted.
/// The orientation is irrelevant; whether a ring is a shell or a hole
/// follows from its position within the polygon (ADR 0003).
/// </remarks>
public interface IRing2D : IPath2D { }

/// <summary>A set of points as one geometry.</summary>
public interface IMultiPoint2D : IGeometry2D, IPointCollection2D { }

/// <summary>A polyline made of one or more paths (corresponds to MultiLineString).</summary>
public interface IPolyline2D : IGeometry2D
{
    /// <summary>The number of paths.</summary>
    int PathCount { get; }

    /// <summary>The path at position <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based position, less than <see cref="PathCount"/>.</param>
    IPath2D this[int index] { get; }
}

/// <summary>
/// A polygon as a flat list of rings that together describe one or more parts
/// (corresponds to MultiPolygon).
/// </summary>
/// <remarks>
/// The area is defined over all rings by the even-odd rule; shells and holes
/// are not marked but derived from nesting (ADR 0003).
/// </remarks>
public interface IPolygon2D : IGeometry2D
{
    /// <summary>The number of rings.</summary>
    int RingCount { get; }

    /// <summary>The ring at position <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based position, less than <see cref="RingCount"/>.</param>
    IRing2D this[int index] { get; }
}
