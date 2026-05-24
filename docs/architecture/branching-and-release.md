# Branching And Release Model

Blockiverse VR uses trunk-based development.

- `main` is protected and always releasable.
- Work happens in short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches only.
- Releases are created from `main` tags only.
- There is no long-lived `develop` branch.
- There are no long-lived release branches.
- GitHub should automatically delete head branches after pull requests merge.

Release tags must match `v*` and point to commits reachable from `origin/main`. The release workflow must verify ancestry before building or signing release artifacts.
