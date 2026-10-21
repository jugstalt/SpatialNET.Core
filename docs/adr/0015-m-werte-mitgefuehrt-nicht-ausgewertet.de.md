# M-Werte: wie Z mitgeführt und interpoliert, von den Geometrie-Algorithmen nie ausgewertet

SpatialNET bekommt **M-Werte** (ein Messwert an einem Stützpunkt, zum Beispiel die Strecke entlang einer Straße). M wird **mitgeführt, nicht gerechnet**: Alle Algorithmen bleiben planar 2D, und M beeinflusst nie das X/Y (auch nicht das Z) eines Ergebnisses. Wo ein Algorithmus einen Stützpunkt erzeugt, wird M so interpoliert, wie es bei Z schon geschieht. Das kehrt den Ausschluss von M aus der Charta (Karte #1, ADR 0009) um, weil die Hosts (gView) M speichern, der WKB-Austausch es braucht und Linear Referencing (geplant) nur damit sinnvoll ist. Die Umkehr passiert jetzt in der Preview-Phase, weil sie `Coordinate` ändert.

*English version: [0015-m-werte-mitgefuehrt-nicht-ausgewertet.md](0015-m-werte-mitgefuehrt-nicht-ausgewertet.md)*

## Consequences

- **Core:**
  - `Coordinate` bekommt einen vierten positionalen Parameter: `Coordinate(X, Y, Z = NaN, M = NaN)`, mit `HasM`. `NaN` heißt „kein M“. Das ist ein Binär-Break des Konstruktors, in 0.x akzeptiert (die Package Validation braucht dafür eine Unterdrückung).
  - `IPointM : IPoint2D` mit `double M { get; }`. Es ist unabhängig von `IPoint3D`: Ein Punkt darf eines oder beide implementieren, und Algorithmen prüfen die beiden Interfaces getrennt (wie `GeometryAccess` es für Z tut). Ein kombiniertes `IPointZM` gibt es nicht.
  - `IGeometryFactory.SupportsM` ist ein Default-Interface-Member, das `false` liefert, damit bestehende Host-Factories weiter kompilieren. Ist es `false`, lassen Algorithmen M in ihren Ergebnissen weg (wie bei `SupportsZ`).
- **Host-Vertrag:** Wie bei Z meldet eine Klasse, die `IPointM` implementiert, ihr `M` als gültig, auch 0. Ein Host, der „kein M“ nicht von 0 unterscheiden kann (gViews `Point` speichert `M = 0.0` immer; nur `IGeometryDef.HasM` weiß es), darf `IPointM` nicht auf dieser Basisklasse implementieren. Er implementiert es auf einer M-fähigen Unterklasse oder einem Wrapper, den er dort einsetzt, wo `HasM` wahr ist. Das gehört in den Integrations-Leitfaden.
- **Interpolation:**
  - An einem neuen Stützpunkt stammt M vom **Quellsegment** der Ergebniskante, linear nach dem Parameter entlang des Segments, genau wie Z im Noder. Treffen mehrere Kanten in einem Gitterpixel zusammen, gewinnt die erste in der deterministischen Ordnung des Noders.
  - M wird **nur interpoliert, wenn beide Enden des Segments M haben**. Sonst hat der neue Stützpunkt kein M. Es wird nichts geraten, und 0 wird nie eingesetzt.
  - Ursprüngliche Stützpunkte behalten ihr M.
- **Algorithmen:** Overlays (`Intersection`, `Union`, `Difference`, `SymDifference`), `Cut`, `Simplify` und `OffsetCurve` tragen M wie Z. `Buffer` und `ConvexHull` erzeugen kein M, so wie `Buffer` kein Z erzeugt (ADR 0006). `Centroid` liefert ein `Coordinate?` ohne M (wie ohne Z, ADR 0012). Prädikate und Maße ignorieren M.
- **IO:** WKT liest und schreibt `M` und `ZM` (`POINT M (1 2 3)`, `POINT ZM (1 2 3 4)`). GeoJSON bleibt ohne M, damit Exporte Standard bleiben (RFC 7946); wer M braucht, nutzt WKT und später WKB.
- **Tests (ADR 0008):** Das NTS-Orakel interpoliert M in Overlays nicht, daher wird M nicht gegen es geprüft. Stattdessen:
  - handgeschriebene Erwartungen (ursprüngliches M bleibt erhalten, M an neuen Stützpunkten liegt zwischen den Enden des Quellsegments, kein M, wenn ein Ende keines hat);
  - ein **Invarianten-Property-Test**: Dieselbe Operation mit und ohne M liefert identisches X/Y (und Z). Das ist die stärkste Garantie, dass M die Berechnung nie beeinflusst, und braucht kein Orakel;
  - `[Covers]` bekommt einen neuen Pflicht-Tag `M`; die Fremd-Host-Geometrien bekommen eine M-Variante.
- **Auslieferung:** in drei Etappen (ADR und Core; Extensions und Tests; IO, Dokumentation), aber **ein Release** (`0.3.0-preview.1`), damit kein Zwischenstand mit halbem M auf NuGet landet.
- **Folgearbeiten, die davon abhängen:** WKB (Lesen ISO und EWKB, Schreiben ISO) und Linear Referencing (`Interpolate`, `Project`, `Substring` nach Länge, dazu `...ByM`-Varianten). Jedes bekommt einen eigenen ADR.

## Considered Options

- M draußen lassen, die Hosts tragen es selbst (verworfen: WKB und Linear Referencing wären für gView-Daten unbrauchbar)
- M als eigener Koordinatentyp neben `Coordinate` (verworfen: verdoppelt Factory- und Algorithmenpfade; ein Werttyp mit `NaN` für „keins“ ist konsistent mit Z)
- `IPointZM` und weitere kombinierte Interfaces (verworfen: größere Interface-Fläche; zwei kleine Interfaces getrennt zu prüfen genügt)
- Ein `HasM`-Flag auf `IPointM` (verworfen: vergrößert den Vertrag, ohne das Host-Problem zu lösen; der Vertrag „implementiert, also gilt es“ ist derselbe wie bei Z)
- `M == 0` als „kein M“ (verworfen: 0 ist ein gültiger Messwert, Linear Referencing beginnt oft dort)
- M mitteln, wo zwei Operanden in einem Stützpunkt zusammentreffen (verworfen: die Ergebniskante gehört zu einer Eingabekante, deren interpoliertes M der natürliche Wert ist)
- M in GeoJSON als viertes Positionselement (verworfen: nicht standardisiert, Exporte sollen interoperabel bleiben)
- Algorithmen, die M auswerten (zum Beispiel Filter nach M), in diesem Schritt (verworfen: hält den 2D-Kern unberührt; Linear Referencing ist eine eigene API darüber)
