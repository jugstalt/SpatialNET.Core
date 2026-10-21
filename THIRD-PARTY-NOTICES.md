# Third-Party Notices

SpatialNET steht unter der [Apache License 2.0](LICENSE). Die Bibliotheken `SpatialNET.Core`,
`SpatialNET.Extensions`, `SpatialNET.Imp` und `SpatialNET.IO` haben keine Laufzeit-Abhängigkeiten zu
Fremdbibliotheken. Dieses Dokument nennt fremde Bestandteile im Repository, Vorlagen für Algorithmen und
die Abhängigkeiten des Playgrounds.

## Fremde Dateien im Repository

### Leaflet 1.9.4

- Ort: `src/SpatialNET.Playground/wwwroot/lib/leaflet/` (unverändert aus dem npm-Paket `leaflet`)
- Lizenz: BSD 2-Clause, Copyright (c) 2010-2023, Volodymyr Agafonkin; Copyright (c) 2010-2011, CloudMade
- Lizenztext: [`lib/leaflet/LICENSE`](src/SpatialNET.Playground/wwwroot/lib/leaflet/LICENSE)
- Projekt: <https://leafletjs.com>

### Leaflet-Geoman 2.17.0

- Ort: `src/SpatialNET.Playground/wwwroot/lib/leaflet-geoman/` (unverändert aus dem npm-Paket `@geoman-io/leaflet-geoman-free`)
- Lizenz: MIT, Copyright (c) 2017 Sumit Kumar
- Lizenztext: [`lib/leaflet-geoman/LICENSE`](src/SpatialNET.Playground/wwwroot/lib/leaflet-geoman/LICENSE)
- Projekt: <https://github.com/geoman-io/leaflet-geoman>

### JTS-Testsuites (Testdaten)

- Ort: `samples/external/jts/` – Eingaben aus den JTS-XML-Testsuites
  (`modules/tests/src/test/resources/testxml/general`), per `tools/SpatialNET.CorpusConverter` umgewandelt
- Lizenz: Eclipse Distribution License 1.0, Copyright (c) 2007, Eclipse Foundation, Inc. and its licensors
- Lizenztext: [`samples/external/jts/LICENSE_EDLv1.txt`](samples/external/jts/LICENSE_EDLv1.txt)
- Projekt: <https://github.com/locationtech/jts>

### polygon-clipping (Testdaten)

- Ort: `samples/external/polygon-clipping/` – End-to-End-Fälle aus `test/end-to-end`, per
  `tools/SpatialNET.CorpusConverter` umgewandelt
- Lizenz: MIT, Copyright (c) 2018 Mike Fogel; Teile aus martinez Copyright (c) 2016 Alexander Milevski
- Lizenztext: [`samples/external/polygon-clipping/LICENSE.md`](samples/external/polygon-clipping/LICENSE.md)
- Projekt: <https://github.com/mfogel/polygon-clipping>

## Vorlagen für Algorithmen

### JTS Topology Suite

Mehrere Algorithmen folgen in Aufbau und Verhalten der JTS Topology Suite, unter anderem Snap-Rounding-Noder,
OverlayNG (Overlay-Kern, RingClipper), Offset-/Buffer-Konstruktion (OffsetCurveBuilder,
BufferInputLineSimplifier) und TopologyPreservingSimplifier. Die Stellen sind im Quellcode mit „wie JTS“ bzw.
„Muster JTS“ gekennzeichnet.

- Lizenz: JTS ist dual lizenziert unter Eclipse Public License 2.0 und Eclipse Distribution License 1.0;
  SpatialNET bezieht sich auf die Eclipse Distribution License 1.0
- Copyright (c) Vivid Solutions, Inc. und weitere JTS-Mitwirkende
- Lizenztext: wie [`samples/external/jts/LICENSE_EDLv1.txt`](samples/external/jts/LICENSE_EDLv1.txt)
- Projekt: <https://github.com/locationtech/jts>

### Wissenschaftliche Verfahren (ohne Lizenzpflicht, zur Quellenangabe)

- J. R. Shewchuk: *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*,
  Discrete & Computational Geometry 18, 1997 – Fehlerschranken des Orientierungsprädikats `orient2d`.
- J. D. Hobby: *Practical segment intersection with finite precision output*, Computational Geometry 13, 1999 –
  Snap-Rounding.
- D. H. Douglas, T. K. Peucker: *Algorithms for the reduction of the number of points required to represent a
  digitized line or its caricature*, The Canadian Cartographer 10, 1973 – Linienvereinfachung.

## Playground

Der Playground (`src/SpatialNET.Playground`) ist ein Entwicklungswerkzeug und kein Teil der Bibliotheken.
Zusätzlich zu Leaflet und Leaflet-Geoman nutzt er:

- **Proj4Net.Core** 2.26.2401 (NuGet) – Apache License 2.0
- **OpenStreetMap-Kacheln** – © OpenStreetMap-Mitwirkende, Daten unter der Open Database License (ODbL);
  der Hinweis wird in der Karte angezeigt. <https://www.openstreetmap.org/copyright>

## Nur für Entwicklung

Folgende Pakete werden nur für Tests, Benchmarks und Werkzeuge per NuGet geladen und nicht mit SpatialNET
ausgeliefert: NetTopologySuite (BSD-3-Clause, Test-Orakel und Benchmark-Vergleich), xUnit, CsCheck,
coverlet, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing, BenchmarkDotNet.
