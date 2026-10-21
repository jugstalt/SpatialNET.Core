# Holes: even-odd on input, parts in the result

An `IPolygon2D` is a flat list of rings without a hole marker (ADR 0002). Its area is defined by the **even-odd rule** over all rings; ring orientation is ignored on input. For valid polygons this corresponds to the nesting depth (even = shell, odd = hole) and thus exactly to the `VerifyHoles()` of gView and WebGIS. Both hosts ignore orientation. An orientation rule as in OGC/GeoJSON would have contradicted their data.

*German version: [0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md](0003-loecher-even-odd-ergebnisse-als-teilflaechen.de.md)*

## Consequences

- **Validity** follows the OGC rule as NTS checks it: rings do not cross. A hole may touch the shell or another hole at exactly one point, a ring may not touch itself. Degenerate rings (fewer than 3 distinct points, area 0) are ignored and reported by `IsValid()`.
- **Measures and predicates** (Area, Centroid, Contains …) assume valid input; for invalid input the result is undefined. **Overlay operations** (Union, Intersection, Buffer …) evaluate the even-odd fill rule anyway and deliver a defined result even for self-crossing rings. A union with even-odd thus replaces the omitted MakeValid for this case.
- **Results** are always valid and closed. Shells run counter-clockwise, holes clockwise (RFC 7946). They are handed to the geometry factory (#4) in structured form as **parts** (shell plus holes), so the hosts do not have to recompute `VerifyHoles()`. The order of the parts and the start points of the rings are not fixed, but results are deterministic. Tests compare in normalized form.
- Partially overlapping shells are invalid: even-odd turns the overlap into a hole, whereas the hosts would count the area twice.

## Considered Options

- Marker interface `IHole2D` (rejected in ADR 0002)
- Orientation as hole marker (rejected: both hosts ignore it)
- Pure nesting instead of even-odd (equivalent for valid input, but undefined for crossings)
