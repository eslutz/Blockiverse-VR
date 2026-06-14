# Branching And Release Model

Blockiverse VR uses trunk-based development.

- `main` is protected and always releasable.
- `main` protection is managed with a repository ruleset, not classic branch protection.
- Work happens in short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches only.
- Production releases are created from builds that originated from trusted `main` history and were intentionally promoted through the Meta release channels.
- There is no long-lived `develop` branch.
- There are no long-lived release branches.
- GitHub should automatically delete head branches after pull requests merge.

Release channels use SemVer-derived Android `versionName` values. The root
`VERSION` file is the SemVer base version source; see
[ADR 0005](../adr/0005-release-versioning.md).

| Workflow | Trigger | Purpose | Meta credentials |
|---|---|---|---|
| `quest-ci.yml` | Pull requests and manual dispatch | Validate Unity tests and an Android smoke APK | No |
| `quest-alpha.yml` | Pushes to `main` and manual trusted dispatch | Build a release-signed Quest APK and upload it to Meta `alpha` | Yes, `meta-alpha` |
| `quest-promote.yml` | Manual dispatch only | Promote an existing Meta build through `alpha -> beta`, `beta -> rc`, or `rc -> store` | Yes, destination environment |

`quest-ci.yml` must not publish to Meta or receive Meta secrets. `quest-alpha.yml`
is the only workflow that builds and uploads a new Meta channel APK. It uses
Unity Personal activation through GameCI and release signs with the stable
Android package name and release keystore. `quest-promote.yml` must not rebuild:
it moves a previously uploaded Meta build to the next channel.

Meta channel progression is:

```text
alpha -> beta
beta -> rc
rc -> store
```

The `meta-alpha` environment may be automatic. `meta-beta` should require
manual approval once external testers are involved. `meta-rc` and
`meta-production` should require manual approval; `meta-production` should also
be restricted to trusted refs such as `main`, release branches, or version tags.

Alpha uses workflow-level GitHub Actions concurrency with
`cancel-in-progress: false` so a newer Alpha build/upload run cannot overtake a
running Alpha build/upload run. GitHub Actions keeps at most one pending run for
a concurrency group; that is an intentional simplification tradeoff.

Known-good engineering checkpoint tags use the `kg/...` family and are governed by
[Voxel Known-Good Git Tagging Policy](../rulesets/voxel_git_known_good_tagging_policy.md).
They are recovery points, not player-facing releases.

The `main` ruleset must require the `Repository checks` status check, require
branches to be up to date before merge, require linear history, require
conversation resolution, and block force pushes and branch deletion. Do not
require approving reviews or CODEOWNERS review while Eric is the only human
maintainer.

Pull requests should link a GitHub issue when active issue tracking exists.
Otherwise, link the relevant execution-plan section or ruleset; GitHub issues
and projects are workflow aids, not the canonical roadmap.
