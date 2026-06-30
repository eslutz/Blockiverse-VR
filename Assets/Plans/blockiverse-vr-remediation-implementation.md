# Blockiverse VR — Remediation Implementation Plan

**Companion to:** `Assets/Plans/blockiverse-vr-technical-audit.md` (findings, rationale, priorities).
**Target:** Meta Quest 3 / 3S · Unity 6000.3.16f1 · URP · OpenXR + Meta XR SDK · Netcode for GameObjects.
**Posture:** Unreleased project — breaking changes allowed, no back-compat, no legacy save support.

This document turns the audit's prioritized findings into concrete, sequenced engineering work. Each step lists the files to change, dependencies, whether it can run in parallel, and how to verify it. Tests are mandatory for every behavioral change (this project has strong EditMode/PlayMode coverage that must stay green).

---

# Project Overview

- **Game Title:** Blockiverse VR
- **High-Level Concept:** A voxel-based immersive VR sandbox (creative + survival-lite) for Meta Quest, with host-authoritative LAN co-op and Meta Horizon avatars.
- **Players:** Single-player and host-authoritative LAN co-op.
- **Tone / Art Direction:** Stylized voxel; authored block atlas; point-filtered textures.
- **Target Platform:** Android / Meta Quest 3 & 3S.
- **Render Pipeline:** URP (`BlockiverseAndroidURPAsset`).
- **Input:** New Input System + XR Interaction Toolkit (OpenXR / Meta XR).

---

# Confirmed Owner Decisions (drive this plan)

1. **Comfort (V1):** Default **Glide**; **tunneling vignette ON at low strength by default** (toggle + strength slider); add a **Glide style** setting — *smooth* vs *walk/run head-bob bobbing* (opt-in, never forced); add a **first-run controls/comfort prompt** whose initial selection options include locomotion mode, **glide bob toggle**, vignette on/off + strength, and turn settings. Values persist to `BlockiverseComfortSettings`.
2. **Assemblies (A2):** Do **not** over-split. **Ratify** the `WorldGen`/`Gameplay` consolidation in the docs; perform only the **netcode extraction (A1)**; **decouple `Blockiverse.UI`** via events/interfaces.
3. **World size (R4b):** **Drop the large / 256³ preset.** Cap footprint at **medium (≤192²)** and height at **≈128** (down from 256). R4 (sparse storage) becomes optional.
4. **Save schema (D1) — REVERSED:** **Abandon migration entirely.** Single canonical schema only; keep the existing fail-fast hard-reject; remove dead "future migration / infinite streaming" references; rewrite the versioning ruleset doc to match.

---

# Key Asset & Context (verified)

- `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs` — `locomotionMode = Glide` (21), `vignetteEnabled` defaults **false** (29), `vignetteStrength` defaults **0** (31), `VignetteAperture = vignetteEnabled ? 1 - strength*0.4 : 1` (112). **No GlideStyle field exists.**
- `Assets/Blockiverse/Scripts/VR/BlockiverseComfortMenu.cs` — comfort UI (Glide/Teleport selectors, vignette toggle + slider already present).
- `Assets/Blockiverse/Scripts/VR/BlockiverseComfortTransition.cs` — vignette/comfort driver.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs` — currently forces vignette off on regen; authors `Controller Mapping Popup` + comfort menu.
- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs` — `RebuildAll` (88–120) synchronous full build; `ProcessPendingColliderRebuilds(int.MaxValue)` (113); deferred `QueueFullRebuild` (122–140) exists but unused by new-world flow; `Configure(..., deferInitialRebuild)` (59–86); per-chunk GameObject/MeshCollider/TeleportationArea creation (`GetOrCreateChunkObject` ~360–412).
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs` — `DiscoverInteractionRays` (297–309) runs **two** `GetComponentsInChildren` per frame via `RefreshActiveInteractionRay`.
- `Assets/Blockiverse/Scripts/Gameplay/WorldSaveGeneration.cs` — `SizeFor` (118–127): `medium`→192², `large`/`infinite`→256², default→128²; stale "infinite/future region streaming" comment (115–117).
- `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs` — `WorldMaxY = 255`, `SeaLevel = 96`.
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs` — `CreateDefaultSurvivalTerrain` uses **height 256** (25).
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs` — `CurrentSchemaVersion = 4` (237); fail-fast at 624–625 (comment already says "No migrations"); registry-hash mismatch is a **warning** (637–640).
- Netcode living in `Blockiverse.Gameplay`: `MultiplayerSurvivalSync.cs` (4,168), `MultiplayerChunkAuthoritySync.cs` (1,509), `MultiplayerWorldPersistence.cs` (711).
- Meta avatar stream cap: `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs` (`MaxPayloadBytes = 8*1024`), `MetaAvatarStreamRelay.cs`.
- Test nets to keep green: `ChunkRenderingEditModeTests`, `VoxelWorldRenderingPlayModeTests`, `CanonicalWorldRenderingPlayModeTests`, `BlockiverseRigPrefabTests`, `CompositionLayerUiEditModeTests`, `MenuRuntimeWiringEditModeTests`, `MenuFlowPlayModeTests`, `NewWorldConfigEditModeTests`, `WorldSaveEditModeTests`, `SurvivalTerrainEditModeTests`, `StructureServiceEditModeTests`.

---

# Implementation Steps

Execution is grouped into **waves**. Steps within a wave marked *Parallelizable: Yes* can run concurrently. Each behavioral step ends with its own tests; do not advance a wave until its tests are green.

## Wave 0 — Cheap, high-impact, low-risk (do first)

### Step 1 — R4b: Drop large/256³; cap footprint ≤192² and height ≈128
- **Description:** Remove the `large`/`infinite` 256² mapping from `WorldSaveGeneration.SizeFor` (cap at `medium`→192², default→128²). Lower world height from 256 to 128 in `WorldGenerationSettings.CreateDefaultSurvivalTerrain` (and any preset/menu that hard-codes 256). Decide and set the canonical max height constant in `WorldConstants` (introduce e.g. `WorldHeight = 128`; keep `SeaLevel = 96` only if it still leaves adequate headroom — verify peak terrain (`SurvivalBiomeResolver` line 48: SeaLevel + ~65 max) fits; if not, lower `SeaLevel` proportionally, e.g. 64, so peaks + structures + caves fit under 128). Remove the `large` option from the new-world size selector (`NewWorldConfig` / menu).
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/WorldSaveGeneration.cs` (`SizeFor` + stale comment), `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`, `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs` (size options/wrap), `Assets/Blockiverse/Scripts/WorldGen/SurvivalBiomeResolver.cs` (verify height math fits).
- **Why it matters:** Removes the worst-case ~4,096-chunk / ~67 MB world; biggest single memory/perf win and shrinks what R1/R2 must handle.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes (with Steps 2, 3, 7)
- **Verify:** Update `NewWorldConfigEditModeTests` (no longer wraps through a `large` option), `SurvivalTerrainEditModeTests` / `WorldSaveEditModeTests` (height 128 not 256), `StructureServiceEditModeTests` (uses `SeaLevel`). Generate each preset (`survival_terrain`/`flat_builder`/`void_builder`) and assert peak terrain + tallest structure fit under the new height with air headroom. Run `CanonicalWorldRenderingPlayModeTests`.

### Step 2 — R3: Remove per-frame allocation in the VR interaction hot path
- **Description:** Cache the left/right interaction rays once after rig wiring; guard with a "rays resolved" flag. Re-discover only on an explicit rig-change event, not every frame. Eliminate both per-frame `GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true)` calls.
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs` (`DiscoverInteractionRays` 297–309, `RefreshActiveInteractionRay`/`ResolveActiveInteractionRay` 323–333).
- **Why it matters:** Steady-state GC on the block-editing path is a direct cause of intermittent VR frame spikes.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes
- **Verify:** Add an EditMode test asserting rays are resolved once and cached (e.g. invoke refresh repeatedly, assert no re-discovery after first resolve via a counter/seam). Manual: Profiler shows no per-frame alloc while aiming in creative. Keep `BlockiverseRigPrefabTests` green.

### Step 3 — D1: Abandon migration; ratify single-schema
- **Description:** Confirm and lock the single-schema, fail-fast model. Remove dead "future migration / infinite region streaming" references (e.g., `SizeFor` comment, any `infinite` size handling left after Step 1). Rewrite `docs/rulesets/voxel_save_versioning_schema.md` to describe single-schema + fail-fast + corruption recovery only (delete migration-format / lazy-chunk-migration / read-only-recovery sections). Ensure **no** `rulesetVersion` field is introduced. Keep registry-hash mismatch as a non-fatal warning.
- **Files:** `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs` (verify only; comment already correct), `Assets/Blockiverse/Scripts/Gameplay/WorldSaveGeneration.cs` (comment/`infinite`), `docs/rulesets/voxel_save_versioning_schema.md`.
- **Why it matters:** Removes a documented-but-unwanted system; keeps persistence simple and the doc truthful.
- **Assigned role:** developer
- **Dependencies:** Step 1 (the `infinite` removal overlaps `SizeFor`)
- **Parallelizable:** Partly (doc rewrite is independent; code cleanup touches `SizeFor` so sequence after Step 1)
- **Verify:** `WorldSaveEditModeTests` stays green; add/keep a test asserting a mismatched `SchemaVersion` fails fast with a clear message and that registry-hash mismatch only warns. Grep confirms no `rulesetVersion` symbol anywhere.

## Wave 1 — Rendering scalability (the 72 FPS levers)

### Step 4 — R1: Skip empty/all-air chunks when building render objects
- **Description:** During meshing, detect all-air chunks and skip creating the GameObject (MeshFilter/MeshRenderer/MeshCollider/`VoxelChunkTarget`/runtime `TeleportationArea`). When a previously non-empty chunk becomes all-air (edits), release/destroy (or pool) its object and deregister its teleport area. Add an "is chunk empty" fast path from the mesher.
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs` (`RebuildAll` 88–120, `RebuildChunk` ~181–189, `GetOrCreateChunkObject` ~360–412, teleport-area wiring).
- **Why it matters:** Even at capped sizes, ~half of chunks are air; avoiding their GameObjects/colliders/teleport interactables saves memory, culling, and scene-graph cost.
- **Assigned role:** developer
- **Dependencies:** Step 1 (work against final world sizes)
- **Parallelizable:** No (shares the renderer with Step 5; do Step 4 then Step 5)
- **Verify:** Extend `ChunkRenderingEditModeTests`: assert all-air chunks create no GameObject/collider/teleport-area; assert an edited-to-empty chunk releases its object; assert a void_builder world (mostly air) creates objects only for the platform region. Keep `CanonicalWorldRenderingPlayModeTests` green (non-air chunks still have meshes/materials/atlas).

### Step 5 — R2: Make initial world render incremental (drain under budgets)
- **Description:** Route new-world creation through the existing deferred `QueueFullRebuild` path instead of synchronous `RebuildAll`. Drain visual(8)/collider(4) budgets across frames. Bake spawn-region chunks/colliders **eagerly** (so the player lands on solid ground), then progressively fill the rest. Add a "world ready" gate (spawn-area baked) that the loading screen waits on; stop calling `ProcessPendingColliderRebuilds(int.MaxValue)` in the initial path.
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs` (`RebuildAll` 113, `QueueFullRebuild` 122–140, `Configure` `deferInitialRebuild`), `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs` (`ConfigureWorldRuntime` → pass `deferInitialRendererRebuild: true`; add world-ready gating before lifting the loading overlay).
- **Why it matters:** Eliminates an unbounded main-thread hitch (ANR/timeout risk on Quest) and a single huge collider-cook spike.
- **Assigned role:** developer
- **Dependencies:** Step 4
- **Parallelizable:** No
- **Verify:** PlayMode test: new `survival_terrain` world reaches a "world ready" state with spawn-area colliders baked, and total queued rebuilds drain to zero over multiple frames without a single-frame full cook. Assert no `int.MaxValue` collider drain on the initial path. Keep `VoxelWorldRenderingPlayModeTests` green. **Unverified on device:** measure initial-render hitch on Quest 3/3S after the change.

## Wave 2 — VR comfort (owner-confirmed posture)

### Step 6 — V1a: Comfort settings model — vignette-on default + GlideStyle
- **Description:** Flip `vignetteEnabled` default to **true** and set a **low** default `vignetteStrength` (e.g. 0.2–0.35). Add a `GlideStyle { Smooth, Bobbing }` field + property to `BlockiverseComfortSettings`; default **Smooth**. Ensure `BlockiverseProjectBootstrapper.XrRig.cs` no longer forces vignette off on regen (set the new defaults instead).
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs`.
- **Why it matters:** Establishes the comfort-first baseline (Roadmap principle 7) that the menu and first-run prompt will surface.
- **Assigned role:** developer
- **Dependencies:** None (but coordinate with Step 5 done; independent code)
- **Parallelizable:** Yes (with Wave 1 if a second developer; otherwise after)
- **Verify:** EditMode: assert new defaults (`VignetteEnabled == true`, `0 < VignetteStrength <= ~0.35`, `GlideStyle == Smooth`). Update `BlockiverseRigPrefabTests` expectations that currently assert vignette-off / aperture 1.0 at startup — note these will change; adjust to the new comfort posture (or gate startup-over-menu vignette separately if the menu should still be unobscured — see Step 7 caveat).

### Step 7 — V1b: Glide bobbing locomotion behavior
- **Description:** Consume `GlideStyle` in the move provider path: *Smooth* = current continuous move; *Bobbing* = apply a subtle walk/run head-bob while moving (amplitude/frequency scaled by speed; disabled when stationary). Keep it strictly opt-in. Ensure bobbing never fights the tunneling vignette or tracking (apply to a camera-offset child, not the tracked HMD pose).
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs` / continuous-move integration, possibly a small `BlockiverseGlideBobController.cs` (new) under `Scripts/VR/`.
- **Why it matters:** Adds the requested immersion option without forcing motion-sensitive users into it.
- **Assigned role:** developer
- **Dependencies:** Step 6
- **Parallelizable:** No
- **Verify:** EditMode: bob offset is zero when stationary and bounded when moving; bob applies to offset transform, not HMD tracking. Manual on device for comfort tuning. **Caveat:** head-bob increases sickness for some users — keep default Smooth and surface clearly.

### Step 8 — V1c: First-run controls/comfort prompt
- **Description:** Add a persisted "first run completed" flag (PlayerPrefs or a small settings store). On first launch, before first world entry, route to a prompt that shows the controller layout (reuse `Controller Mapping Popup`) and the comfort selection (reuse `BlockiverseComfortMenu`) with initial options: **locomotion mode**, **Glide bob toggle (Smooth/Bobbing)**, **vignette on/off + strength**, **turn type/angle**. Apply + persist to `BlockiverseComfortSettings` and mark first-run complete. Subsequent launches skip it; settings remain reachable from Settings → Controls/Comfort.
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs` (route on boot when first-run flag unset), `Assets/Blockiverse/Scripts/VR/BlockiverseComfortMenu.cs` (add Glide bob toggle control + wire to `GlideStyle`), `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Menus.cs` (author the bob toggle into the comfort menu), a small persisted-flag helper (e.g. `Scripts/Core/`).
- **Why it matters:** Gives sensitive users an explicit, safe first choice and satisfies the comfort-first requirement.
- **Assigned role:** developer
- **Dependencies:** Steps 6, 7
- **Parallelizable:** No
- **Verify:** PlayMode (extend `MenuFlowPlayModeTests` / `BootScenePlayModeTests`): with first-run flag unset, the prompt appears before title/world entry and the title waits for it; setting values persists them to `BlockiverseComfortSettings`; with flag set, the prompt is skipped. EditMode: comfort menu exposes the Glide bob toggle and binds it to `GlideStyle`. Keep `BlockiverseRigPrefabTests` (controller-map close-button wiring) and `MenuRuntimeWiringEditModeTests` green.

## Wave 3 — Multiplayer & avatar correctness

### Step 9 — N1: Fix Meta avatar stream size cap (Unverified — measure first)
- **Description:** Measure real avatar stream sizes on device at the configured `StreamLOD`. Either raise `MaxPayloadBytes` to match the SDK LOD or fragment/chunk large streams across messages and reassemble. Replace the silent drop with an explicit warning log when a payload is dropped/too large.
- **Files:** `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs` (`MaxPayloadBytes`), `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs` (`LateUpdate` drop path → log; add fragmentation if needed).
- **Why it matters:** Remote Meta avatars are a headline deliverable; a silent over-cap drop makes them appear broken only on device.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes
- **Verify:** **Unverified — requires device measurement.** Add logging first, capture sizes, then size the cap/fragmentation. PlayMode coverage becomes feasible via Step 11's mock provider (assert relay forwards a >8 KiB stream once the cap/fragmentation is fixed).

### Step 10 — N2: Host-left / session-ended UX (no host migration)
- **Description:** Keep host-authoritative (no migration). On host loss, present a friendly "host left the session" screen routing to the existing LAN/multiplayer surface (which already has reconnect/stop/close). Ensure world input is not re-enabled while this modal/menu owns input. Optionally add client auto-retry on transient transport failure.
- **Files:** `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs` (disconnect callbacks → route reason), `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs` (host-left presentation; extends existing session-ended path), `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs` (routing).
- **Why it matters:** Without this, a host drop looks like a crash to clients.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes
- **Verify:** Extend `MenuFlowPlayModeTests` LAN flow to simulate host-loss (distinct from client-initiated disconnect) and assert routing to the host-left surface with world input suppressed. EditMode for the disconnect-reason mapping.

### Step 11 — N4: Editor-mockable Meta avatar seam
- **Description:** Provide an editor-mock implementation of the existing `IBlockiverseMetaAvatarProvider` so presenter/relay/fallback-switching logic runs in Editor PlayMode. Reserve raw `#if UNITY_ANDROID && !UNITY_EDITOR` SDK calls for device only, behind the seam.
- **Files:** `Assets/Blockiverse/Scripts/MetaAvatars/*` (provider interface + new editor-mock provider), `Assets/Blockiverse/Tests/PlayMode/` (new avatar PlayMode tests).
- **Why it matters:** Lets CI catch avatar regressions (fallback switching, relay forwarding) that currently only surface on device.
- **Assigned role:** developer
- **Dependencies:** Step 9 (so the relay test asserts the corrected cap)
- **Parallelizable:** No (with Step 9)
- **Verify:** New PlayMode tests: with mock provider, local self-avatar initializes; remote avatar appears when streams arrive; fallback proxy engages when provider unavailable; relay forwards a representative stream size.

### Step 12 — N3: Avatar pose/stream staleness timeout
- **Description:** Stamp last-update time on remote pose/stream; hide/flag a remote avatar after a silence threshold instead of holding the last pose forever.
- **Files:** `Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseNetworkAvatarRig.cs`, `MetaAvatarStreamRelay.cs`.
- **Why it matters:** Prevents frozen "ghost" avatars on silent remotes.
- **Assigned role:** developer
- **Dependencies:** Step 11 (use the mock seam to test)
- **Parallelizable:** No
- **Verify:** PlayMode with mock: stop sending → avatar hidden/flagged after threshold; resume → reappears.

## Wave 4 — Architecture & docs (maintainability)

### Step 13 — A1: Extract netcode out of `Blockiverse.Gameplay`
- **Description:** Move `MultiplayerSurvivalSync.cs`, `MultiplayerChunkAuthoritySync.cs`, `MultiplayerWorldPersistence.cs` into `Blockiverse.Networking` (or a new `Blockiverse.Multiplayer` asmdef). Fix references and the assembly graph (keep it acyclic). Begin decomposing `MultiplayerSurvivalSync` (4,168 lines) into per-domain command handlers (harvest / craft / stations / containers / inventory) — at minimum split files; ideally split responsibilities.
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/Multiplayer*.cs`, the relevant `.asmdef`s, callers across `Gameplay`/`UI`.
- **Why it matters:** Removes the largest maintainability debt; `Gameplay` is a 14.7k-line catch-all and the netcode is mislocated.
- **Assigned role:** developer
- **Dependencies:** Waves 1–3 complete (avoid churning files mid-feature-work); coordinate with N-step files that also touch multiplayer.
- **Parallelizable:** No (broad move; do as a focused pass)
- **Verify:** Full EditMode+PlayMode suite green, especially `MultiplayerSessionPlayModeTests`, `MultiplayerChunkAuthority*` tests. Confirm assembly graph stays acyclic. No behavior change intended.

### Step 14 — A2: Ratify consolidation in docs + decouple `Blockiverse.UI`
- **Description:** Update `docs/roadmap/blockiverse_vr_execution_plan.md` and the alignment matrix to ratify the `WorldGen`/`Gameplay` consolidation (drop the Environment/Structures/Vegetation/AudioVfx separate-assembly intent) and to record the netcode extraction (A1). Introduce event/interface seams to reduce `Blockiverse.UI`'s 10-assembly fan-out (start with the heaviest dependencies).
- **Files:** docs under `docs/roadmap/` and `docs/rulesets/`; `Assets/Blockiverse/Scripts/UI/*` + interface/event types (likely in `Scripts/Core/`).
- **Why it matters:** Docs are source-of-truth; they must match the sound implementation. UI fan-out reduction improves testability/maintainability.
- **Assigned role:** developer
- **Dependencies:** Step 13 (docs describe the post-extraction layout)
- **Parallelizable:** Docs portion yes; UI decoupling sequence after A1.
- **Verify:** Docs review; UI tests (`MenuRuntimeWiringEditModeTests`, `MenuFlowPlayModeTests`) green after introducing seams.

## Wave 5 — Diagnostics & polish (lower priority)

### Step 15 — A4: Close the frame-1 input-gate hole
- **Description:** Default `BlockiverseRuntimeState.AllowWorldInput` to **false** until the router publishes initial state; consider an event/interface instead of the raw static so the dependency is explicit/testable.
- **Files:** `Assets/Blockiverse/Scripts/Core/BlockiverseRuntimeState.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs` (publishes initial state).
- **Dependencies:** None · **Parallelizable:** Yes
- **Verify:** EditMode: world input is disabled before the router initializes; enabled only on gameplay routes. Keep `MenuRuntimeWiringEditModeTests` green.

### Step 16 — R5: Instrument WorldGen + lighting; tune collider cooking
- **Description:** Add `ProfilerMarker`s to each WorldGen pass (`SurvivalTerrainPreset`) and to `VoxelLightSampler.SampleAirLight`; cache `registry.Get` as a flat array indexed by block id where hot; evaluate `MeshColliderCookingOptions` for chunk colliders.
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/SurvivalTerrainPreset.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelLightSampler.cs`, `VoxelWorldRenderer.cs` (`AssignColliderMesh`).
- **Dependencies:** Wave 1 · **Parallelizable:** Yes
- **Verify:** Markers visible in Profiler; no behavior change. **Unverified on device:** confirm cost attribution.

### Step 17 — R6/V2: Validate Quest perf levers (Unverified)
- **Description:** On device with OVR Metrics: confirm foveation actually applies (`BlockiverseFoveatedRenderingController`); A/B test Automatic Dynamic Resolution / depth submission / Application SpaceWarp for the voxel workload.
- **Files:** `Assets/XR/Settings/OpenXR Package Settings.asset`, `Assets/Blockiverse/Scripts/VR/BlockiverseFoveatedRenderingController.cs`.
- **Dependencies:** Waves 1–2 · **Parallelizable:** Yes
- **Verify:** **Unverified — device only.** Record before/after frame timing.

### Step 18 — Polish batch: D2, D3, D4, V3, V4, V5, A5, A6, B1
- **Description:** Rename `Torchbud*` → `Glowwick*` (D2); add stack content discriminators to `ItemStack.CanStackWith` or document container-store enforcement (D3); author cave/underground structure templates (D4); move creative-flight toggle off the jump button (V3); resolve comfort settings once in `BlockiverseWorldSpacePanelPresenter` (V4); idempotent guard on `RepairRuntimeTracking` (V5); add a TagManager-vs-`BlockiverseProject` layer-index guard and remove stray `Canvas_-338524` layer (A5); label `MultiplayerTest.unity` as dev-only (A6); configure a Meta system splash if desired (B1).
- **Files:** as listed per item in the audit.
- **Dependencies:** None individually · **Parallelizable:** Yes (independent small tasks)
- **Verify:** Targeted EditMode tests per item (e.g., layer-index guard test; `CanStackWith` discriminator test; creative-flight binding test). Full suite green.

---

# Verification & Testing (global)

- **Per-step gate:** Each behavioral step ships with EditMode and/or PlayMode tests; do not advance a wave until its tests are green and the Console is error/warning-clean.
- **Regression nets that must stay green throughout:** `ChunkRenderingEditModeTests`, `VoxelWorldRenderingPlayModeTests`, `CanonicalWorldRenderingPlayModeTests`, `BlockiverseRigPrefabTests`, `CompositionLayerUiEditModeTests`, `MenuRuntimeWiringEditModeTests`, `MenuFlowPlayModeTests`, `BootScenePlayModeTests`, `NewWorldConfigEditModeTests`, `WorldSaveEditModeTests`, `SurvivalTerrainEditModeTests`, `StructureServiceEditModeTests`, `MultiplayerSessionPlayModeTests`, `MultiplayerChunkAuthority*`.
- **Tests that WILL change (expected, not regressions):** world-size/height tests (Step 1), comfort-default/vignette-startup assertions in `BlockiverseRigPrefabTests` (Step 6), menu-flow first-run routing (Step 8), avatar relay cap (Steps 9/11).
- **On-device verification backlog (Unverified from code):** initial-render hitch on Quest 3/3S after R1/R2 (Step 5); Meta avatar stream sizes (Step 9); foveation/dynamic-res/depth-submission/ASW (Step 17). Validate with OVR Metrics / on-device profiling.
- **Build sanity:** Android/Quest build succeeds after Wave 1 and after Wave 4 (assembly move).

---

# Suggested Sequencing Summary

1. **Wave 0** (parallel): R4b world-size cap · R3 alloc fix · D1 single-schema + doc.
2. **Wave 1** (sequential): R1 skip-air → R2 incremental render.
3. **Wave 2** (sequential): V1a defaults → V1b bobbing → V1c first-run prompt.
4. **Wave 3:** N1 cap (measure) → N4 mock seam → N3 staleness; N2 host-left UX in parallel.
5. **Wave 4:** A1 netcode extraction → A2 docs + UI decoupling.
6. **Wave 5:** A4 input-gate · R5 instrumentation · R6/V2 device validation · polish batch.
