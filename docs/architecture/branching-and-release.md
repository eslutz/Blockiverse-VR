# Branching And Release Model

Blockiverse VR uses trunk-based development.

- `main` is protected and always releasable.
- `main` protection is managed with a repository ruleset, not classic branch protection.
- Work happens in short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches only.
- Releases are created from `main` tags only.
- There is no long-lived `develop` branch.
- There are no long-lived release branches.
- GitHub should automatically delete head branches after pull requests merge.

Release tags must match `v*` and point to commits reachable from `origin/main`. The release workflow must verify ancestry before building or signing release artifacts.

The `main` ruleset must require the `Repository checks` status check, require branches to be up to date before merge, require linear history, require conversation resolution, and block force pushes and branch deletion. Do not require approving reviews or CODEOWNERS review while Eric is the only human maintainer.
