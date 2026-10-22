# Teststrategie: gemeinsame WKT-Fixtures, NTS-Orakel zur Laufzeit, zwei Implementierungen

Testfälle liegen als **eine JSON-Datei pro Fall** unter `samples/<algorithmus>/`. Darin stehen Name, Beschreibung, `tags`, die Eingaben `a`/`b` als **WKT**, `params`, optional `crs`, `expected` (mit `divergence`-Begründung) und `tolerance`. Tests **und** Playground lesen dieselben Dateien, Beispielgeometrien sind die Fälle mit Tag `playground`. Das Soll berechnet **NTS zur Laufzeit** (OverlayNG mit gleichem `PrecisionModel` im Strict-Mode, `BufferOp`, `OffsetCurve`, `Polygonize` …). Gespeicherte Soll-Werte gibt es nur für bewusste, begründete Abweichungen. So fällt jede NTS-Änderung sofort auf, es veralten keine Snapshots, und jede Abweichung ist im Fixture dokumentiert.

*English version: [0008-teststrategie-fixtures-nts-orakel.md](0008-teststrategie-fixtures-nts-orakel.md)*

## Consequences

- **Struktur je Algorithmus** in `SpatialNET.Tests/<Algorithmus>/`:
  - `…FixtureTests`: `[Theory]` über alle Fixtures gegen das Orakel
  - `…ContractTests`: leere Eingaben, fehlender `GeometryFactory.Default`, `SupportsZ`, Z, ungültige Optionen, bitgenau unberührte Teile bei Cut
  - `…PropertyTests`: Invarianten mit **CsCheck**
  - **Stresstests** (`tests/SpatialNET.Tests/Stress`, Kategorie `Stress`, `tools/stress.ps1`, wöchentlicher Workflow `stress-tests.yml`): viele zufällige, absichtlich schmutzige Eingaben je Algorithmus (Werte weit unter dem Gitter, Zehntel mit winzigem Fehler, Selbstschnitte), geprüft gegen Invarianten (gültige Ergebnisse, Flächenidentitäten der Overlays, `Locate` der vorbereiteten und der unvorbereiteten Geometrie) und, wo NTS dasselbe meint, gegen NTS. Sie laufen nur mit `SPATIALNET_STRESS=1` und brauchen Minuten. NTS ist für Abstände unterhalb seiner Gleitkomma-Auflösung kein Orakel (es gibt mit Topologie-Exceptions auf oder polygonisiert Splitter falsch), deshalb beschränken diese Tests ihre Werte; die Invarianten-Tests behalten die schmutzigen Werte. Ein fehlschlagender Fall gibt einen CsCheck-Seed aus; der kleinste Fall wird ein gewöhnlicher Regressionstest.
- **Factory-Vertragstest:** Thread-Sicherheit, keine Referenz auf übergebene Puffer, leere Geometrien, `SupportsZ`. Tests übergeben die Factory immer explizit.
- **Toleranzen** zentral in `OracleAssert`:
  - Fläche und Länge relativ 1e-9, symmetrische Differenz ≤ 1e-9·Fläche(Soll)
  - Hausdorff-Distanz ≤ 2·gridSize·√2
  - Topologie-Kennzahlen, Validität und Prädikate exakt
  - Buffer-Fläche relativ 1e-6
  - Ein Fixture darf einzelne Werte überschreiben. Verglichen wird nie über Koordinatengleichheit.
- **Zwei Implementierungen:** Jeder Fixture-Test läuft gegen die Referenz-Implementierung **und** gegen eine absichtlich unbequeme Fremd-Implementierung im Testprojekt. Diese hat klassenbasierte Indexer, Ringe ohne Schlusspunkt, Außenringe im Uhrzeigersinn, zusätzlich `IReadOnlyList` und immer `IPoint3D` mit Z = 0. Damit fallen Annahmen über konkrete Typen auf.
- **Zweiter Referenzpfad für Buffer:** eine Kapsel-Union, nur im Testprojekt.
- **Fremde Korpora** (JTS-XML-Suites, EDL 1.0; Fixtures von polygon-clipping, MIT) werden einmalig per Konverter-Tool nach `samples/external/<quelle>/` übernommen, mit LICENSE und Herkunftsvermerk.
- **Vollständigkeit:** Ein Reflection-Test prüft, dass jede öffentliche Methode von SpatialNET.Extensions Fixtures oder `[Covers]`-Tests hat und den **Pflicht-Tag-Katalog** abdeckt: `empty`, `degenerate-ring`, `collinear-overlap`, `vertex-touch`, `hole-touch`, `self-crossing`, `near-coincident`, `large-coords`, `z`, `multi`, `nested`. Nicht anwendbare Tags werden mit Begründung als `n/a` markiert. Eine Coverage-Schwelle folgt mit CI.
- **CRS:** Fixtures ohne `crs` gelten als lokal planar, der Playground zeigt sie mit `L.CRS.Simple`.

## Considered Options

- GeoJSON-Fixtures (verworfen: RFC 7946 verlangt WGS84, projizierte Koordinaten passen nicht, Korpora sind WKT)
- Golden Files bzw. Snapshots der NTS-Ergebnisse (verworfen: veralten still)
- FsCheck (verworfen zugunsten von CsCheck: C#-nativ, ohne F#-Abhängigkeit)
