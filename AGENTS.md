# AGENTS.md

This file defines standing workflow instructions for AI agents and automation working in this repository. Treat these instructions as project policy unless Eric Slutz explicitly overrides them.

## Source Of Truth

- Read and follow the committed execution plan: [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md).
- Use the GitHub Project `Blockiverse VR Roadmap` and linked GitHub issues for current scope, priority, status, and detailed work breakdown.
- Preserve the roadmap hierarchy: epics contain features, features contain stories, and implementation work should trace back to the relevant issue whenever one exists.
- Do not duplicate detailed product, architecture, testing, art, release, or platform requirements in this file. Keep those details in the plan, issues, project fields, or pull requests.

## Human Owner

- The project owner is Eric Slutz.
- The GitHub username for assignment and review is `eslutz`.
- When work begins on an issue, assign that issue to `eslutz` unless Eric explicitly says otherwise.
- Eric must provide final approval for complex, high-risk, product-facing, or pull-request-backed work during the `In Review` phase.
- Simple administrative or repository-configuration issues may be validated, moved to `Done`, and closed by an agent without additional Eric approval when all acceptance criteria are objectively satisfied and evidence is posted to the issue before closing.
- Eric is currently the only human on the project. Because automation-created pull requests are created under `eslutz`, GitHub repository rulesets must not require approving PR reviews or Code Owners review. Otherwise Eric cannot approve his own PR and the repository deadlocks.
- Keep `main` protected with a repository ruleset that requires status checks, linear history, conversation resolution, and force-push protection. Do not configure a required approving review count or required Code Owners review unless another human reviewer is added to the project.

## GitHub Issue And Project Workflow

- Before starting work, identify the best matching GitHub issue.
- If an issue exists for the work:
  - Assign it to `eslutz`.
  - Move it to the correct active lane/status in `Blockiverse VR Roadmap`.
  - Create or use a branch linked to the issue.
  - Link every branch, pull request, and review to the issue.
  - Keep the issue updated with material decisions, blockers, validation notes, and follow-up tasks.
- If no issue exists for the work:
  - Create one unless the work is truly trivial.
  - Add it to `Blockiverse VR Roadmap`.
  - Set the appropriate Type, Phase, Priority, Area, Risk, Target Release, Effort, and Roadmap Milestone fields.
  - Link it to the correct parent epic or feature when applicable.
- Move issues through the project lanes as a human developer would:
  - `Backlog` for planned but not started work.
  - `Ready` for work that is scoped and unblocked.
  - `In Progress` when active implementation begins.
  - `Blocked` when progress is waiting on an external dependency or decision.
  - `In Review` when a pull request is open or review is needed.
  - `Done` after implementation, validation, documentation, and any required approval are complete.
- Keep parent and child issue statuses coherent:
  - When a story starts, move that story and its active parent feature or epic to `In Progress`.
  - When completed work is ready for Eric review, move the completed story to `In Review`.
  - Move a parent feature to `In Review` when its relevant child stories are in `In Review` or `Done` and no child is still active.
  - Keep an epic `In Progress` while any of its features remain active or review is still pending.
  - Move completed pre-existing cards to `In Review` when they need Eric review.
  - Move completed pre-existing cards directly to `Done` when they are simple, objectively verifiable, do not require a PR, and closing evidence has been posted.
- Leave a useful issue comment whenever status changes materially:
  - Start comment: branch name, implementation scope, linked parent or children, and expected validation.
  - Progress comment: decisions, blockers, or scope changes.
  - Review comment: PR link, validation commands, manual validation notes, and residual risk.
- Do not close an issue unless the acceptance criteria and relevant validation steps are satisfied.
- Do not move an issue to `Done` until either Eric has approved the completed work or the work qualifies for autonomous closure under the rules below.

## Dependency, Tool, And Workflow Currency

- Before adding or changing GitHub Actions, packages, SDKs, CLIs, Unity packages, build images, or other third-party dependencies, verify the current stable version from official upstream sources such as release pages, package registries, vendor docs, or GitHub API output.
- Prefer the latest stable major version unless the repository has a documented compatibility constraint, required runner/runtime version, licensing issue, or migration risk that justifies staying back.
- Do not pin new work to stale major versions just because they appear in older examples, generated snippets, marketplace pages, or existing workflow files.
- When updating GitHub Actions workflows, review every `uses:` reference in `.github/workflows/`, update related actions together when practical, and check release notes for major-version migration requirements such as Node.js or GitHub Actions runner minimums.
- When a dependency cannot be safely updated to the latest stable major, document the reason in the issue or PR, keep the newest compatible version, and create a follow-up issue if the blocker should be removed later.
- Include version-currency evidence in the validation notes for dependency or workflow changes: what was checked, what version was selected, and why it is compatible with this repo.

### Autonomous Issue Closure

Agents may move issues to `Done` and close them without additional Eric approval only when the work is simple, low-risk, and objectively verifiable.

Autonomous closure is appropriate for tasks such as:

- Creating or verifying repository files, labels, milestones, issue templates, project fields, or folders.
- Updating repository settings or rulesets when Eric has directly requested the setting change.
- Documentation-only policy changes that Eric explicitly requested and that do not change product behavior.
- Scripted roadmap/bootstrap bookkeeping where command output proves the requested state.

Autonomous closure is not appropriate when the issue:

- Is implemented by an open pull request that has not been merged.
- Changes gameplay, VR behavior, networking, persistence, save/load, signing, release, store submission, privacy, licensing, security posture, or user-visible behavior.
- Has ambiguous acceptance criteria or requires product/design judgment.
- Has failing, missing, or incomplete validation.
- Has unresolved blockers, follow-up tasks that are part of the acceptance criteria, or known risk needing Eric's decision.

Before autonomously closing any issue, an agent must:

- Re-read the issue body and linked parent or child issues.
- Verify the completed state with direct evidence from local files, GitHub API output, workflow results, or command output.
- Add an issue comment that includes:
  - What was verified.
  - The exact evidence or validation commands.
  - Any relevant links to files, settings, project items, or PRs.
  - A statement that the issue is being closed under the autonomous closure rule.
- Move the Project item to `Done`.
- Close the issue with state reason `completed`.
- Verify both the GitHub issue state and the Project lane after closing.

### GitHub Project Update Procedure

- Prefer GitHub CLI for ProjectV2 lane updates because the GitHub connector may not expose project field mutations.
- Before changing project lanes, verify authentication and project access:

```sh
gh auth status
gh project list --owner eslutz --limit 100
```

- Resolve the `Blockiverse VR Roadmap` project number and field option IDs instead of hard-coding them:

```sh
gh project list --owner eslutz --limit 100 --format json
gh project field-list <PROJECT_NUMBER> --owner eslutz --format json
```

- Resolve item IDs from the project before editing a lane:

```sh
gh project item-list <PROJECT_NUMBER> --owner eslutz --limit 200 --format json
```

- Update the `Status` field with `gh project item-edit` using the resolved project ID, item ID, Status field ID, and single-select option ID.
- Verify the lane after every batch update with `gh project item-list` or `gh issue view --json projectItems`.
- If `gh auth status` fails, try the same command outside the sandbox if available. If authentication is still missing, start `gh auth login -h github.com`, give Eric the one-time code and URL, then retry after he completes the flow.
- If project updates cannot be completed, still assign/update/comment on the issue and explicitly report the project-lane blocker.

### Issue And Pull Request Linking

- Name branches so the issue relationship is obvious, for example `feature/20-ci-foundation-checks` or `feature/53-block-registry`.
- Link pull requests to issues in the PR body.
- Use non-closing references such as `Related to #20` unless Eric has explicitly asked for merge to close the issue.
- Use closing keywords such as `Closes #20` only when all acceptance criteria are complete and Eric has approved closing on merge.
- For autonomously closeable issues, close the issue directly after posting evidence instead of relying on PR closing keywords.
- Add reciprocal issue comments with the PR link for every linked issue and important parent issue.
- When a PR covers multiple issues, list all of them in the PR body and move each review-ready issue to `In Review`.
- Keep PR descriptions useful enough for a human to resume work: include scope, linked issues, validation commands, manual validation, risk notes, and known follow-ups.

## Branching, Pull Requests, And Reviews

- Use trunk-based development.
- Keep `main` protected and releasable.
- Do not create or use a long-lived `develop` branch.
- Do not create long-lived release branches.
- Use short-lived branches only:
  - `feature/*`
  - `fix/*`
  - `chore/*`
  - `spike/*`
  - `hotfix/*`
- Name branches so the linked issue is obvious, for example `feature/53-block-registry`.
- Keep GitHub repository settings configured to automatically delete head branches after pull requests merge.
- All production releases must be cut from `main`.
- Release tags must match `v*` and point to commits reachable from `origin/main`.
- Prefer pull requests into `main` after CI passes. Direct pushes to `main` should be rare and explicit.
- When a pull request is opened:
  - Link the associated issue if one exists.
  - Move linked issues to `In Review`.
  - Request Eric's final approval in the PR or linked issue comments.
  - Do not require GitHub approving reviews while Eric is the sole human maintainer.
- Do not merge a pull request, close the linked issue, or move the linked issue to `Done` until Eric has approved the work or explicitly asked the agent to merge/complete it.
- PRs must include:
  - Linked issue, when one exists.
  - Summary of player-facing and technical changes.
  - Test evidence.
  - Manual validation steps when VR, save/load, networking, performance, signing, store, or Quest device behavior changes.
  - Risk notes for high-risk areas.

## Project Guardrails

- Treat Meta Quest 3 and Meta Quest 3S as primary target platforms.
- Basic multiplayer scope includes voice chat.
- Use original names.
- Keep assets original and do not copy protected third-party identity.
- Never commit secrets, keystores, signing credentials, API keys, `.env` files, Unity `Library`, `Temp`, `Logs`, or local generated folders.
- Keystores and production signing material must remain outside the repo and be stored in GitHub Actions secrets when needed.
- Current licensing state: source-available / All Rights Reserved. Keep `LICENSE.md`, `NOTICE.md`, and relevant docs aligned with current project intent.

## Local Unity Validation

- Run local Unity EditMode and PlayMode tests with:

```sh
scripts/unity/run-tests.sh
```

- The test script writes NUnit XML results to `TestResults/Unity/EditMode.xml` and `TestResults/Unity/PlayMode.xml`.
- If Unity batchmode logs `ResponseCode: 505`, `Unsupported protocol version '1.18.1'`, or waits on `LicenseClient-ericslutz-6000.3.16`, the Unity Hub licensing client is likely stale or protocol-incompatible with the editor batchmode client. Reset the local Unity/Hub process state, then rerun the test script:

```sh
osascript -e 'tell application "Unity Hub" to quit'
pkill -f 'Unity.Licensing.Client|Unity Hub Helper|Unity Hub.app' || true
pgrep -afil 'Unity|Licensing|UnityPackageManager'
scripts/unity/run-tests.sh
```

- The `pgrep` command should return no Unity editor, Unity Hub, UnityPackageManager, or Unity licensing processes before the retry. A successful retry starts a fresh editor licensing client and logs `Licensing is initialized` before compiling scripts.
- Do not leave stuck Unity batchmode processes running. If a test or build command is trapped in a licensing retry loop, stop the Unity process, verify with `pgrep -x Unity`, and record the blocker or retry from a clean process state.

## Documentation Discipline

- Update documentation when behavior, workflow, architecture, or project policy changes.
- Keep [CHANGELOG.md](CHANGELOG.md) up-to-date with completed work that changes project behavior, workflow, documentation, release process, or user-visible scope.
- Add completed work to the `Unreleased` section unless the change is being documented directly under a release version.
- Keep issue bodies and PR descriptions useful enough for a human developer to resume the work.
- Record important technical decisions under `docs/adr/`.
- Keep the execution plan, GitHub Project, and linked issues aligned when roadmap structure changes.
