# Glossar

Die verbindlichen Definitionen der Begriffe stehen im [GLOSSARY.md](https://github.com/jugstalt/SpatialNET.Core/blob/main/GLOSSARY.md) im Wurzelordner des Repositorys. Diese Seite ordnet die deutschen Begriffe den englischen zu, die im Code und in der englischen Dokumentation verwendet werden.

## Geometrien

| Deutsch | English |
|---|---|
| Geometrie-Interface | geometry interface |
| Host | host |
| Host-Geometrie | host geometry |
| Pfad | path |
| Ring | ring |
| Loch | hole |
| Teilfläche | part (polygon part, `PolygonPart`) |
| Polylinie | polyline |
| Polygon | polygon |
| Envelope | envelope |
| Referenz-Implementierung | reference implementation |

## Berechnung

| Deutsch | English |
|---|---|
| Algorithmus | algorithm |
| Geometrie-Factory | geometry factory |
| Schnittlinie | cutting line |
| Offset-Kurve | offset curve |
| Füllregel | fill rule |
| Gitterweite | grid size |
| Windungszahl | winding number |
| Snap-Rounding | snap rounding |
| Even-Odd-Regel | even-odd rule |
| Strict-Semantik | strict semantics |
| räumlicher Index | spatial index |
| vorbereitete Geometrie | prepared geometry |
| lineare Referenzierung | linear referencing |
| M-Wert | measure (M) |
| Kalibrieren | calibrating (`CalibrateM`) |
| Ereignis | event (`LineEvent<T>`) |
| Punktereignis | point event (`PointEvent<T>`) |
| Neigung | slope (`Triangulation.Slope`) |
| Exposition | aspect (`Triangulation.Aspect`) |
| Drapieren | draping (`Triangulation.Drape`) |
| Natürliche Nachbarn | natural neighbour (`Triangulation.InterpolateNaturalNeighbour`) |
| Qualitätsnetz | quality mesh (`Triangulation.Refine`) |
| Glättung | smoothing (`Smooth`, `Triangulation.Resample`) |
| Höhenstufe | height band (`Triangulation.ContourBands`) |
| Sichtlinie | line of sight (`Triangulation.LineOfSight`) |
| Voronoi-Zelle | Voronoi cell (`VoronoiCell`) |
| Triangulierung | triangulation (`Triangulation`) |
| Bruchkante | break line (constraint) |
| Höhenlinie | contour line (`Triangulation.Contour`) |
| seitlicher Versatz | offset to the side (`Interpolate(distance, offset)`) |

## Playground und Tests

| Deutsch | English |
|---|---|
| Playground | playground |
| Rechen-KBS | computation CRS |
| Beispielgeometrie | sample geometry |
| Fixture | fixture |
| Orakel | oracle |
| Fremd-Implementierung | foreign implementation |
