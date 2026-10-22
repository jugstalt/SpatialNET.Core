# Overlays, Füllregeln und das Gitter

Overlays verknüpfen zwei Geometrien: `Intersection`, `Union`, `Difference` und `SymDifference`. Alle laufen über **einen** Kern. Deshalb verhalten sie sich einheitlich, nehmen eine Füllregel entgegen und liefern Ergebnisse auf einem Gitter. Diese Seite erklärt, wie das funktioniert und was es für dich bedeutet.

## Wie ein Overlay arbeitet

Der Kern folgt dem Muster der JTS-Pipeline OverlayNG ([ADR 0005](../../adr/0005-overlay-winding-number-snap-rounding.de.md)):

1. **Noding mit Snap-Rounding.** Alle Kanten beider Eingaben werden an ihren Schnittpunkten zerlegt, die Punkte werden auf ein Gitter gerundet.
2. **Planarer Graph.** Die genodeten Kanten bilden einen planaren Graphen, dessen Maschen die möglichen Ergebnisflächen sind.
3. **Windungszahlen.** Jede Masche bekommt je Operand eine Windungszahl: wie oft der Rand dieser Eingabe sie umläuft.
4. **Füllregel.** Die Füllregel macht aus jeder Windungszahl innen oder außen.
5. **Auswahl und Aufbau.** Die Maschen werden nach der Operation ausgewählt, das Ergebnis entsteht als Teilflächen (Außenring plus Löcher), Pfade oder Punkte.

Das Ergebnis ist immer gültig und geschlossen; Außenringe laufen gegen den Uhrzeigersinn, Löcher im Uhrzeigersinn.

## Füllregeln

Eine Füllregel entscheidet aus der Windungszahl eines Punktes, ob er zur Fläche einer Eingabe gehört. Jedes Overlay nimmt einen Parameter `FillRule`:

| Füllregel | Innen, wenn die Windungszahl … | Typische Verwendung |
|---|---|---|
| `EvenOdd` (Standard) | ungerade ist | Standard für alle Eingaben; entspricht der Flächendefinition von Polygonen in SpatialNET ([ADR 0003](../../adr/0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)) |
| `NonZero` | ungleich 0 ist | Überlappende Außenringe verschmelzen, z. B. aus Host-Daten |
| `Positive` | größer als 0 ist | Grundlage des Buffers |
| `Negative` | kleiner als 0 ist | Selten nötig |

```csharp
// Two partially overlapping shells in one polygon: invalid by the OGC rules.
var overlapping = Wkt.Read<IPolygon2D>(
    "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0),(5 5, 15 5, 15 15, 5 15, 5 5))");

Console.WriteLine(overlapping.IsValid());                            // False
Console.WriteLine(overlapping.Union(overlapping).Area());            // 150 - even-odd: the overlap becomes a hole
Console.WriteLine(overlapping.Union(overlapping, FillRule.NonZero).Area()); // 175 - the overlap is merged
```

Weil Overlays die Füllregel ohnehin auswerten, liefern sie auch für sich kreuzende oder überlappende Eingaben ein definiertes Ergebnis. Eine Vereinigung mit der passenden Füllregel ist deshalb zugleich ein Reparaturschritt für solche Eingaben.

## Warum es immer ein Gitter gibt

Der Schnittpunkt zweier Segmente ist im Allgemeinen nicht als Paar von `double`-Werten darstellbar. Rundet man ihn naiv, liegt der gerundete Punkt auf keinem der beiden Segmente mehr exakt, und spätere Entscheidungen können sich widersprechen: Die Topologie wird inkonsistent, und Algorithmen liefern fehlende Kanten, ungültige Ringe oder Exceptions.

SpatialNET vermeidet das mit **Snap-Rounding**: Jeder konstruierte Schnittpunkt und jeder Vertex wird auf einen Punkt eines regelmäßigen Gitters gezogen, und jedes Segment, das durch eine solche Gitterzelle läuft, wird durch deren Mitte geführt. Zusammen mit exakten Prädikaten (`orient2d` nach Shewchuk, das nie ein falsches Vorzeichen liefert) entsteht ein korrekt genodetes Arrangement und ein Ergebnis, das konstruktionsbedingt gültig ist.

Der Preis: **Ergebniskoordinaten liegen auf dem Gitter** und können vom exakten Wert um bis zu eine halbe Gittereinheit abweichen. Unveränderte Eingabe-Vertices, die schon auf dem Gitter liegen, bleiben unverändert.

### Die Gitterweite wählen

Die Gitterweite wird in einer Kaskade bestimmt (dasselbe Muster wie bei der Factory):

1. der Parameter `gridSize` des Aufrufs, wenn er nicht `null` ist,
2. sonst `Precision.DefaultGridSize`, wenn gesetzt (anfangs `null`),
3. sonst automatisch: die größte Koordinate (Betrag) aller Eingaben mal 1e-12, abgerundet auf eine Zehnerpotenz. Das sind etwa 12 signifikante Stellen; bei UTM-Koordinaten um 5.000.000 ist das Gitter 1e-6.

```csharp
var strip = Wkt.Read<IPolygon2D>("POLYGON((0 1, 10 1, 10 5, 0 5, 0 1))");
var triangle = Wkt.Read<IPolygon2D>("POLYGON((0 0, 1 3, -1 3, 0 0))");

// Automatic grid (here 1e-11): the intersection point (1/3, 1) is rounded very finely.
Console.WriteLine(strip.Intersection(triangle).ToWkt());                  // POLYGON ((0 1, 0.33333333333 1, 1 3, 0 3, 0 1))

// Explicit grid of 0.01 map units.
Console.WriteLine(strip.Intersection(triangle, gridSize: 0.01).ToWkt());  // POLYGON ((0 1, 0.33 1, 1 3, 0 3, 0 1))

// Global default for all overlays that do not pass a grid size.
Precision.DefaultGridSize = 0.01;
```

Das automatische Gitter wird aus allen Eingaben gemeinsam abgeleitet, `a.Intersection(b)` und `b.Intersection(a)` nutzen also dasselbe Gitter. Eine explizite Gitterweite wählst du, wenn du bewusst gröbere Ergebnisse willst, etwa um verrauschte Daten zu bereinigen. `gridSize` muss endlich und größer als 0 sein; ein Gitter, das für die Koordinaten zu klein ist (die Koordinate passt nicht mehr in den ganzzahligen Bereich des Gitters), wirft eine `ArgumentOutOfRangeException`.

## Viele Geometrien vereinigen

`Union` nimmt zwei Operanden. Um viele Geometrien aufzulösen, gibt es `UnionAll`; eine Schleife über `Union` ist quadratisch, weil jeder Schritt das ganze angesammelte Ergebnis neu verarbeitet ([ADR 0018](../../adr/0018-unionall-kaskadiert-gemeinsames-gitter.de.md)).

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((5 0, 15 0, 15 10, 5 10, 5 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((50 50, 60 50, 60 60, 50 60, 50 50))"),
};

IPolygon2D all = parcels.UnionAll();   // zwei Teilflächen: die verschmolzene Reihe und das einzelne Quadrat

// „Dissolve nach Schlüssel“: gruppieren, dann jede Gruppe vereinigen.
var byOwner = parcels.GroupBy(p => p.Envelope().MinX < 20 ? "west" : "east").Select(g => g.UnionAll());
```

`UnionAll` gibt es für Polygone (mit `FillRule`), Polylinien und Multipunkte sowie allgemein für Geometrien gleicher Dimension. Jede Eingabe wird wie bei `Union` unter der Füllregel gelesen, leere Eingaben werden übersprungen, gemischte Dimensionen werfen `NotSupportedException`. Die Eingaben werden nach ihren Envelopes geordnet und in kleinen Gruppen Ebene für Ebene vereinigt, damit sich Nachbarn früh treffen. **Alle Schritte nutzen ein Gitter**, das einmal über alle Eingaben aufgelöst wird; die Gruppierung kann das Ergebnis deshalb nur um etwa eine Gitterzelle verändern. Dieselbe Folge liefert immer das identische Ergebnis; eine andere Reihenfolge liefert dieselbe Fläche bis auf diese Toleranz.

## Zuschneiden auf ein Rechteck

`Clip` schneidet eine Geometrie auf ein Rechteck zu, der Fall von Kacheln und Ausschnitten ([ADR 0022](../../adr/0022-locate-envelope-helfer-und-clip.de.md)). Es ist der Schnitt mit dem **geschlossenen** Rechteck (der Rand gehört dazu), mit zwei Schnellpfaden:

```csharp
var tile = new Envelope2D(0, 0, 256, 256);

IPolygon2D inTile = parcel.Clip(tile);    // am Rand zugeschnitten
IPolyline2D road = roadLine.Clip(tile);   // die Teile innen oder auf dem Rand
IMultiPoint2D hits = points.Clip(tile);   // gefiltert, nie gerundet
```

- Eine Geometrie, die **vollständig im Rechteck** liegt, kommt als Kopie **ohne Runden** zurück (ein Polygon als normalisierte Kopie), und eine, die es **nicht trifft**, ergibt eine leere Geometrie. Nur wo der Rand schneidet, läuft der Overlay, und **nur diese Ergebnisse liegen auf dem Gitter**.
- Z und M werden wie bei `Intersection` mitgeführt; die Ecken des Rechtecks, die zu Stützpunkten werden, haben keines.
- Ein Rechteck ohne Fläche (Breite oder Höhe null) ergibt ein leeres Polygon bzw. eine leere Polylinie; eine ungültige Envelope (nicht endlich oder `Min > Max`) wirft.

## Was du sonst noch wissen solltest

- **Strikte Semantik.** Ergebnisse sind dimensionshomogen. Berührungs- und Kollapsreste niedrigerer Dimension fallen weg: Zwei Polygone, die sich nur an einer Kante berühren, schneiden sich in einem *leeren Polygon*, nicht in einer Linie. Wo der Ergebnistyp nicht eindeutig wäre, legen ihn die typisierten Overloads fest: Polygon ∩ Polygon ist ein `IPolygon2D`; Polygon ∩ Polylinie und Polylinie ∩ Polylinie sind `IPolyline2D` (nur Überlappungen, reine Kreuzungspunkte fallen weg); Punkt oder Multipunkt ∩ beliebig ist ein `IMultiPoint2D`. `Difference` liefert den Typ des ersten Operanden. `Union` und `SymDifference` gibt es nur für Operanden gleicher Dimension.
- **Prädikate sind exakt, Overlays gerundet.** `Intersects`, `Relate` und die übrigen Prädikate rechnen exakt auf den Eingabekoordinaten, ohne Gitter. Bei Beinahe-Berührungen unterhalb der Gitterweite kann `Intersects` deshalb wahr und `Intersection` leer sein. Das ist erwartet und bei NetTopologySuite genauso.
- **Z.** An neuen Vertices wird Z auf dem ungerundeten Originalsegment interpoliert. Bei Kreuzungen gilt das Z des ersten Operanden. M wird genauso behandelt (mitgeführt, interpoliert, an Kreuzungen gewinnt der erste Operand, verworfen, wenn die Factory kein M unterstützt) und beeinflusst nie das X/Y eines Ergebnisses ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)). Ergebnisse sind 2D, wenn die Factory kein Z unterstützt ([ADR 0004](../../adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md)).
- **Ungültige Eingaben werfen nie.** Overlays interpretieren ungültige Eingaben über die Füllregel; sie werfen nur bei `null`-Argumenten, ungültigen Zahlparametern und nicht unterstützten Geometrie-Arten.
