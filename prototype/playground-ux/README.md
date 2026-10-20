# PROTOTYPE – Playground-UX (Ticket #9)

Wegwerfcode. Frage: Wie sieht der Playground aus, und wie entsteht seine UI aus registrierten Algorithmen?

- Öffnen über einen beliebigen statischen Server, z. B. `python -m http.server 8765` in diesem Ordner, dann `http://localhost:8765/index.html?variant=A|B|C`. Umschalten mit der Leiste unten oder den Tasten ← →.
- **A** Werkbank (3 Spalten), **B** Karte zuerst (Befehlspalette, Chips, Beispiel-Leiste), **C** Katalog (Fixture-Tabelle + Text).
- Berechnungen sind eine **Attrappe** mit Turf.js. `REGISTRY`/`FIXTURES`/`run()` sind Mocks von `GET /api/algorithms`, `GET /api/fixtures` und `POST /api/run`.

**Ergebnis:** Variante **B** wurde gewählt, siehe docs/adr/0011 auf main und den Resolution-Kommentar in #9.
