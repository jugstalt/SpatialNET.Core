# Host-Anbindung

SpatialNET arbeitet ausschließlich über Interfaces. Ein **Host** – eine bestehende GIS-Anwendung mit eigenen Geometrieklassen – bindet es in drei Schritten ein, ohne seine Geometrien umzuwandeln:

1. Die Host-Geometrieklassen implementieren die **Geometrie-Interfaces** aus `SpatialNET.Core`.
2. Der Host stellt eine **`IGeometryFactory`** bereit, die aus Koordinaten Ergebnis-Geometrien als Host-Klassen erzeugt.
3. `GeometryFactory.Default` wird einmal beim Start gesetzt (oder die Factory wird bei jedem Aufruf übergeben).

Danach arbeitet jeder Algorithmus in `SpatialNET.Extensions` direkt auf den Host-Geometrien und liefert Host-Geometrien zurück.

## Die Geometrie-Interfaces

Alle Interfaces sind lesend und minimal ([ADR 0002](../adr/0002-geometrie-interfaces-count-indexer-flache-ringe.de.md)):

| Interface | Member | Bedeutung |
|---|---|---|
| `IGeometry2D` | – | Leerer Basistyp aller Geometrien |
| `IPoint2D` | `X`, `Y` | Ein Punkt |
| `IPoint3D : IPoint2D` | `Z` | Ein Punkt mit Höhe; 3D gibt es nur am Punkt |
| `IPointM : IPoint2D` | `M` | Ein Punkt mit Messwert; unabhängig von `IPoint3D`, ein Punkt darf beide implementieren |
| `IPointCollection2D` | `PointCount`, Indexer | Geordnete Punkte; Basis der nächsten drei |
| `IPath2D : IPointCollection2D` | – | Ein Pfad (Linienzug), Teil einer Polylinie |
| `IRing2D : IPath2D` | – | Ein geschlossener Pfad, Teil eines Polygons |
| `IMultiPoint2D : IGeometry2D, IPointCollection2D` | – | Eine Menge von Punkten |
| `IPolyline2D : IGeometry2D` | `PathCount`, Indexer | Ein oder mehrere Pfade (entspricht MultiLineString) |
| `IPolygon2D : IGeometry2D` | `RingCount`, Indexer | Eine flache Liste von Ringen (entspricht MultiPolygon) |

Wichtig zu wissen:

- **Ringe.** Ein Polygon ist eine flache Ringliste ohne Loch-Marker. Seine Fläche ist über alle Ringe nach der Even-Odd-Regel definiert; Außenringe und Löcher ergeben sich aus der Verschachtelung, die Ring-Orientierung wird bei der Eingabe ignoriert. Der Schlusspunkt eines Rings (gleich dem Startpunkt) darf wiederholt werden oder fehlen ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)).
- **Bewusst kein `IReadOnlyList<T>`.** Punktfolgen nutzen `PointCount` und einen Indexer. So gibt es kein zweites `IEnumerable<T>` an Klassen, die ihre Punkte schon aufzählen und bei denen LINQ sonst mehrdeutig würde. Für LINQ und `foreach` bietet `SpatialNET.Extensions` Listen-Sichten: `points.AsList()`, `polyline.AsList()`, `polygon.AsList()`. Ein Host darf zusätzlich `IReadOnlyList<T>` implementieren und wird dann direkt durchgereicht.
- **Weitere Member** wie Envelope, Fläche oder `IsEmpty` gehören nicht in die Interfaces. Sie sind Extension-Methoden und kollidieren deshalb nicht mit gleichnamigen Membern der Host-Klassen.
- **Z.** Algorithmen interpolieren Z an neuen Knoten, wenn die beteiligten Punkte `IPoint3D` sind. Hosts, die immer eine Höhe führen, liefern einfach `Z = 0`.
- **M.** Algorithmen führen M mit und interpolieren es an neuen Knoten (wie Z), wenn die beteiligten Punkte `IPointM` sind; sie rechnen nie damit ([ADR 0015](../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)). Eine Klasse, die `IPointM` implementiert, meldet ihr `M` als gültig, auch 0; `NaN` heißt „kein M“. Ein Host, der „kein M“ nicht von 0 unterscheiden kann (zum Beispiel eine Punktklasse, die immer `M = 0` speichert), darf `IPointM` **nicht** auf dieser Klasse implementieren, sondern auf einer M-fähigen Unterklasse oder einem Wrapper, den er dort einsetzt, wo die Geometrie M hat.
- **Leere Geometrien** sind Geometrien mit 0 Punkten, Pfaden bzw. Ringen.

Die Interfaces zu implementieren sind meist wenige Zeilen je Klasse: entweder direkt an den Host-Klassen oder – wenn der Host eigene Interfaces hat – an diesen, mit Default-Interface-Membern für Indexer mit abweichendem Rückgabetyp. Ein minimaler Host sieht so aus:

```csharp
// A minimal host: mutable classes that implement the SpatialNET interfaces.
public sealed class HostPoint(double x, double y) : IPoint3D
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Z { get; set; } // hosts that always carry a height simply return 0
}

public sealed class HostRing(params HostPoint[] points) : IRing2D
{
    public int PointCount => points.Length;

    public IPoint2D this[int index] => points[index];
}

public sealed class HostPolygon(params HostRing[] rings) : IPolygon2D
{
    public int RingCount => rings.Length;

    public IRing2D this[int index] => rings[index];
}
```

## Die Geometrie-Factory

Algorithmen konstruieren Geometrien nie selbst. Sie übergeben Koordinaten an die Factory ([ADR 0004](../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md)):

| Member | Erzeugt |
|---|---|
| `SupportsZ` | – (ob erzeugte Geometrien Z tragen sollen) |
| `SupportsM` | – (ob erzeugte Geometrien M tragen sollen; Default-Member, `false`) |
| `CreatePoint(Coordinate, source)` | einen Punkt |
| `CreateMultiPoint(ReadOnlySpan<Coordinate>, source)` | einen Multipunkt; ohne Koordinaten einen leeren |
| `CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>>, source)` | eine Polylinie aus Pfaden; ohne Pfade eine leere |
| `CreatePolygon(ReadOnlySpan<PolygonPart>, source)` | ein Polygon aus Teilflächen; ohne Teilflächen ein leeres |

Eine `PolygonPart` ist ein Außenring (geschlossen, gegen den Uhrzeigersinn) mit den Löchern, die direkt in ihm liegen (geschlossen, im Uhrzeigersinn). Das Ergebnis ist bereits gültig, der Host muss die Löcher also nicht neu ableiten.

Der Vertrag für Implementierungen:

- **Thread-sicher bzw. zustandslos.** `GeometryFactory.Default` wird von allen Threads genutzt.
- **Puffer kopieren.** Übergebene Puffer dürfen nach dem Aufruf nicht mehr referenziert werden.
- **Leer heißt leer.** Leere Eingaben erzeugen leere Geometrien, nie `null`.
- **Z darf verworfen werden.** Bei `SupportsZ == false` liefern Algorithmen gar kein Z.
- **M darf verworfen werden.** Bei `SupportsM == false` (dem Standard des Interfaces) liefern Algorithmen gar kein M.
- **`source`** ist die primäre Eingabe-Geometrie des Algorithmus (oder `null`). Daraus kann der Host Metadaten wie die Spatial Reference übernehmen, ohne dass SpatialNET sie kennt.

Ergebnisse sind dimensionshomogen, deshalb gibt es kein `CreateGeometryCollection`. Hat ein Host für eine Art keine Klasse (hier: Multipunkt und Polylinie), kann er in der jeweiligen Methode eine `NotSupportedException` werfen; Algorithmen, die sie bräuchten, scheitern dann deutlich, statt etwas anderes zurückzugeben.

```csharp
// The factory receives coordinates only and creates the host classes from them.
public sealed class HostGeometryFactory : IGeometryFactory
{
    public bool SupportsZ => false;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) =>
        new HostPoint(coordinate.X, coordinate.Y);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no multipoint class.");

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) =>
        throw new NotSupportedException("This host has no polyline class.");

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<HostRing>();
        foreach (var part in parts)
        {
            rings.Add(ToRing(part.Shell.Span));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(ToRing(hole.Span));
            }
        }

        return new HostPolygon(rings.ToArray());
    }

    private static HostRing ToRing(ReadOnlySpan<Coordinate> coordinates)
    {
        var points = new HostPoint[coordinates.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new HostPoint(coordinates[i].X, coordinates[i].Y);
        }

        return new HostRing(points);
    }
}
```

## Alles zusammen

```csharp
// 1. Your own geometry classes implement the read-only interfaces ...
var square = new HostPolygon(
    new HostRing(new HostPoint(0, 0), new HostPoint(10, 0), new HostPoint(10, 10), new HostPoint(0, 10)));
var other = new HostPolygon(
    new HostRing(new HostPoint(5, 5), new HostPoint(15, 5), new HostPoint(15, 15), new HostPoint(5, 15)));

// 2. ... and a factory creates the results as your own classes.
GeometryFactory.Default = new HostGeometryFactory();

IPolygon2D result = square.Intersection(other);

Console.WriteLine(result is HostPolygon);                            // True
Console.WriteLine(result.AsList().Count);                            // 1 ring
```

`GeometryFactory.Default` ist ein einfaches statisches Property, das einmal beim Start gesetzt werden soll. Für einen einzelnen Aufruf mit anderer Factory wird sie explizit übergeben: `square.Intersection(other, factory: myFactory)`.

## Threads und veränderliche Geometrien

Host-Geometrien sind meist veränderlich; die Bibliothek behandelt sie für die Dauer eines Aufrufs als Werte.

- **Während eines Aufrufs** liest ein Algorithmus Ihre Geometrie. Ändern Sie diese Instanz nicht aus einem anderen Thread, solange ein Algorithmus darauf läuft. Dieselbe Instanz aus vielen Threads zu lesen ist in Ordnung, sofern Ihre eigenen Klassen gleichzeitiges Lesen erlauben.
- **`PreparedGeometry` und `SpatialIndex<T>` kopieren**, was sie brauchen (einen Schnappschuss der Koordinaten, die Envelopes), spätere Änderungen am Host-Objekt wirken sich also nicht aus; für Änderungen bauen Sie eine neue. Beide sind nach dem Bauen **thread-sicher**, bauen Sie sie also einmal und teilen Sie sie ([ADR 0014](../adr/0014-oeffentlicher-raeumlicher-index-statisch-str.de.md), [ADR 0024](../adr/0024-prepared-geometry-thread-sicher-und-distanz.de.md)).
- **Die Factory** wird von dem Thread aufgerufen, der den Algorithmus ausführt, muss also thread-sicher oder zustandslos sein (siehe oben).
- **Ergebnisse sind neue Objekte.** Ein Algorithmus liefert nie eine Eingabeinstanz zurück und verändert nie seine Eingabe, Sie können Eingabe und Ergebnis also nebeneinander behalten.

## Einheiten, Koordinatensysteme und das Gitter

Die Algorithmen sind **planar**: Sie rechnen mit den Zahlen X und Y, was immer diese bedeuten. Abstände und Flächen kommen in den Einheiten der Koordinaten heraus; nehmen Sie für alles, was misst, ein projiziertes Koordinatensystem (Meter): Ein Buffer von `10` um Längen-/Breitengrad-Daten sind 10 Grad. Z und M gehen nie in eine Berechnung ein.

Overlays runden ihr Ergebnis auf ein **Gitter** ([ADR 0005](../adr/0005-overlay-winding-number-snap-rounding.de.md)). Standardmäßig wird die Gitterweite aus der größten Koordinate der Eingabe abgeleitet, was für die meisten Daten passt. Setzen Sie `Precision.DefaultGridSize` einmal, wenn Ihre Daten eine natürliche Auflösung haben (etwa 0,001 m), oder übergeben Sie `gridSize` an einen einzelnen Aufruf; siehe [Optionen und Genauigkeit](erste-schritte.md#optionen-und-genauigkeit). Prädikate und Maße rechnen auf den genauen Eingabekoordinaten und nutzen kein Gitter.

## Fehler und Randfälle

Alle Algorithmen folgen denselben Regeln ([ADR 0012](../adr/0012-api-signaturen-typisierte-overloads.de.md)), Sie können sich also an einer Stelle darauf verlassen:

| Situation | Ergebnis |
|---|---|
| Ein Argument ist `null` | `ArgumentNullException` |
| Ein Parameter liegt außerhalb des Bereichs (ein negativer Abstand, eine Gitterweite, die nicht endlich oder nicht größer als 0 ist, eine `NaN`-Toleranz) | `ArgumentOutOfRangeException` |
| Keine Factory übergeben und `GeometryFactory.Default` nicht gesetzt | `InvalidOperationException` |
| Ein Objekt implementiert `IGeometry2D`, aber keine der vier Arten (Punkt, Multipunkt, Polylinie, Polygon) | `NotSupportedException` |
| Eine Geometrie ist leer oder ungültig | nie eine Exception: ein leeres Ergebnis, 0 bei Fläche und Länge, `NaN` bei einem Abstand, `null`, wo es keinen Punkt gibt, `false` bei einem Prädikat |
| Eine Koordinate ist `NaN` oder unendlich | `IsValid` und `Validate` melden es; `MakeValid` entfernt sie; andere Algorithmen sind darauf nicht definiert |
| Ein ungültiges Polygon ist die Eingabe | das Ergebnis folgt der Even-Odd-Regel über alle Ringe; prüfen Sie `IsValid`, wo Sie eine Garantie brauchen ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)) |

## Das richtige Werkzeug wählen

| Sie tun das oft | Nehmen Sie |
|---|---|
| Eine Geometrie gegen viele andere testen | `Prepare()`, dann die vorbereitete Geometrie abfragen |
| Elemente einer großen Menge nach Ort finden | `SpatialIndex<T>`: `Query` für ein Rechteck, `Nearest` für das nächste |
| Die sich treffenden Elemente zweier Mengen paaren | `Join` und `SelfJoin` auf zwei Indizes |
| Viele Polygone, Linien oder Punkte verschmelzen | `UnionAll`, keine Schleife aus `Union` |
| Geometrie in kleinem Maßstab zeigen | `Simplify`, `Clip` |
| Benutzereingaben sauber halten | `Validate` und `MakeValid` |

## Lesen und Schreiben

`SpatialNET.IO` liest und schreibt WKT, WKB (ISO und EWKB, mit Z und M) und GeoJSON. Jedes `Read` nimmt optional eine Factory: Übergeben Sie Ihre, und was Sie lesen, ist schon eine Host-Geometrie, ohne Umwandlungsschritt. Anwendungsfälle stehen in den [Rezepten](rezepte.md).

## Checkliste

- Die Host-Geometrieklassen implementieren die Interfaces; `PointCount`, `RingCount` und die Indexer sind billig (sie werden in Schleifen aufgerufen).
- `IPoint3D` nur dort, wo Z echt ist; `IPointM` nur dort, wo „kein M“ von 0 unterscheidbar ist.
- Eine Factory, die Ihre Klassen erzeugt, thread-sicher ist, ihre Puffer kopiert und für leere Eingabe leere Geometrien erzeugt.
- `GeometryFactory.Default` einmal beim Start gesetzt.
- Koordinaten in einem projizierten System zum Messen; `Precision.DefaultGridSize` gesetzt, wenn Ihre Daten eine natürliche Auflösung haben.
- `Prepare()` und `SpatialIndex<T>` dort, wo dieselbe Geometrie oder Menge wiederholt abgefragt wird.
- Die Fremd-Geometrie-Tests des Repositorys laufen gegen Ihre Klassen (nächster Abschnitt).

## Die Anbindung prüfen

Im Testprojekt liegt eine absichtlich unbequeme, host-artige Implementierung (`tests/SpatialNET.Tests/Foreign/ForeignGeometries.cs`): klassenbasierte Indexer, Ringe ohne Schlusspunkt, Außenringe im Uhrzeigersinn, zusätzlich `IReadOnlyList<T>` und jeder Punkt als `IPoint3D` mit `Z = 0`. Jeder Fixture-Test läuft auch gegen sie, so fallen Annahmen über konkrete Typen auf ([ADR 0008](../adr/0008-teststrategie-fixtures-nts-orakel.de.md)). Sie ist ein gutes Vorbild dafür, wie ein echter Host aussieht.
