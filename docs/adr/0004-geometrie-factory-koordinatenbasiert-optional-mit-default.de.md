# Geometrie-Factory: koordinatenbasiert, optionaler Parameter mit globalem Default

Algorithmen erzeugen Ergebnisse über `IGeometryFactory`. Jeder Algorithmus nimmt sie als **optionalen Parameter** (`IGeometryFactory? factory = null`). Bei `null` greift er auf das statische `SpatialNET.GeometryFactory.Default` in Core zurück. Ist auch das nicht gesetzt, wirft er eine `InvalidOperationException` mit klarer Meldung. Es gibt bewusst keinen impliziten Default: Extensions hängt nur von Core ab, und ein stiller Fallback auf die Referenz-Implementierung würde fremde Objekte in einen Host schieben. Die Factory bekommt **nur Koordinaten** (`readonly struct Coordinate(X, Y, Z = NaN)`), keine Punkt-Objekte. So können keine Objekte verschiedener Hosts oder Implementierungen gemischt werden, und unveränderte Eingabepunkte müssen nicht neu erzeugt werden.

*English version: [0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md](0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md)*

## Consequences

- Vertrag: `CreatePoint(Coordinate, source)`, `CreateMultiPoint(ReadOnlySpan<Coordinate>, source)`, `CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>>, source)`, `CreatePolygon(ReadOnlySpan<PolygonPart>, source)` und `bool SupportsZ`. `PolygonPart` ist eine Teilfläche (Außenring plus Löcher, gültig, geschlossen, außen gegen den Uhrzeigersinn, ADR 0003). Rückgabe sind die SpatialNET-Interfaces, Host-Wrapper casten auf ihre Typen.
- Factories sind thread-sicher bzw. zustandslos und dürfen übergebene Puffer nach dem Aufruf nicht mehr referenzieren.
- `source` ist die primäre Eingabe-Geometrie (oder `null`). Darüber übernimmt der Host Metadaten wie die Spatial Reference, ohne dass SpatialNET sie kennt.
- Z: Bei `SupportsZ == false` rechnet der Algorithmus rein 2D. Sonst liefert er Z, wenn die Eingabepunkte `IPoint3D` sind, und die Factory darf es verwerfen. Das ist nötig, weil gView/WebGIS immer `IPoint3D` liefern.
- Kein `CreateGeometryCollection`: Ergebnisse sind dimensionshomogen. Leere Ergebnisse sind leere Geometrien (0 Teilflächen/Pfade/Punkte), nie `null`.
- `Default` ist ein einfaches statisches Property, einmal beim Start gesetzt. Tests übergeben die Factory explizit. Ein `AsyncLocal`-Scope lässt sich später ohne Bruch nachrüsten.

## Considered Options

- Nur globale Registrierung (verworfen: Factory pro Aufruf nicht wählbar)
- Factory über die Eingabe-Geometrie (verworfen: neuer Interface-Member, widerspricht ADR 0002)
- Objekt-hierarchische Factory `CreatePath(IReadOnlyList<IPoint2D>)` (verworfen: Mischobjekte, eine Allokation je Vertex) und Builder (verworfen: größerer, zustandsbehafteter Vertrag)
- Generische Factory `IGeometryFactory<TPoint, …>` (verworfen: vier Typparameter an jedem Algorithmus)
