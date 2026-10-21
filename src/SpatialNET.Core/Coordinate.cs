namespace SpatialNET;

/// <summary>
/// A coordinate as a value. Algorithms pass results to the <see cref="IGeometryFactory"/>
/// exclusively as coordinates (ADR 0004).
/// </summary>
/// <param name="X">The X coordinate (easting).</param>
/// <param name="Y">The Y coordinate (northing).</param>
/// <param name="Z">The height; <see cref="double.NaN"/> means "no Z".</param>
public readonly record struct Coordinate(double X, double Y, double Z = double.NaN)
{
    /// <summary>Whether the coordinate carries a height.</summary>
    public bool HasZ => !double.IsNaN(Z);
}
