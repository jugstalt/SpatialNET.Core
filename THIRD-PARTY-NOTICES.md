# Third-Party Notices

SpatialNET is licensed under the [Apache License 2.0](LICENSE). The libraries `SpatialNET.Core`,
`SpatialNET.Extensions`, `SpatialNET.Imp` and `SpatialNET.IO` have no runtime dependencies on third-party
libraries. This document lists third-party components in the repository, templates for algorithms and the
dependencies of the playground.

## Third-party files in the repository

### Leaflet 1.9.4

- Location: `src/SpatialNET.Playground/wwwroot/lib/leaflet/` (unmodified from the npm package `leaflet`)
- License: BSD 2-Clause, Copyright (c) 2010-2023, Volodymyr Agafonkin; Copyright (c) 2010-2011, CloudMade
- License text: [`lib/leaflet/LICENSE`](src/SpatialNET.Playground/wwwroot/lib/leaflet/LICENSE)
- Project: <https://leafletjs.com>

### Leaflet-Geoman 2.17.0

- Location: `src/SpatialNET.Playground/wwwroot/lib/leaflet-geoman/` (unmodified from the npm package `@geoman-io/leaflet-geoman-free`)
- License: MIT, Copyright (c) 2017 Sumit Kumar
- License text: [`lib/leaflet-geoman/LICENSE`](src/SpatialNET.Playground/wwwroot/lib/leaflet-geoman/LICENSE)
- Project: <https://github.com/geoman-io/leaflet-geoman>

### JTS test suites (test data)

- Location: `samples/external/jts/` – inputs from the JTS XML test suites
  (`modules/tests/src/test/resources/testxml/general`), converted with `tools/SpatialNET.CorpusConverter`
- License: Eclipse Distribution License 1.0, Copyright (c) 2007, Eclipse Foundation, Inc. and its licensors
- License text: [`samples/external/jts/LICENSE_EDLv1.txt`](samples/external/jts/LICENSE_EDLv1.txt)
- Project: <https://github.com/locationtech/jts>

### polygon-clipping (test data)

- Location: `samples/external/polygon-clipping/` – end-to-end cases from `test/end-to-end`, converted with
  `tools/SpatialNET.CorpusConverter`
- License: MIT, Copyright (c) 2018 Mike Fogel; parts from martinez Copyright (c) 2016 Alexander Milevski
- License text: [`samples/external/polygon-clipping/LICENSE.md`](samples/external/polygon-clipping/LICENSE.md)
- Project: <https://github.com/mfogel/polygon-clipping>

## Templates for algorithms

### JTS Topology Suite

Several algorithms follow the JTS Topology Suite in structure and behavior, among them the snap-rounding noder,
OverlayNG (overlay core, RingClipper), offset/buffer construction (OffsetCurveBuilder,
BufferInputLineSimplifier) and TopologyPreservingSimplifier. The places are marked in the source code with
"as in JTS" or "after JTS" ("wie JTS" / "Muster JTS" in the German inline comments).

- License: JTS is dual-licensed under the Eclipse Public License 2.0 and the Eclipse Distribution License 1.0;
  SpatialNET relies on the Eclipse Distribution License 1.0
- Copyright (c) Vivid Solutions, Inc. and further JTS contributors
- License text: same as [`samples/external/jts/LICENSE_EDLv1.txt`](samples/external/jts/LICENSE_EDLv1.txt)
- Project: <https://github.com/locationtech/jts>

### Scientific methods (no license obligation, cited for attribution)

- J. R. Shewchuk: *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*,
  Discrete & Computational Geometry 18, 1997 – error bounds of the orientation predicate `orient2d`.
- J. D. Hobby: *Practical segment intersection with finite precision output*, Computational Geometry 13, 1999 –
  snap rounding.
- D. H. Douglas, T. K. Peucker: *Algorithms for the reduction of the number of points required to represent a
  digitized line or its caricature*, The Canadian Cartographer 10, 1973 – line simplification.

## Playground

The playground (`src/SpatialNET.Playground`) is a development tool and not part of the libraries.
In addition to Leaflet and Leaflet-Geoman it uses:

- **Proj4Net.Core** 2.26.2401 (NuGet) – Apache License 2.0
- **OpenStreetMap tiles** – © OpenStreetMap contributors, data under the Open Database License (ODbL);
  the attribution is shown in the map. <https://www.openstreetmap.org/copyright>

## Development only

The following packages are loaded via NuGet only for tests, benchmarks and tools and are not shipped with
SpatialNET: NetTopologySuite (BSD-3-Clause, test oracle and benchmark comparison), xUnit, CsCheck,
coverlet, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing, BenchmarkDotNet.
