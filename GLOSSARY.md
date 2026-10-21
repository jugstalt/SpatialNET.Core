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

**Punkt auf der Fläche**:
Ein Punkt, der auf einer Geometrie liegt, bei einer Fläche in ihrem Inneren; er dient zum Beschriften.
_Avoid_: Interior Point, Label-Punkt

**Nächste Punkte**:
Die zwei Punkte, je einer auf zwei Geometrien, deren Abstand der Abstand der Geometrien ist.
_Avoid_: Nearest Points, kürzeste Linie

**Lineare Referenzierung**:
Das Ansprechen einer Position auf einer Linie über ihre Länge entlang der Linie oder über ihren M-Wert.
_Avoid_: Linear Referencing, Routing

**M-Wert**:
Ein Wert, den ein Punkt zusätzlich zu seiner Lage trägt (z.B. die Strecke entlang einer Straße); er wird mitgeführt, aber nie in eine Berechnung einbezogen.
_Avoid_: Measure, Messwert

**Envelope**:
Das achsenparallele Rechteck, das eine Geometrie umschließt.
_Avoid_: Bounding Box, Extent, BBox

**Räumlicher Index**:
Ein statischer, per Bulk-Load gebauter Baum über Envelopes mit Nutzlast für Bereichsabfragen und nächste Nachbarn (`SpatialIndex<T>`); er liefert nur einen Vorfilter über Envelopes.
_Avoid_: Spatial Index, R-Tree

**Referenz-Implementierung**:
Die Implementierung der Geometrie-Interfaces in SpatialNET.Imp, gedacht für Tests, den Playground und Hosts ohne eigene Geometrien.
_Avoid_: Default-Implementierung, Imp-Klassen

### Berechnung

**Algorithmus**:
Eine räumliche Operation (z.B. Clip, Cut), bereitgestellt als Extension-Methode auf Geometrie-Interfaces, ohne Kenntnis einer konkreten Implementierung.
_Avoid_: Operation, Funktion, Tool

**Geometrie-Factory**:
Der vom Host (oder der Referenz-Implementierung) bereitgestellte Erzeuger, über den ein Algorithmus seine Ergebnis-Geometrien aus Koordinaten als Host-Geometrien anlegt.
_Avoid_: Builder, Creator

**Schnittlinie**:
Die Polylinie, an der Cut ein Polygon oder eine Polylinie in Teile zerlegt.
_Avoid_: Cutter, Schneider, Trennlinie

**Offset-Kurve**:
Eine Polylinie im festen Abstand zu einer Linie (links bei positivem Abstand) oder zum Rand eines Polygons (außen bei positivem Abstand).
_Avoid_: Parallele, Versatzlinie, Offset-Linie

**Füllregel**:
Die Regel, nach der ein Algorithmus aus der Windungszahl eines Punktes entscheidet, ob er zur Fläche einer Eingabe gehört (EvenOdd, NonZero, Positive, Negative).
_Avoid_: Fill-Mode, Polygon-Regel

**Gitterweite**:
Der Abstand des Rasters in Karteneinheiten, auf das ein Overlay alle Koordinaten seines Ergebnisses rundet.
_Avoid_: Toleranz, Präzision, Epsilon, Scale

### Playground

**Playground**:
Eine Web-Anwendung mit Leaflet-Karte, in der Algorithmen interaktiv an Beispiel-, gezeichneten oder hochgeladenen Geometrien ausprobiert werden.
_Avoid_: Demo, Viewer, Testseite

**Rechen-KBS**:
Das projizierte Koordinatenbezugssystem, in das der Playground Eingaben vor der Berechnung transformiert und in dessen Einheiten alle Distanz-Parameter gelten.
_Avoid_: Arbeits-CRS, Ziel-Projektion

**Beispielgeometrie**:
Ein Fixture mit dem Tag `playground`, das der Playground für seinen Algorithmus automatisch anbietet.

### Tests

**Fixture**:
Ein gespeicherter Testfall eines Algorithmus mit Eingaben, Parametern und Tags, den Tests und Playground gemeinsam nutzen.
_Avoid_: Testdatei, Sample, Testfall-Datei

**Orakel**:
Die Vergleichsberechnung mit NetTopologySuite, die das Soll eines Fixtures zur Testlaufzeit liefert.
_Avoid_: Referenz, Golden File, Erwartungswert

**Fremd-Implementierung**:
Eine absichtlich von der Referenz-Implementierung abweichende Implementierung der Geometrie-Interfaces im Testprojekt, gegen die jeder Fixture-Test zusätzlich läuft.
_Avoid_: Mock, Fake, Dummy
