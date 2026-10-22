# Public API tracking and the criteria for 1.0

The public API of the four libraries is now **tracked in files** and checked on every build, and the conditions under which the version may become 1.0 are written down. Until then the library is a preview: the API may still change, but only on purpose and visibly.

*German version: [0028-oeffentliche-api-und-kriterien-fuer-1-0.de.md](0028-oeffentliche-api-und-kriterien-fuer-1-0.de.md)*

## Consequences

- **PublicApiAnalyzers.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` is referenced by `SpatialNET.Core`, `.Extensions`, `.Imp` and `.IO` (development dependency, not part of the packages). Each project has `PublicAPI.Shipped.txt` (the API of the released versions) and `PublicAPI.Unshipped.txt` (the changes since). A public type or member that is in neither file (RS0016), or a listed one that no longer exists (RS0017), fails the build, because warnings are errors. This catches an API change that nobody meant, in the pull request.
- **Together with the package validation.** `EnablePackageValidation` stays: it compares the *packed binary* with the published package and also catches behaviour of the compiler (for example a changed default value). The analyzers compare the *source* with the files in the repository, need no network, and show the change in the diff. The two checks answer different questions.
- **Starting point.** `PublicAPI.Shipped.txt` holds the public API of the tag `v0.4.0-preview.1`, taken from a `git archive` of the tag (the checkout was not touched). Everything added since (prepared distance, `LineMerge`, `Polygonize`, transformations, shapes, clean-up) is in `PublicAPI.Unshipped.txt`.
- **Rules.** RS0016 and RS0017 and the other rules of the package are errors. RS0026 and RS0027 (several overloads with optional parameters) are switched off in `Directory.Build.props`, because the typed overloads with the shared tail `factory = null, gridSize = null` are intended (ADR 0012).
- **Working with it.** A new public API is added to `PublicAPI.Unshipped.txt` by the code fix of the analyzer in the IDE, or on the command line with `dotnet format analyzers src/<project> --diagnostics RS0016 RS0017 --severity info`. A removal or change appears as `*REMOVED*` followed by the old line. When a release is prepared, `tools/ship-public-api.ps1` moves the unshipped entries into the shipped files (RELEASING.md).
- **Surface review (done; the result).** The whole public surface was reviewed when the files were written, and each finding was decided:
  - **Done: `Hull` is merged into `Shapes`.** `ConvexHull` was the only method of its class while the other shape constructions are in `Shapes`. Callers use the extension syntax (`geometry.ConvexHull()`), which is unchanged; only a static call `Hull.ConvexHull(geometry)` has to become `Shapes.ConvexHull(geometry)`.
  - **Done: the concrete classes of `SpatialNET.Imp` are `internal`.** `Point`, `Point3D`, `PointM`, `Point3DM`, `PointSequence`, `MultiPoint`, `Path`, `Ring`, `Polyline` and `Polygon` had short, generic names that clash with a host's own types as soon as it imports `SpatialNET.Imp` next to its namespace. Only `ImpGeometryFactory` is public, because hosts reach the reference implementation through the factory (`factory.CreatePoint(...)`, `Wkt.Read(..., factory)`); `new Point(x, y)` was never needed. The tests, the playground and the benchmarks see the classes through `InternalsVisibleTo`.
  - **Final: `Circle` stays in `SpatialNET.Extensions`.** It is only returned by `MinimumBoundingCircle`. It moves to `SpatialNET.Core` if hosts ever need to pass it around, which is possible without a break.
  - **Final: the names `Location`/`Locations`, `Validation`/`ValidationResult`, `Preparation`/`PreparedGeometry`.** The pattern is consistent: the extension class has a plural or gerund name, the result type a singular one. It is described in the documentation.
  - **Final: the order of optional parameters.** `Simplify(tolerance, preserveTopology, factory, gridSize)`, `Densify(maxSegmentLength, factory)` and `Buffer(distance, options, factory, gridSize)` differ in the middle, but the tail `factory, gridSize` is the same everywhere, which is what matters for calls with named arguments.
  - **Final: `ValidationResult` and `ValidationIssue` stay records with public constructors.** A host never calls them, the risk is small, and records without a public constructor are inconvenient.
  - **The two breaking changes** (`Hull` and the `Imp` classes) are allowed before 1.0 in a minor version. They are in the CHANGELOG, `PublicAPI.Unshipped.txt` lists them as `*REMOVED*`, and `CompatibilitySuppressions.xml` in `SpatialNET.Extensions` and `SpatialNET.Imp` tells the package validation that they are intended; the files go away when a release moves the baseline.
- **The criteria for 1.0.** The version 1.0.0 is released when all of these hold:
  1. **Scope decided.** No candidate feature is left open that would change the API (a decision to leave it out counts); `ConcaveHull` is decided one way or the other.
  2. **Surface reviewed.** Done: the review above is complete, and every finding is changed or declared final.
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
