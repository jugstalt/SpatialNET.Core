# Räumlicher Index

`SpatialIndex<T>` beantwortet über viele Elemente zwei Fragen schnell: *Welche Elemente liegen in diesem Rechteck?* und *Welche Elemente liegen diesem Punkt am nächsten?* Ohne Index ist beides eine Schleife über alle Elemente. Der Index ist ein Baustein für Daten im Speicher und für Hosts ohne eigenen Index; den Index einer Datenbank oder eines GIS-Hosts ersetzt er nicht ([ADR 0014](../../adr/0014-oeffentlicher-raeumlicher-index-statisch-str.de.md)).

## Einmal bauen, oft abfragen

Der Index ist **statisch**: Du baust ihn aus Elementen mit Envelope und Nutzlast, danach ändert er sich nie. Um Änderungen abzubilden, baust du einen neuen. Dafür ist er dicht gepackt (Sort-Tile-Recursive) und **thread-sicher**: Beliebig viele Threads dürfen dieselbe Instanz abfragen.

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((8 8, 22 8, 8 12, 8 8))"),
};

// Build once; the index is immutable, so any number of threads may query it.
SpatialIndex<IPolygon2D> index = parcels.ToSpatialIndex();

// Range query: envelope candidates first, then the exact test on the real geometry.
var probe = Wkt.Read<IPolygon2D>("POLYGON((16 10, 18 10, 18 12, 16 12, 16 10))");
Console.WriteLine(index.Query(probe).Count());                         // 1 - the envelope of the triangle meets the probe's
Console.WriteLine(index.Query(probe).Count(p => p.Intersects(probe))); // 0 - but the triangle itself does not

// "Within 5 map units of a point": query with the point's envelope widened by 5.
var here = new Coordinate(15, 5);
Console.WriteLine(index.Query(new Envelope2D(here.X - 5, here.Y - 5, here.X + 5, here.Y + 5)).Count()); // 3

// The nearest parcel by exact distance (without the delegate, the envelope distance is used).
var hereGeometry = Wkt.Read<IGeometry2D>("POINT(15 5)");
IReadOnlyList<IPolygon2D> nearest = index.Nearest(here, 1, (parcel, _) => parcel.Distance(hereGeometry));
Console.WriteLine(nearest[0].ToWkt()); // POLYGON ((8 8, 22 8, 8 12, 8 8))
```

Für Elemente, die keine Geometrien sind, gibst du den Envelope selbst an: `records.ToSpatialIndex(r => r.Bounds)`, oder du baust `new SpatialIndex<T>(items)` aus `(Envelope2D, T)`-Paaren.

## Was eine Abfrage liefert

Der Index kennt nur **Envelopes**. `Query` liefert die Elemente, deren Envelope den Abfrage-Envelope berührt oder überlappt (abgeschlossene Intervalle). Das Ergebnis ist also ein Vorfilter: Der exakte Test auf der echten Geometrie liegt bei dir (`.Where(x => x.Intersects(g))`, siehe [Prädikate](praedikate.md)). Bei vielen Tests gegen eine Geometrie kombinierst du ihn mit `Prepare()`.

- **Reihenfolge.** Die Reihenfolge der `Query`-Ergebnisse ist nicht festgelegt.
- **Ohne Allokation.** `Query(envelope, results)` fügt in eine Sammlung ein, die du übergibst (etwa eine wiederverwendete `List<T>`), ohne Enumerator.
- **Innerhalb eines Abstands.** Dafür gibt es keine eigene API: Erweitere den Envelope um den Abstand und teste dann exakt.

## Nächste Nachbarn

`Nearest(point, k)` liefert bis zu `k` Elemente, aufsteigend nach Abstand. Standardmäßig ist der Abstand der vom Punkt zum **Envelope** des Elements (0 innerhalb), was nur für Rechtecke exakt ist. Für Geometrien übergibst du den exakten Abstand als Delegate; er darf nie kleiner sein als der Envelope-Abstand, was für jede echte Geometrie innerhalb ihres Envelopes gilt. Elemente mit gleichem Abstand behalten ihre Eingabereihenfolge, die Ergebnisse sind also deterministisch.

## Zwei Indizes verbinden

`Join` findet alle Paare aus einem Eintrag des einen und einem Eintrag eines anderen Index, deren Envelopes sich treffen, indem es beide Bäume gemeinsam durchläuft und ganze Teilbäume überspringt, die sich nicht treffen können ([ADR 0021](../../adr/0021-spatial-join-doppelbaum-nur-vorfilter.de.md)). Wie `Query` ist es ein Vorfilter auf Envelopes; der exakte Test ist Ihrer:

```csharp
var parcels = parcelGeometries.ToSpatialIndex();
var zones = zoneGeometries.ToSpatialIndex();

// welche Flurstücke treffen welche Hochwasserzone? Erst Envelope-Kandidaten, dann der exakte Test:
var hits = parcels.Join(zones).Where(p => p.Left.Intersects(p.Right));

// alle sich überlappenden Paare innerhalb einer Ebene: jedes Paar einmal, nie ein Flurstück mit sich selbst
var overlaps = parcels.SelfJoin().Where(p => p.First.Overlaps(p.Second));

// nahe Paare: die Envelopes werden um 5 Karteneinheiten geweitet
var near = parcels.Join(zones, distance: 5);
```

- **`SelfJoin` liefert jedes ungeordnete Paar einmal**, nie einen Eintrag mit sich selbst. Eine Schleife aus `Query`-Aufrufen kann das nicht: Sie liefert jedes Paar zweimal und jedes Objekt mit sich selbst.
- `distance` weitet die Envelopes einer Seite; es bleibt ein Envelope-Test, kein exakter Abstand.
- Die Reihenfolge der Paare ist nicht festgelegt. Das Ergebnis lässt sich mehrfach aufzählen, und beliebig viele Threads dürfen dieselben Indizes joinen.
- Gemessen: Bei großen, gleichmäßig verteilten Mengen ist `Join` etwa doppelt so schnell wie eine Schleife aus Abfragen; bei kleinen oder sehr dichten Mengen liegt es gleichauf, weil die Zahl der Paare überwiegt.

## Regeln

- **Leere Envelopes** (`Envelope2D.Empty`, der Envelope einer leeren Geometrie) werden beim Bauen übersprungen und zählen nicht in `Count`. Ein leerer Abfrage-Envelope liefert nichts.
- **Ungültige Werte** werfen `ArgumentOutOfRangeException`: ein teilweise NaN-, unendlicher oder `Min > Max`-Envelope, ein nicht endlicher Punkt, `k < 1`, eine Knotenkapazität unter 2 ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.de.md)). Punkte und Envelopes mit Breite 0 (Linien) sind gültig.
- **Duplikate** bleiben erhalten, jedes Element ist ein eigener Eintrag. Die Nutzlast darf `null` sein.
- **Schnappschuss.** Die Eingabe wird beim Bauen kopiert; spätere Änderungen an der Quellsammlung haben keine Wirkung ([ADR 0013](../../adr/0013-praedikate-de9im-relate-prepared.de.md)). Wenn du Host-Geometrien indizierst, beachte, dass der Index deren Envelopes vom Zeitpunkt des Bauens hält: Wurde eine Geometrie verschoben, baust du neu.
- **Thread-Sicherheit.** Abfragen sind parallel sicher. Das unterscheidet den Index von `PreparedGeometry`, die nicht thread-sicher ist.
