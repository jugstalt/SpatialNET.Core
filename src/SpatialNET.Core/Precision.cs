namespace SpatialNET;

/// <summary>
/// The fill rule: decides from the winding number of a point whether it belongs to the area of an input (ADR 0005).
/// </summary>
public enum FillRule
{
    /// <summary>Inside for an odd winding number. Default for all inputs (ADR 0003).</summary>
    EvenOdd,

    /// <summary>Inside for a winding number not equal to 0; merges e.g. overlapping shells.</summary>
    NonZero,

    /// <summary>Inside for a winding number greater than 0; basis of the buffer (ADR 0006).</summary>
    Positive,

    /// <summary>Inside for a winding number less than 0.</summary>
    Negative,
}

/// <summary>Global settings for the grid size of the overlays (ADR 0005).</summary>
public static class Precision
{
    private static double? _defaultGridSize;

    /// <summary>
    /// The grid size in map units that overlays use when their <c>gridSize</c> parameter is
    /// <c>null</c>. If this one is also <c>null</c> (default), the grid size is derived automatically from
    /// the coordinate magnitude of the inputs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or not greater than 0.</exception>
    public static double? DefaultGridSize
    {
        get => _defaultGridSize;
        set
        {
            if (value is { } v && (!double.IsFinite(v) || v <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), v, "The grid size must be finite and greater than 0.");
            }

            _defaultGridSize = value;
        }
    }
}
