# AGENTS.md

This file defines standing workflow instructions for AI agents and automation working in this repository. Treat these instructions as project policy unless Eric Slutz explicitly overrides them.

## Source Of Truth

- Read and follow the committed execution plan: [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md).
- Read and follow the canonical rulesets under [docs/rulesets/](docs/rulesets/). These documents define the current game design, implementation vocabulary, save schema, menus, environment, structures, vegetation, multiplayer behavior, audio/VFX behavior, and known-good checkpoint policy.
- The rulesets define the game. Existing temporary validation worlds, reduced starter registries, and old temporary block/item names are migration inputs only.
- Use [docs/rulesets/voxel_implementation_alignment_matrix.md](docs/rulesets/voxel_implementation_alignment_matrix.md) only as a migration/refactor aid. It is not a second gameplay vocabulary.
- Use [CLAUDE.md](CLAUDE.md) for repository orientation: common developer commands and the high-level code architecture map. It supplements this file and does not override workflow policy here.
- Use GitHub issues and pull requests for active workflow state only: current bugs, blockers, review work, multi-PR initiatives, and durable follow-ups.
- Use the GitHub Project `Blockiverse VR Roadmap` as a lightweight active-work board. It is not a canonical roadmap, product spec, or required issue hierarchy.
- Do not duplicate detailed product, architecture, testing, art, release, or platform requirements in this file. Keep those details in the execution plan, rulesets, pull requests, or focused issues.

## Human Owner

- The project owner is Eric Slutz.
- The GitHub username for assignment and review is `eslutz`.
- When work begins on an issue, assign that issue to `eslutz` unless Eric explicitly says otherwise.
- Eric must provide final approval for complex, high-risk, product-facing, or pull-request-backed work during the `In Review` phase.
- Simple administrative or repository-configuration issues may be validated, moved to `Done`, and closed by an agent without additional Eric approval when all acceptance criteria are objectively satisfied and evidence is posted to the issue before closing.
- Eric is currently the only human on the project. Because automation-created pull requests are created under `eslutz`, GitHub repository rulesets must not require approving PR reviews or Code Owners review. Otherwise Eric cannot approve his own PR and the repository deadlocks.
- Keep `main` protected with a repository ruleset that requires status checks, linear history, conversation resolution, and force-push protection. Do not configure a required approving review count or required Code Owners review unless another human reviewer is added to the project.

## GitHub Issue And Project Workflow

- Use issues deliberately. Do not create or maintain a GitHub issue for every roadmap line, backlog row, feature, or story in the execution plan.
- Before starting work, check for an existing issue only when one is likely to exist. If no issue exists, proceed without creating one unless issue tracking would materially help the work.
- Create or use an issue when Eric asks for one, the work spans multiple pull requests, a bug or blocker needs durable tracking, manual or external validation must remain open, or a follow-up must survive beyond the current pull request.
- If an issue exists for the work:
  - Assign it to `eslutz` unless Eric explicitly says otherwise.
  - Create or use a branch whose relationship to the issue is clear.
  - Link the branch, pull request, and relevant review comments to the issue.
  - Keep the issue updated with material decisions, blockers, validation notes, and follow-up tasks.
- If no issue is needed, reference the relevant execution-plan section or ruleset in the pull request and include normal validation evidence.
- Use the GitHub Project `Blockiverse VR Roadmap` only for active-work visibility. Keep active bugs, blockers, in-review work, and current initiative issues or pull requests there when access is available.
- Treat project status updates as best-effort workflow hygiene, not a blocking implementation requirement. Do not require legacy metadata fields for Type, phase, priority, area, risk, target release, effort, or roadmap milestone.
- Use these project statuses when maintaining active cards:
  - `Backlog` for planned active work that is not started.
  - `Ready` for scoped and unblocked work.
  - `In Progress` for active implementation or investigation.
  - `Blocked` for work waiting on an external dependency or decision.
  - `In Review` for open pull requests or work awaiting Eric review.
  - `Done` after implementation, validation, documentation, and required approval are complete.
- Leave a useful issue comment when issue-backed work changes materially:
  - Start comment: branch name, implementation scope, and expected validation.
  - Progress comment: decisions, blockers, or scope changes.
  - Review comment: PR link, validation commands, manual validation notes, and residual risk.
- Do not close an active bug, blocker, validation-gated issue, or PR-backed issue unless the acceptance criteria and relevant validation steps are satisfied.
- Old roadmap hierarchy issues may be closed with reason `not planned` when Eric has explicitly approved consolidation. In that case, `not planned` means "no longer tracked as a standalone GitHub issue"; it does not cancel canonical roadmap or ruleset scope.

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

- Creating or verifying repository files, labels, milestones, issue templates, project settings, or folders.
- Updating repository settings or rulesets when Eric has directly requested the setting change.
- Documentation-only policy changes that Eric explicitly requested and that do not change product behavior.
- Scripted repository or GitHub cleanup where command output proves the requested state.
- Closing stale tracking-only issues or milestones when Eric has explicitly requested consolidation and the closing comment points to the canonical docs or retained active issue.

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
- Update, archive, or remove the Project item when Project access is available and the issue is tracked there.
- Close the issue with the correct state reason: `completed` for objectively completed work, `not planned` for retired standalone tracking scope, or `duplicate` for duplicate issues.
- Verify the GitHub issue state after closing. Verify the Project state too when Project access is available.

### GitHub Project Use

- Prefer GitHub CLI for project status updates and cleanup because the GitHub connector may not expose all project mutations.
- Before changing project cards, verify authentication and project access:

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

- Update only the `Status` field unless Eric explicitly asks for additional project metadata.
- Archive or remove stale tracking cards when their issues have been closed under a consolidation cleanup.
- Verify the active-card set after every batch update with `gh project item-list` or `gh issue view --json projectItems`.
- If `gh auth status` fails, try the same command outside the sandbox if available. If authentication is still missing, start `gh auth login -h github.com`, give Eric the one-time code and URL, then retry after he completes the flow.
- If project updates cannot be completed because the token is missing `read:project` or `project` scopes, continue the issue or pull-request work and explicitly report the project cleanup blocker.

### Issue And Pull Request Linking

- Name branches so the issue relationship is obvious when an issue exists, for example `feature/53-block-registry`.
- Link pull requests to issues in the PR body when an issue exists. Otherwise link to the relevant execution-plan section or ruleset.
- Use non-closing references such as `Related to #20` unless Eric has explicitly asked for merge to close the issue.
- Use closing keywords such as `Closes #20` only when all acceptance criteria are complete and Eric has approved closing on merge.
- For autonomously closeable issues, close the issue directly after posting evidence instead of relying on PR closing keywords.
- Add reciprocal issue comments with the PR link for linked active issues.
- When a PR covers multiple issues, list all of them in the PR body and move each review-ready issue to `In Review` when Project access is available.
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
- Name branches so the linked issue is obvious when an issue exists, for example `feature/53-block-registry`.
- Keep GitHub repository settings configured to automatically delete head branches after pull requests merge.
- All production releases must be cut from `main`.
- Production release tags must match `vX.Y.Z` and point to commits reachable from `origin/main`.
- Channel prerelease tags must use the release workflow convention: `vX.Y.Z-alpha.prN.RUN.ATTEMPT` for alpha, `vX.Y.Z-beta.runN.ATTEMPT` for beta, and `vX.Y.Z-rc.N` for release candidates. Alpha tags may point to same-repository pull request commits; beta, release-candidate, and production tags must point to commits reachable from `origin/main`.
- Prefer pull requests into `main` after CI passes. Direct pushes to `main` should be rare and explicit.
- When a pull request is opened:
  - Link the associated issue if one exists, or link the relevant execution-plan section or ruleset.
  - Move linked issues to `In Review` when Project access is available.
  - Request Eric's final approval in the PR or linked issue comments.
  - Do not require GitHub approving reviews while Eric is the sole human maintainer.
- Do not merge a pull request, close the linked issue, or move the linked issue to `Done` until Eric has approved the work or explicitly asked the agent to merge/complete it.
- PRs must include:
  - Linked issue, when one exists, or the relevant execution-plan section or ruleset.
  - Summary of player-facing and technical changes.
  - Test evidence.
  - Manual validation steps when VR, save/load, networking, performance, signing, store, or Quest device behavior changes.
  - Risk notes for high-risk areas.

## Project Guardrails

- Treat Meta Quest 3 and Meta Quest 3S as primary target platforms.
- Initial multiplayer uses Meta Quest party chat for voice. Do not add in-app voice chat unless the rulesets and roadmap are explicitly changed.
- Use original names.
- Keep assets original and do not copy protected third-party identity.
- New gameplay code, UI labels, registries, save data, and tests should use stable canonical IDs from the rulesets. Legacy IDs and names should be handled through explicit migration code or marked as historical validation artifacts.
- Never commit secrets, keystores, signing credentials, API keys, `.env` files, Unity `Library`, `Temp`, `Logs`, or local generated folders.
- Keystores and production signing material must remain outside the repo and be stored in GitHub Actions secrets when needed.
- Current licensing state: source-available / All Rights Reserved. Keep `LICENSE.md`, `NOTICE.md`, and relevant docs aligned with current project intent.

## Project Tooling

- Prefer reproducible command-line tooling over GUI-only actions whenever the command output is useful validation evidence.
- Use the Unity MCP server for interactive Unity Editor inspection, simulator-oriented editor workflows, scene or object checks, and Unity-specific automation that is exposed through MCP.
- Use the committed local scripts as the source of truth for repeatable Unity validation. In particular, `scripts/unity/run-tests.sh` remains the required EditMode and PlayMode validation command even when Unity MCP is available.
- Use the globally installed Horizon Debug Bridge CLI, `hzdb`, for Meta Quest device work instead of enabling the hzdb MCP server in the base Codex config while the MCP server advertises schemas that can trigger `invalid_function_parameters` errors. On Eric's current development machine, `hzdb` resolves to `/Users/ericslutz/.nvm/versions/node/v24.16.0/bin/hzdb`; verify with `command -v hzdb` because the path can change when the default `nvm` Node changes. If the shell's default `node` or `npm` points somewhere else, derive the Node prefix from `hzdb` and put that prefix first on `PATH` for package-manager checks.
- Install `hzdb` from the npm package `@meta-quest/hzdb` under the current default `nvm` Node version. If the default Node changes and `hzdb` is no longer on `PATH`, reinstall it with:

```sh
npm install -g @meta-quest/hzdb@1.2.1
```

- Verify hzdb availability before device work with:

```sh
HZDB_BIN="$(command -v hzdb)"
HZDB_NODE_PREFIX="$(cd "$(dirname "$HZDB_BIN")/.." && pwd)"
"$HZDB_NODE_PREFIX/bin/node" --version
PATH="$HZDB_NODE_PREFIX/bin:$PATH" npm list -g --depth=0 @meta-quest/hzdb
hzdb --version
hzdb device list
```

- In Codex sandboxed terminal sessions, `hzdb` may be on `PATH` while physical Quest discovery still fails. If `hzdb device list` reports no devices in the sandbox, rerun the same command outside the sandbox before treating device validation as blocked. Use outside-sandbox `hzdb` for physical device discovery, install, launch, logs, screenshots, and captures when USB/device access requires host-level access.
- Prefer hzdb commands for Quest-device validation tasks such as device discovery, app install and launch, log capture, screenshots, screen recordings, file transfer, and performance captures. Record the exact hzdb commands and relevant output in issue or PR validation notes.
- Use `adb` directly only when hzdb does not expose the needed operation or when comparing behavior against lower-level Android tooling. Document why the fallback was needed.
- Do not commit local device logs, screenshots, recordings, Perfetto traces, APKs, or other large/generated validation artifacts unless a tracked artifact is explicitly required. Store them outside the repo or attach them to the relevant GitHub issue, pull request, or workflow artifact instead.
- Keep experimental or unstable MCP servers out of the base Codex config. If a server is needed for testing but can break prompt execution, put it in a separate Codex profile, validate it with `codex mcp list` and a fresh Codex session, and remove or disable it immediately if it blocks normal prompts.
- Use GitHub CLI for best-effort GitHub Project status updates and cleanup, because GitHub MCP/connector tools may not expose all project mutations.
- Use browser automation tools for local web targets only when the task involves browser-visible UI, screenshots, or interaction checks.
- Use annotated `kg/...` tags only for validated engineering checkpoints and follow [docs/rulesets/voxel_git_known_good_tagging_policy.md](docs/rulesets/voxel_git_known_good_tagging_policy.md). Do not use known-good checkpoint tags as release tags.

## Local Unity Validation

- Follow the tiered validation contract in [docs/testing/README.md](docs/testing/README.md). Use targeted `scripts/unity/run-tests.sh --platform ... --filter ... --results-name ...` runs while iterating, but do not treat them as a replacement for the full gate when Unity-impacting work moves to review or merge.
- Run the full local Unity EditMode and PlayMode gate with:

```sh
scripts/unity/run-tests.sh
```

- With no arguments, the test script writes NUnit XML results to `TestResults/Unity/EditMode.xml` and `TestResults/Unity/PlayMode.xml`. Named targeted runs write separate XML files under `TestResults/Unity/`.
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
- Keep canonical design and implementation rules in [docs/rulesets/](docs/rulesets/) and roadmap sequencing in [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md). Other docs should point to those sources instead of redefining gameplay or design rules.
- Keep [CHANGELOG.md](CHANGELOG.md) up-to-date with completed work that changes project behavior, workflow, documentation, release process, or user-visible scope.
- Add completed work to the `Unreleased` section unless the change is being documented directly under a release version.
- Keep active issue bodies and PR descriptions useful enough for a human developer to resume the work.
- Record important technical decisions under `docs/adr/`.
- Keep the execution plan, rulesets, and active issues or pull requests aligned when roadmap structure or canonical design scope changes.
