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

- Timestamp: 2026-08-20. Branch `feat/swim-locomotion` (PR 3 of the water plan, stacked on `feat/water-surface-rendering` because both regenerate the rig prefab): swimming with negative buoyancy as the ratified default. `FluidSubmersion` (engine-free, `Blockiverse.Voxel`) samples feet/body/head cells from the CharacterController capsule; `BlockiverseSwimMotion` holds every number as pure functions; `BlockiverseSwimProvider` (LocomotionProvider + IGravityController) owns vertical motion. Two things are load-bearing and easy to get wrong: gravity needs `TryLockGravity(ForcedOff)` **plus** `gravityProvider.gravityControllers.Add(this)` (the list auto-populates once, from components already present, so a later addition is never consulted), and `TryStartLocomotionImmediately()` returns **false** once the provider is already Moving — gating on its return value queues motion on the entry frame only, which reads as the player drifting 2 mm and stopping. Swim-up reads the jump ACTION, never `jumpProvider.enabled`, or a Teleport-mode swimmer can descend and not ascend. Wading (feet wet, body dry) deliberately keeps gravity on. Comfort: `SwimPassiveSinkEnabled` (default ON, the off switch is the accommodation), `SwimSpeedFactor`, `SwimVignetteBoost`. Full gate green: EditMode 927/927, PlayMode 123/123. [ADR 0008](docs/adr/0008-swim-locomotion.md). **Open: headset comfort pass on the 0.35 m/s passive sink rate** — reasoned, not validated, and the number most likely to move.
- Timestamp: 2026-08-20. Branch `feat/water-surface-rendering` (PR 2 of the water plan, in the primary checkout): transparent, wave-animated water. One voxel shader with a `_BLOCKIVERSE_WATER` `multi_compile_local` variant and material-driven `Blend`/`ZWrite`/`Cull`; `BlockVisualAtlas.CreateFluidMaterial` clones the same authored atlas transparent and `VoxelWorldRenderer` owns both materials (destroy BOTH on reconfigure and OnDestroy). A second `.shader` file is not an option — the materials are runtime clones, so nothing references them as assets and it would be stripped from the Android player. Fluid meshes carry the surface mask and family in **UV1**, never vertex COLOR (R/G/B are sky exposure, emitter reach, self emission and water needs all three). The wave is strictly downward and only masked vertices move: emitted `+Y` faces, plus the foot of a side wall standing on a lower same-family surface (without that, flowing-water steps open a 5 cm see-through slit). The wave normal and the highlight are gated on the baked normal facing up, so a moved wall foot does not shade or glint like a flat surface. Underwater is fog plus a camera-clear swap in `BlockiverseWaterView` (on the rig) with the fog write staying in `BlockiverseLightingCycleController`, hoisted above its clock/sun guard — never a tint quad, because routed menus are world-space canvases on the same camera. [ADR 0007](docs/adr/0007-water-surface-rendering.md). Full gate green: EditMode 901/901, PlayMode 117/117; Android build clean and 55 KB smaller than baseline. **Open merge gate: three `ovrgpuprofiler` captures on one seed and pose** (today / queue-move-only / full feature) — procedure in `docs/testing/performance/README.md`; the queue-move-only number decides whether transparent water is affordable at all.
- Timestamp: 2026-08-21. Branch `feat/swim-locomotion` also carries the **lightning rework** (stacked onto the swim work at Eric's direction, so PR #329 is no longer swim-only). Lightning was effectively invisible: strikes were drawn from the whole world then rejected within 8 blocks of the player, the "flash" was four fog puffs subtending ~0.4 degrees, there was no sky flash at all, and `TickThunder` faked one every 6-14 s with no strike behind it. Now: `LightningStormSolver` (per-storm character 12-70% shaped by a build-peak-taper arc, both derived from already-synced state), `LightningStrikeSelector` (ring 10-96 blocks around a player, **uniform in radius** so distance varies; the draw consumes a fixed number of RNG values so rejections cannot shift the snow stream, which now has its own `System.Random`), `LightningBoltGeometry`/`LightningBoltView` (procedural ribbon, 3 forks, runtime-generated 64x1 alpha ramp across the width, yaw-only billboard, one mesh/one draw), `LightningFlashSolver` folded INTO `BlockiverseLightingCycleController` (it rewrites ambient every LateUpdate, so an external write is erased within a frame) modulating **ambient and never the sun** — raising the sun at night crosses `MinimumShadowCastingIntensity` and flips the whole shadow pass for two frames. Thunder is delayed `distance/34`, attenuated `1 - distance/128`, near/far by distance, and plays **2D**: `PlayCueAt` moves a pooled round-robin source (cutting off a ringing clip) and applies Unity's own rolloff on top. Three things to know before touching it: `EnvironmentDynamicsController.session` had never been assigned (remote-player exclusion was dead code — fixed); strike PLACEMENT is deliberately no longer seed-reproducible because the anchor is a live head; and the ruleset `/34` vs `/343` conflict is resolved in favour of `/34` with the reason written down in both docs. [ADR 0009](docs/adr/0009-lightning-presentation.md). Full gate green: EditMode 968/968, PlayMode 135/135. **No prefab diff across the whole lightning branch** (`git diff --stat 1b0386b7 -- Assets/Blockiverse/Prefabs/` is empty) — every new cross-component reference is private and non-serialized on purpose. **Open: headset look at the bolt, the flash-vs-distance pairing, the thunder delay, and the VFX tint change.**
- Timestamp: 2026-08-21. **Every VFX cue's tint had been silently discarded since the effects landed.** `BlockiverseVfxPool` writes each cue's tint into `main.startColor` (the particle vertex-colour stream) while the shared material was built as `URP/Unlit`, whose vertex `Attributes` declares only POSITION and TEXCOORD0 — no `COLOR` semantic. Dust, sparks, embers, rain splash, snow and fog all rendered as flat white times their sprite. Fixed by switching `EnsureTransparentVfxParticleMaterial` to `Universal Render Pipeline/Particles/Unlit`. Two traps: `EnsureMaterial` only assigns a shader when it CREATES the asset, so an existing `.mat` must have `material.shader` reassigned explicitly; and this visibly changes all eight cues at once, so it is its own revertible commit and needs its own headset look.
- Timestamp: 2026-08-20. Branch `fix/fluid-physics-layer` (merged as #326): fluid chunk geometry on dedicated layer 13 so gravity's ground cast never sees water (players fall through and teleport lands ON the surface); weather temperature model (ruleset §6.1 biome bases, 0.15 °C/block lapse, per-location rain→snow derived on read — Markov chain/sync/save untouched — driving cues and snowpack settle at ≤0 °C); cold exposure gated to night-or-precipitation while sky-exposed, night-cold strict 3 °C; landing detection probes below the capsule base (XRI gravity can end a fall hovering ~5 mm up, leaving CharacterController.isGrounded false and fall damage uncharged). Full gate green: EditMode 835/835, PlayMode 109/109. Design rulings ratified by Eric are recorded in the plan artifact (see session memory) and code comments; ruleset §5.6 swim scope (negative buoyancy 0.35 m/s default) is PR 3, not this branch.

- Timestamp: 2026-08-21. Branch `claude/p2p-local-play-review-d0f38e` (worktree `.claude/worktrees/p2p-local-play-review-d0f38e`): P2P LAN hardening from the multiplayer/networking review. Landed: batched late-join snapshots (the old single-message snapshot was undeliverable past ~380 changed blocks because UTP sizes fragmentation from `MaxPayloadSize`, which nothing raised — late join was broken on any played world); client resync path for delta gaps, request timeouts and stalled snapshot transfers; 64-request in-flight caps; registry/version hashes in the join handshake with named `BlockiverseJoinRejectionReason` values surfaced through the menu; LAN UDP discovery beacon (signed with the join code, address taken from the packet sender, manual entry retained for AP-isolated networks); host-side max edit distance; avatar pose sequencing plus ~50-byte compressed pose and the NGO 2.x `SendTo` RPC migration; explicit UnityTransport limits in the bootstrapper; `com.unity.multiplayer.playmode` 2.0.2 added to the manifest (registry-verified stable; requires engine >= 6000.3.0b10). Ruleset §7.5 was rewritten: **client prediction is deferred, not shipped** (Eric's call — LAN RTT is a few ms), keeping only the in-flight caps and recovery rules. Docs updated in the same pass: ruleset §2/§5/§6/§7.5/§10/§12/§14/§19/§20, `docs/testing/README.md` (MPPM workflow + discovery-on-real-networks), wiki `Multiplayer.md`/`Known-Issues-and-Support.md`, and the website privacy page (the beacon broadcasts the headset name on the LAN). `codebase-review-lan-multiplayer.md` is marked stale — its findings 3/4/5/6 were already fixed, and its finding 1 diagnosis predates the move to fragmented delivery.

- Timestamp: 2026-08-22. Branch `claude/meta-avatar-implementation-9e9666` (worktree `.claude/worktrees/walking-bob-motion-audio-ca16bf`): the Meta avatar pipeline fix. Diagnosis (report artifact: https://claude.ai/code/artifact/ad24d48d-37b9-47eb-905f-9ec69a680c2e) found four independently fatal device-only defects; all fixed in `769a1df5` + regeneration `77543578`. Key mechanics now: the Avatar SDK manager is a bootstrapper-authored INACTIVE scene object ("Meta Avatar SDK Manager") whose serialized references (Style-2 shader configs, GPU-skinning shaders) are what ship avatar shaders in the Android build — a bare runtime `OvrAvatarManager.Instantiate()` is device-fatal (Standard-shader fallback stripped under URP; editor-only GpuSkinningConfiguration auto-fill). The entity root is the rig root at identity (never the head); tracking comes from `BlockiverseXriInputTrackingDelegate` (rig-relative XRI transforms, no OVRManager/OVRInput); local = FirstPerson+Full (no head/legs by SDK design — final, SDK is EOF at 40.0.1), remote = ThirdPerson+Full; owner Meta user id syncs via NetworkVariable so peers load real avatars; profile load retries with backoff and settles to the SDK default avatar. Eric ratified (2026-08-22, on the report): first-person without legs accepted; mirror ships as a placeable world object — **issue #340, BUILT on this branch** (`f9c7e860` block/recipe/art + `cae06e4a` loopback-studio surface per [ADR 0010](docs/adr/0010-avatar-mirror.md): one 512² avatar-only RT camera on layer 14 `BlockiverseMirrorAvatar`, one active mirror, pane samples X-flipped; no scene reflection — it would sit inside the wall, and ADR 0006 bars casual RT cameras). A 23-agent adversarial review then confirmed 19 findings, all fixed in `5083b8d6` — the two criticals: manager discovery raced scene load (Scene.isLoaded false during first Awake → bare device-fatal manager latched for the session; now `FindAnyObjectByType(FindObjectsInactive.Include)`), and hiding avatars via GameObject deactivation deadlocked loading (SDK never advances inactive entities; visibility is now presenter-owned via SetActiveView(None)/restore — **never SetActive an avatar entity node**). Final gate green: EditMode 1152/1152, PlayMode 146/146. **Still external:** on-device validation (logcat greps in the report §6; mirror perf capture per ADR 0010) and dashboard DUC state (User ID / User Profile / Avatars → "Use Avatars") for app 1176905668835795. Branch is local-only; push + PR await Eric's go.

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
- Occlusion model (updated 2026-08-22): the voxel mesh bakes three channels per face — R sky
  exposure (floor 0: sealed rooms are black, tunnels fade to 0 by 12 blocks), G emitter reach (a
  voxel DDA line-of-sight ray from the face to each emitter in range; `VoxelEmitterIndex` supplies
  the candidates per chunk), B self-emission. The shader gates sun/moon/ambient by R.
  **Punctual lights get exactly ONE occlusion term, never two** (`PunctualOcclusion` in
  `BlockiverseVoxelLit.shader`): a light that owns a shadow slice
  (`GetAdditionalLightShadowParams(i).w >= 0`) is occluded by its cube shadow map alone; every
  other light is occluded by G alone. Applying both was the bug — G is one sample per face (1 m)
  and the shadow map is ~4 cm, so multiplying them let the 25x coarser term zero the finer one and
  stepped emitter shadows onto block boundaries. Past URP's additional-shadow fade, occlusion
  hands back to G. Emitter `shadowStrength` is 1.0 (was 0.7, which never governed anything while
  G was also hard-zeroing the term). Fails safe: with the shadow keyword stripped,
  `GetAdditionalLightShadowParams` reports slice -1 for every light and G gates everything, i.e.
  the pre-2026-08-22 behaviour. `_BakedLightFloor` on the voxel material is still the one knob if
  true black proves unplayable on device (0.01 is the "eyes adjusted" equivalent). Crop growth
  still reads `SampleAirLight` (axis-probe max(sky, emissive)) — deliberately NOT the LOS bake, so
  the berries-adjacent-to-quartz decision is unchanged.
- Remaining known gaps: baked light is still time-of-day independent (the sun/moon do the
  darkening, the bake only gates them); emitters WITHOUT a shadow map still take the per-face G
  gate, so their occlusion edge lands on a block boundary (the escalation if that reads badly on
  device is `MaxShadowCastingLights = 2`, not per-corner sampling — 16 LOS walks per face on the
  main-thread rebuild was measured as too expensive and is parked at
  `~/.claude/plans/glowwick-per-corner-light-parked.patch`); the emitter index and
  `GlowwickLightManager` each scan the world once on load (consolidation candidate).

## Release And Companion Docs

- `.github/workflows/quest-ci.yml` (PR validation, smoke APK), `quest-alpha.yml` (release-signed upload to Meta `alpha`), `quest-promote.yml` (promotes tested build IDs `alpha -> beta -> rc -> store`). All Unity pins updated to `6000.5.8f1` on 2026-08-13; no successful alpha upload exists after PR #323 (2026-06-16) — #324's upload failed and needs rerunning once main is reconciled.
- Timestamp: 2026-08-13. Unresolved product decision: `quest-alpha.yml` defaults `META_AGE_GROUP` to `TEENS_AND_ADULTS` while the runtime implements the Mixed Ages path; set the repo variable or change the default before the next upload.
- Timestamp: 2026-08-19. Wiki repo (`../Blockiverse-VR.wiki`, branch `master`) and website repo (`../Blockiverse-VR.website`, branch `main`) are both current and clean: the wiki was refreshed for the player-size/crouch work and then for lighting (moon, Light and Dark section, Lights table, Known Issues), the website homepage gained a day/night/lighting bullet. Both have local commits that are NOT yet pushed — the wiki publishes on push (every `.md` becomes a public page) and the website deploys to blockiversevr.com via GitHub Pages on push to `main`. The wiki `CLAUDE.md` is git-ignored on purpose; never commit it.

## Source Versus Generated Artifacts

- Project source: `Assets/Blockiverse/**`, `Packages/**`, `ProjectSettings/**`, `docs/**`, `scripts/**`, `.github/**`, root policy docs, authored art/audio.
- Never commit Unity `Library/`, `Temp/`, `Logs/`, device logs, screenshots, recordings, APKs, signing material, secrets, or transient validation artifacts. Preserve `.meta` files when moving assets.
- Timestamp: 2026-08-20. `BlockiverseProjectBootstrapper.Run()` is now actually idempotent: a rerun on a clean tree reproduces `BlockiverseXRRig.prefab`, `Boot.unity`, `OculusProjectConfig.asset`, and `ProjectSettings.asset` byte-identically (repo history before this shows rerun churn — stacked button listeners, renumbered scene fileIDs — so old commits are not evidence that churn is normal). One deliberate exception: `SENTIS_ANALYTICS_ENABLED` and `APP_UI_EDITOR_ONLY` are package-managed defines written to the *active* build target by com.unity.ai.inference (per-machine EditorAnalytics opt-in) and App UI respectively; the bootstrapper's owned-define list must never include them or it oscillates against the packages. A stray one-line ProjectSettings define diff after an editor run is that mechanism, not a bug.

## Audio Pipelines (Two, Deliberately Separate)

- Timestamp: 2026-08-21. Sound effects are **no longer generated**. `scripts/audio/build-audio-assets.py` builds the 89 shipping cues from licensed third-party source recordings listed in `scripts/audio/audio-manifest.json` (authored by `make-audio-manifest.py`). `scripts/audio/generate-audio.py` still synthesizes the full original catalog but only writes `music_*.wav` and `classic_block_*.wav` into `Assets/` — the music is deliberately kept original, and the classic block cues back the Classic Block Sounds setting. `--dump-legacy <dir>` renders the rest outside `Assets/`. Before this split a stray generator run silently overwrote every production cue.
- **Asset GUIDs are `md5(repo-relative path)`** (`scripts/audio/audio_asset_common.py`, shared by both pipelines). Keeping a cue's filename preserves every serialized prefab reference; renaming one orphans the field pointing at it. Treat cue filenames as project ABI.
- Staging lives **outside the repo** at `../Blockiverse-VR-audio-staging/` (`raw/` bundles, `extract/` unpacked, `SOURCES.md` evidence trail). Never committed. `build-audio-assets.py` finds it by walking up from the repo root, so it works from a worktree; override with `BLOCKIVERSE_AUDIO_STAGING`.
- Sonniss is behind a **Cloudflare bot challenge** on both `gdc.sonniss.com` and `downloads.sonniss.com` — scripted download returns 403. The bundles are mirrored file-by-file on the Internet Archive (`sonniss-gdc-<year>-game-audio-bundle-normalized`), which also allows fetching only the ~57 source files actually needed instead of multi-GB ZIPs. Licence authority remains Sonniss's own agreement; **its terms prohibit AI/ML training on the material.**
- Validation split: `validate-audio-assets.py` gates the **committed files** (format, channel layout, level, loop continuity, licence coverage) and is the real gate; `validate-generated-audio.py` only proves the generator still renders. Neither runs in CI yet — both are manual.
- Beds are levelled by **integrated loudness (-23 LUFS + a per-cue offset)**, not peak. The raw sources spanned 29 dB peak-to-peak, which left the day ambience inaudible next to the night bed. One-shots are peak-normalized to 0.82 with the gain offset applied *after* normalization and the edge fades applied *last* — measuring peak before the fade under-levels percussive cues by up to 15 dB.

## Dirty Worktree Constraints

- Timestamp: 2026-08-20. The Unity 6000.5.8f1 + Meta XR 205 upgrade is committed (fe73bdbf); Android target SDK stays at API 34. The crash-recovery artifacts and `mono_crash.*.json` files listed here previously are gone. Remaining tracked cleanup: legacy test-log files (`TempTestResults.txt`, `editmode_test_results.txt`) predating the upgrade.
- Before committing, stage only files explicitly in scope for the task.

## External Gates

- Quest headset acceptance requires a worn headset and active controllers (this is where the black-screen close-out finishes).
- Two-device LAN multiplayer proof remains device-dependent.
- Account-backed Meta Avatar and policy checks require real account/platform state.
- Store submission and Meta channel promotion require GitHub Actions secrets/environments and Eric's approval gates.
- Real Quest performance evidence must come from device runs with captures summarized under `docs/testing/performance/`.
