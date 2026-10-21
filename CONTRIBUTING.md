# Contributing

*[Deutsche Fassung](CONTRIBUTING.de.md)*

Thank you for your interest in SpatialNET. This page describes how to build, test and change the project, and what a good pull request contains.

## Build and test

SpatialNET targets `net10.0`. The .NET 10 SDK is all you need.

```bash
dotnet build SpatialNET.sln
dotnet test tests/SpatialNET.Tests
```

Warnings are errors. The tests (about 6,350) take roughly 20 seconds. The property-based tests (CsCheck) run with their default iteration count; to run them harder, set `SPATIALNET_PROPERTY_ITER`, for example to `10000`. CI additionally checks a coverage threshold (see `.github/scripts/check-coverage.ps1`) and builds the documentation.

## Principles

- **Own algorithms, no third-party geometry engine** ([ADR 0001](docs/adr/0001-eigene-algorithmen-ohne-geometrie-engine.md)). NetTopologySuite is allowed only in the test project, as an oracle.
- **Interfaces, not classes.** Algorithms work on the read-only interfaces of `SpatialNET.Core` and create results through the geometry factory ([ADR 0002](docs/adr/0002-geometrie-interfaces-count-indexer-flache-ringe.md), [ADR 0004](docs/adr/0004-geometrie-factory-koordinatenbasiert-optional-mit-default.md)).
- **Decisions are recorded.** Changes of design or behavior that the existing [ADRs](docs/adr/) settle need a new or amended ADR; the vocabulary is in [GLOSSARY.md](GLOSSARY.md) (German) and [docs/en/glossary.md](docs/en/glossary.md).

## Adding or changing an algorithm

1. **Implement** it as an extension method in `SpatialNET.Extensions`, following the signature rules of [ADR 0012](docs/adr/0012-api-signaturen-typisierte-overloads.md) (typed overloads, optional parameters in the fixed order, exception rules).
2. **Document** it with XML comments (mandatory in all libraries; they are the source of the API reference).
3. **Add fixtures** under `samples/<algorithm>/` and cover the mandatory tag catalog ([samples/README.md](samples/README.md)); the test `FixtureCompletenessTests` enforces it for every public method.
4. **Add a playground descriptor** in `src/SpatialNET.Playground/Algorithms/`; a completeness test requires one per public method ([ADR 0011](docs/adr/0011-playground-karte-zuerst-deskriptor-registry.md)).
5. **Test** against the NTS oracle, with contract tests and, where it makes sense, property tests ([ADR 0008](docs/adr/0008-teststrategie-fixtures-nts-orakel.md)). Every fixture test also runs against the deliberately awkward host-style implementation in `tests/SpatialNET.Tests/Foreign/`.
6. **Document it for users** in the matching concept page under `docs/en/concepts/` and `docs/de/konzepte/`.

## Language

- English leads for everything code-facing: XML comments, exception messages, commit messages, pull requests, issues and the documentation. German is the second language of the documentation.
- **Documentation is bilingual.** A change to a page under `docs/en`, `docs/de`, `docs/adr`, `research/` or a README changes its counterpart in the same pull request (`X.md` is English, `X.de.md` German). If you cannot write German, say so in the pull request and a maintainer will add it.
- Inline `//` comments in existing files are partly German; write new ones in English.

## Documentation

The code examples in the documentation are covered by `tests/SpatialNET.Tests/DocSamples/`; when an example changes, update the page and the test together. Build the documentation sites with `./docs/build.ps1` (see [docs/README.md](docs/README.md)); warnings are errors.

## Pull requests

- Keep a pull request focused on one change; describe what and why.
- Commit messages: a short summary line in the imperative, a conventional prefix (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `ci:`, `chore:`), then details if needed.
- All CI jobs must be green: build and tests on Linux and Windows, packing, the documentation build and the coverage threshold.
- Update [CHANGELOG.md](CHANGELOG.md) under `[Unreleased]` for user-visible changes.

Releases are described in [RELEASING.md](RELEASING.md). By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE).
