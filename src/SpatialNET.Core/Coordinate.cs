namespace SpatialNET;

/// <summary>
/// Eine Koordinate als Wert. Algorithmen übergeben Ergebnisse ausschließlich als Koordinaten
/// an die <see cref="IGeometryFactory"/> (ADR 0004).
/// </summary>
/// <param name="X">Die X-Koordinate (Rechtswert).</param>
/// <param name="Y">Die Y-Koordinate (Hochwert).</param>
/// <param name="Z">Die Höhe; <see cref="double.NaN"/> bedeutet „kein Z“.</param>
public readonly record struct Coordinate(double X, double Y, double Z = double.NaN)
{
    /// <summary>Ob die Koordinate eine Höhe trägt.</summary>
    public bool HasZ => !double.IsNaN(Z);
}
