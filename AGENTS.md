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
- Do not close an issue unless the acceptance criteria and relevant validation steps are satisfied.

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
  - Assign the review to `eslutz`.
  - Request Eric's final approval.
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
- Keep issue bodies and PR descriptions useful enough for a human developer to resume the work.
- Record important technical decisions under `docs/adr/`.
- Keep the execution plan and roadmap scripts aligned when roadmap structure changes.
