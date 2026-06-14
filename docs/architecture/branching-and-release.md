# Branching And Release Model

Blockiverse VR uses trunk-based development.

- `main` is protected and always releasable.
- `main` protection is managed with a repository ruleset, not classic branch protection.
- Work happens in short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches only.
- Production releases are created from `main` tags only.
- There is no long-lived `develop` branch.
- There are no long-lived release branches.
- GitHub should automatically delete head branches after pull requests merge.

Release channels use SemVer-compatible tags and GitHub Releases:

| Channel | Trigger | Version convention | Build | Main ancestry |
|---|---|---|---|---|
| Alpha | Same-repository pull request updates | `vX.Y.Z-alpha.prN.RUN.ATTEMPT` | Build development APK and upload to Meta `alpha` | Not required |
| Beta | Pushes to `main`, normally from merged pull requests | `vX.Y.Z-beta.runN.ATTEMPT` | Build signed release APK and upload to Meta `beta` | Required |
| Release candidate | Manual workflow dispatch | `vX.Y.Z-rc.N` | Promote selected Meta Beta build to Meta `rc` | Required for promoted GitHub release tags |
| Production | Manual workflow dispatch after Meta Store submission/review approval | `vX.Y.Z` | Promote selected Meta RC build to Meta `store` | Required for production tags |

The `-alpha`, `-beta`, and `-rc` families are GitHub pre-releases. Production tags must match `vX.Y.Z` and point to commits reachable from `origin/main`. The shared release build workflow creates only alpha and beta APKs. RC and production workflows must preserve Meta artifact identity by promoting the selected `meta_build_id` instead of rebuilding.

Known-good engineering checkpoint tags use the `kg/...` family and are governed by [Voxel Known-Good Git Tagging Policy](../rulesets/voxel_git_known_good_tagging_policy.md). They are recovery points, not player-facing releases.

The `main` ruleset must require the `Repository checks` status check, require branches to be up to date before merge, require linear history, require conversation resolution, and block force pushes and branch deletion. Do not require approving reviews or CODEOWNERS review while Eric is the only human maintainer.

Pull requests should link a GitHub issue when active issue tracking exists. Otherwise, link the relevant execution-plan section or ruleset; GitHub issues and projects are workflow aids, not the canonical roadmap.
