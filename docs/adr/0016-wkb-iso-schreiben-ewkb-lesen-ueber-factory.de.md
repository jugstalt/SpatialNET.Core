# WKB: ISO und EWKB lesen, ISO schreiben, über die Factory

`SpatialNET.IO` bekommt **WKB** (`Wkb` neben `Wkt` und `GeoJson`), weil Hosts mit Datenbank (PostGIS, SQL Server, GeoPackage-Blobs) Geometrien in diesem Format austauschen und gView bereits einen Reader hat. Es folgt dem Muster aus ADR 0009: Beim Lesen entstehen alle Geometrien über die Geometrie-Factory, es gibt kein Feature-Modell, und ein KBS wird nicht ausgewertet. Das Lesen akzeptiert **ISO-WKB und EWKB** (die PostGIS-Variante mit Flags), das Schreiben erzeugt **ISO-WKB** in Little Endian. Z und M werden unterstützt (ADR 0015). Das hebt den Ausschluss von WKB aus ADR 0009 auf; der Rest dieses ADR gilt weiter.

*English version: [0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.md](0016-wkb-iso-schreiben-ewkb-lesen-ueber-factory.md)*

## Consequences

- **Ort:** `SpatialNET.IO`, Namespace `SpatialNET.IO`. Kein neues Paket, keine neue Abhängigkeit (`System.Buffers.Binary` gehört zum Framework). `SpatialNET.Core` bleibt reine Verträge. Für die Package Validation ist das nur eine Ergänzung.
- **API** (dieselbe Form wie `Wkt`, ADR 0009):
  - `Wkb.Read(ReadOnlySpan<byte>, IGeometryFactory? factory = null)` liefert `IGeometry2D`, `Wkb.Read<T>(…)` erwartet eine Art, `Wkb.ReadAll(…)` liefert `IReadOnlyList<IGeometry2D>`.
  - `ToWkb(this IGeometry2D, WkbOptions? options = null)` liefert `byte[]`. `WkbOptions` hat `ZMode` und `MMode` mit derselben Bedeutung wie in `WktOptions`.
  - Keine Stream-, Hex- oder Text-Überladungen. Wer einen `Stream` oder Hex-String hat, wandelt ihn vorher um; das hält die Oberfläche klein und lässt sich ohne Bruch erweitern.
- **Lesen:**
  - **Formate:** ISO-WKB (Typcodes 1–6 sowie 1000/2000/3000 für Z/M/ZM) und EWKB (Flag-Bits `0x80000000` Z, `0x40000000` M, `0x20000000` SRID). Beide Byte-Reihenfolgen. Sie wird je Geometrie-Header gelesen, auch verschachtelte Teile einer Multi-Geometrie dürfen verschieden sein.
  - **Gemischte Codes** werden akzeptiert: ISO-Dimensionscodes zusammen mit dem SRID-Flag, wie NTS sie schreibt.
  - **Typen:** Point, LineString, Polygon, MultiPoint, MultiLineString und MultiPolygon werden wie bei WKT abgebildet (ADR 0009): Punkt auf `IPoint2D`, ein Punkt mit NaN-Koordinaten (die WKB-Form von `POINT EMPTY`) und MultiPoint auf `IMultiPoint2D`, Linien auf `IPolyline2D`, Flächen auf `IPolygon2D`.
  - **GeometryCollection** wird von `Read` mit `NotSupportedException` abgelehnt und von `ReadAll` flachgeklopft, genau wie bei WKT und GeoJSON. Eine Geometry-Collection gibt es im Modell nicht (ADR 0002).
  - **Kurven** (CircularString, CompoundCurve, CurvePolygon, MultiCurve, MultiSurface), andere Flächenarten, TIN und Dreiecke werfen `NotSupportedException`. Die Hosts linearisieren Kurven selbst (Karte #1), und der Typcode steht in der Meldung.
  - **SRID** in einem EWKB-Header wird gelesen und **verworfen**, dieselbe Linie wie beim KBS in GeoJSON.
  - **Z und M** bleiben erhalten, wenn die Factory sie unterstützt (ADR 0015), sonst werden sie verworfen. Eine Koordinate, die in einem Z- oder M-Slot `NaN` ist, heißt „keins“.
  - **Ungültige Eingabe stürzt nie ab:** abgeschnittene Daten, eine unbekannte Byte-Reihenfolge oder ein unbekannter Typ und überzählige Bytes hinter der Geometrie werfen `FormatException` mit dem Byte-Offset. Eine Anzahl (Punkte, Ringe, Teile), die nicht in die restlichen Bytes passt, wird abgelehnt, bevor etwas angelegt wird; ein gefälschter Header kann so keinen Speicherfehler auslösen. Geometrisch ungültige, aber syntaktisch korrekte Eingabe geht unverändert an die Factory, wie bei WKT.
- **Schreiben:**
  - ISO-WKB, Little Endian, schlankster Typ wie bei WKT: ein Polygon aus einer Teilfläche ist ein Polygon, mehrere Teilflächen ein MultiPolygon, ein Pfad ein LineString, mehrere ein MultiLineString, ein Multipoint bleibt MultiPoint. Ein einzelner Punkt ist ein Point; ein leerer Multipoint ist ein MultiPoint mit null Punkten, der Writer muss den NaN-Punkt also nie erzeugen.
  - Ringe sind geschlossen, Außenringe gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn, entartete Ringe entfallen (dieselbe Normalisierung wie beim WKT-Writer, damit beide die Struktur des Ergebnisses gleich festlegen).
  - Z folgt `ZMode` (`Auto` schreibt Z nur, wenn ein Punkt eine Höhe ungleich 0 hat, weil Hosts wie gView immer `Z = 0` liefern), M folgt `MMode` (geschrieben, wenn ein Punkt M hat; Punkte ohne M bekommen NaN).
  - Nicht endliches X oder Y wirft `ArgumentException`, wie im GeoJSON-Writer, weil die meisten Abnehmer es nicht zurücklesen können und ein NaN-Paar mit dem Marker des leeren Punkts kollidieren würde.
  - Es wird keine SRID geschrieben. Wer EWKB oder einen GeoPackage-Header braucht, baut ihn um die ISO-Bytes.
- **Herkunft des Codes:** Der gView-Reader (`gView.Framework.OGC`, gleicher Inhaber, Apache-2.0) ist der Ausgangspunkt für die Formatbehandlung, der Code wird aber gegen `Coordinate` und die Factory neu geschrieben, damit kein gView-Typ in die Bibliothek gelangt. Seine Kurven- und SRID-Behandlung wird durch die Regeln oben ersetzt. Ein Eintrag in `THIRD-PARTY-NOTICES.md` ist nicht nötig, weil es derselbe Rechteinhaber ist.
- **Tests (ADR 0008):** Fixtures passen nicht, weil das Ergebnis Bytes sind und keine Geometrie aus einem Algorithmus.
  - Orakel: NTS `WKBReader` und `WKBWriter`, in beide Richtungen. Was wir schreiben, liest NTS zu denselben Koordinaten, und was NTS schreibt (auch EWKB mit SRID, Big Endian, Z und M), lesen wir. Z und M werden über die Ordinaten verglichen, die NTS meldet.
  - Property-Tests (CsCheck): Round-Trip `ToWkb` → `Read` über zufällige Geometrien mit Z, M und leeren Teilen, für die Referenz-Implementierung und die Fremd-Implementierung; WKB stimmt mit WKT in der normalisierten Struktur überein.
  - Handgeschriebene Fälle: beide Byte-Reihenfolgen, verschachtelte Byte-Reihenfolgen, EWKB-Flags mit und ohne SRID, der NaN-Punkt, jeder abgelehnte Typ, abgeschnittene und gefälschte Eingabe (Anzahlen, Offsets), M gleich 0 ist ein Messwert.
  - Die `Covers`-Regel aus ADR 0008 ist erfüllt, indem diese Tests `Wkb.Read`, `Wkb.ReadAll` und `ToWkb` nennen.
- **Playground:** unverändert; WKB ist dort kein Eingabeformat.

## Considered Options

- WKB draußen lassen, Hosts nutzen eigene Reader (verworfen: jeder Host mit Datenbank braucht einen, und der von gView existiert; das IO-Paket ist der Ort für Formate, ADR 0009)
- EWKB schreiben (verworfen: ISO ist der Standard, und PostGIS liest ihn; EWKB mit SRID würde ein KBS einführen, das IO bewusst nicht trägt)
- Nur ISO lesen (verworfen: PostGIS `ST_AsEWKB` und viele Bibliotheken erzeugen weiter EWKB)
- Die SRID behalten (verworfen: braucht einen Platz im Modell; das `crs` aus GeoJSON wird aus demselben Grund verworfen)
- Kurven beim Lesen in Linien umwandeln (verworfen für jetzt: braucht einen Toleranzparameter und eine eigene Entscheidung; die Hosts linearisieren bereits)
- Stream-, Hex- und nur-`byte[]`-Überladungen von Anfang an (verworfen: größere Oberfläche für Bequemlichkeit, die ein Aufrufer in einer Zeile ergänzt; lässt sich später ohne Bruch hinzufügen)
- Den gView-Code unverändert übernehmen (verworfen: er erzeugt gView-Typen und kennt keine Factory)
- Ein eigenes Paket `SpatialNET.IO.Wkb` (verworfen: keine Abhängigkeit rechtfertigt es; ADR 0014 hat dasselbe für den Index begründet)
