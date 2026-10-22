# Vereinfachen, konvexe Hülle und Validierung

## Simplify

`Simplify` verringert die Zahl der Vertices mit dem Douglas-Peucker-Algorithmus ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.de.md)). Ein Vertex bleibt erhalten, wenn er weiter als `tolerance` von der Sehne zwischen seinen Nachbarn entfernt ist.

```csharp
var noisy = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 1 0.1, 2 -0.1, 3 0.05, 4 0)");
Console.WriteLine(noisy.Simplify(0.5).ToWkt()); // LINESTRING (0 0, 4 0)
```

- **Polylinien:** Jeder Pfad wird vereinfacht; Anfangs- und Endpunkt bleiben, die übrigen Vertices sind unverändert (inklusive Z und M).
- **Polygone:** Jeder Ring wird vereinfacht; auch der Startpunkt eines Rings darf entfallen, und Ringe, die weniger als 4 Punkte behalten, fallen weg. Das Ergebnis wird über das Overlay normalisiert und ist deshalb **immer gültig** (siehe [Das Geometriemodell](geometriemodell.md)).
- **`preserveTopology: true`** nutzt eine topologieerhaltende Variante nach dem Muster von JTS `TopologyPreservingSimplifier`: Es entstehen keine neuen Kreuzungen zwischen oder innerhalb der Pfade, und es gehen keine Ringe verloren.
- `tolerance` gilt in Karteneinheiten und muss endlich und ≥ 0 sein.

Die Signaturen sind `Simplify(this IPolyline2D, double tolerance, bool preserveTopology = false, IGeometryFactory? factory = null)` und für Polygone zusätzlich mit `gridSize` für die Normalisierung.

## Konvexe Hülle

`ConvexHull` liefert die konvexe Hülle aller Punkte einer Geometrie als `IGeometry2D`. Je nach Eingabe ist das ein Polygon (gegen den Uhrzeigersinn), eine Polylinie zwischen den beiden Extrempunkten (alle Punkte kollinear) oder ein Punkt; bei leerer Eingabe ist das Ergebnis leer.

## Validierung

```csharp
var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");
Console.WriteLine(bowtie.IsValid()); // False

foreach (var issue in bowtie.Validate().Issues)
{
    Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
}
```

`IsValid()` liefert ein `bool`. `Validate()` liefert ein `ValidationResult`, dessen `Issues` jeden Befund mit Art und Ort aufführen (`ValidationIssue(Kind, Location)`); bei einer gültigen Geometrie ist die Liste leer. Die Gültigkeit folgt [ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md): Die Fläche wird nach Even-Odd gelesen, für Kreuzungen und Berührungen gelten die OGC-Regeln. Alle Entscheidungen sind exakt.

| `ValidationIssueKind` | Bedeutung |
|---|---|
| `SelfIntersection` | Ringe kreuzen sich, oder ein Ring kreuzt oder überlagert sich selbst |
| `RingSelfTouch` | Ein Ring berührt sich selbst in einem Punkt (z. B. ein „invertiertes Loch“) |
| `DegenerateRing` | Ein Ring ohne Fläche: weniger als 3 verschiedene Punkte oder alle Punkte kollinear |
| `InvalidCoordinate` | Eine Koordinate ist `NaN` oder unendlich |
| `TooFewPoints` | Ein Pfad mit weniger als 2 verschiedenen Punkten |
| `OverlappingShells` | Zwei Ringe überlagern sich entlang einer Kante |
| `DisconnectedInterior` | Ringe berühren sich so, dass die Fläche in getrennte Teile zerfällt (z. B. zwei Berührpunkte desselben Ringpaars) |

Keine Fehler sind: ein **fehlender Schlusspunkt**, die **Ring-Orientierung** und ein **Ring in einem Loch** (das ist eine Insel). Um ungültige Eingaben zu reparieren, gibt es `MakeValid` (unten).

## Reparatur: MakeValid

`MakeValid` verwandelt eine ungültige Geometrie in eine gültige ([ADR 0020](../../adr/0020-makevalid-even-odd-normalisierung.de.md)). Bei einem Polygon ist das die **Even-Odd-Normalisierung** über den Overlay-Kern, dieselbe Selbst-Union, die `Simplify` ausführt: Die Fläche, die die Eingabe unter der Füllregel hat, ist genau die Fläche des Ergebnisses. Sich kreuzende, überlappende und berührende Ringe lösen sich auf, Löcher, Inseln und Teilflächen entstehen aus dem Overlay, Ringe ohne Fläche entfallen.

```csharp
var bowtie = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

bowtie.IsValid();                  // false
IPolygon2D valid = bowtie.MakeValid();  // zwei Dreiecke, gültig
```

- **Es ist nicht das OGC-`ST_MakeValid`.** Ergebnisse sind dimensionshomogen, und eine Geometry-Collection gibt es nicht, kollabierte Reste bleiben also nicht als Linien erhalten.
- **Es läuft auf dem Gitter** ([Overlays](overlay-und-gitter.md)): Das Ergebnis liegt auf dem Gitter, auch die Teile, die vorher gültig waren. Wer gültige Eingaben unberührt lassen will, prüft vorher `IsValid()`. `FillRule.NonZero` verschmilzt überlappende Außenringe, statt die Überlappung zum Loch zu machen.
- **Polylinien** verlieren nicht endliche Punkte, aufeinanderfolgende doppelte Punkte und Pfade mit weniger als zwei verschiedenen Punkten; sich kreuzende Linien sind gültig. **Punkte** verlieren nicht endliche; Duplikate bleiben. Linien und Punkte werden nicht gerundet.
- Das Ergebnis ist immer eine neue Geometrie aus der Factory, und `MakeValid(MakeValid(g))` ist bis auf das Gitter wieder dieselbe Geometrie (zweimal ausgeführt kann es eine Kante eines sehr dünnen Splitters um etwa eine Gitterzelle biegen).

