# SpatialNET

SpatialNET ist eine .NET-Bibliothek für planare räumliche Berechnungen, die ausschließlich über Interfaces arbeitet, damit bestehende GIS-Software sie über ihre eigenen Geometrieklassen nutzen kann. Die Algorithmen sind selbst implementiert: Overlays, Buffer und Offset-Kurven, Cut, Fangen an ein Ziel, Vereinfachung, räumliche Prädikate (DE-9IM), Gültigkeitsprüfung und Maße, lineare Referenzierung und Ereignistabellen sowie Triangulierungen (Delaunay, Voronoi, Geländemodelle mit Höhenlinien, Neigung, Volumen und Glättung).

- **[Erste Schritte](getting-started.md)**: Installation, erstes Programm, Optionen und Genauigkeit, Lesen und Schreiben, der Playground.
- **[Host-Anbindung](host-integration.md)**: die Geometrie-Interfaces und die Geometrie-Factory in der eigenen GIS-Anwendung umsetzen.
- **[Rezepte](cookbook.md)**: kurze, getestete Rezepte für alltägliche Aufgaben: Flurstücke finden, Verschnitt, gezeichnete Polygone reparieren, Teilen, Stationierung, Straße mit Kilometersteinen, Ereignistabellen, Einzugsgebiete, Gelände, Kartendarstellung, Austausch.
- **[Konzepte](concepts/geometry-model.md)**: das Geometriemodell, Overlays und das Gitter, Prädikate, Buffer und Offset-Kurven, Vereinfachen und Validieren, Cut, räumlicher Index, lineare Referenzierung, Ereignistabellen, Triangulierung, Voronoi und Gelände.
- **[Glossar](glossary.md)**: die Begriffe in Code und Dokumentation, Deutsch und Englisch.
- **API-Referenz**: aus den XML-Kommentaren generiert, nur auf Englisch (Link in der Fußzeile).
- **[Entscheidungen](decisions/toc.yml)**: die Architecture Decision Records, die Begründungen hinter dem Design.
- **[Recherche](https://github.com/jugstalt/SpatialNET.Core/tree/main/research)**: Hintergründe zur Algorithmenwahl.

SpatialNET ist eine Vorabversion und auf NuGet verfügbar. Der Quelltext liegt auf [GitHub](https://github.com/jugstalt/SpatialNET.Core) unter der Apache License 2.0.
