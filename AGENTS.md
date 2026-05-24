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
- Eric must provide final approval for all work during the `In Review` phase.
- Eric is currently the only human on the project. Because automation-created pull requests are created under `eslutz`, GitHub branch protection must not require approving PR reviews or Code Owners review. Otherwise Eric cannot approve his own PR and the repository deadlocks.
- Keep `main` protected with required status checks, linear history, conversation resolution, and force-push protection. Do not configure a required approving review count or required Code Owners review unless another human reviewer is added to the project.

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
  - `Done` only after implementation, validation, documentation, and Eric's approval are complete.
- Keep parent and child issue statuses coherent:
  - When a story starts, move that story and its active parent feature or epic to `In Progress`.
  - When completed work is ready for Eric review, move the completed story to `In Review`.
  - Move a parent feature to `In Review` when its relevant child stories are in `In Review` or `Done` and no child is still active.
  - Keep an epic `In Progress` while any of its features remain active or review is still pending.
  - Move completed pre-existing cards to `In Review`, not `Done`, when Eric still needs to approve them.
- Leave a useful issue comment whenever status changes materially:
  - Start comment: branch name, implementation scope, linked parent or children, and expected validation.
  - Progress comment: decisions, blockers, or scope changes.
  - Review comment: PR link, validation commands, manual validation notes, and residual risk.
- Do not close an issue unless the acceptance criteria and relevant validation steps are satisfied.
- Do not move an issue to `Done` unless Eric has explicitly approved the completed work.

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
- All production releases must be cut from `main`.
- Release tags must match `v*` and point to commits reachable from `origin/main`.
- Prefer pull requests into `main` after CI passes. Direct pushes to `main` should be rare and explicit.
- When a pull request is opened:
  - Link the associated issue if one exists.
  - Move linked issues to `In Review`.
  - Request Eric's final approval in the PR or linked issue comments.
  - Do not require GitHub approving reviews while Eric is the sole human maintainer.
- Do not merge a pull request, close the linked issue, or move the linked issue to `Done` until Eric has approved the work.
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

## Documentation Discipline

- Update documentation when behavior, workflow, architecture, or project policy changes.
- Keep [CHANGELOG.md](CHANGELOG.md) up-to-date with completed work that changes project behavior, workflow, documentation, release process, or user-visible scope.
- Add completed work to the `Unreleased` section unless the change is being documented directly under a release version.
- Keep issue bodies and PR descriptions useful enough for a human developer to resume the work.
- Record important technical decisions under `docs/adr/`.
- Keep the execution plan and roadmap scripts aligned when roadmap structure changes.
