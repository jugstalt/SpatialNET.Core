# Prädikate: volles DE-9IM, öffentliches Relate, Prepared-Geometrien als Schnappschuss

Alle Prädikate folgen dem **vollen DE-9IM-Modell** wie OGC und NTS und rechnen exakt auf den Eingabekoordinaten (ADR 0005). Der Kern berechnet die Schnittmatrix nach dem Muster von JTS RelateNG mit Fast-Paths (Envelope, Punkt-in-Polygon). Weil diese Matrix für korrekte `Contains`/`Touches` ohnehin entsteht, ist `Relate` öffentlich, und alle OGC-Prädikate sind nur noch Muster darauf. DE-9IM statt einer vereinfachten Lesart, weil es exakt gegen das Orakel prüfbar ist und die Hosts und ihre Datenbanken (PostGIS, SQL Server) genauso rechnen. Die intuitive Lesart „liegt drin, Rand zählt mit“ gibt es als `Covers`/`CoveredBy`.

*English version: [0013-praedikate-de9im-relate-prepared.md](0013-praedikate-de9im-relate-prepared.md)*

## Consequences

- **API:**
  - `Relate(a, b)` liefert `IntersectionMatrix`, `Relate(a, b, pattern)` liefert `bool`.
  - Prädikate: `Intersects`, `Disjoint`, `Contains`, `Within`, `Touches`, `Covers`, `CoveredBy`, `Crosses`, `Overlaps`, `EqualsTopo`.
  - Folge von DE-9IM: `polygon.Contains(linieAufDemRand)` ist falsch, `Covers` dagegen wahr.
- **Leere Geometrien:** `Disjoint` ist wahr, alle anderen Prädikate sind falsch, und `Relate` liefert eine Matrix nur aus `F`.
- **Ungültige Eingaben:** Punkt-in-Fläche-Tests nutzen Even-Odd (ADR 0003). Ergebnisse sind deterministisch, es gibt nie eine Exception. Ob die Prädikate untereinander konsistent sind, ist dann nicht zugesichert.
- **Randregel für Linien:** nur Mod-2 (OGC): Endpunkte sind Rand, geschlossene Pfade haben keinen Rand, bei Multi-Linien zählt nur eine ungerade Zahl von Pfadenden. Eine Endpoint-Option lässt sich später ohne Bruch ergänzen.
- **Prepared-Geometrien:** `Prepare()` liefert eine vorbereitete Geometrie mit Segment-Index und Punkt-in-Polygon-Struktur für wiederholte Prädikate (Intersects, Contains, Covers, Within, CoveredBy, Disjoint, Touches). Dafür wird ein **Schnappschuss** der Koordinaten kopiert, weil Host-Geometrien veränderbar sind. Der offene Kartenpunkt „Performance“ betrifft danach nur noch einen allgemeinen öffentlichen Index.

## Considered Options

- Vereinfachte Semantik („Contains = nichts liegt außen“) (verworfen: nicht OGC-konform, nicht gegen NTS testbar)
- Nur die fünf Prädikate aus der Charta, `Relate` intern (verworfen: kaum Ersparnis)
- Endpoint-Randregel bzw. wählbare Regel (verschoben)
- Prepared-Geometrie mit Referenz statt Schnappschuss (verworfen: undefiniert bei Änderungen am Host-Objekt)
