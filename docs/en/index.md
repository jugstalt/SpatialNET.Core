# SpatialNET

SpatialNET is a .NET library for planar spatial computations that works exclusively through interfaces, so that existing GIS software can use it through its own geometry classes. The algorithms are implemented in-house: overlays, buffer and offset curves, cut, simplify, spatial predicates (DE-9IM), validity checks and measures.

- **[Getting started](getting-started.md)**: install, first program, options and precision, reading and writing, the playground.
- **[Host integration](host-integration.md)**: implement the geometry interfaces and the geometry factory in your own GIS application.
- **[Cookbook](cookbook.md)**: short, tested recipes for everyday tasks: finding parcels, joins, repairing drawn polygons, splitting, stationing, road with kilometre posts, event tables, service areas, terrain, map display, exchange.
- **[Concepts](concepts/geometry-model.md)**: the geometry model, overlays and the grid, predicates, buffer and offset curves, simplify and validation, cut, spatial index, linear referencing, event tables, triangulation, Voronoi and terrain.
- **[Glossary](glossary.md)**: the terms used in code and documentation, English and German.
- **[API reference](api/index.md)**: generated from the XML comments.
- **[Decisions](decisions/toc.yml)**: the architecture decision records, the reasoning behind the design.
- **[Research notes](https://github.com/jugstalt/SpatialNET.Core/tree/main/research)**: background on the algorithm choices.

SpatialNET is a pre-release and is available on NuGet. The source is on [GitHub](https://github.com/jugstalt/SpatialNET.Core) under the Apache License 2.0.
