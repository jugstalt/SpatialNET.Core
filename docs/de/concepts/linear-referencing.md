# Lineare Referenzierung

Lineare Referenzierung spricht eine Position auf einer Linie über eine einzige Zahl an: die **Länge entlang der Linie** oder einen **M-Wert**, den die Stützpunkte tragen (zum Beispiel den Kilometer einer Straße). `LinearReferencing` bietet sechs Methoden auf Polylinien, in zwei Varianten ([ADR 0017](../../adr/0017-lineare-referenzierung-laenge-und-m-erster-treffer.de.md)):

| Nach Länge | Nach M | Bedeutung |
|---|---|---|
| `Interpolate(distance)` | `InterpolateByM(m)` | der Punkt an einer Position |
| `Project(point)` | `ProjectToM(point)` | die Position des Punktes, der einem gegebenen Punkt am nächsten liegt |
| `Substring(from, to)` | `SubstringByM(fromM, toM)` | das Stück zwischen zwei Positionen |

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 100, 10 0 200, 10 10 250)");

Coordinate? at15 = road.Interpolate(15);        // (10 5), M = 225: auch M wird interpoliert
double position = road.Project(new Coordinate(4, 3)); // 4: der nächste Punkt ist (4 0)
Coordinate? km = road.InterpolateByM(150);      // (5 0): wo die Linie zuerst M = 150 erreicht
IPolyline2D piece = road.SubstringByM(150, 225); // von (5 0) über (10 0) nach (10 5)
```

## Wofür man das braucht

Eine Straße ist geometrisch eine Linie, fachlich aber eine **Route mit Kilometrierung**: Schaden bei Kilometer 12,4, Tempo 50 von 3,0 bis 5,5, Brücke bei 8,2. Die Daten hängen an der Kilometrierung, nicht an Koordinaten, und die lineare Referenzierung übersetzt zwischen beiden: Koordinate zu Messwert, und Messwert zu Punkt oder Strecke. Der typische Ablauf in einem WebGIS:

1. **Die Route einmal laden.** Ein Straßenabschnitt hat oft kein M, und die Messwerte sitzen auf getrennten Punkten (Kilometersteine). `CalibrateM` macht aus den Steinen M auf der Linie. Danach genügt ein Aufruf pro Klick, statt jedes Mal den vorherigen und den nächsten Stein zu suchen und zu rechnen, und die kalibrierte Linie lässt sich speichern oder zwischenspeichern. Ein Anker, der weiter als die Toleranz von der Linie entfernt ist, ist ein Fehler: Ein Stein, der 40 m neben der Straße liegt, fällt sofort auf, statt still falsche Kilometer zu liefern.
2. **Ein Klick wird zum Messwert.** `ProjectToM` macht aus einem Klick auf die Karte „Kilometer 12,437“, zum Anzeigen oder zum Speichern als Position einer Meldung.
3. **Ein Messwert wird zum Punkt.** `LocateAlong` beantwortet „zeige Kilometer 12,5“. Meist ist das ein Punkt; wo eine Route dieselbe Stelle zweimal passiert oder M zurückgesetzt wurde, sind es mehrere, und es liefert alle.
4. **Ein Bereich wird zur Strecke.** `LocateBetween` zeichnet „Tempo 50 von 3,0 bis 5,5“ oder „Baustelle von 12 bis 15“ als Linie, die der Straße folgt, nicht nur ihren zwei Endpunkten. Sie läuft in Richtung der Linie und hat mehrere Pfade, wenn die Route den Bereich mehrfach durchläuft oder Lücken ohne M hat.
5. **Neben der Straße.** `Interpolate(distance, offset)` setzt ein Symbol oder eine Beschriftung links oder rechts der Achse, und `ProjectDetailed` sagt einem Klick nicht nur seinen Kilometer, sondern auch, wie weit von der Achse und auf welcher Seite er fiel, damit der Host entscheiden kann, ob der Klick die Straße überhaupt gemeint hat.
6. **Spitzkehren und Schleifen.** Wo eine Linie sich selbst nahekommt, liegt ein Klick zwischen zwei Durchgängen, und `Project` liefert den früheren. `Project(point, from, to)` beschränkt die Suche auf den Abschnitt, in dem man arbeitet, oder auf den Teil nach dem letzten Treffer. Dasselbe hilft, wenn ein GPS-Track der Straße folgt: Jeder Punkt wird nahe dem vorigen projiziert.

Das [Cookbook](../cookbook.md) hat diesen Ablauf als lauffähiges Rezept.

## Längen

Eine Länge ist die planare Länge entlang der Linie, dieselbe wie `Length()`; Z nimmt nicht teil. Die Pfade einer Polylinie werden **in ihrer Reihenfolge aneinandergelegt**, und zwischen dem Ende eines Pfades und dem Anfang des nächsten wird keine Verbindung angenommen. Eine Länge genau auf der Grenze zweier Pfade gehört zum Ende des früheren Pfades.

- `Interpolate` **begrenzt** die Distanz auf `[0, Länge]`. Eine negative Distanz liefert den Anfang (sie zählt nicht vom Ende, wie in JTS: dafür `Länge - d` verwenden).
- `Project` liefert die Länge des nächsten Punktes. Sind mehrere Punkte gleich nah, etwa wo sich eine Linie kreuzt oder auf sich zurückläuft, gewinnt die **kleinste Länge**.
- `Substring` begrenzt beide Längen. Ist `from` größer als `to`, läuft das Stück in die Gegenrichtung. Gleiche Längen ergeben eine leere Polylinie: Ein Stück ohne Länge ist nichts. Ein Stück über mehrere Pfade hat einen Pfad je berührtem Pfad; die Lücke dazwischen wird nicht überbrückt.

## M-Werte

M spricht eine Position nur dort an, wo die Linie M trägt, und verändert die Geometrie nie. Die Regeln:

- **Nur Segmente mit M an beiden Enden nehmen teil.** Eine Strecke ohne M wird weder überinterpoliert noch ist sie ein Fehler; sie kann einfach nicht antworten, deshalb liefert `InterpolateByM` dann `null`, `ProjectToM` `NaN` und `SubstringByM` eine leere Polylinie.
- **Erster Treffer entlang der Linie.** Echte M-Werte sind nicht immer monoton: Eine Straße kann eine Schleife machen, oder M springt zurück. Die Linie wird in ihrer Reihenfolge durchlaufen, und das erste Segment, dessen M-Bereich den Wert enthält (geschlossenes Intervall), antwortet. Ein mehrfach vorkommender Wert wird also bei seinem ersten Vorkommen gefunden, und nichts wirft.
- Z und M der Ergebnispunkte werden **auf dem Segment interpoliert**, wenn beide Enden sie tragen; an einem ursprünglichen Stützpunkt hat der Punkt genau dessen eigene Werte ([ADR 0015](../../adr/0015-m-werte-mitgefuehrt-nicht-ausgewertet.de.md)).

## Neben der Linie: Versatz und Seite

Symbole, Beschriftungen und Ereignisse werden neben der Straße gezeichnet, und ein Klick sagt, auf welcher Seite er fiel. Beides ist eine Zahl, der **vorzeichenbehaftete Versatz** zur Linie: positiv links der Richtung der Linie, negativ rechts ([ADR 0035](../../adr/0035-seitlicher-versatz-und-projektion-mit-seite.de.md)).

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0, 10 10)");

Coordinate? beside = road.Interpolate(5, -2);                     // (5 -2): 5 along, 2 to the right
LineProjection? click = road.ProjectDetailed(new Coordinate(4, 3)); // position 4, distance 3, offset +3 (left)
Coordinate? back = road.Interpolate(click!.Value.Position, click.Value.Offset); // (4 3): the click again
```

- `Interpolate(distance, offset)` und `InterpolateByM(m, offset)` verschieben den Punkt senkrecht zum Segment an dieser Position. Mit einem Versatz von 0 sind sie die Methoden ohne einen. Z und M sind die des Punktes auf der Linie.
- `ProjectDetailed(point)` liefert die Position (wie `Project`), den nächsten Punkt auf der Linie (mit Z und M), den Abstand und den vorzeichenbehafteten Versatz. Ein Längenfenster `(from, to)` wie bei `Project` gibt es auch, damit ein Punkt nahe einer Schleife seinen Durchgang wählt und trotzdem seine Seite bekommt.
- **An einem Stützpunkt** ist die Richtung die des Segments, das dort endet; NTS nimmt das, das dort beginnt. Der Punkt springt von einer Seite der Biegung auf die andere, einen Bogen um die Ecke gibt es nicht (das ist `OffsetCurve`).
- **Ein Punkt jenseits des Linienendes** hat seinen nächsten Punkt am Ende und die Seite der Verlängerung des letzten Segments; genau auf dieser Verlängerung zählt er als links. Eine Linie ganz ohne Segment mit Länge hat keine Richtung, ein Versatz ungleich 0 ergibt also `null`.
- Die Rundreise `Interpolate(Position, Offset)` ergibt den Punkt zurück, wenn sein nächster Punkt im Inneren eines Segments liegt; an einem Stützpunkt oder einem Ende ist die Senkrechte nicht eindeutig.

## Alle Strecken und alle Punkte zu M

Die `...ByM`-Methoden oben folgen dem ersten Treffer. Ist M nicht monoton, oder soll alles zwischen zwei Werten gefunden werden, finden `LocateBetween` und `LocateAlong` alles ([ADR 0032](../../adr/0032-locatebetween-und-locatealong.de.md)):

```csharp
IPolyline2D km12to15 = route.LocateBetween(12_000, 15_000);  // jede Strecke mit M in [12000, 15000]
IMultiPoint2D at12_5 = route.LocateAlong(12_500);            // jeder Punkt, an dem M 12500 ist
```

- `LocateBetween` liefert die Strecken als eine Polylinie: Aufeinanderfolgende Segmente bilden einen Pfad, eine Lücke (außerhalb des Intervalls oder ohne M) beginnt den nächsten. Das Intervall ist geschlossen, vertauschte Grenzen sind dasselbe Intervall, und die Richtung der Linie bleibt erhalten. Eine Strecke ohne Länge (M berührt das Intervall in einem Punkt) entfällt.
- `LocateAlong` liefert jeden Treffer in der Reihenfolge entlang der Linie; ein Treffer an einem Stützpunkt zählt einmal, und ein Segment mit konstantem M antwortet mit seinem Anfang, wie `InterpolateByM`.
- Beide nutzen nur Segmente mit M an beiden Enden; kein Treffer ergibt ein leeres Ergebnis.

## Von einer Geometrie zu Bereichen von M

`LocateBetween` geht von M zur Straße; `LocateFeature` geht zurück, von einer Linie oder Fläche zu den Bereichen von M entlang der Route, in denen sie liegt ([ADR 0034](../../adr/0034-locatefeature-geometrie-zu-m-bereichen.de.md)). Ein Flurstück neben einer Straße wird zu „von 4 200 bis 4 900“, das sich als Ereignis speichern und mit anderen verrechnen lässt ([Ereignistabellen](event-tables.md)).

```csharp
var route = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 0, 100 0 1000)");
var parcel = Wkt.Read<IPolygon2D>("POLYGON((20 0, 50 0, 50 10, 20 10, 20 0))");

var ranges = route.LocateFeature(parcel, tolerance: 0);   // one range: 200 to 500
```

- Die Geometrie wird um `tolerance` erweitert und mit der Route geschnitten; jedes Stück der Route darin ergibt einen Bereich vom niedrigsten bis zum höchsten M. Die Toleranz ist Pflicht: Für ein Polygon ist 0 erlaubt, für eine Linie muss sie größer als 0 sein.
- Eine Route, die die Geometrie zweimal passiert, ergibt zwei Bereiche, wenn sie in M auseinanderliegen. Sich berührende Bereiche werden verbunden, eine Berührung in einem einzigen Punkt ergibt nichts, und eine Strecke ohne M bleibt draußen.
- Ein Punkt hat einen M-Wert, keinen Bereich: `ProjectToM` verwenden. M an den Enden eines Bereichs ist auf etwa das Gitter des Overlays genau.

## Linien, die sich kreuzen: ein Längenfenster

Eine Spitzkehre oder eine Schleife gibt einem Klick mehrere nächste Punkte, und `Project` antwortet mit der kleinsten Länge, dem früheren Durchgang. `Project(point, from, to)` und `ProjectToM(point, from, to)` suchen nur in der Strecke zwischen zwei Längen und antworten trotzdem mit einer Länge (oder M) auf der ganzen Linie ([ADR 0031](../../adr/0031-project-mit-laengenfenster.de.md)):

```csharp
double first = road.Project(click);                     // der frühere Durchgang
double second = road.Project(click, 11, road.Length()); // nur nach der Länge 11
```

Beide Längen werden begrenzt und vertauscht, wenn `from` größer als `to` ist; das Fenster ist geschlossen. Ein Punkt, der der Linie außerhalb des Fensters näher ist, bekommt den nächsten Punkt innerhalb. „Nach einer Position“ ist das Fenster `[Position, Length()]`.

## Eine Linie aus Ankerpunkten kalibrieren

Ein Straßenabschnitt hat oft kein M, und die Messwerte sitzen auf **Punkten entlang der Linie** (Kilometersteine). `CalibrateM` macht aus den Punkten einmal M auf der Linie ([ADR 0030](../../adr/0030-calibratem-m-werte-aus-ankerpunkten.de.md)); danach antworten `ProjectToM` und `InterpolateByM` aus der Linie.

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING (0 0, 10 0, 10 20)");
var posts = new[] { new Coordinate(5, 0, M: 100), new Coordinate(10, 15, M: 200) };

IPolyline2D withM = road.CalibrateM(posts, maxSnapDistance: 0.5);
double m = withM.ProjectToM(new Coordinate(10, 4)); // 145: 100 + (200 - 100) / (25 - 5) * (14 - 5)
```

- Jeder Anker wird auf die Linie **projiziert**, an seiner Position wird ein Stützpunkt eingefügt, und M wird **linear in der Länge entlang der Linie** zwischen den Ankern interpoliert, genau wie das Interpolieren zwischen dem vorherigen und dem nächsten Stein von Hand. Die Reihenfolge der Anker ist egal, M muss nicht monoton sein, und das M, das die Linie trägt, wird ersetzt.
- `maxSnapDistance` (Karteneinheiten) ist Pflicht. Ein Anker, der weiter von der Linie entfernt ist, ist **ein Fehler** und wird nicht übersprungen: Ein Stein, der nicht auf der Straße liegt, ist ein Datenproblem.
- Zwei Anker an derselben Position müssen in M übereinstimmen, sonst ist es ein Fehler.
- Vor dem ersten und nach dem letzten Anker entscheidet `MExtrapolation`: `None` (Standard) lässt die Strecke ohne M, `ProjectToM` liefert dort also `NaN`; `Clamp` nimmt das M des nächsten Ankers; `Linear` setzt die Steigung des Endintervalls fort.
- Auf einer Linie, die sich kreuzt, landet ein Anker nahe der Kreuzung auf dem früheren Durchgang, wie bei `Project`.

## Werte, keine Geometrien

Wie sonst ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.de.md)) sind punktförmige und skalare Ergebnisse Werte (`Coordinate?`, `double`), die keine Factory brauchen. Nur die Stücke von `Substring` und `SubstringByM` sind Geometrien und entstehen über die Factory; sie werden auf das beschränkt, was diese unterstützt (`SupportsZ`, `SupportsM`).

## Randfälle

Leere und entartete Linien werfen nie: `Interpolate` liefert `null` für eine Linie ohne Punkte, `Project` liefert `NaN`, und `Substring` liefert eine leere Polylinie. `NaN` oder unendliche Parameter werfen `ArgumentOutOfRangeException`, wie bei allen Algorithmen.
