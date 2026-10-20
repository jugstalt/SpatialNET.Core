# SpatialNET

Eine Bibliothek für planare räumliche Berechnungen auf Geometrien, die ausschließlich über Interfaces arbeitet, damit bestehende GIS-Software (gView, WebGIS) sie über ihre eigenen Geometrieklassen nutzen kann.

## Language

### Geometrien

**Geometrie-Interface**:
Ein Vertrag in SpatialNET.Core (z.B. IPoint2D, IPolygon2D), den eine Host-Geometrie erfüllt, um von SpatialNET berechnet werden zu können.
_Avoid_: Geometrieklasse, Modell

**Host**:
Eine bestehende GIS-Software (z.B. gView, WebGIS), die SpatialNET einbindet, indem ihre eigenen Geometrietypen die Geometrie-Interfaces implementieren.
_Avoid_: Client, Anwendung, Consumer

**Host-Geometrie**:
Eine Geometrieklasse eines Hosts, die ein oder mehrere Geometrie-Interfaces implementiert.

**Referenz-Implementierung**:
Die Implementierung der Geometrie-Interfaces in SpatialNET.Imp, gedacht für Tests, den Playground und Hosts ohne eigene Geometrien.
_Avoid_: Default-Implementierung, Imp-Klassen

### Berechnung

**Algorithmus**:
Eine räumliche Operation (z.B. Clip, Cut), bereitgestellt als Extension-Methode auf Geometrie-Interfaces, ohne Kenntnis einer konkreten Implementierung.
_Avoid_: Operation, Funktion, Tool

**Geometrie-Factory**:
Der vom Host bereitgestellte Erzeuger, über den ein Algorithmus seine Ergebnis-Geometrien als Host-Geometrien anlegt.
_Avoid_: Builder, Creator

### Playground

**Playground**:
Eine Web-Anwendung mit Leaflet-Karte, in der Algorithmen interaktiv an Beispiel-, gezeichneten oder hochgeladenen Geometrien ausprobiert werden.
_Avoid_: Demo, Viewer, Testseite

**Beispielgeometrie**:
Eine pro Algorithmus vorbereitete Eingabe, die der Playground automatisch anbietet.
