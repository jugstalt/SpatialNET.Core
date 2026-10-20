namespace SpatialNET;

/// <summary>
/// Gemeinsamer Basistyp aller Geometrien: Punkt, Multipunkt, Polylinie und Polygon.
/// Pfade und Ringe sind Teile einer Geometrie und selbst keine Geometrie (ADR 0002).
/// </summary>
/// <remarks>
/// Alle Geometrie-Interfaces sind nur lesend. Weitere Member wie Envelope, Fläche oder
/// IsEmpty gibt es als Extension-Methoden, damit sie nicht mit gleichnamigen Membern
/// der Host-Geometrien kollidieren.
/// </remarks>
public interface IGeometry2D { }

/// <summary>Ein Punkt in der Ebene.</summary>
public interface IPoint2D : IGeometry2D
{
    /// <summary>Die X-Koordinate (Rechtswert).</summary>
    double X { get; }

    /// <summary>Die Y-Koordinate (Hochwert).</summary>
    double Y { get; }
}

/// <summary>
/// Ein Punkt mit Höhe. Algorithmen interpolieren Z an neuen Knoten, wenn die beteiligten
/// Punkte <see cref="IPoint3D"/> sind; 3D gibt es nur am Punkt (ADR 0002).
/// </summary>
public interface IPoint3D : IPoint2D
{
    /// <summary>Die Z-Koordinate (Höhe).</summary>
    double Z { get; }
}

/// <summary>
/// Eine geordnete Folge von Punkten; gemeinsame Basis von <see cref="IPath2D"/>,
/// <see cref="IRing2D"/> und <see cref="IMultiPoint2D"/>.
/// </summary>
/// <remarks>
/// Der Zugriff erfolgt über <see cref="PointCount"/> und den Indexer. Für LINQ und
/// <c>foreach</c> bietet SpatialNET.Extensions eine Listen-Sicht (<c>AsList()</c>);
/// eine Implementierung darf zusätzlich <see cref="IReadOnlyList{T}"/> umsetzen (ADR 0002).
/// </remarks>
public interface IPointCollection2D
{
    /// <summary>Die Anzahl der Punkte.</summary>
    int PointCount { get; }

    /// <summary>Der Punkt an der Position <paramref name="index"/>.</summary>
    /// <param name="index">Nullbasierte Position, kleiner als <see cref="PointCount"/>.</param>
    IPoint2D this[int index] { get; }
}

/// <summary>Ein Pfad: ein Linienzug als Teil einer <see cref="IPolyline2D"/>.</summary>
public interface IPath2D : IPointCollection2D { }

/// <summary>
/// Ein Ring: ein geschlossener Pfad als Teil eines <see cref="IPolygon2D"/>.
/// </summary>
/// <remarks>
/// Der Schlusspunkt (gleich dem Startpunkt) darf wiederholt werden oder fehlen.
/// Die Orientierung ist ohne Bedeutung; ob ein Ring Außenring oder Loch ist,
/// ergibt sich aus seiner Lage im Polygon (ADR 0003).
/// </remarks>
public interface IRing2D : IPath2D { }

/// <summary>Eine Menge von Punkten als eine Geometrie.</summary>
public interface IMultiPoint2D : IGeometry2D, IPointCollection2D { }

/// <summary>Eine Polylinie aus einem oder mehreren Pfaden (entspricht MultiLineString).</summary>
public interface IPolyline2D : IGeometry2D
{
    /// <summary>Die Anzahl der Pfade.</summary>
    int PathCount { get; }

    /// <summary>Der Pfad an der Position <paramref name="index"/>.</summary>
    /// <param name="index">Nullbasierte Position, kleiner als <see cref="PathCount"/>.</param>
    IPath2D this[int index] { get; }
}

/// <summary>
/// Ein Polygon als flache Liste von Ringen, die zusammen eine oder mehrere Teilflächen
/// beschreiben (entspricht MultiPolygon).
/// </summary>
/// <remarks>
/// Die Fläche ist nach der Even-Odd-Regel über alle Ringe definiert; Außenringe und Löcher
/// werden nicht markiert, sondern aus der Verschachtelung ermittelt (ADR 0003).
/// </remarks>
public interface IPolygon2D : IGeometry2D
{
    /// <summary>Die Anzahl der Ringe.</summary>
    int RingCount { get; }

    /// <summary>Der Ring an der Position <paramref name="index"/>.</summary>
    /// <param name="index">Nullbasierte Position, kleiner als <see cref="RingCount"/>.</param>
    IRing2D this[int index] { get; }
}
