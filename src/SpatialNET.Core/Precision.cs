namespace SpatialNET;

/// <summary>
/// Die Füllregel: entscheidet aus der Windungszahl eines Punktes, ob er zur Fläche einer Eingabe gehört (ADR 0005).
/// </summary>
public enum FillRule
{
    /// <summary>Innen bei ungerader Windungszahl. Standard für alle Eingaben (ADR 0003).</summary>
    EvenOdd,

    /// <summary>Innen bei Windungszahl ungleich 0; verschmilzt z. B. überlappende Außenringe.</summary>
    NonZero,

    /// <summary>Innen bei Windungszahl größer 0; Grundlage des Buffers (ADR 0006).</summary>
    Positive,

    /// <summary>Innen bei Windungszahl kleiner 0.</summary>
    Negative,
}

/// <summary>Globale Einstellungen zur Gitterweite der Overlays (ADR 0005).</summary>
public static class Precision
{
    private static double? _defaultGridSize;

    /// <summary>
    /// Die Gitterweite in Karteneinheiten, die Overlays verwenden, wenn ihr <c>gridSize</c>-Parameter
    /// <c>null</c> ist. Ist auch diese <c>null</c> (Standard), wird die Gitterweite automatisch aus
    /// der Koordinatengröße der Eingaben abgeleitet.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Der Wert ist nicht endlich oder nicht größer als 0.</exception>
    public static double? DefaultGridSize
    {
        get => _defaultGridSize;
        set
        {
            if (value is { } v && (!double.IsFinite(v) || v <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), v, "Die Gitterweite muss endlich und größer als 0 sein.");
            }

            _defaultGridSize = value;
        }
    }
}
