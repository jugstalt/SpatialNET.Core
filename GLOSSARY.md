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

**Kalibrieren**:
Die Linie bekommt M-Werte aus Ankerpunkten, die M tragen: Die Anker werden auf die Linie projiziert, an ihnen werden Stützpunkte eingefügt, und dazwischen wird M linear in der Länge interpoliert.
_Avoid_: Eichen, Messwerte zuweisen

**Seitlicher Versatz**:
Der Abstand eines Punktes von einer Linie mit Vorzeichen: positiv links der Richtung der Linie, negativ rechts; nicht die Offset-Kurve.
_Avoid_: Offset, Querabstand

**Ereignis**:
Eine Aussage über einen Bereich von M-Werten auf einer Route mit Attributen nach Wahl des Hosts, z.B. ein Tempolimit von Kilometer 3 bis 5,5; hat keine eigene Geometrie.
_Avoid_: Event, Streckenobjekt

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

**Lage**:
Die Stellung eines Punktes zu einer Geometrie: im Inneren, auf dem Rand oder außerhalb.
_Avoid_: Position, Location

**Zuschneiden**:
Das Abschneiden einer Geometrie an einem Rechteck; der Rand des Rechtecks gehört zum Ergebnis.
_Avoid_: Clippen, Ausschneiden

**Einfach**:
Eine Linie, die sich außer an ihren Endpunkten weder berührt noch kreuzt (`IsSimple`).
_Avoid_: selbstüberschneidungsfrei, simple

**Hausdorff-Distanz**:
Wie weit zwei Formen als Ganzes auseinanderliegen: der größte Abstand eines Punkts der einen zur anderen, in beiden Richtungen (`HausdorffDistance`, diskret).
_Avoid_: Fréchet-Distanz, Formabstand

**Kleinstes umschließendes Rechteck**:
Das Rechteck mit der kleinsten Fläche, das eine Geometrie enthält, in beliebigem Winkel (`MinimumRotatedRectangle`).
_Avoid_: Mindestrechteck, Bounding Box (das ist die achsparallele Envelope)

**Kleinster umschließender Kreis**:
Der kleinste Kreis, der eine Geometrie enthält (`MinimumBoundingCircle`).
_Avoid_: Umkreis, Bounding Circle

**Pol der Unzugänglichkeit**:
Der Punkt im Inneren eines Polygons mit dem größten Abstand zum Rand, ein guter Ort für eine Beschriftung (`Polylabel`).
_Avoid_: Labelpunkt, Schwerpunkt (der kann außerhalb liegen)

**Auf das Raster legen**:
Das Runden jeder Koordinate auf den nächsten Rasterpunkt und sonst nichts; kann ein Polygon ungültig machen (`SnapToGrid`).
_Avoid_: Quantisieren, Snappen

**Genauigkeit reduzieren**:
Snap Rounding auf ein Raster mit gültigem Ergebnis (`ReducePrecision`).
_Avoid_: Präzisionsreduktion

**Konkave Hülle**:
Ein Polygon, das jeden Punkt enthält und sie enger umschließt als die konvexe Hülle; gefunden, indem man Dreiecke aus der Delaunay-Triangulierung wegnimmt (`ConcaveHull`).
_Avoid_: konkaves Hüllpolygon, Alpha-Shape (das ist ein anderes Verfahren)

**Delaunay-Triangulierung**:
Die Triangulierung von Punkten, bei der kein Punkt im Umkreis eines Dreiecks liegt (`DelaunayTriangles`).
_Avoid_: Dreiecksvermaschung, TIN (das ist ein Geländemodell darauf)

**Polygonisieren**:
Das Umwandeln von Linien in die Flächen, die sie umschließen: Jede beschränkte Fläche der Anordnung der Linien wird zu einem Polygon.
_Avoid_: Flächenbildung, Polygonizer

**Linien verbinden**:
Das Verbinden der Pfade einer Polylinie dort, wo genau zwei Pfadenden zusammentreffen.
_Avoid_: Line Merge, Zusammenfügen

**Gültig machen**:
Das Reparieren einer ungültigen Geometrie: Ein Polygon wird nach der Even-Odd-Regel neu aufgebaut, seine Fläche bleibt dabei erhalten.
_Avoid_: Fixen, Bereinigen

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
