// PROTOTYPE – Wegwerfcode (Ticket #2). Kandidat für die Form der SpatialNET.Core-Interfaces.
// Variante A: Count + Indexer, Member-Namen an die Hosts angelehnt (PointCount/PathCount/RingCount).

namespace SpatialNET.Core;

/// <summary>Leerer gemeinsamer Basistyp aller Geometrien (Punkt, Multipunkt, Linie, Fläche).</summary>
public interface IGeometry2D { }

public interface IPoint2D : IGeometry2D
{
    double X { get; }
    double Y { get; }
}

/// <summary>Punkt mit Z. Algorithmen prüfen per <c>is IPoint3D</c>, ob Z interpoliert wird.</summary>
public interface IPoint3D : IPoint2D
{
    double Z { get; }
}

/// <summary>Geordnete Punktfolge – gemeinsame Basis von Pfad, Ring und Multipunkt (kein Geometrie-Typ).</summary>
public interface IPointCollection2D
{
    int PointCount { get; }
    IPoint2D this[int index] { get; }
}

/// <summary>Linienzug, Teil einer Polylinie. Selbst keine Geometrie.</summary>
public interface IPath2D : IPointCollection2D { }

/// <summary>Geschlossener Pfad. Schlusspunkt (= Startpunkt) darf fehlen oder vorhanden sein.</summary>
public interface IRing2D : IPath2D { }

public interface IMultiPoint2D : IGeometry2D, IPointCollection2D { }

/// <summary>Polylinie = 1..n Pfade (entspricht MultiLineString).</summary>
public interface IPolyline2D : IGeometry2D
{
    int PathCount { get; }
    IPath2D this[int index] { get; }
}

/// <summary>Polygon = flache Liste von Ringen (entspricht MultiPolygon). Außen/Loch ermittelt der Algorithmus.</summary>
public interface IPolygon2D : IGeometry2D
{
    int RingCount { get; }
    IRing2D this[int index] { get; }
}

/// <summary>Envelope als Wert, nicht als Interface: wird nur berechnet/übergeben, nie vom Host implementiert.</summary>
public readonly record struct Envelope2D(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}
