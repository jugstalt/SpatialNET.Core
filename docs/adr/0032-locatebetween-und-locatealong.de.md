# LocateBetween und LocateAlong: alle Strecken und alle Punkte zu M

`SubstringByM` und `InterpolateByM` (ADR 0017) folgen dem **ersten Treffer** eines M-Wertes. Echtes M ist nicht monoton: Eine Straße macht eine Schleife, M springt zurück, eine Route führt an sich selbst entlang zurück. Die Fragen eines WebGIS sind dann andere: „Welche Strecken dieser Route liegen zwischen Kilometer 12 und 15?“ und „Wo liegt Kilometer 12,5, überall, wo er vorkommt?“. **`LocateBetween`** beantwortet die erste mit jeder Strecke, deren M in einem Intervall liegt, **`LocateAlong`** die zweite mit jedem Punkt, an dem M einen Wert hat. Es sind die OGC-Operationen `ST_LocateBetween` und `ST_LocateAlong` für Linien.

*English version: [0032-locatebetween-und-locatealong.md](0032-locatebetween-und-locatealong.md)*

## Consequences

- **Ort:** `LocateBetween` und `LocateAlong` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **API** (Erweiterungsmethoden auf `IPolyline2D`; Parameterreihenfolge wie in ADR 0012):

  | Methode | Liefert | Bedeutung |
  |---|---|---|
  | `LocateBetween(double fromM, double toM, factory)` | `IPolyline2D` | alle Strecken, auf denen M in `[fromM, toM]` liegt |
  | `LocateAlong(double m, factory)` | `IMultiPoint2D` | alle Punkte, an denen M gleich `m` ist, in der Reihenfolge entlang der Linie |

  Beide Ergebnisse sind Geometrien und kommen aus der Factory. Eine Trefferliste, die in ADR 0017 ein Wert war („eine Liste aller Treffer gehört nicht zu diesem Schritt“), ist hier eine Geometrie, weil die OGC-Operationen eine liefern und weil ein Host die Ergebnisse zeichnet.
- **Dieselben Regeln wie die anderen M-Methoden (ADR 0017):** Nur Segmente, deren **beide Enden M tragen**, nehmen teil; eine Strecke ohne M wird weder überbrückt noch ist sie ein Fehler, sondern eine Lücke. M wird auf einem Segment linear interpoliert. An Daten wirft nichts: Kein Treffer ergibt eine leere Polylinie bzw. ein leeres Multipoint. Z wird an neuen Punkten interpoliert, ursprüngliche Stützpunkte behalten ihre eigenen Werte (ADR 0015).
- **`LocateBetween`:**
  - Das Intervall ist **geschlossen**; ist `fromM` größer als `toM`, werden die Grenzen **vertauscht**, da eine Suche keine Richtung hat. Das Ergebnis behält die **Richtung der Linie**: Eine Strecke, auf der M fällt, läuft vom höheren zum niedrigeren M.
  - Die Strecken aufeinanderfolgender Segmente bilden **einen Pfad**; eine Lücke (ein Segment außerhalb des Intervalls oder ohne M) oder das Ende eines Eingabepfades beginnt den nächsten Pfad. Die Schnittenden bekommen genau die Grenze als M (kein Rundungsfehler aus der Interpolation).
  - Eine Strecke **ohne Länge** gehört nicht zum Ergebnis: Berührt M das Intervall nur an einem Punkt (die Spitze eines Hügels, eine Grenze gleich M an genau einem Stützpunkt), kommt dafür nichts zurück, denn ein Stück der Länge 0 ist nichts (ADR 0017, `Substring`). `LocateAlong` findet solche Punkte. Segmente der Länge 0 werden übersprungen.
  - Ein Segment mit konstantem M liegt ganz innerhalb oder ganz außerhalb.
- **`LocateAlong`:** Jedes Segment, dessen M-Bereich den Wert enthält, ergibt einen Punkt; ein Treffer an einem Stützpunkt, den zwei Segmente teilen, zählt **einmal**; ein Segment mit konstantem M gleich dem Wert antwortet mit seinem **Anfang**, wie `InterpolateByM` (der erste Punkt von `LocateAlong` ist also `InterpolateByM`). Es liefert nicht das ganze konstante Segment: Das tut `LocateBetween(m, m)`.
- **Ausnahmen (ADR 0012):** `ArgumentNullException` bei `null`, `ArgumentOutOfRangeException` bei `NaN` oder unendlichem `fromM`, `toM` oder `m`, `InvalidOperationException` ohne Factory.
- **Kosten:** O(n), ein Durchlauf, kein Zustand.
- **Tests (ADR 0008):** NTS hat kein M-basiertes Gegenstück, die Fälle sind deshalb von Hand geschrieben (`[Covers]`) und mit einem Brute-Force-Durchlauf verglichen (die Länge im Intervall über viele zufällige Linien mit zufälligem M). Außerdem: nicht monotones M mit mehreren Strecken und ihrer Richtung, verbundene Segmente, Grenzen und vertauschte Grenzen, Lücken ohne M und mehrere Pfade, konstantes M, Berührung in einem Punkt, Z an den Schnittenden, die Regel zum geteilten Stützpunkt, Übereinstimmung mit `InterpolateByM`, ungültige Eingaben, leere Linien.
- **Playground und Dokumentation:** Beide haben einen Deskriptor (ADR 0011) auf den Beispielen mit M der Längenvarianten, wie die übrigen `...ByM`-Methoden; die Konzeptseite „Lineare Referenzierung“ hat einen Abschnitt.
- **Vorerst nicht dabei:** M-Intervalle für mehrere Linien auf einmal, ein seitlicher Versatz, Ereignistabellen (Punkt- und Linienereignisse mit Overlay von Intervallen).

## Considered Options

- Nur `LocateBetween`, verworfen: `LocateAlong` ist derselbe Durchlauf für Punkte, und ohne sie findet man einen wiederholten M-Wert nur über den ersten Treffer
- Eine Liste oder ein Array als Ergebnis statt Geometrien, verworfen: Die OGC-Operationen liefern Geometrien, und ein Host zeichnet oder speichert das Ergebnis wie jede andere Linie oder jedes Multipoint
- Einen Pfad je Segment liefern, verworfen: Eine Strecke, die über viele Stützpunkte weitergeht, würde zerlegt und der Aufrufer müsste sie wieder verbinden
- Strecken ohne Länge als Ein-Punkt-Pfade liefern, verworfen: Ein Polylinienpfad braucht eine Länge; für die Punkte ist `LocateAlong` da
- Werfen, wenn `fromM` größer als `toM` ist, verworfen: Für eine Suche hat die Reihenfolge der Grenzen keine Bedeutung, Vertauschen verliert nichts
- `LocateAlong` ein konstantes Segment ganz liefern lassen, verworfen: Ein Punktergebnis bleibt ein Punkt; das Segment ist `LocateBetween(m, m)`
- Über eine Lücke ohne M hinweg interpolieren, verworfen wie in ADR 0017: Es würde Messwerte erfinden
