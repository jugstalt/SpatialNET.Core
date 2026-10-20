namespace SpatialNET;

/// <summary>
/// Der Envelope: das achsenparallele Rechteck, das eine Geometrie umschließt.
/// Ein Wert, kein Interface; Hosts müssen nichts implementieren (ADR 0002).
/// </summary>
/// <param name="MinX">Kleinste X-Koordinate.</param>
/// <param name="MinY">Kleinste Y-Koordinate.</param>
/// <param name="MaxX">Größte X-Koordinate.</param>
/// <param name="MaxY">Größte Y-Koordinate.</param>
public readonly record struct Envelope2D(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>Der Envelope einer leeren Geometrie; alle Werte sind <see cref="double.NaN"/>.</summary>
    public static Envelope2D Empty { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>Ob es der Envelope einer leeren Geometrie ist.</summary>
    public bool IsEmpty => double.IsNaN(MinX);

    /// <summary>Die Ausdehnung in X-Richtung; <see cref="double.NaN"/> bei <see cref="IsEmpty"/>.</summary>
    public double Width => MaxX - MinX;

    /// <summary>Die Ausdehnung in Y-Richtung; <see cref="double.NaN"/> bei <see cref="IsEmpty"/>.</summary>
    public double Height => MaxY - MinY;
}
