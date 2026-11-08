# Vektorkacheln (MVT)

`SpatialNET.IO` schreibt und liest **Mapbox Vector Tiles** (MVT 2.1): einen Vektor-Kachelcache neben einem Raster-Kachelcache. Und zwar **für jedes Kachelgitter in jedem Koordinatenbezugssystem**, nicht nur WebMercator. Der Host liefert die Features, ihre Attribute und das Gitter; SpatialNET erledigt alles, was Geometrie und Kachelformat betrifft: Gitterrechnung, Zuschneiden, Vereinfachen, Runden auf die ganzzahligen Kachelkoordinaten, den Umlaufsinn der Ringe und die Protobuf-Kodierung. Ausliefern der Kacheln und Schreiben eines GL-Styles bleiben beim Host ([ADR 0048](../../adr/0048-vektorkacheln-mvt.de.md)).

Neue Abhängigkeiten gibt es keine: Der Protobuf-Codec ist von Hand nach der [Spezifikation](https://github.com/mapbox/vector-tile-spec) geschrieben, gzip nutzt `System.IO.Compression`.

## Eine Kachel bauen

```csharp
// Das Gitter des Kachelcaches, in jedem KBS: hier EPSG:31256 mit den Maßstäben des Dienstes bei 96 dpi.
var grid = TileGrid.FromScales(-115000, 375000, [500000, 250000, 100000, 50000, 10000], dpi: 96);
Envelope2D tile = grid.TileEnvelope(4, 12, 30);    // Stufe 4, Zeile 12, Spalte 30

// Ein Grundstück von 30 x 20 m nahe der linken unteren Ecke der Kachel, in Weltkoordinaten.
var parcel = new Envelope2D(tile.MinX + 10, tile.MinY + 10, tile.MinX + 40, tile.MinY + 30).ToPolygon();

// Ein Builder je Kachel; ein MVT-Layer je Kartenlayer; die Objekt-ID wird zur Feature-ID.
var builder = new MvtTileBuilder(grid, level: 4, row: 12, column: 30);
builder.AddFeature("parcels", parcel, [new("name", "Parcel 7"), new("area", 600.0)], id: 7);
byte[] pbf = builder.ToBytes(gzip: true);

MvtTile read = Mvt.Decode(pbf);                   // gzip wird erkannt
MvtFeature feature = read["parcels"]!.Features[0];
Console.WriteLine(feature.Id);                    // 7
Console.WriteLine(feature.ToGeometry().ToWkt());  // POLYGON ((60 3915, 242 3915, 242 4036, 60 4036, 60 3915))
```

Eine Kachel ist eine `.pbf` mit einem MVT-Layer je Kartenlayer. Layer behalten die Reihenfolge ihres ersten Features, ein Layer ohne Features wird nicht geschrieben; `builder.IsEmpty` sagt, ob überhaupt etwas zu speichern ist. `AddFeature` liefert `false`, wenn von der Geometrie in der Kachel nichts übrig bleibt.

## Das Kachelgitter

`TileGrid` ist reine Arithmetik und projiziert nie. Es kennt den Ursprung, die **Ursprungsecke** (`TileOrigin.UpperLeft`: Zeile 0 oben, wie XYZ und WMTS; `TileOrigin.LowerLeft`: Zeile 0 unten, wie TMS), die Kachelgröße in Pixeln (Breite und Höhe getrennt) und je Stufe eine Auflösung, direkt oder als Maßstab mit dpi (`Auflösung = Maßstab · 0,0254 / dpi`). `TileEnvelope`, `TileAt` und `TileRange` rechnen zwischen Kacheln und Weltkoordinaten um.

`TileGrid.WebMercator()` ist das Standardgitter von Google, OSM und Mapbox (EPSG:3857, Ursprung −20037508,342789244 / 20037508,342789244 links oben, Kacheln mit 256 px): Stufe = z, Zeile = y, Spalte = x. Ob eine Stufe des eigenen Gitters ein solcher Standard-Zoom ist, entscheidet, ob ihre Kacheln unverändert als z/x/y ausgeliefert werden können (`webGrid` ist hier ein Gitter aus den WebMercator-Maßstäben eines Dienstes):

```csharp
// Ist Stufe 10 des Gitters ein Standard-Zoom? Dann können ihre Kacheln unverändert als z/x/y ausgeliefert werden.
bool standard = webGrid.TryGetWebMercatorZoom(10, out int zoom);  // True, zoom = 10

// Dasselbe für einen einzelnen Maßstab (1:577791,71 bei 96 dpi ist Zoom 10).
bool fromScale = TileGrid.TryGetWebMercatorZoom(577791.71, out int z);  // True, z = 10
```

Maßstäbe in Dienst-Metadaten sind oft gerundet; die voreingestellte relative Toleranz von `1e-6` akzeptiert bei Zoom 10 zwei Nachkommastellen, gröbere Rundungen brauchen eine größere `relativeTolerance`.

## Von Welt- zu Kachelkoordinaten

`MvtTileBuilder.AddFeature` führt jede Geometrie durch diese Schritte (`MvtTileOptions` setzt die Zahlen):

1. **Zuschneiden** auf die Kachel plus Puffer (`Buffer`, Vorgabe 64 von 4096 Einheiten), damit Linien und Umrisse nicht sichtbar am Kachelrand enden. Polygone und Linien gehen durch `Clip` ([Overlays](overlay-and-grid.md)); Punkte außerhalb des Puffers fallen weg.
2. **Vereinfachen** nach Douglas-Peucker mit einer Toleranz von etwa einem Pixel der gezeichneten Kachel (`SimplifyTolerance`, Vorgabe 1 px; 0 schaltet ab, `PreserveTopology` nimmt die topologieerhaltende Variante, siehe [Vereinfachen](simplify-and-validation.md)).
3. **Transformieren**, affin in Kachelkoordinaten: X vom linken Rand, **Y nach unten** vom oberen Rand, 0..`Extent` (Vorgabe 4096). Bei nicht quadratischen Kacheln werden beide Achsen getrennt skaliert.
4. **Runden** auf ganze Zahlen, wiederholte Punkte entfernen und zusammengefallene Teile weglassen: eine Linie mit weniger als zwei Punkten, einen Ring mit weniger als drei oder ohne Fläche. Fällt der äußere Ring einer Teilfläche zusammen, gehen ihre Löcher mit.
5. **Ausrichten** der Ringe, wie MVT es verlangt: der äußere Ring jeder Teilfläche mit **positiver Fläche** in Kachelkoordinaten (im Uhrzeigersinn auf dem Bildschirm, mit Y nach unten), ihre Löcher mit negativer. Ein Multipolygon wird Teilfläche für Teilfläche geschrieben, jeder äußere Ring gefolgt von seinen Löchern.

`MvtTileTransform` ist die Abbildung allein (`ToTile`, `ToWorld`, `BufferedEnvelope`), `builder.ToTileGeometry(g)` zeigt, was aus einer Weltgeometrie in der Kachel wird, und `feature.ToGeometry(factory, builder.Transform)` rechnet ein gelesenes Feature zurück in Weltkoordinaten.

Das Runden auf ganze Zahlen kann ein gültiges Polygon an Details unter einer Kacheleinheit sich selbst berühren oder kreuzen lassen. Das ist der übliche Kompromiss des Formats, Renderer kommen damit zurecht; eine Kachel ist ein Bild, kein Datenaustausch.

## Attribute und Werte

Attribute sind Paare `(Schlüssel, Wert)`; `null`-Werte werden übersprungen (MVT kennt kein null), ein wiederholter Schlüssel behält seinen ersten Wert. `MvtValue.From` bildet CLR-Werte auf die Protobuf-Felder der `Value`-Nachricht ab: `string`, `bool`, `float`, `double` (und `decimal`), vorzeichenlose Ganzzahlen auf `uint_value`, vorzeichenbehaftete auf `uint_value` bei ≥ 0 und auf `sint_value` bei negativen, alles andere als Zeichenkette in der invarianten Kultur. `MvtValue.Int(…)` und die anderen Fabrikmethoden wählen ein Feld ausdrücklich. Ein gelesener Wert behält sein Feld, Lesen und erneutes Schreiben ergibt also dieselben Bytes. Schlüssel und Werte werden je Layer in der Reihenfolge der ersten Verwendung dedupliziert.

## Unterhalb des Builders

`Mvt.Encode` und `Mvt.Decode` arbeiten direkt auf `MvtTile`, `MvtLayer` und `MvtFeature`; ein Feature trägt seine Geometrie als die Befehls-Ganzzahlen der Spezifikation (`MvtGeometry.Encode`, `Decode`, `Command`, `ZigZag`). `MvtFeature.Create` baut ein Feature aus einer Geometrie, die schon in Kachelkoordinaten vorliegt. Der Decoder akzeptiert jede Feldreihenfolge, gepackte und ungepackte wiederholte Felder, überspringt unbekannte Felder und liest eine Kachel der Version 1, deren erster Ring ein innerer ist, als äußeren Ring. `Mvt.Gzip`, `Mvt.Gunzip` und `Mvt.IsGzip` sind die gzip-Helfer.

Der Writer verwendet die Feldreihenfolge der verbreiteten Referenz-Encoder (Layer: version, name, features, keys, values, extent; Feature: id, tags, type, geometry). Die Tests bilden die Erwartungen der [mvt-fixtures](https://github.com/mapbox/mvt-fixtures) und die Beispiele der Spezifikation Byte für Byte nach.
