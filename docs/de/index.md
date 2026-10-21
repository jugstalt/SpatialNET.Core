# SpatialNET

SpatialNET ist eine .NET-Bibliothek für planare räumliche Berechnungen, die ausschließlich über Interfaces arbeitet, damit bestehende GIS-Software sie über ihre eigenen Geometrieklassen nutzen kann. Die Algorithmen sind selbst implementiert: Overlays, Buffer und Offset-Kurven, Cut, Vereinfachung, räumliche Prädikate (DE-9IM), Gültigkeitsprüfung und Maße.

- **[Erste Schritte](erste-schritte.md)**: Installation, erstes Programm, Optionen und Genauigkeit, Lesen und Schreiben, der Playground.
- **[Host-Anbindung](host-anbindung.md)**: die Geometrie-Interfaces und die Geometrie-Factory in der eigenen GIS-Anwendung umsetzen.
- **[Konzepte](konzepte/geometriemodell.md)**: das Geometriemodell, Overlays und das Gitter, Prädikate, Buffer und Offset-Kurven, Vereinfachen und Validieren, Cut.
- **[Glossar](glossar.md)**: die Begriffe in Code und Dokumentation, Deutsch und Englisch.
- **API-Referenz**: aus den XML-Kommentaren generiert, nur auf Englisch (Link in der Fußzeile).
- **[Entscheidungen](decisions/toc.yml)**: die Architecture Decision Records, die Begründungen hinter dem Design.
- **[Recherche](https://github.com/jugstalt/SpatialNET.Core/tree/main/research)**: Hintergründe zur Algorithmenwahl.

SpatialNET ist eine Vorabversion (0.1.0) und noch nicht auf NuGet veröffentlicht. Der Quelltext liegt auf [GitHub](https://github.com/jugstalt/SpatialNET.Core) unter der Apache License 2.0.
