# Locate, Envelope-Helfer und Rechteck-Clip

Drei kleine Bausteine schließen Lücken, auf die jeder Host stößt: **`Locate`** (wo liegt ein Punkt relativ zu einer Geometrie: innen, auf dem Rand oder außen), **Helfer auf `Envelope2D`** (`Intersects`, `Contains`, `Union`, `Intersection`, `Expand`, `Center`, dazu `ToPolygon`) und **`Clip`** (eine auf ein Rechteck zugeschnittene Geometrie, der Kachel- und Ausschnittsfall). `Locate` macht öffentlich, was die Prädikate intern schon berechnen. `Clip` ist ehrlich gesagt eine Bequemlichkeit mit Schnellpfaden: Sein Kern ist der vorhandene Schnitt mit dem Rechteck. Sie folgen den schon festgelegten Regeln (exakte Entscheidungen ohne Gitter für das, was nicht konstruiert wird, das Overlay-Gitter für das, was konstruiert wird, ADR 0005; leere und ungültige Eingaben werfen nie, ungültige Zahlenparameter werfen, ADR 0012).

*English version: [0022-locate-envelope-helfer-und-clip.md](0022-locate-envelope-helfer-und-clip.md)*

## Consequences

- **`Location`** ist eine öffentliche Aufzählung in `SpatialNET.Extensions`, Namespace `SpatialNET`, mit `Outside`, `Boundary` und `Inside` (die interne Aufzählung gleichen Namens zieht dorthin um). Der Name ist der von JTS und NTS; Code, der `SpatialNET` und `NetTopologySuite.Geometries` zugleich importiert, muss ihn qualifizieren, was das Testprojekt mit einem Alias tut.
- **`Locate(this IGeometry2D geometry, Coordinate point)`** (und eine Überladung für `IPoint2D`) in der neuen statischen Klasse `Locations` liefert, wo der Punkt liegt:
  - **Polygon:** nach der Even-Odd-Regel über alle Ringe (ADR 0003): `Inside` in der Fläche, `Outside` auch in einem Loch, `Boundary` auf jedem Ring, auch dem eines Lochs. Ringe ohne Fläche werden ignoriert, und Ringe, die sich gegenseitig aufheben, wie beim Bilden der Teilflächen.
  - **Polylinie:** `Boundary` auf einem Endpunkt nach der Mod-2-Regel (ADR 0013), sonst `Inside` auf der Linie, `Outside` abseits.
  - **Punkt und Multipunkt:** `Inside`, wenn der Punkt einem von ihnen gleicht, sonst `Outside`; Punkte haben keinen Rand.
  - **Leere Geometrie:** `Outside`. Ungültige Polygone werden deterministisch nach Even-Odd gelesen.
  - Die Entscheidungen sind exakt (orient2d), wie bei den Prädikaten, `Locate` stimmt also mit `Relate` überein: Bei einem Polygon und einem Punkt ist `Inside` gleich `Contains`, `Outside` gleich `Disjoint`, und `Boundary` ist der Rest von `Intersects`.
  - Ein Punkt mit `NaN` oder unendlicher Koordinate wirft `ArgumentOutOfRangeException`; `null` wirft `ArgumentNullException`.
- **`PreparedGeometry.Locate(Coordinate)`** liefert dieselbe Antwort für wiederholte Punkte gegen eine Geometrie: Ein Polygon geht über den Segmentindex, alles andere über die unvorbereitete Berechnung. `PreparedGeometry` bleibt nicht thread-sicher (ADR 0013). Eine Batch-Überladung mit Spans gibt es in diesem Schritt nicht.
- **Envelope-Helfer** sind Instanzmitglieder von `Envelope2D` in `SpatialNET.Core` (rein additiv, binärkompatibel; ein Werttyp mit Arithmetik bleibt im Rahmen von „Core = Verträge und Werttypen“, ADR 0002):
  - `Intersects(Envelope2D)` und `Contains(Envelope2D)` rechnen den Rand zur Envelope (geschlossene Intervalle); `Contains(Coordinate)` ebenso. Eine leere Envelope trifft nichts, enthält nichts und liegt in nichts.
  - `Union(Envelope2D)` ist die kleinste Envelope, die beide enthält (die andere, wenn eine leer ist), `Intersection(Envelope2D)` der gemeinsame Teil (`Empty`, wenn sie sich nicht treffen), `Center` die Mitte (NaN, wenn leer).
  - `Expand(double distance)` weitet allseitig; eine negative Distanz schrumpft und ergibt `Empty`, wenn nichts übrig bleibt. Core wirft nicht: Eine leere Envelope oder eine `NaN`-Distanz ergibt `Empty`.
  - `ToPolygon(factory)` in der statischen Klasse `Envelopes` (`SpatialNET.Extensions`) erzeugt das Rechteck als Polygon, gegen den Uhrzeigersinn und geschlossen. Eine leere Envelope oder eine ohne Fläche (Breite oder Höhe null) ergibt ein leeres Polygon. Eine ungültige Envelope (nicht endlich oder Min größer als Max) wirft `ArgumentOutOfRangeException`.
- **`Clip(this IPolygon2D, Envelope2D, factory, gridSize)`** und die Überladungen für `IPolyline2D`, `IMultiPoint2D` und `IGeometry2D` in `Overlays` liefern den Teil im **geschlossenen** Rechteck:
  - Polygon und Polylinie laufen durch den Overlay mit dem Rechteck als Polygon (ein Schnitt, eine Polylinie behält also die Teile innen und auf dem Rand; Strict-Semantik, ADR 0005); Punkte werden exakt gefiltert und nie gerundet. `gridSize` folgt der Kaskade aus ADR 0005.
  - **Schnellpfade:** Eine Geometrie, deren Envelope vollständig im Rechteck liegt, kommt als Kopie **ohne Runden** zurück (ein Polygon als normalisierte Kopie: Orientierung korrigiert, Ringe ohne Fläche entfallen; ungültige Eingaben werden nicht repariert, dafür gibt es `MakeValid`), und eine Geometrie, deren Envelope das Rechteck nicht trifft, ergibt eine leere Geometrie. Nur wo der Rand des Rechtecks schneidet, läuft der Overlay, und nur diese Ergebnisse liegen auf dem Gitter. Dieser Unterschied ist dokumentiert.
  - Ein Rechteck ohne Fläche (Breite oder Höhe null) ergibt ein leeres Polygon bzw. eine leere Polylinie; ein Multipunkt wird weiter gefiltert, denn ein Punkt kann auf einem entarteten Rechteck liegen. Eine leere Envelope ergibt ein leeres Ergebnis; eine ungültige wirft `ArgumentOutOfRangeException`.
  - Z und M werden wie bei `Intersection` mitgeführt (ADR 0005, ADR 0015). Die Ecken des Rechtecks, die zu Stützpunkten des Ergebnisses werden, gehören zum Rechteck, das kein Z und M hat, sie tragen also keines.
- **Kein vorbereiteter Clip** (ein Polygon gegen viele Objekte) in diesem Schritt; der vorhandene `Intersection` mit einer `PreparedGeometry` ist ebenfalls nicht Teil der API.
- **Tests (ADR 0008):**
  - `locate`-Fixtures tragen die Geometrie; der Test prüft jeden Stützpunkt, jeden Segmentmittelpunkt, die Mitte der Envelope und Punkte knapp außerhalb und vergleicht jede Antwort mit dem NTS-`PointLocator` (bei Polygonen auf der Geometrie in der Semantik von ADR 0003). Property-Tests: `Locate` stimmt mit `Contains`, `Disjoint` und `Intersects` der Prädikate überein, die vorbereitete Antwort gleicht der unvorbereiteten, ein Loch ist außen und sein Ring ist Rand.
  - `clip`-Fixtures tragen die Geometrie und das Rechteck als Parameter (`minX`, `minY`, `maxX`, `maxY`) und werden mit NTS-`OverlayNG` verglichen, das dasselbe Rechteck auf demselben Gitter schneidet; zusätzliche Fixtures für ein Rechteck, das die Geometrie enthält, und eines, das sie verfehlt. Property-Tests: Das Ergebnis liegt im Rechteck, eine innen liegende Geometrie kommt mit identischen Koordinaten zurück, und `Clip` gleicht `Intersection` mit `ToPolygon` bis auf das Gitter.
  - Die Envelope-Helfer werden gegen Brute-Force-Definitionen auf zufälligen Rechtecken geprüft, auch leeren und mit sich berührenden Rändern.
- **Playground und Dokumentation:** `Clip` bekommt einen Deskriptor mit vier Zahlenparametern (ADR 0011). `Locate` und `ToPolygon` sind ausgenommen: Das erste antwortet mit einer Kategorie, die die Oberfläche nicht anzeigt, das zweite ist eine Umwandlung und kein Algorithmus. Ein Abschnitt in den Konzeptseiten zu Geometriemodell und Overlays, Glossareinträge.

## Considered Options

- Ein anderer Name für `Location`, etwa `PointLocation` (verworfen: Der Name von JTS und NTS ist das, was Leute suchen; der Konflikt beschränkt sich auf Code, der beide Namespaces importiert)
- `Locate` liefert ein Boolesches Paar oder nur `Contains`/`Touches` (verworfen: `Relate` gibt schon die Matrix; eine dreiwertige Antwort ist, was wiederholte Punktabfragen wollen)
- Ein Batch `Locate(ReadOnlySpan<Coordinate>, Span<Location>)` (verworfen für jetzt: Noch verlangt es niemand, und es lässt sich ohne Bruch ergänzen)
- Envelope-Helfer als Extension-Methoden in `SpatialNET.Extensions` (verworfen: Es ist schlichte Arithmetik auf einem Werttyp aus `SpatialNET.Core`; Instanzmitglieder findet man ohne `using`)
- `Envelope2D.ToPolygon` in Core (verworfen: Es braucht die Factory, und `SpatialNET.Core` ist nur Verträge und Werte)
- `Clip` per Sutherland–Hodgman auf den Ringen (verworfen: Bei konkaven Polygonen hinterlässt es entartete Brücken am Rand, die ein Overlay ohnehin reparieren müsste; der Schnitt mit dem Rechteck ist für jedes Polygon richtig)
- `Clip` rundet jedes Ergebnis aufs Gitter (verworfen: Beim Kacheln sollen vollständig innen liegende Geometrien unverändert bleiben; die Regel „gerundet nur, wo geschnitten wird“ ist leicht zu sagen)
- Ein vorbereiteter Clip oder Clippen an einem beliebigen Polygon als eigene Methode (verworfen: Das ist `Intersection`)
