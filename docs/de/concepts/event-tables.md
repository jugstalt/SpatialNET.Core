# Ereignistabellen

Ein **Ereignis** ist eine Aussage über eine Strecke einer Route, die über ihre M-Werte angesprochen wird und keine eigene Geometrie hat: *Tempolimit 50 von Kilometer 0 bis 5*, *Belag Schotter von 3 bis 8*, *Brücke bei 8,2*. Die Route selbst ist die kalibrierte Polylinie der [linearen Referenzierung](linear-referencing.md); die Ereignisse liegen in der Datenbank des Hosts. `LineEvents` rechnet **zwischen** Ereignistabellen ([ADR 0033](../../adr/0033-ereignistabellen-intervallrechnung.de.md)): welche Ereignisse gleichzeitig gelten, was eine Tabelle hat und die andere nicht, welche Nachbarn sich zusammenfassen lassen. Es rechnet nur mit den Zahlen, braucht also **keine Geometrie, keine Factory und keinen Datenbankzugriff**; die Bibliothek liest nie eine Tabelle, Sie geben ihr die Zeilen.

## Was hineingeht, was herauskommt

Ein Ereignis ist ein `LineEvent<T>(From, To, Value)`: ein Bereich von M-Werten und ein beliebiger Wert für die Attribute (ein `int` für ein Tempo, ein Text, ein Record mit mehreren Feldern). Eine Tabelle ist jedes `IEnumerable<LineEvent<T>>`.

Zwei Tabellen einer Route:

| Von | Bis | Tempo |  | Von | Bis | Belag |
|---:|---:|---:|---|---:|---:|---|
| 0 | 5 000 | 50 |  | 0 | 3 000 | Asphalt |
| 5 000 | 12 000 | 70 |  | 3 000 | 8 000 | Schotter |
| | | |  | 8 000 | 12 000 | Asphalt |

Die Frage „wo gilt welches Tempo auf welchem Belag?“ ist der **Schnitt** der beiden Tabellen, und das Ergebnis ist wieder eine Tabelle:

| Von | Bis | Tempo | Belag |
|---:|---:|---:|---|
| 0 | 3 000 | 50 | Asphalt |
| 3 000 | 5 000 | 50 | Schotter |
| 5 000 | 8 000 | 70 | Schotter |
| 8 000 | 12 000 | 70 | Asphalt |

```csharp
var speed = new[] { new LineEvent<int>(0, 5000, 50), new LineEvent<int>(5000, 12000, 70) };
var surface = new[]
{
    new LineEvent<string>(0, 3000, "asphalt"), new LineEvent<string>(3000, 8000, "gravel"), new LineEvent<string>(8000, 12000, "asphalt"),
};

// Where do both hold? One piece per overlap, with the two values combined.
var both = LineEvents.Intersect(speed, surface, (s, b) => (Speed: s, Surface: b));
// 0-3000 (50, asphalt), 3000-5000 (50, gravel), 5000-8000 (70, gravel), 8000-12000 (70, asphalt)

// Join neighbours with the same value: the surface alone, after cutting at the speed limits.
var surfaceOnly = LineEvents.Dissolve(both.Select(e => new LineEvent<string>(e.From, e.To, e.Value.Surface)));
// 0-3000 asphalt, 3000-8000 gravel, 8000-12000 asphalt

// Which speed limit holds at a bridge at 8200?
var atBridge = LineEvents.At(speed, 8200);               // one event: 5000-12000, 70

// Where is the road without a bicycle path?
var bikePath = new[] { new LineEvent<bool>(2000, 6000, true) };
var withoutPath = LineEvents.Difference(speed, bikePath); // 0-2000 (50) and 6000-12000 (70)
```

## Die Operationen

| Operation | Frage | Ergebnis |
|---|---|---|
| `Intersect(a, b, combine)` | wo gelten ein Ereignis aus `a` und eines aus `b` gleichzeitig? | ein Stück je überlappendem Paar, mit dem Wert, den `combine` aus beiden macht |
| `Union(a, b, both, onlyA, onlyB)` | die Route an jeder Grenze beider Tabellen zerlegt | ein Stück überall dort, wo mindestens eine Tabelle ein Ereignis hat, mit dem Wert von `both`, `onlyA` oder `onlyB` |
| `Difference(a, b)` | was hat `a` dort, wo `b` nichts hat? | die Teile der Ereignisse von `a`, die kein Ereignis von `b` bedeckt, mit den Werten von `a` |
| `Dissolve(events, comparer?)` | Zusammengehöriges verbinden | Ereignisse, die sich berühren oder überlappen und denselben Wert haben, werden eins |
| `Clip(events, from, to)` | nur ein Abschnitt | die Ereignisse auf den Bereich zugeschnitten |
| `At(events, m)` | was gilt hier? | die Ereignisse, deren Bereich `m` enthält |

Einiges, das man wissen sollte:

- **`Union` benennt die Seite.** Ein Stück, das nur eine Tabelle bedeckt, hat keinen Wert der anderen, und für ein `T`, das eine Zahl oder ein Struct sein kann, gibt es kein ehrliches „leer“. Sie geben also an, was aus jedem Fall wird: `Union(speed, surface, (s, b) => $"{s}, {b}", s => $"{s}, keine Angabe", b => $"kein Limit, {b}")`.
- **`Union` und `Intersect` schneiden, `Dissolve` verbindet.** Beide schneiden an jeder Grenze beider Tabellen, benachbarte Stücke können also gleiche Werte haben; mit `Dissolve` danach werden sie verbunden. `Dissolve` verbindet auch über Ereignisse anderer Werte hinweg (ein `A`, darin ein `B`, ein `A`, das weiterläuft, verbindet die beiden `A`) und lässt Ereignisse verschiedener Werte in Ruhe, auch wo sie überlappen. `comparer` bestimmt, was „derselbe Wert“ heißt; ohne ihn gilt die Standard-Gleichheit von `T`, die bei einem Record alle Felder vergleicht.
- **Überlappungen.** `Intersect` nimmt Ereignisse an, die sich innerhalb einer Tabelle überlappen, und liefert für jedes überlappende Paar ein Stück. `Union` nicht: Ein Stück hätte zwei Werte aus derselben Tabelle, überlappende Ereignisse innerhalb einer Tabelle sind deshalb eine `ArgumentException`, die die Tabelle nennt. `Difference` sieht von der zweiten Tabelle nur die Bereiche, Überlappungen dort sind also in Ordnung.
- **Grenzen.** Bereiche sind geschlossen, aber nur ein Bereich **mit Länge** zählt. Ein Ereignis mit `From == To` wird ignoriert, und keine Operation liefert ein Stück ohne Länge: Zwei Nachbarn `[0, 5]` und `[5, 9]` treffen sich bei 5, ohne dass dazwischen ein Stück läge. Sichtbar wird das nur bei `At(events, 5)`: Beide Nachbarn enthalten 5, also kommen beide zurück. Brauchen Sie eines, entscheiden Sie nach Ihrer Regel (zum Beispiel das spätere).
- **Reihenfolge und vertauschte Grenzen.** Die Eingabe darf in beliebiger Reihenfolge sein, und ein Ereignis mit `From` größer als `To` wird mit vertauschten Grenzen gelesen (M kann abwärts laufen). Die Ergebnisse sind nach `From`, dann `To` sortiert.
- **Ungültige Zahlen.** `NaN` oder ein unendliches `From` oder `To` ist eine `ArgumentOutOfRangeException`, die das Ereignis nennt.

## Von der Tabelle zur Karte

Die Rechnung liefert M-Bereiche. Um sie zu zeichnen, geben Sie jeden Bereich an `LocateBetween` auf der kalibrierten Route: Das Ergebnis folgt der Straße mit ihren Kurven, in Richtung der Route, und hat mehrere Pfade, wenn die Route den Bereich mehrfach durchläuft ([Lineare Referenzierung](linear-referencing.md)). Das [Cookbook](../cookbook.md) zeigt das als vollständiges Rezept, von den Tabellen bis zu den Längen der Stücke auf der Straße.

Ein Punktereignis (eine Brücke bei 8 200) ist ein `LocateAlong` von einem Punkt entfernt; ein Punkt innerhalb eines Linienereignisses ist `At`.

## Mehrere Routen und die Datenbank

`LineEvents` arbeitet mit **einer Route auf einmal**. Eine echte Tabelle hat eine Routen-Id; gruppieren Sie selbst danach, das ist eine Zeile LINQ und hält die Bibliothek frei von Datenbankbegriffen:

```csharp
// The rows come from your database; the library only gets the numbers of one route at a time.
var rows = new[]
{
    (Route: "A12", From: 0.0, To: 5000.0, Speed: 50),
    (Route: "A12", From: 5000.0, To: 12000.0, Speed: 70),
    (Route: "B3", From: 0.0, To: 800.0, Speed: 30),
};

Dictionary<string, LineEvent<int>[]> byRoute = rows
    .GroupBy(r => r.Route)
    .ToDictionary(g => g.Key, g => g.Select(r => new LineEvent<int>(r.From, r.To, r.Speed)).ToArray());
```

Die Tabellen sind in M-Werten, nicht in Längen: Liegen Ihre Ereignisse als Meter ab Routenanfang vor, rechnen Sie sie zuerst um (oder kalibrieren die Route so, dass M diese Länge ist).

## Nicht dabei

Punktereignisse als eigener Typ und die Gegenrichtung, **von einer Geometrie zu einem Ereignis** (ein Flurstück an einer Straße wird zu „von 4 200 bis 4 900“), gehören nicht zu diesem Schritt; für einen Punkt liefert `ProjectToM` den M-Wert schon.
