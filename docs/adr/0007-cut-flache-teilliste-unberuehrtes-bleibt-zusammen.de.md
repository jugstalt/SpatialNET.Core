# Cut: flache Liste der Teile, Unberührtes bleibt zusammen und ungerundet

`Cut(this IPolygon2D, IPolyline2D cutter, factory?, gridSize?)` liefert `IReadOnlyList<IPolygon2D>`, `Cut(this IPolyline2D, IPolyline2D cutter, …)` liefert `IReadOnlyList<IPolyline2D>`, also eine flache Liste ohne links/rechts-Zuordnung, wie die bestehenden `Cut`-Funktionen in gView und WebGIS. Das Editier-Werkzeug (WebGIS `CutFeature`) macht aus jedem Element ein neues Feature. Deshalb gilt: Jedes zerschnittene Stück ist ein eigenes Element, aber alle **unberührten** Teilflächen bzw. Pfade bleiben **zusammen ein Element** und behalten ihre **Originalkoordinaten**, ohne Snap-Rounding. Ein Multipart-Feature zerfällt also nicht nebenbei, und was der Schnitt nicht trifft, bleibt unverändert. Wird nichts geschnitten, enthält die Liste genau ein Element.

*English version: [0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.md](0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.md)*

## Consequences

- **Polygon-Semantik:** Die Teile sind die Maschen aus dem genodeten Polygonrand plus den Teilen der Schnittlinie im Inneren (ADR 0005). Lose Enden, Verläufe entlang des Randes und Berührungen an Vertices schneiden nicht. Teile der Schnittlinie in Löchern zählen nicht, und die Löcher werden passend auf die Teile verteilt.
- **Linien-Semantik:** Die Linie wird an jedem Kontakt mit der Schnittlinie zerlegt: Kreuzung, Berührung in einem Punkt sowie Anfang und Ende einer kollinearen Überlappung. Jedes Teil ist eine Polylinie mit genau einem Pfad.
- SpatialNET verlängert die Schnittlinie **nicht** implizit. Eine Fangtoleranz, wie WebGIS sie mit `Extend(tolerance)` umsetzt, bleibt Sache des Werkzeugs.
- Alle Pfade der Schnittlinie schneiden, mehrere Schnitte in einem Aufruf sind also möglich. Es gibt keine Optionen-Klasse. Die Reihenfolge der Elemente ist nicht festgelegt (ADR 0003), das Ergebnis aber deterministisch.
- **Test-Orakel:** NTS `Polygonize(Union(Rand, Schnittlinie))`, gefiltert auf Teile im Inneren.

## Considered Options

- Links/rechts-Ergebnis wie ESRI (verworfen: bei Zickzack-Schnitten nicht eindeutig)
- Jede Teilfläche als eigenes Element, auch unberührte (verworfen: zerlegt Multipart-Features, die der Nutzer nicht schneiden wollte)
- Schnittlinie bis zum Rand verlängern (verworfen: nicht deterministisch, gehört ins UI)
