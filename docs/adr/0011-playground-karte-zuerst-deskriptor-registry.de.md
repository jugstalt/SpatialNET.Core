# Playground: „Karte zuerst“, Minimal API ohne Frontend-Build, UI aus expliziten Algorithmus-Deskriptoren

Der Playground ist eine ASP.NET Core Minimal API (net10.0) mit einer einzigen statischen Seite in reinem JavaScript (ES-Module, kein npm/Build). Leaflet 1.9 und Leaflet-Geoman free liegen lokal in `wwwroot/lib`. Die UI folgt der Prototyp-Variante **„Karte zuerst“**: Vollbild-Karte, Befehlspalette (Strg+K) mit den Parametern des gewählten Algorithmus darunter, Chips für Rechen-KBS, Layer und Kennzahlen, unten eine Leiste mit Vorschaukarten der Beispielgeometrien. Die UI kennt keinen Algorithmus fest, sondern baut sich aus `GET /api/algorithms`. Jeder Algorithmus hat dafür einen **expliziten Deskriptor im Playground** (`IPlaygroundAlgorithm`: Id, Name, Gruppe, Eingaben mit erlaubten Typen, Parameter mit Typ, Default, Einheit `kbs` und deutschem Label, `Run(...)`), gefunden per Assembly-Scan. Reflection über die Extension-Methoden mit Attributen in SpatialNET.Extensions wurde verworfen, weil sie Playground-Belange in die Bibliothek trägt.

*English version: [0011-playground-karte-zuerst-deskriptor-registry.md](0011-playground-karte-zuerst-deskriptor-registry.md)*

## Consequences

- **Vollständigkeitstest:** Jede öffentliche Methode von SpatialNET.Extensions muss einen Playground-Deskriptor haben (analog zum Reflection-Test aus ADR 0008).
- **`GET /api/fixtures?algorithm=`** liest `samples/` (ins Ausgabeverzeichnis kopiert) und wandelt WKT mit SpatialNET.IO in GeoJSON. Fixtures mit `crs` werden nach WGS84 projiziert, lokal planare kommen roh mit Kennzeichen `local` (ADR 0010). Standardmäßig erscheinen nur Fixtures mit Tag `playground`, ein Schalter zeigt alle.
- **`POST /api/run`** rechnet automatisch bei jeder Änderung (300 ms Entprellung). Die Antwort enthält das Ergebnis als GeoJSON (WGS84 bzw. lokal), das Rechen-KBS, Kennzahlen (Fläche, Länge, Vertices, Teile) und Warnungen. Listen-Ergebnisse wie bei Cut werden pro Teil eingefärbt und nummeriert. Fehler erscheinen als Chip, das letzte Ergebnis bleibt stehen.
- Eingaben entstehen durch Zeichnen mit Geoman (Ziel A/B wählbar), GeoJSON-Upload (`GeoJson.ReadAll`, ADR 0009) oder Beispielgeometrien.
- Der Eintrag `node_modules/` in `.gitignore` entfällt.

## Considered Options

- Varianten „Werkbank“ (3 Spalten) und „Katalog“ (Fixture-Tabelle mit Text-Editor) – Prototyp auf Branch `prototype/playground-ux`
- Frontend mit npm/Vite-Build, Blazor (verworfen: Werkzeugballast für eine Seite)
- Reflection plus Attribute in SpatialNET.Extensions (verworfen: Playground-Belange in der Bibliothek)
