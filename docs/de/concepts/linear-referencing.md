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

## Alle Strecken und alle Punkte zu M

Die `...ByM`-Methoden oben folgen dem ersten Treffer. Ist M nicht monoton, oder soll alles zwischen zwei Werten gefunden werden, finden `LocateBetween` und `LocateAlong` alles ([ADR 0032](../../adr/0032-locatebetween-und-locatealong.de.md)):

```csharp
IPolyline2D km12to15 = route.LocateBetween(12_000, 15_000);  // jede Strecke mit M in [12000, 15000]
IMultiPoint2D at12_5 = route.LocateAlong(12_500);            // jeder Punkt, an dem M 12500 ist
```

- `LocateBetween` liefert die Strecken als eine Polylinie: Aufeinanderfolgende Segmente bilden einen Pfad, eine Lücke (außerhalb des Intervalls oder ohne M) beginnt den nächsten. Das Intervall ist geschlossen, vertauschte Grenzen sind dasselbe Intervall, und die Richtung der Linie bleibt erhalten. Eine Strecke ohne Länge (M berührt das Intervall in einem Punkt) entfällt.
- `LocateAlong` liefert jeden Treffer in der Reihenfolge entlang der Linie; ein Treffer an einem Stützpunkt zählt einmal, und ein Segment mit konstantem M antwortet mit seinem Anfang, wie `InterpolateByM`.
- Beide nutzen nur Segmente mit M an beiden Enden; kein Treffer ergibt ein leeres Ergebnis.

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
