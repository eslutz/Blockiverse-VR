# ADR 0005: Release Versioning

## Status

Accepted

## Context

Blockiverse VR releases through Meta Quest release channels, but CI and CD are
separate:

- Pull requests validate the Unity project without publishing to Meta.
- Alpha builds are release-signed Quest APKs uploaded from trusted `main`
  pushes or manual trusted refs.
- Beta promotes a known-good Alpha Meta build.
- Release candidates promote a known-good Beta Meta build.
- Production promotes a known-good RC Meta build to the Meta `store` channel
  only after the Store release path is intentionally approved.

The release process needs stable, predictable Android package metadata that
works with Meta release channels, Android upgrade ordering, and human review.

## Decision

`ProjectSettings/BlockiverseVersion.txt` is the single source of truth for the
SemVer base version. It contains only `MAJOR.MINOR.PATCH`, without a leading
`v` and without prerelease metadata. The version file intentionally does not
live at the repository root because Android IL2CPP compilation on
case-insensitive developer filesystems can resolve a root `VERSION` file as
`./version`, shadowing the standard C++ `<version>` header.

Android `versionName` values derive from `ProjectSettings/BlockiverseVersion.txt`:

| Stage | Android versionName format | Example |
|---|---|---|
| Local development | `MAJOR.MINOR.PATCH-dev.local.YYYYMMDDHHMMSS` | `0.1.0-dev.local.20260621120000` |
| CI smoke | `MAJOR.MINOR.PATCH-ci.runRUN.ATTEMPT.SHORTSHA` | `0.1.0-ci.run318.4.663f074` |
| Alpha | `MAJOR.MINOR.PATCH-alpha.runRUN.ATTEMPT.SHORTSHA` | `0.1.0-alpha.run318.4.663f074` |
| Beta candidate | `MAJOR.MINOR.PATCH-beta.N` | `0.1.0-beta.1` |
| RC candidate | `MAJOR.MINOR.PATCH-rc.N` | `0.1.0-rc.1` |
| Production | `MAJOR.MINOR.PATCH` | `0.1.0` |

`ProjectSettings/BlockiverseVersion.txt` is advanced by a normal pull request
before starting a new release train. `quest-alpha.yml` computes Alpha version
names automatically for normal `main` pushes. Manual Alpha dispatch may supply
a `version_name` override when a build should be tested in Alpha with a Beta,
RC, or Production-facing name before promotion.

Android `versionCode` is generated as seconds since `2020-01-01T00:00:00Z`.
It is monotonic across all newly uploaded builds and must not reset per channel.
The Android code is not the product version; it exists only to satisfy Android
package upgrade ordering.

Local development APKs follow the same Android `versionCode` rule by default so
current source builds can install over previously tested Alpha or development
builds without uninstalling the app or invoking Android downgrade behavior.

Promotion does not rewrite APK metadata. A build promoted from Alpha to Beta,
RC, or Store keeps the exact `versionName`, `versionCode`, package name,
keystore signature, and binary contents it had when it was uploaded to Alpha.

## Consequences

- Version bumps are explicit, reviewable code changes.
- Local headset validation installs current development builds over earlier
  builds instead of resetting package data because of stale project-setting
  version codes.
- Pull requests cannot leak Meta credentials or publish channel builds.
- Alpha artifacts can be uploaded frequently while still using the same release
  signing path as later channels.
- Beta, RC, and Production cannot silently drift to a different artifact because
  promotion moves an existing Meta build instead of rebuilding.
- The workflow intentionally avoids automatically inferring the next SemVer
  version from Git history or commit messages. That keeps release intent
  explicit for a small single-maintainer project.
