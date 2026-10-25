# CalibrateM: M-Werte für eine Linie aus Ankerpunkten

Im WebGIS liegt ein Straßenabschnitt oft als Polylinie **ohne M** vor, daneben **Punkte auf der Linie, die M tragen** (zum Beispiel Kilometersteine). Um das M einer angeklickten Stelle zu bestimmen, sucht der Host den vorherigen und den nächsten Anker und interpoliert linear in der Station dazwischen. **`CalibrateM`** macht das einmal und behält das Ergebnis: Es projiziert die Anker auf die Linie, fügt an jedem einen Stützpunkt mit M ein und interpoliert M entlang der Linie dazwischen. Das Ergebnis ist eine Polylinie mit M, und `ProjectToM` und `InterpolateByM` (ADR 0017) antworten daraus genau wie die Formel von Hand. Das Kalibrieren macht aus einer Tabelle von Punkten außerdem eine Linie, die man speichern und wie jede andere Linie mit M abfragen kann.

*English version: [0030-calibratem-m-werte-aus-ankerpunkten.md](0030-calibratem-m-werte-aus-ankerpunkten.md)*

## Consequences

- **Ort:** `CalibrateM` in `LinearReferencing` (ADR 0017) und der Enum `MExtrapolation`, in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **API** (Erweiterungsmethoden auf `IPolyline2D`; Parameterreihenfolge wie in ADR 0012):

  ```csharp
  IPolyline2D CalibrateM(IEnumerable<Coordinate> anchors, double maxSnapDistance,
      MExtrapolation extrapolation = MExtrapolation.None, IGeometryFactory? factory = null)
  ```

  Eine Überladung nimmt `IEnumerable<IPoint2D>` und liest M über `IPointM` (ein Punkt ohne es hat kein M). Die Anker sind **Werte** (`Coordinate` mit X, Y und M), keine Geometrien, wie die Helfer aus ADR 0019. Das Ergebnis ist eine Geometrie und kommt aus der Factory.
- **Die Toleranz ist Pflicht und hat keinen Standardwert.** `maxSnapDistance` ist in Karteneinheiten; kein Wert passt zu jedem Koordinatensystem, also nennt ihn der Aufrufer. Ein Anker, der weiter von der Linie entfernt ist, ist **ein Fehler** (`ArgumentException`, die den Anker und seinen Abstand nennt), nie stillschweigend übersprungen: Ein Punkt, der nicht auf der Straße liegt, ist ein Datenproblem, das der Aufrufer sehen soll. 0 ist erlaubt und heißt „genau auf der Linie“.
- **Algorithmus.** Jeder Anker wird wie bei `Project` (ADR 0017) auf die Linie projiziert: nächster Punkt, bei gleich nahen die kleinste Länge. Das ergibt seine Station (Länge entlang der Linie, Pfade aneinandergelegt). Die Anker werden nach Station sortiert, ihre Reihenfolge in der Eingabe ist also egal. An jeder Station, die nicht schon auf einem Stützpunkt liegt, wird ein Stützpunkt eingefügt (er liegt auf der Linie, nicht auf dem eigenen X und Y des Ankers, und bekommt interpoliertes Z); eine Station innerhalb von 1e-9 der Linienlänge von einem Stützpunkt wird auf diesen gelegt, damit keine fast doppelten Stützpunkte entstehen. Jeder Stützpunkt bekommt M durch **lineare Interpolation in der Station** zwischen den Ankern beiderseits: die Formel `M1 + (M2 − M1) / (s2 − s1) · (s − s1)`. M muss nicht monoton sein. M, das die Linie schon trägt, wird **ignoriert und ersetzt**; Z bleibt.
- **Anker an derselben Station.** Stationen innerhalb von 1e-9 der Linienlänge sind dieselbe Position. Gleiches M fasst sie zusammen; **verschiedenes M ist ein Fehler**, weil die Linie an einem Punkt zwei Messwerte bräuchte.
- **Vor dem ersten und nach dem letzten Anker** entscheidet `MExtrapolation`. `None` (Standard): kein M, die Strecke kann also nicht antworten, wie jede Linie ohne M (ADR 0017: `ProjectToM` liefert `NaN`); es wird kein Messwert erfunden. `Clamp`: das M des nächsten Ankers. `Linear`: die Steigung des ersten oder letzten Intervalls wird fortgesetzt; das braucht Anker an zwei verschiedenen Stationen und ist sonst ein Fehler.
- **Weitere Fehler (ADR 0012):** `ArgumentNullException` bei `null`; `ArgumentOutOfRangeException` bei negativem, `NaN` oder unendlichem `maxSnapDistance`, nicht endlichem X oder Y eines Ankers oder einem undefinierten Enum-Wert; `ArgumentException` bei **keinem Anker**, einem Anker ohne endliches M und den Fällen oben; `InvalidOperationException` ohne Factory **oder mit einer Factory ohne M-Unterstützung** (das Ergebnis hätte keins, was den Aufruf sinnlos macht). Eine Linie ohne Punkte ergibt eine leere Polylinie.
- **Bekannte Grenze: Schleifen.** Eine Linie, die sich kreuzt oder auf sich zurückläuft, hat für einen Anker nahe der Kreuzung mehrere nächste Punkte; die kleinste Länge gewinnt, wie bei `Project`. Ein Anker auf dem späteren Durchgang landet daher auf dem früheren. Eine Projektion auf ein Längenfenster (JTS `IndexOfAfter`) löst das und ist ein eigener Schritt, siehe unten.
- **Kosten:** O(Anker · Stützpunkte): Jeder Anker wird durch einen Durchlauf projiziert, wie bei `Project`, und das Einrasten auf Stützpunkte ist ebenfalls ein linearer Durchlauf. Für Straßenabschnitte genügt das; ein Längenindex für viele Anker auf einer langen Linie kommt mit dem Index, den ADR 0017 schon aufschiebt.
- **Tests (ADR 0008):** NTS hat kein Gegenstück, die Fälle sind von Hand geschrieben (`[Covers("CalibrateM")]`) und vergleichen mit der Formel des Hosts (M nach Station zwischen vorherigem und nächstem Anker), dazu eingefügte Stützpunkte, Einrasten, Toleranz, Reihenfolge, nicht monotones M, Zusammenfassen und Konflikte, alle drei Extrapolationen, Z, mehrere Pfade, `IPoint2D`-Eingabe und die Fehlerfälle.
- **Playground und Dokumentation:** von der Deskriptor-Regel ausgenommen (ADR 0011): Es braucht eine Liste von Ankerpunkten mit M als zweite Eingabe, die die Oberfläche nicht bietet (und GeoJSON hat kein M, ADR 0015). Ergänzt auf der Konzeptseite „Lineare Referenzierung“.
- **Vorerst nicht dabei:** Projektion mit Längenfenster (`ProjectAfter`), ein Ergebnis mit Abstand und Seite des nächsten Punktes, alle Treffer für einen M-Wert, `LocateBetween` (alle Strecken, deren M in einem Bereich liegt), ein seitlicher Versatz in `Interpolate` und Ereignistabellen.

## Considered Options

- Das M eines Klicks direkt aus den Ankern berechnen (`InterpolateM(line, anchors, click)`), verworfen: Die kalibrierte Linie lässt sich speichern, zwischenspeichern und für jede spätere Abfrage nutzen, die direkte Form würde die Projektion aller Anker pro Klick wiederholen
- Ein Standardwert für `maxSnapDistance`, verworfen: Karteneinheiten unterscheiden sich zu stark; ein falscher Standard würde gute Daten ablehnen oder schlechte annehmen
- Anker jenseits der Toleranz überspringen, verworfen: Ein Punkt, der nicht auf der Straße liegt, ist ein Datenproblem, und stilles Weglassen ändert die Messwerte
- M nach der Luftlinie zwischen den Ankern statt nach der Station interpolieren, verworfen: M läuft entlang der Linie, und die bisherige Formel nimmt die Station
- Standardmäßig extrapolieren, verworfen: Die Bibliothek erfindet keine Messwerte (ADR 0017); `Clamp` und `Linear` sind eine ausdrückliche Wahl
- Das eigene M der Linie behalten, wo die Anker keins geben, verworfen: Es würde zwei Quellen für Messwerte mischen
- M nur auf vorhandene Stützpunkte setzen und keine einfügen, verworfen: M wäre zwischen Stützpunkten falsch, wo ein Anker mitten in einem Segment liegt
- Schleifen über die Reihenfolge der Anker auflösen, verworfen für jetzt: Die Reihenfolge der Eingabe sagt nichts über die Linie; die Fensterprojektion ist das bessere Mittel
