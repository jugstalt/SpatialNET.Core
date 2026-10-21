# Own algorithms, no third-party geometry engine

All algorithms in SpatialNET.Extensions are implemented in-house and work directly against the geometry interfaces; neither NetTopologySuite nor Clipper2 or similar is used internally. This keeps the library free of dependencies and fully under our own control, at the price of considerably more effort for robustness (overlay edge cases, numerical precision). NetTopologySuite may appear only in the test project, as a comparison oracle.

*German version: [0001-eigene-algorithmen-ohne-geometrie-engine.de.md](0001-eigene-algorithmen-ohne-geometrie-engine.de.md)*

## Considered Options

- Convert the interface to NTS/Clipper2, compute there, write back (rejected: third-party engine as a dependency)
- Mixed: simple algorithms in-house, overlays via Clipper2 (rejected)
