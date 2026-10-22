# Public API tracking and the criteria for 1.0

The public API of the four libraries is now **tracked in files** and checked on every build, and the conditions under which the version may become 1.0 are written down. Until then the library is a preview: the API may still change, but only on purpose and visibly.

*German version: [0028-oeffentliche-api-und-kriterien-fuer-1-0.de.md](0028-oeffentliche-api-und-kriterien-fuer-1-0.de.md)*

## Consequences

- **PublicApiAnalyzers.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` is referenced by `SpatialNET.Core`, `.Extensions`, `.Imp` and `.IO` (development dependency, not part of the packages). Each project has `PublicAPI.Shipped.txt` (the API of the released versions) and `PublicAPI.Unshipped.txt` (the changes since). A public type or member that is in neither file (RS0016), or a listed one that no longer exists (RS0017), fails the build, because warnings are errors. This catches an API change that nobody meant, in the pull request.
- **Together with the package validation.** `EnablePackageValidation` stays: it compares the *packed binary* with the published package and also catches behaviour of the compiler (for example a changed default value). The analyzers compare the *source* with the files in the repository, need no network, and show the change in the diff. The two checks answer different questions.
- **Starting point.** `PublicAPI.Shipped.txt` holds the public API of the tag `v0.4.0-preview.1`, taken from a `git archive` of the tag (the checkout was not touched). Everything added since (prepared distance, `LineMerge`, `Polygonize`, transformations, shapes, clean-up) is in `PublicAPI.Unshipped.txt`.
- **Rules.** RS0016 and RS0017 and the other rules of the package are errors. RS0026 and RS0027 (several overloads with optional parameters) are switched off in `Directory.Build.props`, because the typed overloads with the shared tail `factory = null, gridSize = null` are intended (ADR 0012).
- **Working with it.** A new public API is added to `PublicAPI.Unshipped.txt` by the code fix of the analyzer in the IDE, or on the command line with `dotnet format analyzers src/<project> --diagnostics RS0016 RS0017 --severity info`. A removal or change appears as `*REMOVED*` followed by the old line. When a release is prepared, `tools/ship-public-api.ps1` moves the unshipped entries into the shipped files (RELEASING.md).
- **Surface review (made when the files were written; nothing changed yet).** Things a reviewer of the whole public surface should decide before 1.0:
  - `Hull` holds a single method (`ConvexHull`) while the other shape constructions are in `Shapes`; they could be merged. Callers use the extension syntax, so the class name hardly matters, but the API documentation lists classes.
  - `Circle` is a value type in `SpatialNET.Extensions`, while the other value types (`Coordinate`, `Envelope2D`) are in `SpatialNET.Core`. It is only returned by `MinimumBoundingCircle`; it should move to Core only if hosts need to pass it around.
  - `Location` (an enum of `Locate`) and `Locations` (the extension class) are two names for one topic; the same holds for `Validation` / `ValidationResult` and `Preparation` / `PreparedGeometry`. The pattern is consistent (extension class in the plural or as a gerund, result type in the singular), but not obvious.
  - The optional-parameter order is not uniform: `Simplify(tolerance, preserveTopology, factory, gridSize)`, `Densify(maxSegmentLength, factory)`, `Buffer(distance, options, factory, gridSize)`. The tail `factory, gridSize` is the same everywhere, which is what matters for call sites with named arguments.
  - `SpatialNET.Imp` exposes its concrete classes (`Point`, `Path`, `Ring`, `Polygon`, ...) under short, generic names. They are in their own namespace, but a host that imports `SpatialNET.Imp` next to its own namespace will meet name clashes; whether they should be `internal` behind `ImpGeometryFactory` is a question for 1.0.
  - `ValidationResult` and `ValidationIssue` are records with public constructors that a host would never call; they could be made constructible only by the library.
- **The criteria for 1.0.** The version 1.0.0 is released when all of these hold:
  1. **Scope decided.** No candidate feature is left open that would change the API (a decision to leave it out counts); `ConcaveHull` is decided one way or the other.
  2. **Surface reviewed.** The review above is done, and what it found is either changed or declared final here.
  3. **Contracts final.** The interfaces of `SpatialNET.Core` (`IGeometryFactory` included) and the exception rules (ADR 0012) are what a host can rely on for the whole 1.x line. Anything that might still be added to an interface is already a default interface member.
  4. **A real host works.** At least one real GIS application (gView) uses the library through its own geometry classes and its own factory, and its findings are fixed.
  5. **Quality is shown.** The weekly stress tests have run green for four weeks in a row, the oracle tests (NTS) are green on both operating systems, and the benchmarks have a recorded baseline for the main algorithms.
  6. **Releasing is rehearsed.** The release workflow has published at least two pre-releases to nuget.org, and the package validation baseline is the previous release.
  7. **Documentation is complete.** Every public member has an XML comment, every concept has a page in both languages, and the host integration guide and the cookbook run against the final API.
  8. **No open preview notes.** The CHANGELOG has no entry that says "preview", "may change" or "experimental".
- **After 1.0.** Semantic Versioning: no breaking change in 1.x; a removed or changed API needs a new major version, preceded by `[Obsolete]` for at least one minor version. New API is added with a minor version. RELEASING.md and the package validation already enforce the mechanics.
- **Before 1.0 (now).** A minor version may break, with an entry in the CHANGELOG under "Changed" or "Removed" that says so. A patch version never breaks.

## Considered Options

- Package validation alone (rejected: it needs a published baseline and the network, shows breaks only when packing, and not the intended additions)
- A hand-written API list in the documentation (rejected: it drifts; the analyzers keep the list true on every build)
- RS0026/RS0027 as warnings (rejected: with warnings as errors they fail the build; the pattern is intended)
- Treating the current state as the start of "shipped" (rejected: the tag is the truth about what users have; the later additions are unreleased and belong in the unshipped file)
- A fixed date or version for 1.0 (rejected: the criteria decide, not the calendar)
