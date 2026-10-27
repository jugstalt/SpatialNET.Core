# Die Sichtbarkeitsfläche: was von einem Punkt aus zu sehen ist

`LineOfSight` (ADR 0041) beantwortet, ob ein Ziel zu sehen ist. Die Planung eines Windrads, eines Aussichtspunkts, eines Masts oder einer Straße fragt nach **allen Zielen auf einmal**: Welcher Teil der Landschaft ist von hier aus zu sehen, und welcher liegt hinter einem Kamm? `Viewshed` liefert das als Polygon.

*English version: [0044-sichtbarkeitsflaeche.md](0044-sichtbarkeitsflaeche.md)*

## Consequences

- **Ort:** ein Mitglied von `Triangulation` (ADR 0037) in einer eigenen Datei, `Triangulation.Viewshed.cs` in `SpatialNET.Extensions`. Für die Paketvalidierung und die API-Verfolgung ist das nur eine Ergänzung (ADR 0028).
- **API:**

  ```csharp
  IPolygon2D Viewshed(Coordinate observer, double maxDistance, double observerHeight = 0, double targetHeight = 0, int rays = 360, IGeometryFactory? factory = null)
  ```

  Das Auge liegt `observerHeight` über dem Gelände bei `observer`, ein Ziel `targetHeight` über dem Gelände an seinem Ort; das Ergebnis ist alles innerhalb von `maxDistance`, was zu sehen ist. `rays` liegt zwischen 8 und 100.000. Ein Punkt außerhalb der Fläche oder ein Eckpunkt ohne Höhe hat nichts zu sehen: ein leeres Polygon. Das Ergebnis kann mehrere Teile haben (und Löcher, wo eine Erhebung einen Teil des Geländes verdeckt).
- **Wie es arbeitet: ein Fächer von Strahlen, entlang jedes Strahls exakt.** `rays` Strahlen gehen in gleichen Winkeln vom Beobachter aus.
  - **Das Profil eines Strahls** ist die Linie über der Fläche, die `Drape` (ADR 0039) berechnet, mit der Höhe an jeder Kante, die sie kreuzt; zwischen zwei solchen Punkten ist die Fläche linear. Der Code von `Drape` wurde dafür geteilt (der Teil, der ein Segment drapiert, wird gemeinsam benutzt; `Drape` ist unverändert).
  - **Der Horizont wird exakt verfolgt.** Entlang des Profils wird die größte Steigung vom Auge zum bisherigen Gelände (der Horizont) mitgeführt. Über einem Abschnitt des Profils ist das Gelände `z = z₀ + s · (d − d₀)`, die Steigung vom Auge dorthin also `s + c / d` und zu einem Ziel darüber `s + c′ / d`: beide sind in der Entfernung `d` monoton, sodass jede Grenze eines sichtbaren Abschnitts die Lösung einer linearen Gleichung ist. Die sichtbaren Abschnitte eines Strahls kommen als exakte Intervalle heraus, nicht als Ergebnis einer Abtastung. Ein ansteigender Abschnitt, dessen Steigung über den Horizont steigt, wird zum neuen Horizont (das Gelände selbst ist zu sehen); ein abfallender liegt darunter, und ein Ziel darüber ist nur dort zu sehen, wo seine Steigung nicht unter dem Horizont liegt. Ein Abschnitt eines Strahls, der die Fläche verlässt, hat kein Gelände, das zu sehen ist, und versperrt die Sicht nicht. (Eine erste Fassung hielt Gelände, das hinter einem Kamm abfällt und dessen Steigung dort genau gleich dem Horizont ist, für Gelände auf dem Horizont und zeigte alles hinter einem Kamm: vom Test an einem Kamm gefunden.)
  - **Scheiben statt einer Vereinigung.** Ein Strahl steht für die Winkelscheibe um ihn, von einem halben Schritt davor bis einem halben Schritt danach. Die sichtbaren Abschnitte eines Strahls sind die radiale Ausdehnung seiner Scheibe. Benachbarte Scheiben treffen sich exakt am Winkel zwischen zwei Strahlen, also ist der Rand der sichtbaren Fläche das, was von den Seiten einer Scheibe übrig bleibt, wo der Nachbarstrahl nicht sieht (die Differenz zweier sortierter Intervalllisten), und die Bögen am nahen und am fernen Ende jedes Abschnitts. Diese Kanten, mit der Fläche links gerichtet, werden vom Code von `SlopeZones` (ADR 0041) zu Ringen verkettet: Löcher werden dem kleinsten Ring um sie gegeben, und ein Ring, der sich berührt, wird aufgetrennt. **Es gibt kein Overlay**: Die Vereinigung der Tausenden Vierecke durch `UnionAll` war der langsamste Teil (0,56 s gegen 0,075 s bei 360 Strahlen auf 10.000 Punkten). Die Enden der Abschnitte werden auf ein Milliardstel der Entfernung gerundet, sodass derselbe Ort entlang zweier Strahlen (das Ende der Entfernung, der Rand der Fläche) dieselbe Zahl ist und keinen Splitter an der Seite zwischen zwei Scheiben macht.
  - **Genau entlang der Strahlen, angenähert dazwischen.** Die Bögen sind Sehnen, und der Rand zwischen zwei Strahlen ist nur an den Strahlen bekannt: In der Entfernung `r` ist er auf etwa `r · π / rays` genau, das sind 1 % der Entfernung bei 360 Strahlen und 0,1 % bei 3600. Ein Kamm, der schmaler als eine Scheibe ist, kann zwischen zwei Strahlen hindurchrutschen. Die parallele Berechnung der Strahlen wird in der Reihenfolge der Strahlen zusammengefügt, das Ergebnis ist also bei jedem Lauf dasselbe.
- **Monoton.** Entlang jedes Strahls sieht ein höheres Auge, ein höheres Ziel oder eine größere Entfernung mindestens, was das niedrigere sah, und das Polygon ebenso (getestet). Ein Ziel, das `LineOfSight` sieht, liegt in der Sichtbarkeitsfläche, und eines, das es nicht sieht, liegt nicht darin, bis auf die Näherung zwischen den Strahlen (getestet gegen zufälliges Gelände: höchstens eines von zehn Zielen nahe einem Rand weicht ab, bei 720 Strahlen).
- **Geschwindigkeit.** Einmalmessungen (`--quick-terrain`, Release-Build, der Beobachter in der Mitte, ein Drittel der Ausdehnung weit schauend):

  | Punkte | 360 Strahlen | 1.440 Strahlen |
  |---|---|---|
  | 10.000 | 75 ms | 304 ms |
  | 50.000 | 250 ms | 2,7 s |

  Der Aufwand ist der von `rays` Linien über der Fläche, nicht der der Größe der Fläche, er wächst also mit der Zahl der Strahlen und der Entfernung.
- **Tests (ADR 0008):** eine Kreisfläche auf ebenem Gelände und ein Viertel davon an einer Ecke, ein Kamm, der verdeckt, was dahinter liegt (die Fläche und die Lage von Punkten vor und hinter ihm), Monotonie in den Höhen und der Entfernung, die Übereinstimmung mit `LineOfSight` für zufällige Ziele auf zufälligem Gelände, ein Beobachter außerhalb, keine Höhen, die Reihenfolge der Threads, Fehler; der Stresstest auf schmutzigen Punkten (gültige Polygone, innerhalb der Kreisfläche, mit der Höhe des Auges wachsend).
- **Playground und Dokumentation:** der Eintrag `viewshed` (die zweite Eingabe ist der Beobachter, das Hügelbeispiel wird geteilt), ein Kochbuch-Rezept „Was von einem Punkt aus zu sehen ist“, die Konzeptseite, das Glossar.
- **Nicht jetzt:** die Erdkrümmung und die Lichtbrechung der Luft (die Fläche ist eben, ADR 0010; für eine Sicht über Dutzende Kilometer vorher projizieren und die Höhen korrigieren), die Sichtbarkeitsfläche einer Linie oder einer Fläche (die Vereinigung der Sichtbarkeitsflächen ihrer Punkte; `Viewshed` je Punkt aufrufen und vereinigen), eine Sicht, die im Winkel begrenzt ist (ein Kegel, ein Gesichtsfeld), und ein exakter Rand zwischen den Strahlen (die Sichtbarkeitsregion einer Triangulierung ist ein Polygon mit gekrümmten Kanten, das ein Fächer von Strahlen annähert).

## Considered Options

- `LineOfSight` zu jeder Zelle eines Rasters prüfen (verworfen: ein Raster, mit dem Aufwand Zellen mal Länge der Linie, und das Ergebnis ist eine Treppe; die Strahlen folgen dem Horizont exakt, und das Ergebnis ist ein Polygon)
- Die Scheiben mit `UnionAll` vereinigen (verworfen: achtmal langsamer; benachbarte Scheiben treffen sich an einem exakten Winkel, ihre gemeinsamen Seiten müssen also nicht von einem Overlay doppelt berechnet werden)
- Strahlen an den Eckpunkten der Triangulierung (das exakte Verfahren von Franklin und Ray, ein Rundumlauf um den Beobachter, der an jedem Eckpunkt und jeder Kante anhält) (vorerst verworfen: Es gibt den exakten Rand, ist aber ein viel größeres Stück Arbeit mit vielen Sonderfällen (kollineare Eckpunkte, Kanten durch den Beobachter); der Fächer beantwortet die Frage für die genannten Anwendungen, mit einer Genauigkeit, die ein Parameter ist)
- Eine kleinere Voreinstellung der Zahl der Strahlen oder eine Regel aus der Entfernung (verworfen: Die Genauigkeit hängt davon ab, wie das Ergebnis benutzt wird; 360 Strahlen sind ein Grad, und die Dokumentation sagt, wie der Fehler wächst)
- Ein Raster sichtbarer Zellen zurückgeben (verworfen: Die Bibliothek arbeitet mit Vektorgeometrie; ein Raster kann der Aufrufer aus dem Polygon machen)
