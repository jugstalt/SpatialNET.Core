# SpatialNET.IO: WKT und GeoJSON über die Factory, nur Geometrien

SpatialNET.IO liest und schreibt **WKT** und **GeoJSON** und erzeugt beim Lesen alle Geometrien über die Geometrie-Factory (optionaler Parameter, Fallback `GeometryFactory.Default`, ADR 0004). Die Abnehmer sind Fixtures und Orakel (ADR 0008), der Playground (Upload, Leaflet) und Hosts ohne eigene Geometrien. gView und WebGIS haben eigene GeoJSON-Unterstützung. Deshalb bleibt IO bewusst schmal: kein WKB, kein Feature-Modell (GeoJSON-`properties` werden ignoriert), keine CRS-Auswertung. IO hängt von Core **und Extensions** ab, damit der Writer die Gruppierung in Teilflächen und die Orientierung nicht nachbauen muss. JSON läuft über System.Text.Json, weitere Abhängigkeiten gibt es nicht.

*English version: [0009-io-wkt-geojson-ueber-factory.md](0009-io-wkt-geojson-ueber-factory.md)*

## Consequences

- **Typabbildung:** POINT → `IPoint2D`, MULTIPOINT → `IMultiPoint2D`, (MULTI)LINESTRING → `IPolyline2D`, (MULTI)POLYGON → `IPolygon2D` mit Teilflächen. Geschrieben wird jeweils der schlankste Typ, z. B. POLYGON bei einer Teilfläche.
- **GeometryCollection:** Beim Lesen einer Einzelgeometrie gibt es eine `NotSupportedException`. In `ReadAll` bzw. bei `FeatureCollection` wird sie flachgeklopft. `POINT EMPTY` wird als leerer `IMultiPoint2D` gelesen und als `MULTIPOINT EMPTY` geschrieben.
- **Z/M/CRS:** Z wird gelesen. M wird still verworfen *(teilweise abgelöst durch [ADR 0015](0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md): WKT liest und schreibt künftig M)*. Ein CRS (auch das GeoJSON-`crs`-Member von 2008) wird ignoriert.
- **Schreiben von Z** über `ZMode`:
  - `Auto` (Standard): Z wird geschrieben, wenn die Punkte `IPoint3D` sind und mindestens ein Z ≠ 0 und nicht NaN ist. Das ist nötig, weil die Hosts immer `IPoint3D` mit Z = 0 liefern.
  - `Always` / `Never`.
- **Orientierung:** Beide Writer normalisieren, Außenring gegen den Uhrzeigersinn, Loch im Uhrzeigersinn (RFC 7946, ADR 0003).
- **API:**
  - **WKT:** `Wkt.Read`, `Wkt.Read<T>`, `ToWkt(WktOptions?)`
  - **GeoJSON:** `GeoJson.Read`/`ReadAll` (string, `ReadOnlySpan<byte>`, `Stream`), `ToGeoJson(options?)`, `WriteTo(Utf8JsonWriter)`
  - **Zahlenformat:** invariant und Round-Trip-genau. `Read(ToWkt(g))` liefert bitgenau dieselben Koordinaten.
- **Strenge:**
  - Syntaxfehler → `FormatException` mit Position bzw. JSON-Pfad
  - Geometrisch ungültige, aber syntaktisch korrekte Eingaben werden akzeptiert (ADR 0002/0003)
  - `EMPTY` wird zu einer leeren Geometrie

## Considered Options

- WKB in v1 (verschoben: kein Abnehmer, lässt sich ohne Bruch nachrüsten; geplant nach den M-Werten, siehe [ADR 0015](0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md))
- Feature-Modell mit Properties (verworfen: neues Domänenkonzept, gehört zu den Hosts)
- IO nur auf Core, Hole-Gruppierung nachgebaut (verworfen: doppelte Logik)
