# Blockiverse VR Project Memory

## Purpose

This file is the concise handoff for future agent work in the Blockiverse VR project. Use it to avoid rediscovering current project state, local tooling decisions, validation expectations, and known external gates.

## Memory Timestamp Policy

- Timestamp: 2026-08-13. Add a concrete date to new memory entries and materially updated decisions. Prefer `Timestamp: YYYY-MM-DD` near the relevant section or bullet.
- Entries not re-dated below survive from the 2026-06-20/21 handoff and were re-verified or left intentionally.

## Repository And Source Of Truth

- Repository root: `/Users/ericslutz/Developer/Code/Side Projects/Blockiverse/Blockiverse-VR` (moved from `.../Code/Blockiverse/` in mid-June; mtimes bulk-stamped 2026-06-15 by the move are not evidence of edit dates).
- Remote: `https://github.com/eslutz/Blockiverse-VR.git`.
- Timestamp: 2026-08-13. Checked-out branch: `codex/complete-m85-rendering-menu-gate` (M8.5 rendering/menu gate). Git state needs reconciliation before new PR work: the local `origin/main` ref is stale (fetch first), remote `main` is at #324 (2026-06-26) whose quest-alpha upload FAILED, local `main` carries 3 unpushed commits (which commit `unity-skills`/`unity-mcp` manifest entries against the tooling policy below — strip before pushing), and this branch has 3 unpushed commits plus the working-tree upgrade described below.
- `CLAUDE.md` is the canonical agent instruction file; `AGENTS.md` points there. Canonical testing contract: `docs/testing/README.md`. Canonical game design: `docs/rulesets/`. Roadmap: `docs/roadmap/blockiverse_vr_execution_plan.md`. ADRs: `docs/adr/` (ADR 0006 was amended 2026-08-13 to record the world-space menu baseline).

## Current Unity Project Shape

- Timestamp: 2026-08-13. Unity editor: `6000.5.8f1`. Target: Meta Quest 3 and Quest 3S. Local scripts and the Quest CI/alpha workflows were updated from the old `6000.3.16f1` pins on 2026-08-13.
- Main scene: `Assets/Blockiverse/Scenes/Boot.unity` — the whole game, no scene switching. Generated scene/prefab/input wiring is owned by `BlockiverseProjectBootstrapper.Run()`; change the bootstrapper and regenerate.
- Timestamp: 2026-08-13. Menu architecture: routed menus are direct world-space XR canvases (commit `9c6f435c`, headset-verified, tag `quest-menu-good-2026-07-02`); composition layers are used only for the startup splash. ADR 0006's shared-Quad menu decision is superseded (see its Amendment section).
- Timestamp: 2026-08-13. The July "black screen after splash" regression (`Assets/Plans/diagnostic-black-screen.md`) does NOT reproduce in the Meta XR Simulator on the upgraded stack — verified in-runtime via Meta XR Operator (session FOCUSED, single healthy projection layer, world/UI/hands render). On-device (Quest) confirmation is still outstanding; treat the plan as closed-pending-device-check.

## Package And Tooling State

- Timestamp: 2026-08-13. Meta XR SDK family is `205.0.0`: Interaction, Interaction OVR, and Platform from the Meta registry; **Core is embedded** at `Packages/com.meta.xr.sdk.core` because Core 205.0.0 fails to compile on Unity 6000.5 (CS0619 `instanceId` obsolete-as-error in `SceneListenerNGO.cs`; embedded copy carries the two-line `.entityId` fix). Drop the embed when a fixed Meta release ships. Details: `docs/testing/meta-xr-simulator-and-mcp.md`.
- Timestamp: 2026-08-13. Other pins in the working tree: Netcode for GameObjects `2.13.1`, Transport `6.5.0`, URP `17.5.0`, XRI `3.5.1`, Composition Layers `2.5.0`, Input System `1.20.0`, Meta Avatars `40.0.1`, OpenXR `1.17.1`, test-framework `1.7.0`.
- Timestamp: 2026-08-13. Pre-upgrade state (patched embedded 203 packages, old manifest/lock, git snapshots) is preserved at `../Blockiverse-VR-local-backups/2026-08-12-pre-operator-sdk205/`.
- Timestamp: 2026-08-13. Meta XR Operator (experimental) is the runtime in-VR validation bridge: activate once via `Meta > Meta XR Operator > Activate`, activate Meta XR Simulator per editor session, enter Play mode, and connect ONLY through the `meta-xr-operator` MCP proxy (`~/meta-xr-operator/meta-xr-operator-mcp-proxy`, registered in the local Claude config along with the `meta-xr-unity-runtime` Agent Bridge). Never probe `localhost:8720/sse` directly during layer startup — aborted SSE connections double-faulted the layer and crashed the editor twice on 2026-08-13. The AI Tools panel's own `claude mcp add` buttons fail (Unity's PATH lacks `claude`); run those commands from a terminal.
- MCP for Unity and Unity Skills remain optional local developer tooling only; do not commit their package entries. Note the unpushed local-main commits currently violate this — clean up before pushing.
- Use `hzdb` for Quest device work (`hzdb --version`, `hzdb device list`); `adb` only when `hzdb` lacks the operation.

## Validation Source Of Truth

- Required local Unity gate: `scripts/unity/run-tests.sh` (EditMode then PlayMode; `UNITY_EDITOR` overrides the editor path). Wrapper: `scripts/unity/run-local-validation.sh`. Dev APK: `scripts/unity/build-development-apk.sh`.
- Timestamp: 2026-08-13. The full gate is GREEN on the upgraded stack: EditMode 775/775 and PlayMode 105/105 (`TestResults/Unity/EditMode.xml` / `PlayMode.xml`, 2026-08-13). Getting there took 25 test updates for the M8.5 world-input/mini-world/world-space-menu contracts plus two product fixes (pause-menu Creative Tools route restored; Return-to-Title no longer calls `Application.Quit()` — see CHANGELOG). This is the first combined green gate on the post-pivot menu architecture.
- Do not call validation complete from MCP/Operator diagnostics alone; rely on committed scripts and generated XML/APK/device evidence for acceptance.

## Codebase Review Status (Correcting The Stale STATUS File)

- Timestamp: 2026-08-13. `codebase-review-STATUS.md` (2026-06-11) is stale: dedup (245→184) and adversarial verification (107 confirmed / 2 disputed / 0 refuted / 5 downgraded / 70 pass-through) DID run and are committed (`f14c2945`, PR #314, which also remediated findings; see also PR #312 and the CHANGELOG 131-observation pass). Criticals 1–4 are spot-verified fixed in code. Open items: verify fixes for Critical #5 (single-player save overwritten by LAN session) and #6 (crafting UI exposes ~5 of ~60 recipes), and the final consolidated report was never produced.

## Release And Companion Docs

- `.github/workflows/quest-ci.yml` (PR validation, smoke APK), `quest-alpha.yml` (release-signed upload to Meta `alpha`), `quest-promote.yml` (promotes tested build IDs `alpha -> beta -> rc -> store`). All Unity pins updated to `6000.5.8f1` on 2026-08-13; no successful alpha upload exists after PR #323 (2026-06-16) — #324's upload failed and needs rerunning once main is reconciled.
- Timestamp: 2026-08-13. Unresolved product decision: `quest-alpha.yml` defaults `META_AGE_GROUP` to `TEENS_AND_ADULTS` while the runtime implements the Mixed Ages path; set the repo variable or change the default before the next upload.
- Wiki repo (`../Blockiverse-VR.wiki`): frozen 2026-06-13 and stale (cites deleted workflows/scripts); the local clone has 15 pages deleted-but-uncommitted — an unfinished overhaul that should be completed or reverted. Website repo (`../Blockiverse-VR.website`): frozen 2026-06-21.

## Source Versus Generated Artifacts

- Project source: `Assets/Blockiverse/**`, `Packages/**`, `ProjectSettings/**`, `docs/**`, `scripts/**`, `.github/**`, root policy docs, authored art/audio.
- Never commit Unity `Library/`, `Temp/`, `Logs/`, device logs, screenshots, recordings, APKs, signing material, secrets, or transient validation artifacts. Preserve `.meta` files when moving assets.

## Dirty Worktree Constraints

- Timestamp: 2026-08-13. The working tree intentionally carries the Unity 6000.5.8f1 + Meta XR 205 upgrade (uncommitted). Android target SDK stays at API 34 (held through the upgrade). Cleanup pending before commit: crash-recovery artifacts in `Assets/_Recovery` and stray scenes per `Assets/Plans/scene-cleanup.md`, plus root-level `mono_crash.*.json` and committed test-log files (`TempTestResults.txt`, `editmode_test_results.txt`) that predate this work.
- Before committing, stage only files explicitly in scope for the task.

## External Gates

- Quest headset acceptance requires a worn headset and active controllers (this is where the black-screen close-out finishes).
- Two-device LAN multiplayer proof remains device-dependent.
- Account-backed Meta Avatar and policy checks require real account/platform state.
- Store submission and Meta channel promotion require GitHub Actions secrets/environments and Eric's approval gates.
- Real Quest performance evidence must come from device runs with captures summarized under `docs/testing/performance/`.
