# Releasing

Releases are cut by pushing a tag; the workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) does the rest. The version number lives **only in the tag** – there is no file to edit for the packages themselves.

## Versioning

- All four packages (`SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp`, `SpatialNET.IO`) share one version, following [Semantic Versioning](https://semver.org/).
- The tag has the form `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-suffix`, for example `v0.1.0` or `v0.1.0-preview.2`. The workflow rejects any other tag. A tag with a suffix is published as a pre-release (on nuget.org and on GitHub).
- `VersionPrefix` in [`Directory.Build.props`](Directory.Build.props) is only the version of local builds; the workflow overrides it with `-p:Version`.

## Choosing the next version

| Change since the last release | Next version |
|---|---|
| Still experimenting, API may change (before 1.0) | next pre-release of the same version: `v0.1.0-preview.1` → `v0.1.0-preview.2` |
| First usable release | drop the suffix: `v0.1.0` |
| Bug fixes only | patch: `v0.1.0` → `v0.1.1` |
| New features, backwards compatible | minor: `v0.1.1` → `v0.2.0` |
| Breaking API change | major: `v1.4.2` → `v2.0.0`; before 1.0 a breaking change goes into a minor version (`v0.2.0` → `v0.3.0`) |

A version that has been pushed to nuget.org can never be changed or reused. If something is wrong, you publish the next version.

## Releasing a new version, step by step

Example: from `v0.1.0-preview.1` to `v0.1.0-preview.2`.

1. **Decide the version** using the table above.
2. **Make sure `main` is green.** Everything that should be in the release is merged, and the [CI workflow](.github/workflows/ci.yml) is green on the latest commit of `main`.
3. **Update the changelog.** In [`CHANGELOG.md`](CHANGELOG.md), rename the heading `[Unreleased]` to the new version and date, for example `## [0.1.0-preview.2] - 2026-11-03`, and add a fresh, empty `## [Unreleased]` section above it.
4. **Optional – raise `VersionPrefix`.** If the new version has a higher `MAJOR.MINOR.PATCH` than the one in [`Directory.Build.props`](Directory.Build.props) (for example `0.1.0` → `0.2.0`), change `VersionPrefix` as well, so that local builds are not older than the last release. This does not affect the published packages.
5. **Commit and push** to `main` (directly or via a pull request) and wait for CI to turn green:

   ```bash
   git add CHANGELOG.md Directory.Build.props
   git commit -m "chore: prepare release 0.1.0-preview.2"
   git push origin main
   ```

6. **Tag that commit and push the tag.** Check that you are on the latest `main` first:

   ```bash
   git checkout main
   git pull
   git tag v0.1.0-preview.2
   git push origin v0.1.0-preview.2
   ```

7. **Approve and watch the workflow.** Open the *Release* run in the Actions tab. If you required a reviewer for the environment `nuget`, approve the run there. The workflow builds, runs the tests, packs the libraries (with symbol packages), pushes them to nuget.org and creates a GitHub release with the packages attached and generated release notes.
8. **Verify.** On nuget.org, each of the four packages shows the new version, the README and the license. On GitHub, the release is marked as a pre-release if the version has a suffix. New versions can take a few minutes to appear in the nuget.org search.
9. **Polish the GitHub release notes** if needed (Releases → edit), for example by pasting the matching section of the changelog.

## If something goes wrong

- **The workflow failed before the push** (build, tests or packing): fix the problem on `main`, then move the tag to the fixed commit. The version has not been published, so it can be reused:

  ```bash
  git push --delete origin v0.1.0-preview.2
  git tag -d v0.1.0-preview.2
  git tag v0.1.0-preview.2
  git push origin v0.1.0-preview.2
  ```

- **The packages were pushed but are faulty:** that version is final. Unlist it on nuget.org (package page → Manage → Listing; packages cannot be deleted), fix the problem and release the next version, for example `v0.1.0-preview.3`. Delete the faulty GitHub release if you like.
- **Only some packages were pushed:** `dotnet nuget push` runs with `--skip-duplicate`, so re-running the failed workflow run (Actions → *Re-run failed jobs*) pushes the missing packages and skips those that already exist. Re-run the tag's workflow rather than creating a new tag.
- **The tag has the wrong form:** the workflow fails in its first step. Delete the tag as shown above and push a correct one.

## One-time setup

- **nuget.org:** reserve the package IDs `SpatialNET.Core`, `SpatialNET.Extensions`, `SpatialNET.Imp` and `SpatialNET.IO`, and create an API key that is allowed to push them. API keys expire; renew it before it does.
- **GitHub:** add the API key as the secret `NUGET_API_KEY` of the environment `nuget` (Settings → Environments). Requiring a reviewer for that environment makes every release a deliberate approval.
- **GitHub Pages:** in Settings → Pages, set the source to "GitHub Actions" so that [`.github/workflows/docs.yml`](.github/workflows/docs.yml) can publish the documentation.

## After the first stable release

Enable package validation (`<EnablePackageValidation>true</EnablePackageValidation>` together with `<PackageValidationBaselineVersion>`) so that breaking API changes between releases are caught at pack time.
