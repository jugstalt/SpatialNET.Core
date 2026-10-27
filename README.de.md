# SpatialNET

*[English version](README.md)*

SpatialNET ist eine .NET-Bibliothek für planare räumliche Berechnungen: Overlays (Schnitt, Vereinigung, Differenz), Buffer und Offset-Kurven, Cut, Fangen an ein Ziel, Vereinfachung, räumliche Prädikate (DE-9IM), Gültigkeitsprüfung und Maße, lineare Referenzierung und Triangulierungen (Delaunay, Voronoi, Geländemodelle mit Höhenlinien, Neigung, Volumen und Glättung).

- **Eigene Algorithmen, keine fremde Engine.** Alles ist selbst implementiert, es gibt keine Abhängigkeit zu NetTopologySuite, Clipper o. ä. ([ADR 0001](docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.de.md)). NetTopologySuite kommt nur im Testprojekt als Vergleichs-Orakel vor.
- **Interfaces statt eigener Geometrieklassen.** Die Algorithmen sind Extension-Methoden auf kleinen, lesenden Interfaces (`IPolygon2D`, `IPolyline2D`, …). Bestehende GIS-Software wie gView oder WebGIS kann sie damit über ihre eigenen Geometrieklassen nutzen. Ergebnisse entstehen über eine Geometrie-Factory, die der Host bereitstellt.
- **Robust durch Konstruktion.** Overlays runden per Snap-Rounding auf ein Gitter und verwenden exakte Prädikate; die Ergebnisse sind konstruktionsbedingt gültig ([ADR 0005](docs/adr/0005-overlay-winding-number-snap-rounding.de.md)).

## Pakete

| Paket | Inhalt |
|---|---|
| `SpatialNET.Core` | Geometrie-Interfaces, Wert-Typen (`Coordinate`, `Envelope2D`), Vertrag der Geometrie-Factory |
| `SpatialNET.Extensions` | Die Algorithmen als Extension-Methoden: Overlays, Buffer, Offset-Kurve, Cut, `SnapTo`, Simplify, Prädikate und `Relate`, `IsValid`/`Validate`, `Prepare`, `SpatialIndex<T>`, Maße, lineare Referenzierung und Routenereignisse, Triangulierung, Voronoi und Geländeanalyse |
| `SpatialNET.Imp` | Referenz-Implementierung der Geometrie-Interfaces und der Factory, für Tests, Werkzeuge und Hosts ohne eigene Geometrien |
| `SpatialNET.IO` | WKT, GeoJSON und WKB lesen und schreiben, über die Geometrie-Factory |

## Schnellstart

> SpatialNET ist eine Vorabversion und liegt auf NuGet; die Pakete mit `--prerelease` installieren, zum Beispiel `dotnet add package SpatialNET.Extensions --prerelease` (dazu bei Bedarf `SpatialNET.Imp` und `SpatialNET.IO`). Alternativ das Repository klonen und die Projekte referenzieren.

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

Hosts implementieren die Interfaces an ihren eigenen Geometrieklassen und stellen eine eigene Factory bereit, siehe [Host-Anbindung](docs/de/host-integration.md).

## Dokumentation

- [Erste Schritte](docs/de/getting-started.md) und [Host-Anbindung](docs/de/host-integration.md)
- [Konzepte](docs/de/concepts/geometry-model.md): Geometriemodell, [Overlays und Gitter](docs/de/concepts/overlay-and-grid.md), [Prädikate](docs/de/concepts/predicates.md), [Buffer und Offset](docs/de/concepts/buffer-and-offset.md), [Vereinfachen und Validieren](docs/de/concepts/simplify-and-validation.md), [Cut](docs/de/concepts/cut.md), [Räumlicher Index](docs/de/concepts/spatial-index.md), [Lineare Referenzierung](docs/de/concepts/linear-referencing.md), [Ereignistabellen](docs/de/concepts/event-tables.md), [Triangulierung, Voronoi und Gelände](docs/de/concepts/triangulation-and-voronoi.md), [Maße und Konstruktionen](docs/de/concepts/measures-and-constructions.md)
- [Glossar](docs/de/glossary.md) (Begriffe Deutsch ↔ Englisch)
- API-Referenz: aus den XML-Kommentaren mit DocFX generiert (nur Englisch), Build mit `docs/build.ps1` (siehe [docs/de/getting-started.md](docs/de/getting-started.md#die-dokumentation-bauen))
- [Architekturentscheidungen (ADRs)](docs/adr/) – die Begründungen hinter dem Design (Englisch, mit deutschen Fassungen)
- [Recherche](research/) zur Algorithmenwahl

## Playground

Eine interaktive Web-Anwendung, in der die Algorithmen auf einer Karte ausprobiert werden (ASP.NET Core + Leaflet):

```bash
dotnet run --project src/SpatialNET.Playground
```

Dann `http://localhost:5003` öffnen. Die Beispielgeometrien stammen aus [samples/](samples/README.de.md), denselben Fixtures, die die Tests verwenden.

## Qualität

Die Testsuite (xUnit, rund 7.800 Tests) vergleicht jeden Algorithmus mit NetTopologySuite als Orakel auf gemeinsamen Fixtures und lässt jedes Fixture gegen die Referenz-Implementierung und gegen eine absichtlich unbequeme, host-artige Implementierung laufen ([ADR 0008](docs/adr/0008-teststrategie-fixtures-nts-orakel.de.md)). Benchmarks liegen in [benchmarks/](benchmarks/).

## Mitwirken

Siehe [CONTRIBUTING.de.md](CONTRIBUTING.de.md) zu Bauen, Testen und dem Inhalt eines Pull Requests; Releases sind in [RELEASING.md](RELEASING.md) beschrieben (Englisch). Änderungen stehen im [Changelog](CHANGELOG.md).

## Lizenz

SpatialNET steht unter der [Apache License 2.0](LICENSE). Fremde Bestandteile, Vorlagen für Algorithmen und Testdaten sind in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) aufgeführt (Englisch).
