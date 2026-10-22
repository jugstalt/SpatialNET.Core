# Mitwirken

*[English version](CONTRIBUTING.md)*

Danke für dein Interesse an SpatialNET. Diese Seite beschreibt, wie das Projekt gebaut, getestet und geändert wird und was zu einem guten Pull Request gehört.

## Bauen und testen

SpatialNET zielt auf `net10.0`. Du brauchst nur das .NET-10-SDK.

```bash
dotnet build SpatialNET.sln
dotnet test tests/SpatialNET.Tests
```

Warnungen sind Fehler. Die Tests (rund 6.350) laufen etwa 20 Sekunden. Die Property-Tests (CsCheck) laufen mit ihrer Standard-Iterationszahl; um sie härter zu fahren, setzt du `SPATIALNET_PROPERTY_ITER`, zum Beispiel auf `10000`. Die **Stresstests** (zufällige, absichtlich schmutzige Eingaben je Algorithmus, minutenlang) werden standardmäßig übersprungen; du startest sie mit `./tools/stress.ps1` oder `SPATIALNET_STRESS=1` (siehe [ADR 0008](docs/adr/0008-teststrategie-fixtures-nts-orakel.de.md)). Die CI prüft zusätzlich eine Coverage-Schwelle (siehe `.github/scripts/check-coverage.ps1`) und baut die Dokumentation.

## Grundsätze

- **Eigene Algorithmen, keine fremde Geometrie-Engine** ([ADR 0001](docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.de.md)). NetTopologySuite ist nur im Testprojekt erlaubt, als Orakel.
- **Interfaces, keine Klassen.** Algorithmen arbeiten auf den lesenden Interfaces aus `SpatialNET.Core` und erzeugen Ergebnisse über die Geometrie-Factory ([ADR 0002](docs/adr/0002-geometrie-interfaces-count-indexer-flache-ringe.de.md), [ADR 0004](docs/adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.de.md)).
- **Entscheidungen werden festgehalten.** Änderungen an Design oder Verhalten, die die bestehenden [ADRs](docs/adr/) festlegen, brauchen einen neuen oder angepassten ADR; das Vokabular steht in [GLOSSARY.md](GLOSSARY.md) (Deutsch) und [docs/en/glossary.md](docs/en/glossary.md).

## Einen Algorithmus hinzufügen oder ändern

1. **Implementieren** als Extension-Methode in `SpatialNET.Extensions`, nach den Signaturregeln aus [ADR 0012](docs/adr/0012-api-signaturen-typisierte-overloads.de.md) (typisierte Overloads, optionale Parameter in fester Reihenfolge, Exception-Regeln).
2. **Dokumentieren** mit XML-Kommentaren (Pflicht in allen Bibliotheken; sie sind die Quelle der API-Referenz).
3. **Fixtures anlegen** unter `samples/<algorithmus>/` und den Pflicht-Tag-Katalog abdecken ([samples/README.de.md](samples/README.de.md)); der Test `FixtureCompletenessTests` erzwingt das für jede öffentliche Methode.
4. **Playground-Deskriptor anlegen** in `src/SpatialNET.Playground/Algorithms/`; ein Vollständigkeitstest verlangt einen je öffentlicher Methode ([ADR 0011](docs/adr/0011-playground-karte-zuerst-deskriptor-registry.de.md)).
5. **Testen** gegen das NTS-Orakel, mit Contract-Tests und, wo sinnvoll, Property-Tests ([ADR 0008](docs/adr/0008-teststrategie-fixtures-nts-orakel.de.md)). Jeder Fixture-Test läuft auch gegen die absichtlich unbequeme, host-artige Implementierung in `tests/SpatialNET.Tests/Foreign/`.
6. **Für Anwender dokumentieren** auf der passenden Konzeptseite unter `docs/en/concepts/` und `docs/de/konzepte/`.

## Öffentliche API

Die öffentliche API wird verfolgt ([ADR 0028](docs/adr/0028-oeffentliche-api-und-kriterien-fuer-1-0.de.md)): Jeder öffentliche Typ und Member steht in `PublicAPI.Shipped.txt` oder `PublicAPI.Unshipped.txt` seines Projekts, und der Build scheitert, wenn Code und Dateien auseinanderlaufen. Wenn Sie öffentliche API hinzufügen, ändern oder entfernen, lassen Sie den Analyzer die Liste nachziehen: in der IDE mit dem Code-Fix für RS0016/RS0017, oder auf der Kommandozeile:

```bash
dotnet format analyzers src/SpatialNET.Extensions --diagnostics RS0016 RS0017 --severity info
```

Prüfen Sie die Änderung an der `.txt`-Datei im Diff wie jede andere: Sie ist die API-Änderung, die ein Leser des Pull Requests sieht.

## Sprache

- Englisch führt bei allem, was am Code hängt: XML-Kommentare, Exception-Meldungen, Commit-Messages, Pull Requests, Issues und die Dokumentation. Deutsch ist die zweite Sprache der Dokumentation.
- **Die Dokumentation ist zweisprachig.** Eine Änderung an einer Seite unter `docs/en`, `docs/de`, `docs/adr`, `research/` oder an einer README ändert im selben Pull Request auch die Gegenstück-Seite (`X.md` ist Englisch, `X.de.md` Deutsch). Wenn du kein Deutsch schreiben kannst, sag es im Pull Request, ein Maintainer ergänzt es.
- Inline-`//`-Kommentare in bestehenden Dateien sind teils deutsch; neue schreibst du auf Englisch.

## Dokumentation

Die Code-Beispiele der Dokumentation deckt `tests/SpatialNET.Tests/DocSamples/` ab; ändert sich ein Beispiel, werden Seite und Test gemeinsam angepasst. Die Dokumentationssites baust du mit `./docs/build.ps1` (siehe [docs/README.md](docs/README.md)); Warnungen sind Fehler.

## Pull Requests

- Ein Pull Request behandelt eine Änderung; beschreibe, was und warum.
- Commit-Messages: eine kurze Zusammenfassung im Imperativ, ein Conventional-Präfix (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `ci:`, `chore:`), bei Bedarf Details.
- Alle CI-Jobs müssen grün sein: Build und Tests unter Linux und Windows, Packen, Dokumentations-Build und Coverage-Schwelle.
- Bei sichtbaren Änderungen [CHANGELOG.md](CHANGELOG.md) unter `[Unreleased]` ergänzen.

Releases sind in [RELEASING.md](RELEASING.md) beschrieben. Mit deinem Beitrag stimmst du zu, dass er unter der [Apache License 2.0](LICENSE) steht.
