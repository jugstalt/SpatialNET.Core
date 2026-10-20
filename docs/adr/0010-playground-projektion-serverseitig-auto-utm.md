# Playground-Projektion: serverseitig mit Proj4Net, Standard Auto-UTM, Distanzen in KBS-Einheiten

Der Playground projiziert **auf dem Server** mit Proj4Net.Core. Der Client schickt und empfängt nur GeoJSON in WGS84 für Leaflet. Gerechnet wird in einem wählbaren Rechen-KBS, Standard ist **Auto-UTM**: WGS84/UTM, wobei die Zone pro Berechnung aus dem Envelope-Mittelpunkt aller Eingaben bestimmt wird. Distanz-Parameter (Buffer, Offset, Toleranzen) stehen **in den Einheiten des Rechen-KBS** und werden nicht umgerechnet. Der Playground zeigt damit genau, was SpatialNET tut, nämlich planar in Karteneinheiten zu rechnen, und das Standard-KBS liefert trotzdem sofort korrekte Meter. EPSG:3857 als fester Standard wurde verworfen, weil ein „Meter“ dort z. B. in Mitteleuropa etwa 1,5 echten Metern entspricht.

## Consequences

- **Rechen-KBS:** Auswahl aus Auto-UTM, EPSG:3857, gängigen Landes-KBS (z. B. 25832/25833, 31256) oder einem freien EPSG-Code. Die gewählte bzw. automatisch bestimmte Zone wird angezeigt, sodass ein Fall reproduzierbar ist. Die UI zeigt die Einheit des KBS an. Bei EPSG:3857 kommt ein Hinweis auf die Maßstabsverzerrung am Ort dazu.
- **Fixtures mit `crs`:** Ihr `crs` ist das Rechen-KBS, die Auswahl ist dann gesperrt. Gezeichnete oder hochgeladene Geometrien werden in dieses KBS projiziert. Fixtures ohne `crs` werden ohne Projektion in lokalen Koordinaten mit `L.CRS.Simple` gezeigt und bearbeitet (ADR 0008).
- **Zurückprojizieren:** Nur die Vertices werden transformiert, ohne Verdichtung. Angezeigt werden genau die Ergebnis-Vertices.
- **Code-Grenze:** Proj4Net.Core ist eine Abhängigkeit **nur** von `SpatialNET.Playground`. Die Projektion läuft über eine kleine Playground-Funktion (`Project(IGeometry2D, transform, factory)`) mit der Factory der Referenz-Implementierung. Die Bibliothekspakete bekommen keine Projektions-API.

## Considered Options

- Fester Standard EPSG:3857 (verworfen: nicht längentreu)
- Projektion im Client mit proj4js (verworfen: zweite Projektionsbibliothek)
- Distanzen immer in Metern mit Umrechnung (verworfen: verschleiert planares Rechnen, bei 3857 nur näherungsweise)
- Zone pro Sitzung fixieren (verworfen: Ergebnis hinge vom Kartenausschnitt ab)
