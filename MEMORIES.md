# Blockiverse VR Project Memory

## Purpose

This file is the concise handoff for future Codex/agent work in the original Blockiverse VR project. Use it to avoid rediscovering current project state, local tooling decisions, validation expectations, and known external gates.

## Memory Timestamp Policy

- Timestamp: 2026-06-20. Add a concrete date to new memory entries and materially updated decisions. Prefer `Timestamp: YYYY-MM-DD` near the relevant section or bullet.
- Timestamp: unknown. Existing project history that is not dated here should be treated as legacy context until refreshed from current repo state.

## Repository And Source Of Truth

- Repository root: `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR`.
- Branch observed on 2026-06-21 for the Unity tooling cleanup: `codex/cleanup-unity-tooling-docs`, based on `origin/main`.
- Remote observed on 2026-06-20: `origin` points to `https://github.com/eslutz/Blockiverse-VR.git`.
- `CLAUDE.md` is the canonical agent instruction file; `AGENTS.md` intentionally points there.
- Canonical testing contract: `docs/testing/README.md`.
- Canonical game design: `docs/rulesets/`.
- Roadmap: `docs/roadmap/blockiverse_vr_execution_plan.md`.
- ADRs: `docs/adr/`.

## Current Unity Project Shape

- Unity editor: `6000.3.18f1` (`5ebeb53e4c07`).
- Target: Meta Quest 3 and Quest 3S.
- Main scene: `Assets/Blockiverse/Scenes/Boot.unity`; this is the whole game scene, not a scene-switching flow.
- Generated scene/prefab/input wiring is owned by `BlockiverseProjectBootstrapper.Run()`. Change the bootstrapper and regenerate instead of hand-authoring generated scene and prefab wiring.
- Timestamp: 2026-06-21. Composition-layer menu routing must create `Blockiverse Menu Canvas` before adding/enabling `InteractableUIMirror`; otherwise the Unity Composition Layers package can auto-create a default child `Canvas` and bind `CompositionLayer.m_UICanvas` to the wrong surface. The generated Boot scene must also remove root-level composition layers such as `Composition Render Scale Surface`; an enabled root composition layer with null/invalid `LayerData` produced Quest-wide repeated black vertical bars and hid the actual menus.
- Runtime code lives under `Assets/Blockiverse/Scripts`, with key areas including `Core`, `Voxel`, `WorldGen`, `Survival`, `SurvivalHealth`, `Persistence`, `MetaAvatars`, `MetaPlatform`, `Networking`, `Gameplay`, `VR`, `UI`, and `Editor`.
- Tests live under `Assets/Blockiverse/Tests/EditMode` and `Assets/Blockiverse/Tests/PlayMode`.
- Art/audio source assets live under `Assets/Blockiverse/Art` and `Assets/Blockiverse/Audio`.

## Package And Tooling State

- Timestamp: 2026-06-25. Manifest pins do not include package-based Unity MCP editor tooling. The latest currently supported editor/package matrix is Unity `6000.3.18f1` with Meta Avatars `40.0.1`, Meta XR Core/Interaction OVR/Platform `81.0.1`, Addressables `3.1.0`, Input System `1.19.0`, Multiplayer Center `1.0.1`, Netcode for GameObjects `2.13.0`, Unity Transport `2.7.3`, URP `17.0.1`, Unity AI Assistant `2.12.0-pre.2`, Unity AI Inference `2.6.1`, Unity Test Framework `1.4.6`, Composition Layers `2.5.0`, XRI `3.5.1`, XR Management `4.6.0`, Unity OpenXR: Meta `2.5.0`, OpenXR `1.17.1`, and UGUI `2.0.0`. Unity `6000.5.1f1` and Meta XR Core/Interaction OVR/Platform `203.0.0` were checked but are not yet supported because Meta package code fails compilation under that editor/package combination.
- Timestamp: 2026-06-25. Use the Unity IDE built-in MCP server as the default live Editor bridge for inspection, console reads, scene/object checks, script/asset automation, and test jobs exposed through MCP. It is local developer tooling, not a committed project dependency. Restart it from `Project Settings > AI > Unity MCP Server` if Codex reports a stale named pipe such as `/tmp/unity-mcp-c7c423ae-41914`.

## Validation Source Of Truth

- Required local Unity gate: `scripts/unity/run-tests.sh`.
- Local validation wrapper: `scripts/unity/run-local-validation.sh`, which runs shell syntax checks, full Unity tests, and a development APK build.
- Development APK builder: `scripts/unity/build-development-apk.sh`.
- Docs/repo-only validation tier: `git diff --check` and `bash -n scripts/unity/*.sh`.
- Use `docs/testing/README.md` for selecting targeted EditMode, PlayMode, APK, Quest, Meta XR Simulator, and MCP validation.
- Do not call validation complete from MCP diagnostics alone. Use MCP to investigate and run targeted jobs; rely on committed scripts and generated test XML/APK/device evidence for acceptance.
- Timestamp: 2026-06-20. The Unity MCP setup pass kept the Unity Editor open to host live automation, so `scripts/unity/run-tests.sh` was not run in that pass. Run it before treating the package import/setup as merge-ready.
- Timestamp: 2026-06-21. When Unity is open for MCP for Unity, prefer an open-Editor Quest install: build through MCP or `Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid()`, then let the running Unity editor spawn `hzdb app install --replace --grant-permissions Builds/Android/BlockiverseVR-development.apk`. This keeps the editor and MCP bridge alive while still replacing the APK on the headset. A `LaunchCheckControllerRequiredDialogActivity` result after install means launch verification is headset-state blocked, not that APK replacement failed.

## Release And Companion Docs

- `.github/workflows/quest-ci.yml` validates pull requests with repository checks, Unity Personal activation through GameCI, and Android smoke APK packaging.
- `.github/workflows/quest-alpha.yml` builds a release-signed Quest APK from `main` or trusted manual refs and uploads to Meta `alpha`.
- `.github/workflows/quest-promote.yml` promotes an already-tested Meta build ID through `alpha -> beta`, `beta -> rc`, or `rc -> store` without rebuilding.
- Wiki repo: `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR.wiki`. Keep user-facing gameplay, controls, rules, save/multiplayer setup, release notes, and known issues aligned with shipped behavior.
- Website repo: `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR.website`. Keep public feature lists, store metadata, screenshots, and privacy/support surfaces aligned with shipped behavior.

## Source Versus Generated Artifacts

- Treat project source as `Assets/Blockiverse/**`, `Packages/**`, `ProjectSettings/**`, `docs/**`, `scripts/**`, `.github/**`, root policy/docs, and intentionally authored art/audio.
- Never commit Unity `Library/`, `Temp/`, `Logs/`, local generated folders, device logs, screenshots, recordings, Perfetto traces, APKs, signing material, secrets, `.env` files, or transient validation artifacts unless a tracked artifact is explicitly required.
- Preserve `.meta` files when moving Unity assets.
- Do not copy protected third-party identity. Use original names and original assets.

## Dirty Worktree Constraints

- Timestamp: 2026-06-21. Dirty package and Android SDK drift from the Unity tooling setup was intentionally discarded from the cleanup branch. Keep Android target SDK at API 34 unless Eric explicitly opens a separate Android target migration task.
- Before committing, inspect the diff and stage only files explicitly in scope for the task. Unity MCP package additions, incidental package bumps, local device logs, and validation artifacts are out of scope unless explicitly requested.

## External Gates

- Quest headset acceptance requires a worn headset and active controllers.
- Two-device LAN multiplayer proof remains device-dependent.
- Account-backed Meta Avatar and policy checks require real account/platform state.
- Store submission and Meta channel promotion require GitHub Actions secrets/environments and Eric's approval gates.
- Real Quest performance evidence must come from device runs, OVR Metrics or equivalent captures, and summaries under `docs/testing/performance/`.
