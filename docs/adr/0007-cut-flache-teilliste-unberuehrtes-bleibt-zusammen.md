# Cut: flat list of pieces, untouched parts stay together and unrounded

`Cut(this IPolygon2D, IPolyline2D cutter, factory?, gridSize?)` returns `IReadOnlyList<IPolygon2D>`, `Cut(this IPolyline2D, IPolyline2D cutter, …)` returns `IReadOnlyList<IPolyline2D>`: a flat list without left/right assignment, like the existing `Cut` functions in gView and WebGIS. The editing tool (WebGIS `CutFeature`) turns every element into a new feature. Therefore the following applies: every cut piece is an element of its own, but all **untouched** parts or paths **stay together as one element** and keep their **original coordinates**, without snap rounding. A multipart feature thus does not fall apart incidentally, and whatever the cut does not hit stays unchanged. If nothing is cut, the list contains exactly one element.

*German version: [0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.de.md](0007-cut-flache-teilliste-unberuehrtes-bleibt-zusammen.de.md)*

## Consequences

- **Polygon semantics:** the pieces are the faces from the noded polygon boundary plus the parts of the cutting line in the interior (ADR 0005). Loose ends, runs along the boundary and touches at vertices do not cut. Parts of the cutting line in holes do not count, and the holes are distributed over the pieces accordingly.
- **Line semantics:** the line is split at every contact with the cutting line: crossing, touch at a point, and start and end of a collinear overlap. Each piece is a polyline with exactly one path.
- SpatialNET does **not** extend the cutting line implicitly. A snap tolerance, as WebGIS implements it with `Extend(tolerance)`, remains the tool's business.
- All paths of the cutting line cut, so several cuts in one call are possible. There is no options class. The order of the elements is not fixed (ADR 0003), but the result is deterministic.
- **Test oracle:** NTS `Polygonize(Union(boundary, cutting line))`, filtered to pieces in the interior.

## Considered Options

- Left/right result as in ESRI (rejected: not unambiguous for zigzag cuts)
- Every part as an element of its own, including untouched ones (rejected: breaks up multipart features the user did not want to cut)
- Extend the cutting line to the boundary (rejected: not deterministic, belongs in the UI)
