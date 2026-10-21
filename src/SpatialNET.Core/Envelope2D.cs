namespace SpatialNET;

/// <summary>
/// The envelope: the axis-aligned rectangle that encloses a geometry.
/// A value, not an interface; hosts do not have to implement anything (ADR 0002).
/// </summary>
/// <param name="MinX">Smallest X coordinate.</param>
/// <param name="MinY">Smallest Y coordinate.</param>
/// <param name="MaxX">Largest X coordinate.</param>
/// <param name="MaxY">Largest Y coordinate.</param>
public readonly record struct Envelope2D(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The envelope of an empty geometry; all values are <see cref="double.NaN"/>.</summary>
    public static Envelope2D Empty { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>Whether this is the envelope of an empty geometry.</summary>
    public bool IsEmpty => double.IsNaN(MinX);

    /// <summary>The extent in X direction; <see cref="double.NaN"/> if <see cref="IsEmpty"/>.</summary>
    public double Width => MaxX - MinX;

    /// <summary>The extent in Y direction; <see cref="double.NaN"/> if <see cref="IsEmpty"/>.</summary>
    public double Height => MaxY - MinY;
}
