# Store Submission Checklist

This checklist tracks in-repository store-readiness work for M6. External release preparation remains intentionally out of scope for this pass: no Meta Developer Dashboard changes, build uploads, release channels, tester invites, production signing secrets, or Submit for Review action.

## In-Repo Metadata And Policy Artifacts

| Item | Source | Status |
| --- | --- | --- |
| App metadata | [metadata.md](metadata.md) | Draft ready |
| Short description | [metadata.md](metadata.md) | Draft ready |
| Long description | [metadata.md](metadata.md) | Draft ready |
| Search keywords | [metadata.md](metadata.md) | Draft ready |
| Comfort rating notes | [metadata.md](metadata.md) | Draft ready |
| Privacy policy | [privacy-policy.md](privacy-policy.md) | Draft ready; public HTTPS hosting still external |
| Data usage declarations | [data-use.md](data-use.md) | Draft ready |
| Screenshot and capture plan | [screenshots.md](screenshots.md) | Planned; final captures pending |
| VRC checklist | [vrc-checklist.md](vrc-checklist.md) | In-repo checklist ready |
| Known issues | [known-issues.md](known-issues.md) | Draft ready |
| Release notes | [release-notes.md](release-notes.md) | Draft ready |
| Support information | [support.md](support.md) | Draft ready; public support path still external |

## App Metadata Evidence

| Check | Evidence | Status |
| --- | --- | --- |
| Unity product name | `ProjectSettings/ProjectSettings.asset` has `productName: Blockiverse VR` | Ready |
| Publisher/company name | `ProjectSettings/ProjectSettings.asset` has `companyName: Eric Slutz` | Ready |
| Android package id | `ProjectSettings/ProjectSettings.asset` has `dev.ericslutz.blockiversevr` | Ready |
| Quest device support | Android manifest declares `quest3|quest3s` | Ready |
| App label | Android branding resource sets `app_name` to `Blockiverse VR` | Ready |
| VR launch category | Android manifest includes `com.oculus.intent.category.VR` | Ready |
| Production signing material | No keystore or signing secret committed; checked by `scripts/ci/forbidden-files.sh` | In-repo ready; external signing out of scope |

## External Steps Not Done In This Pass

| External item | Reason |
| --- | --- |
| Create or update Meta Developer Dashboard app | Outside the app/repo scope requested for this pass |
| Upload candidate build | Requires release-channel/build workflow and external dashboard action |
| Create release channels | External Meta dashboard action |
| Invite private testers | External Meta dashboard action |
| Configure production signing secrets | Secret-management work is excluded from this pass |
| Publish privacy/support URLs | Requires selected external hosting and Eric approval |
| Submit for review | Requires final approved build, metadata, VRC evidence, and Eric approval |

## Validation

Run the in-repo validation script before opening a PR:

```sh
scripts/store/validate-store-submission-docs.sh
```

Also run:

```sh
scripts/ci/forbidden-files.sh
git diff --check
```
