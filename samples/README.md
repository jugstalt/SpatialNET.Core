# Fixtures

Gemeinsame Testfälle für Tests und Playground (ADR 0008). Eine JSON-Datei pro Fall unter
`samples/<algorithmus>/`; der Ordnername ist der Algorithmus (Methodenname in Kleinbuchstaben, z. B. `area`).

```json
{
  "name": "Insel im Loch",
  "description": "optional",
  "tags": ["nested", "multi", "playground"],
  "a": "WKT",
  "b": "WKT (optional, zweiter Operand)",
  "params": { "distance": 10 },
  "crs": "EPSG:25833 (optional; ohne = lokal planar)",
  "expected": "optional: Soll statt NTS-Orakel (WKT oder Zahl)",
  "divergence": "Pflicht, wenn expected bewusst vom NTS-Orakel abweicht",
  "tolerance": { "area": 1e-6 }
}
```

- **Soll:** berechnet NTS zur Laufzeit (`Oracle/`); nur Fälle mit `expected` nutzen den gespeicherten Wert.
- **Playground:** zeigt Fälle mit Tag `playground` als Beispielgeometrien.
- **Toleranzen:** zentral in `OracleAssert`, per `tolerance` überschreibbar.

## Pflicht-Tag-Katalog

Jeder Algorithmus braucht mindestens einen Fall je Tag. Trifft ein Tag fachlich nicht zu, wird er in
`samples/<algorithmus>/_catalog.json` begründet ausgenommen: `{ "n/a": { "z": "Begründung" } }`.

| Tag | Bedeutung |
|---|---|
| `empty` | leere Eingabe |
| `degenerate-ring` | Ring ohne Fläche bzw. unter 3 Punkten |
| `collinear-overlap` | kollineare Kanten bzw. Überlappung |
| `vertex-touch` | Berührung in einem Vertex |
| `hole-touch` | Loch berührt den Außenring |
| `self-crossing` | ungültige Eingabe nach ADR 0003 |
| `near-coincident` | Abstände im ULP-Bereich |
| `large-coords` | Koordinaten ≥ 1e6 (UTM-artig) |
| `z` | 3D-Eingaben |
| `multi` | mehrere Teilflächen bzw. Pfade |
| `nested` | Insel im Loch |

Der Test `FixtureCompletenessTests` erzwingt Katalog und Abdeckung jeder öffentlichen Methode.
