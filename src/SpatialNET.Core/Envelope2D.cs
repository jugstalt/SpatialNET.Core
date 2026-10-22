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

    /// <summary>The center; both values are <see cref="double.NaN"/> if <see cref="IsEmpty"/>.</summary>
    public Coordinate Center => new(MinX / 2 + MaxX / 2, MinY / 2 + MaxY / 2);

    /// <summary>Whether the two envelopes touch or overlap (closed intervals). An empty envelope meets nothing.</summary>
    /// <param name="other">The other envelope.</param>
    public bool Intersects(Envelope2D other) =>
        !IsEmpty && !other.IsEmpty && MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

    /// <summary>Whether <paramref name="other"/> lies completely in this envelope (the border counts). An empty envelope contains nothing and lies in nothing.</summary>
    /// <param name="other">The other envelope.</param>
    public bool Contains(Envelope2D other) =>
        !IsEmpty && !other.IsEmpty && MinX <= other.MinX && other.MaxX <= MaxX && MinY <= other.MinY && other.MaxY <= MaxY;

    /// <summary>Whether the point lies in this envelope (the border counts); a point with a <see cref="double.NaN"/> value does not.</summary>
    /// <param name="point">The point.</param>
    public bool Contains(Coordinate point) =>
        !IsEmpty && MinX <= point.X && point.X <= MaxX && MinY <= point.Y && point.Y <= MaxY;

    /// <summary>The smallest envelope that contains both; the other one if this or <paramref name="other"/> is empty.</summary>
    /// <param name="other">The other envelope.</param>
    public Envelope2D Union(Envelope2D other) =>
        IsEmpty ? other : other.IsEmpty ? this
            : new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    /// <summary>The common part of both envelopes; <see cref="Empty"/> if they do not meet.</summary>
    /// <param name="other">The other envelope.</param>
    public Envelope2D Intersection(Envelope2D other) =>
        Intersects(other)
            ? new(Math.Max(MinX, other.MinX), Math.Max(MinY, other.MinY), Math.Min(MaxX, other.MaxX), Math.Min(MaxY, other.MaxY))
            : Empty;

    /// <summary>
    /// The envelope widened on all sides by <paramref name="distance"/>; a negative distance shrinks it and gives
    /// <see cref="Empty"/> if nothing is left. An empty envelope, or a <see cref="double.NaN"/> distance, gives <see cref="Empty"/>.
    /// </summary>
    /// <param name="distance">The distance; negative to shrink.</param>
    public Envelope2D Expand(double distance)
    {
        if (IsEmpty || double.IsNaN(distance))
        {
            return Empty;
        }

        var expanded = new Envelope2D(MinX - distance, MinY - distance, MaxX + distance, MaxY + distance);
        return expanded.MinX > expanded.MaxX || expanded.MinY > expanded.MaxY ? Empty : expanded;
    }
}
