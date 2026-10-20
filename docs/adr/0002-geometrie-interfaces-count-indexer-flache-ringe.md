# Geometrie-Interfaces: Count + Indexer, flache Ringliste, Z nur am Punkt

Die Geometrie-Interfaces in SpatialNET.Core sind lesend und minimal: `IGeometry2D` (leerer Basistyp), `IPoint2D` (X, Y), `IPoint3D : IPoint2D` (Z), `IPointCollection2D` (`PointCount` + Indexer) als Basis von `IPath2D`, `IRing2D : IPath2D` und `IMultiPoint2D`, dazu `IPolyline2D` (`PathCount` + Indexer) und `IPolygon2D` (`RingCount` + Indexer über eine flache Ringliste). Envelope ist kein Interface, sondern der Wert-Typ `Envelope2D`. Punktfolgen sind bewusst **nicht** `IReadOnlyList<T>`: gViews `IPointCollection`/`IPolyline`/`IPolygon` sind bereits `IEnumerable<IPoint/IPath/IRing>`. Ein zweites `IEnumerable<T>` macht LINQ auf allen gView-Geometrien mehrdeutig (CS0411), auch in gViews bestehendem Code. Die Namen `PointCount`/`PathCount`/`RingCount` werden dagegen von beiden Hosts schon implizit erfüllt. Für LINQ/`foreach` liefert SpatialNET.Extensions eine `IReadOnlyList`-Sicht (`.AsList()`). Ein Host darf `IReadOnlyList<T>` zusätzlich selbst implementieren und wird dann direkt durchgereicht.

## Consequences

- Die Anprobe an die Hosts ist klein. **gView** hängt die Interfaces an seine eigenen Interfaces (`IPoint : IPoint3D` …) und setzt die drei Indexer mit abweichendem Rückgabetyp als Default-Interface-Member dort um. Die Klassen bleiben unverändert. **WebGIS** hängt die Interfaces an die Klassen und braucht drei explizite Indexer (`PointCollection`, `Polyline`, `Polygon`).
- 3D gibt es nur am Punkt (keine `IPath3D`/`IPolyline3D`/`IPolygon3D`). Ein Algorithmus interpoliert Z, wenn die beteiligten Punkte `IPoint3D` sind. Beide Hosts liefern immer `IPoint3D`, auch mit Z = 0.
- `IPolyline2D` ist zugleich Multi-Linie und `IPolygon2D` zugleich Multi-Polygon. Als eigenen Multi-Typ gibt es nur `IMultiPoint2D`, keine GeometryCollection in v1. Pfad und Ring sind Teile, keine Geometrien.
- Ob ein Ring Außenring oder Loch ist, steht nicht im Interface (kein `IHole2D`). Algorithmen ermitteln es selbst, die Regeln dafür regelt #3. Ein Ring darf seinen Schlusspunkt wiederholen oder weglassen.
- Weitere Member (Envelope, IsEmpty, Length, Area …) gehören nicht in die Interfaces. Sie kollidieren in den Hosts mit gleichnamigen Membern anderer Form (z. B. gView `IsEmpty()` vs. WebGIS `IsEmpty`). Sie kommen als Extensions. Algorithmen sind Extension-*Methoden*: Eine C#-14-Extension-*Property* würde auf Host-Typen von einer gleichnamigen Host-Property (z. B. `Area`) still überdeckt. Extension-Properties sind deshalb nur für Komfort-Member ohne solche Namensgleichheit gedacht. Extension-Methoden wie `Area()` kollidieren dagegen nicht mit Host-Properties.

## Considered Options

- `IReadOnlyList<T>` als Pflicht (verworfen: CS0411 auf allen gView-Geometrien, zusätzlich `Count` neben `PointCount`)
- Parallele 3D-Hierarchie (verworfen: verdoppelt Interfaces und Overloads)
- `IHole2D`-Marker oder strukturiertes Außenring→Löcher-Modell (verworfen: setzt Host-Garantien voraus bzw. passt zu keinem Host)
- `IEnvelope2D` als Interface (verworfen: Ergebnis-Envelopes bräuchten die Factory)

Prototyp: Branch `prototype/interface-form`.
