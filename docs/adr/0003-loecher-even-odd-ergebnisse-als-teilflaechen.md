# Löcher: Even-Odd in der Eingabe, Teilflächen im Ergebnis

Ein `IPolygon2D` ist eine flache Ringliste ohne Loch-Marker (ADR 0002). Seine Fläche ist nach der **Even-Odd-Regel** über alle Ringe definiert, die Ring-Orientierung wird bei der Eingabe ignoriert. Für gültige Polygone entspricht das der Verschachtelungstiefe (gerade = Außenring, ungerade = Loch) und damit genau dem `VerifyHoles()` von gView und WebGIS. Beide Hosts ignorieren die Orientierung. Eine Orientierungsregel wie bei OGC/GeoJSON hätte ihren Daten widersprochen.

## Consequences

- **Gültigkeit** folgt der OGC-Regel, wie NTS sie prüft: Ringe kreuzen sich nicht. Ein Loch darf den Außenring oder ein anderes Loch in genau einem Punkt berühren, ein Ring sich selbst nicht. Degenerierte Ringe (weniger als 3 verschiedene Punkte, Fläche 0) werden ignoriert und von `IsValid()` gemeldet.
- **Maße und Prädikate** (Area, Centroid, Contains …) setzen gültige Eingaben voraus, bei ungültiger Eingabe ist das Ergebnis undefiniert. **Overlay-Operationen** (Union, Intersection, Buffer …) werten die Even-Odd-Füllregel ohnehin aus und liefern auch für sich kreuzende Ringe ein definiertes Ergebnis. Eine Union mit Even-Odd ersetzt damit das ausgeklammerte MakeValid für diesen Fall.
- **Ergebnisse** sind immer gültig und geschlossen. Außenringe laufen gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn (RFC 7946). Sie gehen strukturiert als **Teilflächen** (Außenring plus Löcher) an die Geometrie-Factory (#4), damit die Hosts kein `VerifyHoles()` nachrechnen müssen. Die Reihenfolge der Teilflächen und die Startpunkte der Ringe sind nicht festgelegt, Ergebnisse sind aber deterministisch. Tests vergleichen normalisiert.
- Teilweise überlappende Außenringe sind ungültig: Even-Odd macht die Überlappung zum Loch, die Hosts würden die Fläche dagegen doppelt zählen.

## Considered Options

- Marker-Interface `IHole2D` (verworfen in ADR 0002)
- Orientierung als Loch-Kennzeichen (verworfen: beide Hosts ignorieren sie)
- Reine Verschachtelung statt Even-Odd (gleichwertig für gültige Eingaben, aber für Kreuzungen undefiniert)
