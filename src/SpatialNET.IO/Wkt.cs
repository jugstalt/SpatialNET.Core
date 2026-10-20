namespace SpatialNET.IO;

/// <summary>Wie Z beim Schreiben behandelt wird (ADR 0009).</summary>
public enum ZMode
{
    /// <summary>Z wird geschrieben, wenn mindestens ein Punkt eine Höhe ungleich 0 trägt.</summary>
    /// <remarks>Hosts wie gView und WebGIS liefern immer Punkte mit Z = 0; die bleiben damit 2D.</remarks>
    Auto,

    /// <summary>Z wird immer geschrieben; Punkte ohne Höhe mit 0.</summary>
    Always,

    /// <summary>Z wird nie geschrieben.</summary>
    Never,
}

/// <summary>Optionen für das Schreiben von WKT.</summary>
public sealed record WktOptions
{
    /// <summary>Die Standard-Optionen.</summary>
    public static WktOptions Default { get; } = new();

    /// <summary>Wie Z geschrieben wird; Standard <see cref="ZMode.Auto"/>.</summary>
    public ZMode ZMode { get; init; } = ZMode.Auto;
}

/// <summary>
/// Lesen und Schreiben von WKT (ADR 0009). Gelesen wird über die Geometrie-Factory; geometrisch ungültige,
/// aber syntaktisch korrekte Eingaben werden akzeptiert und unverändert an die Factory gegeben.
/// </summary>
/// <remarks>
/// Typabbildung: POINT → <see cref="IPoint2D"/>, POINT EMPTY und MULTIPOINT → <see cref="IMultiPoint2D"/>,
/// (MULTI)LINESTRING → <see cref="IPolyline2D"/>, (MULTI)POLYGON → <see cref="IPolygon2D"/>.
/// M-Werte werden verworfen; Zahlen sind invariant und dürfen auch <c>NaN</c>/<c>Infinity</c> sein.
/// </remarks>
public static class Wkt
{
    /// <summary>Liest eine Geometrie.</summary>
    /// <param name="wkt">Der WKT-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wkt"/> ist <c>null</c>.</exception>
    /// <exception cref="FormatException">Der Text ist kein gültiges WKT; die Meldung nennt die Position.</exception>
    /// <exception cref="NotSupportedException">Der Text ist eine GEOMETRYCOLLECTION (dafür <see cref="ReadAll"/>).</exception>
    /// <exception cref="InvalidOperationException">Weder <paramref name="factory"/> noch <see cref="GeometryFactory.Default"/> ist gesetzt.</exception>
    public static IGeometry2D Read(string wkt, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return new WktReader(wkt, GeometryFactory.Resolve(factory)).ReadDocument(flatten: false)[0];
    }

    /// <summary>Liest eine Geometrie einer erwarteten Art.</summary>
    /// <typeparam name="T">Die erwartete Geometrie-Art, z. B. <see cref="IPolygon2D"/>.</typeparam>
    /// <param name="wkt">Der WKT-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="FormatException">Der Text ist kein gültiges WKT oder keine Geometrie der Art <typeparamref name="T"/>.</exception>
    public static T Read<T>(string wkt, IGeometryFactory? factory = null) where T : IGeometry2D =>
        Read(wkt, factory) is T geometry
            ? geometry
            : throw new FormatException($"Das WKT beschreibt keine Geometrie der Art {typeof(T).Name}.");

    /// <summary>Liest alle Geometrien; eine GEOMETRYCOLLECTION wird flachgeklopft.</summary>
    /// <param name="wkt">Der WKT-Text.</param>
    /// <param name="factory">Die Factory; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wkt"/> ist <c>null</c>.</exception>
    /// <exception cref="FormatException">Der Text ist kein gültiges WKT.</exception>
    public static IReadOnlyList<IGeometry2D> ReadAll(string wkt, IGeometryFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return new WktReader(wkt, GeometryFactory.Resolve(factory)).ReadDocument(flatten: true);
    }

    /// <summary>
    /// Schreibt die Geometrie als WKT: schlankster Typ (POLYGON bei einer Teilfläche, LINESTRING bei einem Pfad;
    /// ein Multipunkt bleibt MULTIPOINT), Ringe geschlossen, Außenringe gegen und Löcher im Uhrzeigersinn,
    /// degenerierte Ringe entfallen.
    /// </summary>
    /// <param name="geometry">Die Geometrie.</param>
    /// <param name="options">Die Optionen; <c>null</c> = <see cref="WktOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> ist <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> ist keine bekannte Geometrie-Art.</exception>
    public static string ToWkt(this IGeometry2D geometry, WktOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return WktWriter.Write(geometry, options ?? WktOptions.Default);
    }
}
