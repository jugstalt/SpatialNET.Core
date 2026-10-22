# Öffentlicher räumlicher Index: statisch, STR-gepackt, über Envelopes mit Nutzlast

SpatialNET bekommt einen kleinen **öffentlichen räumlichen Index**, `SpatialIndex<T>` in `SpatialNET.Extensions`. Er ist ein Baustein für Nutzer ohne Host-Index und für Ergebnislisten im Speicher, kein Ersatz für die Indizes eines Hosts oder einer Datenbank. Er ist **statisch**: einmal aus `(Envelope2D, T)`-Elementen per Bulk-Load gebaut (Sort-Tile-Recursive-Packing) und danach unveränderlich. Veränderung (Insert, Remove) fehlt mit Absicht: Ein Neubau ist billig, und eine unveränderliche Struktur ist für Lesezugriffe ohne Sperren thread-sicher, was zur Snapshot-Semantik aus ADR 0013 passt. Das interne Gitter `SegmentIndex` von `PreparedGeometry` bleibt, weil der Benchmark einen Ersatz nicht rechtfertigt.

*English version: [0014-oeffentlicher-raeumlicher-index-statisch-str.md](0014-oeffentlicher-raeumlicher-index-statisch-str.md)*

## Consequences

- **Umfang:** Envelope-Bereichsabfragen und k-nächste-Nachbarn. „Within distance d“ ist eine Bereichsabfrage mit um d erweitertem Envelope und hat keine eigene API. Union und Spatial Join sind eigene Themen.
- **Ort:** `SpatialNET.Extensions`, Namespace `SpatialNET`. Kein neues Paket, `SpatialNET.Core` bleibt reine Verträge. Für die Package Validation ist das nur eine Ergänzung.
- **API:**
  - `new SpatialIndex<T>(IEnumerable<(Envelope2D Envelope, T Item)> items, int nodeCapacity = 16)`, dazu `Count` und `Extent`.
  - `IEnumerable<T> Query(Envelope2D)` ist die Hauptform. `Query(Envelope2D, ICollection<T> results)` fügt ohne Enumerator-Allokation in eine Sammlung ein, für heiße Pfade. Eine Callback-Form gibt es nicht.
  - `IReadOnlyList<T> Nearest(Coordinate point, int k, Func<T, Coordinate, double>? distance = null)` liefert die k nächsten Elemente, aufsteigend nach Abstand.
  - Für Geometrien: `ToSpatialIndex()` auf `IEnumerable<TGeometry>` (Schlüssel ist `Envelope()`), `ToSpatialIndex(Func<T, Envelope2D>)` für beliebige Elemente und die Extension `Query(IGeometry2D)` mit dem Envelope als Vorfilter.
  - Optionale Parameter folgen ADR 0012.
- **Nur Vorfilter:** Der Index kennt Envelopes, keine Geometrien. Eine Abfrage liefert die Elemente, deren Envelope den Abfrage-Envelope berührt oder überlappt (abgeschlossene Intervalle). Der exakte Test bleibt beim Aufrufer, etwa `index.Query(g).Where(x => x.Intersects(g))`. Einen Prädikat-Shortcut gibt es bewusst nicht, um keine zweite API für Prädikate zu pflegen.
- **Nearest:** Standardmäßig ist der Abstand der vom Punkt zum Envelope des Elements. Der optionale `distance`-Delegate liefert den exakten Abstand und darf nie kleiner sein als der Envelope-Abstand, was für jede echte Geometrie innerhalb ihres Envelopes gilt. Die Suche ist Best-first mit exakter Verfeinerung. Gleichstände werden deterministisch nach Eingabereihenfolge aufgelöst, damit sich das Ergebnis gegen Brute Force vergleichen lässt.
- **Leere und ungültige Envelopes:**
  - Ein leerer Envelope (`Envelope2D.Empty`) in der Eingabe wird übersprungen, weil eine leere Geometrie nie etwas trifft. `Count` zählt nur indizierte Elemente.
  - Teilweise NaN, unendliche oder `Min > Max`-Envelopes werfen `ArgumentOutOfRangeException`, ebenso ein ungültiger Abfrage-Envelope, ein nicht endlicher Punkt, `k < 1` und `nodeCapacity < 2` (Regeln aus ADR 0012). Ein leerer Abfrage-Envelope liefert nichts. Liefert ein `distance`-Delegate NaN, wirft das eine `InvalidOperationException`.
  - Punkte und Envelopes mit Breite 0 (Linien) sind gültig.
- **Duplikate und Reihenfolge:** Duplikate bleiben erhalten, jedes Element ist ein eigener Eintrag. Die Reihenfolge der `Query`-Ergebnisse ist nicht festgelegt, `Nearest` ist geordnet. `T` darf `null` sein. Die Eingabe wird kopiert (Schnappschuss, ADR 0013).
- **Thread-Sicherheit:** Nach dem Bauen ist die Instanz unveränderlich, und jede Abfrage nutzt nur lokalen Zustand. Beliebig viele Threads dürfen dieselbe Instanz abfragen. `PreparedGeometry` baut darauf auf und ist ebenfalls thread-sicher (ADR 0024).
- **`SegmentIndex` blieb zunächst und wurde später ersetzt.** Der Benchmark zeigte damals keinen Gewinn für den Baum (das Gitter baute schneller, die Abfragen waren gleich schnell), also blieb der interne Gitterindex. ADR 0024 hat ihn durch diesen Baum ersetzt, damit `PreparedGeometry` thread-sicher wird, bei langsamerem Vorbereiten für kleine Polygone.
- **Tests (ADR 0008):** Orakel sind Brute Force (exakt, auch in der Reihenfolge bei Gleichstand) und der NTS-`STRtree` (Bereichsabfragen und Nearest-Abstände). Property-Tests (CsCheck) decken Zufallsmengen mit Duplikaten, Punkten, Linien und vielen gleichen Abständen ab, Knotenkapazitäten von 2 bis 20, leere und ungültige Eingaben und parallele Abfragen. Fixtures passen nicht, weil der Index kein Geometrie-Ergebnis hat; `[Covers]`-Tests treten an ihre Stelle.
- **Benchmarks:** `SpatialIndexBenchmarks` (Bauen, Abfrage, Nearest gegen Brute Force und den NTS-`STRtree`, 1k bis 1M Elemente, gleichmäßig und geclustert). Ein Einmallauf zeigt das Bauen bei 1M Elementen etwa zwei- bis dreimal so schnell wie beim NTS-`STRtree`, die Bereichsabfragen gleich schnell oder schneller.

## Considered Options

- Kein öffentlicher Index, Verweis auf die Host-Indizes (verworfen: eine Bibliothek ganz ohne Index lässt Nutzer bei Brute Force auf Listen im Speicher)
- Veränderbarer Index mit Insert und Remove (verworfen: nicht thread-sicher, schwerer zu testen, Neubau ist billig)
- Index nur über Geometrien (verworfen: Aufrufer indizieren oft Ids oder Datensätze; Geometrien deckt der Helfer ab)
- Prädikat-Shortcut wie `Query(geometry, predicate)` (verworfen: zweite API für Prädikate, `.Where` genügt)
- Gitterindex als öffentliche Struktur (verworfen: hängt von der Verteilung der Daten ab, STR passt sich an)
- Ersatz des internen `SegmentIndex` (damals verworfen mangels Gewinn; später in ADR 0024 für die Thread-Sicherheit erfolgt)
- Neues Paket für den Index (verworfen: `SpatialNET.Extensions` ist die Heimat der Algorithmen, ein Paket mehr lohnt nicht)
