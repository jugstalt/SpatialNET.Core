# Glossary

The terms of SpatialNET with their German counterparts. The normative definitions are maintained in German in [GLOSSARY.md](https://github.com/jugstalt/SpatialNET.Core/blob/main/GLOSSARY.md); this page gives the English terms used in code and documentation.

## Geometries

| English | Deutsch | Meaning |
|---|---|---|
| geometry interface | Geometrie-Interface | A contract in `SpatialNET.Core` (e.g. `IPoint2D`, `IPolygon2D`) that a host geometry fulfills so that SpatialNET can compute on it |
| host | Host | An existing GIS application (e.g. gView, WebGIS) that integrates SpatialNET by letting its own geometry types implement the geometry interfaces |
| host geometry | Host-Geometrie | A geometry class of a host that implements one or more geometry interfaces |
| path | Pfad | An ordered sequence of points forming a line string; part of a polyline, not a geometry itself |
| ring | Ring | A closed path that encloses an area; whether it is a shell or a hole follows from its position in the polygon |
| hole | Loch | A ring that lies inside an odd number of other rings of the same polygon and cuts out their area |
| part (polygon part) | Teilfläche | A shell together with the holes that lie directly in it; an island in a hole is a part of its own (`PolygonPart`) |
| polyline | Polylinie | A geometry of one or more paths (corresponds to MultiLineString) |
| polygon | Polygon | A geometry of one or more rings that together describe one or more areas (corresponds to MultiPolygon) |
| point on surface (interior point) | Punkt auf der Fläche (innerer Punkt) | A point that lies on a geometry, in the interior of an area where it has one; used for labels (`PointOnSurface`) |
| nearest points | nächste Punkte | The two points, one on each of two geometries, whose distance is the distance of the geometries (`NearestPoints`) |
| linear referencing | lineare Referenzierung | Addressing a position on a line by its length along the line or by its M value (`Interpolate`, `Project`, `Substring` and their `...ByM` variants) |
| measure (M) | M-Wert | A value that a point carries in addition to its position (for example the distance along a road); carried and interpolated like Z, never used in a computation (`IPointM`) |
| calibrating | Kalibrieren | Giving a line M values from anchor points that carry M, by projecting them onto the line and interpolating in the length between them (`CalibrateM`) |
| offset to the side | seitlicher Versatz | The signed distance of a point from a line, positive to the left of the direction of the line, negative to the right (`Interpolate(distance, offset)`, `LineProjection.Offset`); not the offset curve of `OffsetCurve` |
| Voronoi cell | Voronoi-Zelle | The area that lies nearer to one of a set of points than to every other (a Thiessen polygon); cut to a clip area (`VoronoiDiagram`, `VoronoiCell`) |
| triangulation | Triangulierung | Triangles that tile a set of points or a polygon, with their neighbours (`Triangulation`) |
| break line | Bruchkante | A line that must be an edge of the triangulation, such as a ridge or a ditch (a constraint, `FromPointsAndLines`) |
| contour line | Höhenlinie | The line where a surface has one height (`Triangulation.Contour`) |
| slope | Neigung | The angle of a triangle's plane to the horizontal, in degrees (`Triangulation.Slope`) |
| aspect | Exposition | The compass direction that a triangle faces, the direction of steepest descent, clockwise from north (`Triangulation.Aspect`) |
| natural neighbour | Natürliche Nachbarn | A smooth interpolation: the heights of the neighbours that a new point would get, weighted by the area of their Voronoi cells that it takes over (`Triangulation.InterpolateNaturalNeighbour`) |
| quality mesh | Qualitätsnetz | A triangulation without sharp triangles, made by adding points (`Triangulation.Refine`) |
| smoothing | Glättung | Replacing the corners of a line by a curve (`Smooth`: corner cutting or a spline), or the surface by its interpolation on a grid (`Triangulation.Resample`) |
| height band | Höhenstufe | The area of a surface between two heights, bounded by contour lines (`Triangulation.ContourBands`) |
| viewshed | Sichtbarkeitsfläche | The part of a surface that can be seen from a point (`Triangulation.Viewshed`) |
| line of sight | Sichtlinie | Whether the straight line from an eye to a target passes above the ground (`Triangulation.LineOfSight`) |
| draping | Drapieren | Laying a line over a surface: the same line in the map, with the height as Z and the distance along it as M (`Triangulation.Drape`) |
| point event | Punktereignis | An event at a single M value, such as a bridge (`PointEvent<T>`) |
| event | Ereignis | A fact about a range of M values on a route, with attributes of the host's choice: a speed limit from kilometre 3 to 5.5 (`LineEvent<T>`); the arithmetic between event tables is `LineEvents` |
| envelope | Envelope | The axis-aligned rectangle that encloses a geometry (`Envelope2D`) |
| reference implementation | Referenz-Implementierung | The implementation of the geometry interfaces in `SpatialNET.Imp`, meant for tests, the playground and hosts without geometries of their own |

## Computation

| English | Deutsch | Meaning |
|---|---|---|
| algorithm | Algorithmus | A spatial operation (e.g. clip, cut) provided as an extension method on the geometry interfaces, without knowledge of a concrete implementation |
| geometry factory | Geometrie-Factory | The creator provided by the host (or the reference implementation) through which an algorithm creates its result geometries from coordinates (`IGeometryFactory`) |
| cutting line | Schnittlinie | The polyline along which `Cut` splits a polygon or polyline into parts |
| offset curve | Offset-Kurve | A polyline at a fixed distance from a line (to the left for a positive distance) or from the boundary of a polygon (outside for a positive distance) |
| fill rule | Füllregel | The rule by which an algorithm decides from the winding number of a point whether it belongs to the area of an input (`FillRule`: EvenOdd, NonZero, Positive, Negative) |
| grid size | Gitterweite | The spacing, in map units, of the grid to which an overlay rounds all coordinates of its result (`gridSize`) |
| location | Lage | Where a point lies relative to a geometry: inside, on the boundary or outside (`Locate`, `Location`) |
| clip | zuschneiden | Cutting a geometry to a rectangle (`Clip`) |
| simple | einfach | A line that does not touch or cross itself except at its end points (`IsSimple`) |
| Hausdorff distance | Hausdorff-Distanz | How far two shapes are apart as a whole: the greatest distance from a point of one to the other, in both directions (`HausdorffDistance`, discrete) |
| minimum rotated rectangle | kleinstes umschließendes Rechteck | The rectangle of least area that contains a geometry, at any angle (`MinimumRotatedRectangle`) |
| minimum bounding circle | kleinster umschließender Kreis | The smallest circle that contains a geometry (`MinimumBoundingCircle`) |
| pole of inaccessibility | Pol der Unzugänglichkeit | The point inside a polygon that is farthest from its boundary, a good place for a label (`Polylabel`) |
| snap to grid | auf das Raster legen | Rounding every coordinate to the nearest grid point and nothing else; may make a polygon invalid (`SnapToGrid`) |
| reduce precision | Genauigkeit reduzieren | Snap rounding to a grid with a valid result (`ReducePrecision`) |
| snap to a target | fangen | Moving the vertices of a geometry that lie within a tolerance of another geometry onto it, a corner of the target before an edge; for lines by default only the loose ends (`SnapTo`). Not snap rounding, not snapping to the grid |
| concave hull | konkave Hülle | A polygon that contains every point and wraps them more tightly than the convex hull, found by taking triangles away from the Delaunay triangulation (`ConcaveHull`) |
| Delaunay triangulation | Delaunay-Triangulierung | The triangulation of points in which no point lies inside the circumcircle of a triangle (`DelaunayTriangles`) |
| polygonize | polygonisieren | Turning lines into the areas they enclose: every bounded face of the arrangement of the lines becomes a polygon (`Polygonize`) |
| line merge | Linien verbinden | Joining the paths of a polyline where exactly two path ends meet (`LineMerge`) |
| make valid | gültig machen | Repairing an invalid geometry: a polygon by even-odd normalisation through the overlay core (`MakeValid`) |
| winding number | Windungszahl | How often the boundary of an input winds around a point; the fill rule turns it into inside/outside |
| snap rounding | Snap-Rounding | Rounding of constructed intersection points to the grid in a way that keeps the topology consistent |
| even-odd rule | Even-Odd-Regel | A point is inside if it lies within an odd number of rings; the default for all inputs |
| strict semantics | Strict-Semantik | Overlay results are dimension-homogeneous; touch and collapse remnants of lower dimension are dropped |
| spatial index | räumlicher Index | A static, bulk-loaded tree over envelopes with a payload for range queries and nearest neighbours (`SpatialIndex<T>`) |
| prepared geometry | vorbereitete Geometrie | A geometry with a coordinate snapshot and a spatial index for repeated predicates (`Prepare()`) |

## Playground and tests

| English | Deutsch | Meaning |
|---|---|---|
| playground | Playground | A web application with a Leaflet map in which algorithms are tried interactively on sample, drawn or uploaded geometries |
| computation CRS | Rechen-KBS | The projected coordinate reference system into which the playground transforms inputs before computing and in whose units all distance parameters apply |
| sample geometry | Beispielgeometrie | A fixture with the tag `playground` that the playground offers automatically for its algorithm |
| fixture | Fixture | A stored test case of an algorithm with inputs, parameters and tags that tests and playground share |
| oracle | Orakel | The comparison computation with NetTopologySuite that provides the expected value of a fixture at test run time |
| foreign implementation | Fremd-Implementierung | A deliberately awkward implementation of the geometry interfaces in the test project against which every fixture test also runs |
