# LocateFeature: von einer Geometrie zu Bereichen von M entlang einer Route

`LocateBetween` (ADR 0032) macht aus einem Bereich von M eine Strecke der Straße. Ein Routensystem braucht auch den Rückweg: *Zu welchem Teil der Route gehört dieses Flurstück, diese Leitung, diese überflutete Fläche?* Ein Flurstück neben einer Straße wird zu „von 4 200 bis 4 900“, und dieser Bereich lässt sich als Ereignis speichern und mit den anderen verrechnen (ADR 0033). **`LocateFeature`** berechnet ihn: Die Geometrie wird um eine Toleranz erweitert und mit der Route geschnitten, und die M-Werte dessen, was übrig bleibt, sind die Bereiche.

*English version: [0034-locatefeature-geometrie-zu-m-bereichen.md](0034-locatefeature-geometrie-zu-m-bereichen.md)*

## Consequences

- **Ort:** `LocateFeature` in `LinearReferencing` (ADR 0017), in `SpatialNET.Extensions`, Namespace `SpatialNET`. Für die Paketvalidierung und die API-Verfolgung ist das eine reine Ergänzung (ADR 0028).
- **API** (eine Erweiterungsmethode auf `IPolyline2D`, der Route):

  ```csharp
  IReadOnlyList<(double From, double To)> LocateFeature(this IPolyline2D route, IGeometry2D feature,
      double tolerance, IGeometryFactory? factory = null)
  ```

  Die Geometrie ist eine Polylinie oder ein Polygon. Das Ergebnis ist eine **Liste einfacher `(From, To)`-Paare**, sortiert, und keine `LineEvent<T>`-Werte: Das Attribut eines Ereignisses gehört dem Host, und `ranges.Select(r => new LineEvent<T>(r.From, r.To, value))` ist eine Zeile. Ein `LineEvent<T>` mit einem Wert-Parameter würde zwei Aufgaben mischen.
- **Die Toleranz ist Pflicht und hat keinen Standardwert**, wie bei `CalibrateM` (ADR 0030): Karteneinheiten unterscheiden sich zu stark für einen Standard. Für ein **Polygon** darf sie 0 sein (nur die Fläche selbst, mit Rand); für eine **Linie** muss sie größer als 0 sein, da eine Linie keine Fläche hat und „genau auf der Route“ ein Puffer nicht ausdrücken kann. Eine negative, `NaN` oder unendliche Toleranz ist eine `ArgumentOutOfRangeException`.
- **Algorithmus.** Die Geometrie wird um die Toleranz gepuffert (`Buffer`, ADR 0006; bei einem Polygon mit 0 wird es nach Even-Odd normalisiert, ungültige Eingaben werden also wie üblich gelesen), und die Route wird per Overlay (`Intersection`, ADR 0005) mit dem entstandenen Polygon geschnitten, das M mitführt und an neuen Stützpunkten interpoliert (ADR 0015). Jedes Stück der Route in der Zone ergibt einen Bereich: den **niedrigsten und den höchsten M-Wert seiner Stützpunkte**. Da M auf einem Segment linear interpoliert wird, ist das genau das M, das das Stück abdeckt, auch wo M fällt oder das Stück zurückknickt; ein Stück braucht kein monotones M. Bereiche, die sich berühren oder überlappen, werden danach verbunden.
- **Regeln, wie bei den anderen M-Methoden (ADR 0017):**
  - Nur Segmente mit M an beiden Enden nehmen teil. Ein Stück wird an einem Stützpunkt ohne M geteilt, und die Strecke ohne M bleibt draußen; eine Route ohne M ergibt eine leere Liste.
  - Ein Stück **ohne Länge** ist kein Bereich: Eine Geometrie, die die Route nur in einem Punkt berührt, ergibt nichts, wie `LocateBetween` keine Strecke der Länge 0 liefert (ADR 0032).
  - Eine Route, die die Geometrie zweimal passiert, ergibt zwei Bereiche, wenn sie in M auseinanderliegen, und einen, wenn sie sich in M berühren oder überlappen. Die Bereiche sind Bereiche von M, nicht von Durchgängen: Wo das M zweier Durchgänge überlappt, lassen sie sich nicht unterscheiden.
  - Die Route darf in beide Richtungen von M laufen; ein Bereich ist immer `From` &lt; `To`.
- **Genauigkeit.** Die M-Werte an den Enden eines Bereichs stammen aus dem Overlay und sind auf etwa dessen Gitter genau (ADR 0005), nicht auf das letzte Bit. `gridSize` wird nicht herausgegeben; das automatische Gitter ist für Kartendaten in Ordnung.
- **Die Factory muss M unterstützen.** Die Route wird über die Factory geschnitten, eine Factory ohne M würde also das M verlieren, aus dem das Ergebnis besteht: `InvalidOperationException`, wie bei `CalibrateM`. Eine leere Route, eine leere Geometrie oder ein weggeschrumpftes Polygon ergeben eine leere Liste.
- **Punkte sind hier keine Geometrien.** Ein Punkt hat einen M-Wert, keinen Bereich, und `LineEvents` ignoriert Bereiche ohne Länge. Ein Punkt oder ein Multipoint ist eine `ArgumentException`, die auf `ProjectToM` verweist; mit Abstandsprüfung ergänzt der Aufrufer `IsWithinDistance`. Bei einer `IGeometry2D`-Implementierung mit mehreren Geometriearten gilt der erste Treffer (Polygon, dann Polylinie).
- **Kosten:** ein Puffer und ein Overlay der Route damit, O(n log n) in der Größe beider. Für viele Geometrien gegen eine lange Route würden ein Vorfilter über die Envelope und eine Batch-Variante sich lohnen; sie gehören nicht zu diesem Schritt.
- **Tests (ADR 0008):** NTS hat kein Gegenstück, die Fälle sind von Hand geschrieben (`[Covers]`): eine Fläche quer über der Straße, mit gemeinsamer Kante, daneben mit und ohne Toleranz, eine Linie daneben, eine Route, die zweimal vorbeiläuft, Verbinden an einem Stützpunkt, fallendes M, Strecken ohne M, eine Berührung in einem Punkt, leere Eingaben, die Fehlerfälle und eine Verwendung des Ergebnisses als Ereignisse (`Intersect` mit einer Tempotabelle). M wird mit den exakten Werten verglichen, mit kleinem Delta dort, wo der Puffer rund ist.
- **Playground und Dokumentation:** kein Deskriptor (ADR 0011): Das Ergebnis ist eine Liste von Zahlenpaaren, die die Oberfläche nicht zeigt, und es braucht eine Route mit M und eine zweite Geometrie; mit dieser Begründung als ausgenommen geführt. Die Konzeptseiten „Lineare Referenzierung“ und „Ereignistabellen“ haben einen Abschnitt, und das Cookbook-Rezept zu Ereignistabellen endet damit.
- **Vorerst nicht dabei:** Seite und Abstand der Geometrie zur Route, ein Batch für viele Geometrien mit Envelope-Vorfilter, Punkte, ein Längenfenster, damit eine Geometrie neben einer Schleife einen Durchgang wählt (`Project` hat eines, ADR 0031), und ein Gitterweiten-Parameter.

## Considered Options

- `LineEvent<T>` mit Wert-Parameter zurückgeben, verworfen: Das Attribut gehört dem Host; zwei Zeilen LINQ halten die beiden Aufgaben auseinander
- Eine Standard-Toleranz, verworfen: wie bei `CalibrateM` passt kein Wert zu jedem Koordinatensystem
- Eine Toleranz 0 für Linien über den Schnitt zweier Polylinien, verworfen: gemeinsame Stücke zweier Linien hängen an exakter Kollinearität, die echte Daten selten haben, und das Ergebnis sähe exakt aus und wäre es nicht
- Die Bereiche über die Projektion jedes Stützpunkts der Geometrie (`ProjectToM`) mit niedrigstem und höchstem Wert berechnen, verworfen: Eine Geometrie, die um eine Ecke biegt oder eine Kurve der Route überspannt, ergäbe einen zu langen oder zu kurzen Bereich; der Schnitt mit der Route ist die richtige Antwort
- Anfangs- und End-M jedes Stücks statt niedrigstem und höchstem, verworfen: Ein Stück mit Spitzkehre oder fallender Strecke würde einen Teil des Bereichs verlieren
- Sich berührende Bereiche nicht verbinden, verworfen: Das Overlay kann ein Routenstück an einem Stützpunkt teilen, und der Aufrufer sähe zwei Bereiche, die einer sind
- Punkte mit einem Bereich der Länge 0 einschließen, verworfen: `LineEvents` ignoriert Bereiche ohne Länge; für einen Punkt ist `ProjectToM` der richtige Aufruf
- Bei einer Route ohne M werfen, verworfen wie in ADR 0017: Eine Strecke, die nicht antworten kann, ist eine Lücke, kein Fehler
