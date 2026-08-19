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
- Timestamp: 2026-08-19. Editor-open workflow: when the Unity editor holds the project lock, build and regenerate through the Meta XR Agent Bridge (`http://127.0.0.1:48736/mcpbridge/`, bearer token in `Assets/Resources/DevAgentSettings.asset`; `IReflectionService.InvokeStaticMethodFromJson` can call `BlockiverseBuildSmoke.BuildDevelopmentAndroid` and `BlockiverseProjectBootstrapper.Run`). Its `TestRunnerTools.GetResults` returns a stale accumulated buffer (identical totals across runs, tests listed as both Passed and Failed) — do not use it as a test verdict; the only trustworthy gate is `scripts/unity/run-tests.sh` with the editor closed. Operator MCP tools are not mounted in every agent session; the stdio proxy (`~/meta-xr-operator/meta-xr-operator-mcp-proxy http://127.0.0.1:8720`) works from a small JSON-RPC client, and an active `adb forward tcp:8720` hijacks that port away from the editor server. Meta's Project Setup Tool flags `applicationEntry = GameActivity` as "Required" — applying it re-breaks the Quest system keyboard (see CHANGELOG); leave it.
- Use `hzdb` for Quest device work (`hzdb --version`, `hzdb device list`); `adb` only when `hzdb` lacks the operation.
- Timestamp: 2026-08-19. **Unity CLI** (`~/.unity/bin/unity`, `1.0.0-beta.5`, experimental; not on PATH
  by default — `source ~/.unity/env`) is installed and signed in as Eric. Run `unity pipeline list` BEFORE
  any batchmode invocation: it lists every editor instance with PID/Running even without the Pipeline
  package, and a GUI editor holding the project makes `run-tests.sh`/`unity test` fail with "Multiple
  Unity instances cannot open the same project". `unity test . --mode EditMode|PlayMode --output <xml>`
  and `unity build . --target Android --execute-method ... -o <path>` (Android signing flags; refuses a
  dirty worktree without `--allow-dirty-build`) are alternatives to the committed scripts, which remain
  the acceptance gate. `unity command`/`unity mcp`/`unity status` need the Unity Pipeline package
  (`0.5.0-exp.1`) — NOT installed; do not `unity pipeline install` without Eric's approval (same
  local-only rule as MCP for Unity / Unity Skills). `unity skill install claude-code` is available but
  is persistent config — ask first.
- Timestamp: 2026-08-19. **Editor MCP channels are cwd-scoped and currently mis-scoped.** The Meta XR
  SDK editor MCPBridge (`meta-xr-unity-runtime`, `http://127.0.0.1:48736/mcpbridge/` + Bearer; includes
  TestRunner/Compilation tools and runs tests INSIDE an already-open editor, sidestepping the instance
  lock) and the Operator proxy (`meta-xr-operator`) are registered in `~/.claude.json` under the PARENT
  `.../Side Projects/Blockiverse` directory, not `.../Blockiverse-VR`, so sessions started in the repo
  root see neither. Re-register for this cwd from a terminal (`claude mcp add ...`; the Unity AI Tools
  panel buttons fail because Unity's PATH lacks `claude`). Meta XR Simulator + Operator runbook:
  `docs/testing/meta-xr-simulator-and-mcp.md`.

## Validation Source Of Truth

- Required local Unity gate: `scripts/unity/run-tests.sh` (EditMode then PlayMode; `UNITY_EDITOR` overrides the editor path). Wrapper: `scripts/unity/run-local-validation.sh`. Dev APK: `scripts/unity/build-development-apk.sh`.
- Timestamp: 2026-08-13. The full gate is GREEN on the upgraded stack: EditMode 775/775 and PlayMode 105/105 (`TestResults/Unity/EditMode.xml` / `PlayMode.xml`, 2026-08-13). Getting there took 25 test updates for the M8.5 world-input/mini-world/world-space-menu contracts plus two product fixes (pause-menu Creative Tools route restored; Return-to-Title no longer calls `Application.Quit()` — see CHANGELOG). This is the first combined green gate on the post-pivot menu architecture.
- Do not call validation complete from MCP/Operator diagnostics alone; rely on committed scripts and generated XML/APK/device evidence for acceptance.

## Codebase Review Status (Correcting The Stale STATUS File)

- Timestamp: 2026-08-13. `codebase-review-STATUS.md` (2026-06-11) is stale: dedup (245→184) and adversarial verification (107 confirmed / 2 disputed / 0 refuted / 5 downgraded / 70 pass-through) DID run and are committed (`f14c2945`, PR #314, which also remediated findings; see also PR #312 and the CHANGELOG 131-observation pass). Criticals 1–4 are spot-verified fixed in code. Open items: verify fixes for Critical #5 (single-player save overwritten by LAN session) and #6 (crafting UI exposes ~5 of ~60 recipes), and the final consolidated report was never produced.

## Lighting And Shadow Baseline

- Timestamp: 2026-08-19. Branch `fix/night-and-emissive-lighting` (off `main`) reverses the old
  "shadows/additional lights off" Quest rendering baseline. Canonical authority is the 2026-08-19
  amendment in [ADR 0006](docs/adr/0006-quest-openxr-rendering-and-asset-policy.md) — no ruleset
  covers shadows.
- Shipped Quest budget: main-light shadows 1024 / one cascade / 30 m, hard shadows only, additional
  lights Per Pixel capped at 4 per object, additional-light shadows on at 1024 but only the nearest
  `GlowwickLightManager.MaxShadowCastingLights` emitters actually cast. Renderer stays Forward
  (`m_RenderingMode: 0`); Forward+ is the documented escalation if per-chunk light clipping shows up.
- The URP asset is generated: `BlockiverseProjectBootstrapper.ConfigureQuestUrpShadowPolicy` and
  `ConfigureQuestRendererMode` own these values. Hand-editing the asset alone silently reverts.
- Watch the `m_PrefilteringMode*` fields in the URP asset. `0` means Remove, which strips the shadow
  and additional-light keywords at BUILD time only — the "works in the editor, black on device"
  trap. They are recomputed by URP's build preprocessor, so the real check is a device run.
- One directional light serves as both sun and moon; URP promotes only a single directional to the
  main light, so a second one would become a costly additional light. Moon phase is derived from
  `WorldTimeClock.TotalElapsedTicks / WorldConstants.TicksPerDay`, which already travels in the
  environment snapshot and the save file — no wire or save-schema change.
- OPEN GATE: none of this is profiled on a headset. `docs/testing/performance/` still has no
  committed capture. The number that most needs measuring is the added ShadowCaster pass over
  loaded chunks. Emitter shadows are the first thing to reduce (to 0) if the frame budget blows.
- Timestamp: 2026-08-19. DECIDED (Eric): `lumen_quartz_cluster` and `staropal_geode` now carry their
  canonical `emissiveLight` of 7 and 5. Underground crystal-lit farming is intended, not an exploit
  — berries grow directly adjacent to lumen quartz, reeds adjacent to either, grain never (its
  minimum light is 8, and the level drops to 6 one block out). Saves are unaffected:
  `WorldSaveService.ComputeBlockRegistryHash` hashes canonical IDs only, not block attributes.
  Pinned by `CaveCrystalsLightCropsEnoughForReedsAndBerriesButNotGrain`.
- Occlusion model (2026-08-19): the voxel mesh bakes three channels per face — R sky exposure
  (floor 0: sealed rooms are black, tunnels fade to 0 by 12 blocks), G emitter reach (a voxel
  DDA line-of-sight ray from the face to each emitter in range; `VoxelEmitterIndex` supplies the
  candidates per chunk), B self-emission. The shader gates sun/moon/ambient by R and realtime
  point lights by G, so emitters cannot shine through walls or the ground even though only the
  nearest one owns a shadow map. `_BakedLightFloor` on the voxel material is the one knob if true
  black proves unplayable on device (0.01 is the "eyes adjusted" equivalent). Crop growth still
  reads `SampleAirLight` (axis-probe max(sky, emissive)) — deliberately NOT the LOS bake, so the
  berries-adjacent-to-quartz decision is unchanged.
- Remaining known gaps: baked light is still time-of-day independent (the sun/moon do the
  darkening, the bake only gates them); the LOS gate is per face, so an occlusion edge lands on a
  block boundary rather than interpolating across a face; the emitter index and
  `GlowwickLightManager` each scan the world once on load (consolidation candidate).

## Release And Companion Docs

- `.github/workflows/quest-ci.yml` (PR validation, smoke APK), `quest-alpha.yml` (release-signed upload to Meta `alpha`), `quest-promote.yml` (promotes tested build IDs `alpha -> beta -> rc -> store`). All Unity pins updated to `6000.5.8f1` on 2026-08-13; no successful alpha upload exists after PR #323 (2026-06-16) — #324's upload failed and needs rerunning once main is reconciled.
- Timestamp: 2026-08-13. Unresolved product decision: `quest-alpha.yml` defaults `META_AGE_GROUP` to `TEENS_AND_ADULTS` while the runtime implements the Mixed Ages path; set the repo variable or change the default before the next upload.
- Timestamp: 2026-08-19. Wiki repo (`../Blockiverse-VR.wiki`, branch `master`) and website repo (`../Blockiverse-VR.website`, branch `main`) are both current and clean: the wiki was refreshed for the player-size/crouch work and then for lighting (moon, Light and Dark section, Lights table, Known Issues), the website homepage gained a day/night/lighting bullet. Both have local commits that are NOT yet pushed — the wiki publishes on push (every `.md` becomes a public page) and the website deploys to blockiversevr.com via GitHub Pages on push to `main`. The wiki `CLAUDE.md` is git-ignored on purpose; never commit it.

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
