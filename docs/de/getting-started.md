# Erste Schritte

Diese Seite führt durch das erste Programm: Installation, Einrichten der Geometrie-Factory, Geometrien lesen, einen Algorithmus ausführen und das Ergebnis schreiben.

## Installation

SpatialNET ist eine Vorabversion und liegt auf NuGet. Die benötigten Pakete mit `--prerelease` installieren:

```bash
dotnet add package SpatialNET.Extensions --prerelease
dotnet add package SpatialNET.Imp --prerelease
dotnet add package SpatialNET.IO --prerelease
```

Alternativ das Repository klonen und die Projekte referenzieren:

```bash
git clone https://github.com/jugstalt/SpatialNET.Core.git
dotnet add <dein Projekt> reference SpatialNET.Core/src/SpatialNET.Extensions/SpatialNET.Extensions.csproj
dotnet add <dein Projekt> reference SpatialNET.Core/src/SpatialNET.Imp/SpatialNET.Imp.csproj
dotnet add <dein Projekt> reference SpatialNET.Core/src/SpatialNET.IO/SpatialNET.IO.csproj
```

Das Zielframework ist `net10.0`. Welche Projekte gebraucht werden, hängt von der Rolle ab:

| Du bist … | Referenz |
|---|---|
| ein Host (GIS-Anwendung mit eigenen Geometrieklassen) | `SpatialNET.Extensions` (bringt `SpatialNET.Core` mit); siehe [Host-Anbindung](host-integration.md) |
| Anwender ohne eigene Geometrieklassen | `SpatialNET.Extensions`, `SpatialNET.Imp` und für Textformate `SpatialNET.IO` |

## Ein erstes Programm

```csharp
using SpatialNET;
using SpatialNET.Imp;
using SpatialNET.IO;

// Einmal beim Start: die Factory, über die Algorithmen ihre Ergebnisse erzeugen.
GeometryFactory.Default = ImpGeometryFactory.Instance;

var a = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var b = Wkt.Read<IPolygon2D>("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))");

IPolygon2D overlap = a.Intersection(b);

Console.WriteLine(overlap.ToWkt());          // POLYGON ((10 5, 10 10, 5 10, 5 5, 10 5))
Console.WriteLine(overlap.Area());           // 25
Console.WriteLine(a.Intersects(b));          // True
Console.WriteLine(a.Buffer(1).Area() > 100); // True
```

Was hier passiert:

- **Geometrien sind Interfaces.** `IPolygon2D` ist ein lesendes Interface; `Wkt.Read<T>` erzeugt die Geometrie über die Factory, hier als Klassen der Referenz-Implementierung `SpatialNET.Imp`.
- **Algorithmen sind Extension-Methoden** auf den Interfaces (`Intersection`, `Buffer`, `Area`, `Intersects`, …). Alle liegen im Namespace `SpatialNET`.
- **Ergebnisse kommen aus der Factory.** Ein Algorithmus erzeugt seine Ergebnis-Geometrien über `IGeometryFactory`. Jeder Algorithmus nimmt sie als optionalen Parameter `factory`; bei `null` (Standard) gilt `GeometryFactory.Default`. Ist auch diese nicht gesetzt, wirft der Algorithmus eine `InvalidOperationException` mit klarer Meldung ([ADR 0004](../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md)).

## Optionen und Genauigkeit

Optionale Parameter folgen einer festen Reihenfolge: Pflichtparameter → fachliche Optionen → `factory` → `gridSize`.

```csharp
// Gitterweite in Karteneinheiten (Standard: automatisch aus den Koordinaten abgeleitet).
IPolygon2D union = a.Union(b, FillRule.NonZero, gridSize: 0.001);

// Mehrere aufwendige Prädikate gegen dieselbe Geometrie: einmal vorbereiten.
PreparedGeometry prepared = a.Prepare();
bool inside = prepared.Contains(Wkt.Read<IPolygon2D>("POLYGON((1 1, 2 1, 2 2, 1 2, 1 1))"));

// Volles DE-9IM: Matrix und Mustervergleich.
string matrix = a.Relate(b).ToString(); // "212101212"
```

- **Füllregel.** Die Fläche eines Polygons ist über alle Ringe nach der Even-Odd-Regel definiert; Außenringe und Löcher ergeben sich aus der Verschachtelung und werden nicht markiert ([ADR 0003](../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)). Overlay-Operationen nehmen eine `FillRule` (`EvenOdd`, `NonZero`, `Positive`, `Negative`), z. B. um überlappende Außenringe mit `NonZero` zu verschmelzen.
- **Gitter.** Overlays runden ihre Ergebnisse immer auf ein Gitter (Snap-Rounding): Ergebniskoordinaten liegen auf dem Gitter, dafür ist die Topologie beweisbar konsistent und das Ergebnis gültig. Die Gitterweite kommt aus dem Parameter `gridSize`, sonst aus `Precision.DefaultGridSize`, sonst wird sie automatisch aus der größten Koordinate der Eingaben abgeleitet ([ADR 0005](../adr/0005-overlay-winding-number-snap-rounding.de.md)). Prädikate wie `Intersects` und `Relate` rechnen dagegen exakt auf den Eingabekoordinaten, ohne Gitter.
- **Strikte Ergebnisse.** Ergebnisse sind dimensionshomogen: Berührungsreste niedrigerer Dimension fallen weg (zwei Polygone, die sich an einer Kante berühren, schneiden sich in einem leeren Polygon, nicht in einer Linie).

## Lesen und Schreiben

`SpatialNET.IO` liest und schreibt WKT, GeoJSON und WKB ([ADR 0009](../adr/0009-io-wkt-geojson-ueber-factory.de.md), [ADR 0016](../adr/0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.de.md)):

```csharp
IGeometry2D g = Wkt.Read("LINESTRING(0 0, 10 10)");
string json = g.ToGeoJson();
IReadOnlyList<IGeometry2D> all = GeoJson.ReadAll(json); // auch FeatureCollection

byte[] wkb = g.ToWkb();                                 // ISO-WKB, Little Endian
IGeometry2D fromWkb = Wkb.Read(wkb);                    // ISO und EWKB
```

Gelesen wird immer über die Factory (Parameter `factory`, sonst `GeometryFactory.Default`). Syntaktisch korrekte, aber geometrisch ungültige Geometrien werden unverändert akzeptiert.

## Was es gibt

| Bereich | Methoden (Extension-Methoden im Namespace `SpatialNET`; die Ereignistabellen und die Triangulierung sind statische Methoden und Klassen dieses Namespace) |
|---|---|
| Overlays | `Intersection`, `Union`, `UnionAll`, `Difference`, `SymDifference`, `Clip`, `Polygonize` |
| Lage eines Punktes | `Locate` (`Location`), auch auf `PreparedGeometry` |
| Abgeleitete Geometrien | `Buffer`, `OffsetCurve`, `ConvexHull`, `Simplify`, `Cut` |
| Prädikate | `Intersects`, `Disjoint`, `Contains`, `Within`, `Covers`, `CoveredBy`, `Touches`, `Crosses`, `Overlaps`, `EqualsTopo`, `Relate`, `Prepare` |
| Räumlicher Index | `SpatialIndex<T>`, `ToSpatialIndex`, `Query`, `Nearest`, `Join`, `SelfJoin` |
| Lineare Referenzierung | `Interpolate` (auch mit seitlichem Versatz), `Project` (auch in einem Längenfenster), `ProjectDetailed`, `Substring`, `InterpolateByM`, `ProjectToM`, `SubstringByM`, `CalibrateM`, `LocateBetween`, `LocateAlong`, `LocateFeature`, `LocateEvents` |
| Ereignistabellen (Routenereignisse, ohne Geometrie) | `LineEvents` (`Intersect`, `Union`, `Difference`, `Dissolve`, `Clip`, `At`), `PointEvents` (`Between`, `Within`, `Outside`) |
| Triangulierung, Voronoi und Gelände | `DelaunayTriangles`, `DelaunayEdges`, `VoronoiDiagram`, `VoronoiEdges`, `Triangulate` und die Klasse `Triangulation` (`FromPoints`, `FromPointsAndLines`, `FromPolygon`, `Locate`, `InterpolateZ`, `Contour`, `Slope`, `Aspect`, `SurfaceArea`, `Volume`, `Drape`, `InterpolateNaturalNeighbour`, `Resample`, `Refine`, `SlopeZones`, `ContourBands`, `LineOfSight`, `Viewshed`) |
| Gültigkeit | `IsValid`, `Validate`, `MakeValid` |
| Maße | `Area`, `Length`, `Envelope`, `Centroid`, `Distance`, `NearestPoints`, `IsWithinDistance`, `PointOnSurface`, `HausdorffDistance`, `Polylabel` |
| Konstruktionen | `Boundary`, `Densify`, `Smooth`, `SmoothToTolerance`, `Reverse`, `LineMerge`, `Translate`, `Scale`, `Rotate`, `Transform`, `MinimumRotatedRectangle`, `MinimumBoundingCircle`, `ConcaveHull`, `ConcaveHullByRatio` |
| Bereinigen | `RemoveHoles`, `RemoveSmallParts`, `SnapToGrid`, `ReducePrecision` |
| Formprüfungen | `IsSimple`, `IsClosed`, `IsRing`, `EqualsExact` |
| Umwandlung | `AsList` (LINQ-taugliche Sichten), `ToWkt`, `ToGeoJson`, `ToWkb` |

Signaturen, Parameter und Exceptions jeder Methode stehen in der API-Referenz (Englisch), die aus den XML-Kommentaren generiert wird. Die Methoden stehen in statischen Klassen, die nach Themen heißen (`Measures`, `Overlays`, `Validation`, `Preparation`, …; die Erweiterungsklasse hat einen Namen im Plural oder in der Verlaufsform, ihr Ergebnistyp einen im Singular, etwa `ValidationResult`), aber Sie nennen sie selten, weil Sie die Methoden an der Geometrie aufrufen. Geometrien erzeugen Sie über eine Factory (`ImpGeometryFactory` oder Ihre eigene); die Klassen dahinter sind nicht öffentlich.

## Der Playground

Der Playground ist eine kleine Web-Anwendung, um die Algorithmen auf einer Karte auszuprobieren:

```bash
dotnet run --project src/SpatialNET.Playground
```

`http://localhost:5003` öffnen, mit Strg+K einen Algorithmus wählen, Eingaben zeichnen (Ziel A/B), GeoJSON hochladen oder ein Beispiel aus der Leiste wählen. Die Beispiele sind die Fixtures mit dem Tag `playground` aus dem Repository-Ordner [`samples/`](https://github.com/jugstalt/SpatialNET.Core/tree/main/samples). Distanzen gelten in den Einheiten des Rechen-KBS, das standardmäßig automatisch gewählt wird (Auto-UTM) ([ADR 0010](../adr/0010-playground-projektion-serverseitig-auto-utm.de.md)). Jede Eingabe hat außerdem ein WKT-Feld (mit Z und M), und jedes Ergebnis zeigt sein WKT; so bekommen die M-Algorithmen ihre Daten (GeoJSON hat kein M, [ADR 0015](../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)).

## Die Dokumentation bauen

Die Dokumentationsseite (Englisch und Deutsch) wird mit [DocFX](https://dotnet.github.io/docfx/) gebaut, das als lokales Tool festgelegt ist:

```powershell
./docs/build.ps1          # baut docs/_site/en und docs/_site/de
./docs/build.ps1 -Serve   # stellt das Ergebnis zusätzlich unter http://localhost:8080 bereit
```

Die API-Referenz wird aus den XML-Kommentaren der vier Bibliotheken generiert und gibt es nur auf Englisch.
