# Voxel Known-Good Git Tagging Policy

**Document status:** Proposed repository workflow policy
**Project:** Blockiverse VR
**Purpose:** Define how to mark validated good states in Git so world-generation, networking, VR interaction, and save-system changes can be safely recovered.

---

## 1. Goals

| Goal | Rule |
|---|---|
| Easy recovery | Every validated good state should have a remote tag that can be branched from. |
| No release confusion | Engineering checkpoint tags must not look like release tags. |
| Validation evidence | A tag should only be created after tests/manual checks prove the state is good. |
| No destructive rollback | Recover by branching from a tag, not by force-resetting protected `main`. |
| Clear history | Tags should be descriptive enough to identify why the state was good. |

---

## 2. Tag families

| Tag Family | Purpose | Example |
|---|---|---|
| `v<semver>` | Production release tags cut from `main`. | `v0.1.0` |
| `v<semver>-alpha.*`, `v<semver>-beta.*`, `v<semver>-rc.*` | Player-facing prerelease channel tags created by GitHub Actions. Alpha tags may point to same-repository pull request commits; beta and RC tags must point to `main` history. | `v0.1.0-alpha.pr315.42.1`, `v0.1.0-beta.run12.1`, `v0.1.0-rc.1` |
| `kg/YYYYMMDD-HHMM-<slug>` | Known-good engineering checkpoint. | `kg/20260606-1430-ruleset-world-docs` |
| `checkpoint/<slug>` | Optional local-only temporary branch checkpoint. Do not push unless explicitly needed. | `checkpoint/worldgen-spike` |

Known-good tags should be annotated tags, not lightweight tags.

---

## 3. When to create a known-good tag

Create a known-good tag after any of these states is validated:

| Good State | Minimum Validation |
|---|---|
| Documentation-only ruleset update | Markdown review, cross-document consistency check, links confirmed. |
| Unity gameplay/runtime change | `scripts/unity/run-tests.sh` passes. |
| VR movement/interaction change | Unity tests pass plus headset or simulator manual validation. |
| Save/load or migration change | Tests pass with old/new save fixtures and manual load/save smoke check. |
| Multiplayer/networking change | Tests pass plus local host/client validation; Quest device validation when the change is Quest-specific. |
| Audio/VFX change | Tests pass, cue assets validated, headset comfort/output check when available. |
| Release candidate | All required tests, store docs, signing/build checks, and manual Quest checks pass. |

---

## 4. Tag naming

Use:

```txt
kg/YYYYMMDD-HHMM-short-slug
```

Rules:

| Part | Rule |
|---|---|
| `kg/` | Required prefix for known-good checkpoints. |
| `YYYYMMDD-HHMM` | Use local project time or UTC consistently. Prefer local project time in notes. |
| `short-slug` | Lowercase, hyphenated, no spaces. Keep it specific. |

Examples:

```txt
kg/20260606-1430-before-world-migration
kg/20260606-1815-canonical-world-docs
kg/20260607-1010-survival-registry-migration
kg/20260607-1640-lan-delta-sync-stable
```

---

## 5. Create a known-good tag

Preferred flow from `main`:

```sh
git fetch --tags origin
git switch main
git pull --ff-only origin main

git status --short
scripts/unity/run-tests.sh

git tag -a kg/YYYYMMDD-HHMM-short-slug \
  -m "Known good: short summary." \
  -m "Validation: scripts/unity/run-tests.sh passed." \
  -m "Manual validation: describe headset/simulator/doc review, if applicable."

git push origin kg/YYYYMMDD-HHMM-short-slug
```

Documentation-only flow:

```sh
git fetch --tags origin
git switch main
git pull --ff-only origin main

git status --short
# Run any markdown/link/consistency checks available for the repo.

git tag -a kg/YYYYMMDD-HHMM-short-slug \
  -m "Known good: documentation ruleset checkpoint." \
  -m "Validation: markdown consistency review completed."

git push origin kg/YYYYMMDD-HHMM-short-slug
```

---

## 6. Verify a tag

```sh
git fetch --tags origin
git show --stat kg/YYYYMMDD-HHMM-short-slug
git ls-remote --tags origin kg/YYYYMMDD-HHMM-short-slug
```

The tag should point to the intended commit and should exist on `origin`.

---

## 7. Recover from a known-good tag

Create a branch from the tag:

```sh
git fetch --tags origin
git switch -c restore/from-known-good kg/YYYYMMDD-HHMM-short-slug
```

Then either:

| Need | Action |
|---|---|
| Inspect old state | Work on the restore branch only. |
| Build a hotfix from old state | Create a `hotfix/*` branch from the tag and open a PR. |
| Revert bad changes on main | Use `git revert` commits or a PR from a restore branch. Do not force-push protected `main`. |

---

## 8. Do not move known-good tags

Once pushed, a known-good tag is immutable.

Do not run:

```sh
git tag -f kg/YYYYMMDD-HHMM-short-slug
git push --force origin kg/YYYYMMDD-HHMM-short-slug
```

If a tag message was wrong or a better state is found, create a new tag.

---

## 9. Recommended first tag

Before replacing the temporary world implementation with the canonical ruleset-defined world, tag the current validated state:

```txt
kg/20260606-before-world-ruleset-migration
```

Suggested command:

```sh
git fetch --tags origin
git switch main
git pull --ff-only origin main
scripts/unity/run-tests.sh

git tag -a kg/20260606-before-world-ruleset-migration \
  -m "Known good before canonical world ruleset migration." \
  -m "Validation: scripts/unity/run-tests.sh passed before migration work began."

git push origin kg/20260606-before-world-ruleset-migration
```

---

## 10. Suggested repo doc updates

Add this policy to:

```txt
docs/architecture/known-good-tags.md
```

Then reference it from:

```txt
AGENTS.md
docs/architecture/branching-and-release.md
README.md, if desired
```
