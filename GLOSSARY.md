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

**Pfad**:
Eine geordnete Folge von Punkten, die einen Linienzug bildet; Teil einer Polylinie, selbst keine Geometrie.
_Avoid_: Linestring, Segmentkette

**Ring**:
Ein geschlossener Pfad, der eine Fläche umrandet; ob er Außenring oder Loch ist, ergibt sich aus seiner Lage im Polygon.
_Avoid_: Kontur, Shell

**Loch**:
Ein Ring, der innerhalb einer ungeraden Zahl anderer Ringe desselben Polygons liegt und deren Fläche ausspart.
_Avoid_: Hole, Innenring

**Teilfläche**:
Ein Außenring eines Polygons zusammen mit den Löchern, die direkt in ihm liegen; eine Insel in einem Loch ist eine eigene Teilfläche.
_Avoid_: Part, Polygonteil, Einzelpolygon

**Polylinie**:
Eine Geometrie aus einem oder mehreren Pfaden.
_Avoid_: MultiLineString, Linie

**Polygon**:
Eine Geometrie aus einem oder mehreren Ringen, die zusammen eine oder mehrere Flächen beschreiben.
_Avoid_: MultiPolygon, Fläche

**Envelope**:
Das achsenparallele Rechteck, das eine Geometrie umschließt.
_Avoid_: Bounding Box, Extent, BBox

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
