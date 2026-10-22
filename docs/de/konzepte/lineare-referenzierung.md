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

## Werte, keine Geometrien

Wie sonst ([ADR 0012](../../adr/0012-api-signaturen-typisierte-overloads.de.md)) sind punktförmige und skalare Ergebnisse Werte (`Coordinate?`, `double`), die keine Factory brauchen. Nur die Stücke von `Substring` und `SubstringByM` sind Geometrien und entstehen über die Factory; sie werden auf das beschränkt, was diese unterstützt (`SupportsZ`, `SupportsM`).

## Randfälle

Leere und entartete Linien werfen nie: `Interpolate` liefert `null` für eine Linie ohne Punkte, `Project` liefert `NaN`, und `Substring` liefert eine leere Polylinie. `NaN` oder unendliche Parameter werfen `ArgumentOutOfRangeException`, wie bei allen Algorithmen.
