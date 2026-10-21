# Releasing

Releases are cut by pushing a tag; the workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) does the rest.

## Versioning

- All four packages share one version, following [Semantic Versioning](https://semver.org/).
- The version comes from the tag: `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-suffix`, for example `v0.1.0-preview.1`. A tag with a suffix is published as a pre-release.
- `VersionPrefix` in [`Directory.Build.props`](Directory.Build.props) is only the version of local builds; the workflow overrides it with `-p:Version`.

## Steps

1. Move the entries of `[Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md) under the new version and date, and commit.
2. Tag and push:

   ```bash
   git tag v0.1.0-preview.1
   git push origin v0.1.0-preview.1
   ```

3. The workflow builds, runs the tests, packs the libraries (with symbol packages), pushes them to nuget.org and creates a GitHub release with the packages attached.

## One-time setup

- **nuget.org:** reserve the package IDs `SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp` and `SpatialNET.IO`, and create an API key that is allowed to push them.
- **GitHub:** add the API key as the secret `NUGET_API_KEY` of the environment `nuget` (Settings → Environments). Requiring a reviewer for that environment makes every release a deliberate approval.
- **GitHub Pages:** in Settings → Pages, set the source to "GitHub Actions" so that [`.github/workflows/docs.yml`](.github/workflows/docs.yml) can publish the documentation.

## After the first release

Enable package validation (`<EnablePackageValidation>true</EnablePackageValidation>` together with `<PackageValidationBaselineVersion>`) so that breaking API changes between releases are caught at pack time.
