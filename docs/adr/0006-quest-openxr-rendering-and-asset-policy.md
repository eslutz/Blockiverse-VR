# ADR 0006: Quest OpenXR Rendering And Asset Policy

## Status

Accepted; menu-surface decision amended 2026-08-13, lighting/shadow policy amended 2026-08-19, emitter occlusion amended 2026-08-22, and the avatar-mirror RenderTexture exception recorded 2026-08-23 (see Amendments below)

## Date

2026-06-16 (amended 2026-08-13, 2026-08-19, 2026-08-22 and 2026-08-23)

## Amendment 2026-08-23: The Avatar Mirror Is The One Sanctioned RenderTexture Camera

This policy bars casual camera+RT use after the on-headset failures that reversed the
composition-menu model. The placeable avatar mirror (`mirror_pane`, issue
[#340](https://github.com/eslutz/Blockiverse-VR/issues/340)) is the single sanctioned exception,
with a deliberately narrow shape (folded from the retired avatar-mirror ADR, 2026-08-22):

- **Loopback avatar, not a scene reflection.** A second remote-style avatar entity (`ThirdPerson`
  + `Full` — the configuration with legs, which first person never shows; SDK design, final at the
  40.0.1 EOF release) is posed by the local player's own `RecordStreamData` stream at 24 Hz, in a
  "studio" pocket floating 12 m above the active pane. A true reflected avatar fails structurally
  in a voxel world: the reflected position sits inside the wall the mirror hangs on.
- **One small camera renders only the studio**: culling mask exactly layer 14
  (`BlockiverseMirrorAvatar`), 512² RenderTexture, shadows and post off, solid clear. The main
  camera culls layer 14 (bootstrapper-owned), so nothing renders twice.
- **One mirror active at a time** (`BlockiverseMirrorSurfaceManager`): nearest pane within 6 m with
  an open viewer-side face. Reflection = rig-root pose reflected across the pane plane
  (`MirrorPoseMath`) + the pane sampling the RT X-flipped — the geometric half plus the image flip
  together make the improper transform a mirror needs.
- Accepted v1 approximations: fixed 60° FOV from the pane centre (no off-axis projection yet);
  tracking-space offsets not themselves reflected; editor shows the studio backdrop (avatar
  entities are device-only); remote players' mirrors reflect only that player.
- The mirror inherits every avatar-pipeline dependency, including the child-account policy: no
  stream is recorded for a suppressed avatar, so a child's mirror stays dark.
- **Device gate before shipping:** `ovrgpuprofiler` frame cost of the active-mirror camera on
  Quest 3 plus an in-headset visual pass. Mirrors are a classic mobile-VR trap; the budget is one
  512² avatar-only pass, and the number must be measured, not reasoned.

## Amendment 2026-08-22: Each Punctual Light Gets One Occlusion Term, Not Two

The 2026-08-19 amendment below says the shader "gates the realtime term by the result" of the
baked line-of-sight bake. It gated **every** punctual light by it, including the one emitter that
owns a real shadow map. That is a composition of two occlusion terms by multiplication, and the
coarser one wins wherever it is zero:

- The baked `emitterReach` term is one sample per face (`VoxelLightSampler.SampleEmitterReach`
  returns exactly 0 or 1, and `ChunkMeshBuilder` writes one value to all four vertices), so it
  resolves occlusion at **1 m**.
- The nearest emitter's cube shadow map is a 1024 atlas holding six slices on a 4x4 grid — 256 px
  per cube face, about **4 cm** at five metres, 25 times finer.

The visible consequence was that emitter shadows stepped along block boundaries instead of
following the geometry actually occluding the light: the bake zeroed the punctual term across a
whole face wherever the face centre was occluded, annihilating the shadow map's silhouette inside
that face.

The rule is therefore one occlusion term per light, chosen by whether the light owns a shadow
slice:

- A light with a shadow slice (`GetAdditionalLightShadowParams(i).w >= 0`) is occluded by **its
  shadow map only**. `GlowwickLightManager.MaxShadowCastingLights` rations that to the nearest
  emitter, which is the one whose shadow edge a player is close enough to read.
- Every other light is occluded by **the baked gate only**, exactly as before. Without a shadow
  map, URP punctual attenuation is pure inverse-square with no occlusion at all, so the bake
  remains the only thing stopping a torch lighting the far side of a wall.
- Past URP's additional-shadow fade the cube map stops answering and occlusion is handed back to
  the bake, so distance cannot reopen the bleed-through the bake exists to prevent. That crossfade
  must use the **raw** shadow sample (`AdditionalLightRealtimeShadow`), never
  `Light.shadowAttenuation`: URP has already mixed the fade into the latter, so a fully shadowed
  texel reads back as `fade` rather than 0. Combining two envelopes that have both been lifted by
  the same fade reopens pixels that *both* terms call occluded — the naive
  `min(shadowAttenuation, max(emitterReach, 1 - fade))` peaks at 0.5 mid-band and puts half the
  punctual light through a wall.
- `Light.shadowStrength` for emitters moves from 0.7 to 1.0. It was tuned while the bake also
  hard-zeroed the punctual term, so it never governed anything; it is now the sole control over
  how dark an emitter shadow is, and 1.0 preserves the "no punctual light through a wall"
  contract.

This changes no CPU work at all: no new rays, no change to `ChunkMeshBuilder` or
`VoxelLightSampler`, and the bake keeps its exact previous semantics. It costs one constant-buffer
read, one uniform branch and one lerp per light per fragment, plus moving the gate multiply inside
the light loop (at most four per fragment under the per-object cap).

It fails safe. `GetAdditionalLightShadowParams` returns a slice index of -1 for every light when
`ADDITIONAL_LIGHT_CALCULATE_SHADOWS` is undefined, so a player whose shadow keywords were stripped
by the build preprocessor — the `m_PrefilteringMode` trap — falls back to the baked gate
everywhere and renders as it did before this amendment.

**What is deliberately not fixed:** emitters without a shadow map keep the 1 m block-aligned
occlusion edge. They are farther, dimmer by inverse-square at the surfaces in question, and not
the light a player is reading. If a multi-emitter room proves objectionable on device, the next
move is `MaxShadowCastingLights = 2` — bounded, and the atlas already packs 12 slices at the same
256 px per face — not per-corner sampling on the chunk rebuild path.

**Still open, and unchanged by this amendment:** the device profiling gate below. This amendment
was reasoned and unit-tested, not measured.

**If emitter shadows misbehave in a device build, check `Assets/UniversalRenderPipelineGlobalSettings.asset`
before suspecting the shader.** The fallback above is only safe because `_ADDITIONAL_LIGHT_SHADOWS`
is present or absent *predictably*, and `ShaderStrippingSetting` — one of the 13 entries under
`m_RuntimeSettings` — is what governs that. A Unity batchmode run has twice been observed emptying
that list (mid-EditMode, on two unrelated worktrees, with no project code referencing the asset;
see the tooling policy in `CLAUDE.md`). The dropped entries include XR runtime resources, variable
rate shading — the foveated rendering this project pins at 0.66 — and shader stripping. Committed
empty, that is a device-only rendering regression that looks nothing like a shader bug. Nobody has
built a player from an emptied list to confirm breakage; what is established is what the entries
are. Revert that file unconditionally if a run dirties it.

## Amendment 2026-08-19: Budgeted Realtime Lighting And Shadows Supersede "Shadows And Additional Lights Off"

The original Quest rendering baseline disabled main-light shadows, additional lights, and shadow
distance entirely in the generated Android URP asset. That baseline was never profiled on device and
it made two shipped behaviours impossible rather than merely cheap:

- Night rendered at roughly 2% of daylight radiance, against the canonical full-moon target of 4/15
  (~27%) in `docs/rulesets/voxel_world_environment_effects.md` §4.3/§4.4.
- Every placed light-emitting block (`glowwick`, `lumen_lamp`, `campfire`, `spark_flare`,
  `emberflow`) was discarded by the pipeline, so torches and lanterns emitted no light at all.

The rendering baseline is therefore budgeted, not disabled:

- Main-light shadows on, 1024 shadowmap, **one** cascade, 30 m shadow distance.
- Additional lights per-pixel, capped at **4 per object**, additional-light shadows on at 1024.
- Hard shadows only. Unity flags soft shadows as a significant cost on tile-based mobile and
  untethered XR GPUs, and Meta's mobile-VR guidance is "Hard Shadows Only or Disable Shadows".
- Exactly **one** directional light exists. URP promotes a single directional to the main light, so
  the sun and the moon share it and whichever body is above the horizon drives it. A second
  always-on directional light would silently become a costly additional light.
- Realtime punctual emitters are capped (`GlowwickLightManager.MaxRuntimePointLights`) and the slots
  are spent on the emitters nearest the viewer, with only the closest one owning a shadow map.
- Occlusion for every other emitter is baked, not rendered: the chunk mesh bake traces a voxel
  line-of-sight ray from each face to each emitter in range and the shader gates the realtime
  term by the result. Light does not pass through solid blocks at any emitter count, and the
  shadow map budget is spent only on sub-block detail (avatars, props) around the nearest light.
  Sky light is baked with a floor of zero — enclosed spaces are dark unless something in them emits.

Shadows are not specified by any ruleset; this amendment is the canonical authority for them.

Quest device profiling remains an open gate: the added ShadowCaster pass draws loaded chunks a
second time, and the frame cost of that pass is the number this amendment most needs measured.

## Amendment 2026-08-13: World-Space Menu Baseline Supersedes The Shared Composition Quad

The shared `Blockiverse Menu Composition Surface` Quad-layer menu model below was
reversed in practice by commit `9c6f435c` ("Stabilize Quest menus with world-space
UI baseline", 2026-07-01) after on-headset visual failures with the
RenderTexture/composition-layer menu path. The current baseline is:

- Routed game menus are direct world-space XR canvases. No shared menu Quad layer,
  no `InteractableUIMirror`, no hidden `CanvasCamera`, no composition menu cursor.
- Composition layers are retained only for the startup splash overlay.
- The controller/ray policy (normal main-camera render path, no
  `ProjectionLayerRigData` ray rendering) is unchanged and remains in force.

Validation: headset-verified builds tagged `quest-world-space-menus-known-good-2026-07-01`
and `quest-menu-good-2026-07-02`; runtime-verified again 2026-08-13 via Meta XR
Operator inspection in the Meta XR Simulator on Unity 6000.5.8f1 + Meta XR SDK
205.0.0 + Composition Layers 2.5.0 (frame-end layer inventory shows a single
projection layer; world-space menus render correctly; no stuck splash layer).

The menu-related bullets in the Decision and Consequences sections below are
retained for history but no longer describe the shipped architecture.

## Decision

Blockiverse VR uses the current Unity/OpenXR/Meta stack with explicit runtime package ownership:

- `com.unity.xr.compositionlayers` is a direct dependency, not only a transitive Meta/OpenXR dependency.
- `com.unity.addressables` is a direct dependency for large or streamable content.
- Android Quest builds stay on OpenXR, Vulkan, URP, IL2CPP, ARM64, and Single Pass Instanced rendering.
- Routed game menus share one generated `Blockiverse Menu Composition Surface` Quad layer with one source canvas. Individual menu screens must not add their own `CompositionLayer`, `TexturesExtension`, `InteractableUIMirror`, or hidden `CanvasCamera`.
- Controller and ray visuals stay on the normal main-camera render path. The compositor-backed menu uses the generated composition menu cursor for menu-local hover feedback instead of rendering controller rays through `ProjectionLayerRigData`.
- Startup/loading, gameplay HUD, and block quick menu remain normal world-space canvases unless headset captures prove they need their own compositor treatment.

## Context

Quest text and menu artwork benefit from compositor-backed Quad layers, but a per-screen layer model consumes scarce user layers and makes controller/ray visibility brittle. Physical Quest validation showed that rendering controller rays through an additional projection-layer camera path can decouple ray visuals from the tracked-controller render path, causing visible jitter and alignment drift.

Addressables is required before content growth because biome catalogs, texture sets, audio, VFX, avatar assets, and future additive scenes cannot safely scale through `Resources`, ad hoc `StreamingAssets`, or synchronous scene bootstrap references.

## Consequences

- Bootstrap-generated rig tests must verify the single menu composition surface, absence of projection-layer controller-ray rendering, the composition menu cursor, and package manifest ownership.
- New routed menus go under the shared composition canvas and use presenter-controlled active state rather than canvas enablement.
- Main scene cameras cull the composition UI source layer and any unused `BlockiverseXrVisuals` layer, while keeping normal world/interaction layers visible.
- New large assets must be Addressables candidates and must release handles after use. Runtime gameplay code must not call `WaitForCompletion()` or block the main thread on Addressables loads.
- Quest UI or rendering changes require targeted EditMode coverage plus physical Quest or Meta XR Simulator acceptance when ray/controller ordering cannot be proven in edit-mode tests.
