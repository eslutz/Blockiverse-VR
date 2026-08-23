# ADR 0010: Avatar Mirror as a Placeable Block with a Loopback Studio

- Status: Accepted (device validation pending)
- Date: 2026-08-22
- Issue: [#340](https://github.com/eslutz/Blockiverse-VR/issues/340)

## Context

The Meta Avatars SDK never shows the local player their own legs in first person —
deliberate SDK design with no override, final now that the SDK is End-of-Feature at
40.0.1. Meta's guidance for letting players see their full avatar is an in-world
mirror. Eric ratified the direction on 2026-08-22: the first-person no-legs view is
accepted, and the mirror ships as a **placeable world object** (`mirror_pane`,
BlockId 81), not a fixed fixture.

Two prior decisions constrain the rendering approach:

- [ADR 0006](0006-quest-openxr-rendering-and-asset-policy.md) records that the
  RenderTexture / hidden-camera composition path for menus was reversed after
  on-headset visual failures. Any new camera+RT use needs a narrow scope and its own
  device gate.
- A "reflected avatar behind the pane" without any camera fails structurally in a
  voxel world: the reflected position lies inside the wall the mirror hangs on, so
  chunk geometry z-occludes it.

## Decision

`mirror_pane` is an ordinary glass-like crafted block (Clay Kiln:
`clearpane_glass ×1` + `paletin_bar ×1`). Its surface is presented by a companion
system, not the chunk mesh:

1. **Loopback avatar, not a scene reflection.** A second remote-style avatar entity
   (`ThirdPerson` view, `Full` manifestation — the configuration with legs) is posed
   by the same `RecordStreamData` stream the local first-person avatar already
   supports, at 24 Hz. It stands in a "studio": a pocket on the dedicated
   `BlockiverseMirrorAvatar` layer (14), floating 12 m above the active pane.
2. **One small camera renders only the studio.** The studio camera's culling mask is
   exactly layer 14, target is a 512² RenderTexture, shadows and post off, solid
   clear color. The main camera culls layer 14, so nothing is rendered twice. This is
   the entire per-frame cost of the feature: one Light-quality avatar into a 512²
   target while a mirror is active.
3. **One mirror active at a time** (`BlockiverseMirrorSurfaceManager`, modeled on
   `GlowwickLightManager`): nearest pane within 6 m whose viewer-side horizontal face
   has an open neighbour. The pane face gets a pooled quad showing the RT.
4. **Reflection = reflected pose + image flip.** The rig-root pose is reflected
   across the pane plane into studio space (`MirrorPoseMath`); the pane samples the
   RT X-flipped. A reflection is an improper transform, so the geometric half plus
   the image flip together read as a mirror.

## Accepted approximations (v1)

- The studio camera sits at the pane's centre with a fixed 60° FOV rather than at the
  viewer's reflected eye point: parallax is approximate. Upgrade path: off-axis
  projection from the reflected eye.
- The stream's tracking-space offsets (room-scale walking) are not themselves
  reflected — only the rig root is. The X-flipped image visually compensates; the
  residual error is bounded by play-space size. Thumbstick locomotion moves the rig
  root and is reflected exactly.
- In the editor the pane shows the studio backdrop (avatar entities exist only on
  device, like the rest of the avatar pipeline).
- Remote players' mirrors do not reflect other players (local-only per issue #340's
  acceptable first cut).

## Consequences

- Layer 14 (`BlockiverseMirrorAvatar`) is claimed; the main camera must keep culling
  it (bootstrapper-owned, like the CompositionUI/XrVisuals exclusions).
- The mirror inherits every avatar-pipeline dependency: SDK manager configuration,
  entitlement state, and the child-account policy (no stream is recorded for a
  suppressed avatar, so a child's mirror stays dark — consistent with the proxy
  policy).
- **Device gate before shipping:** frame cost of the active-mirror camera on Quest 3
  (`ovrgpuprofiler`, procedure in `docs/testing/performance/README.md`), plus a
  visual check of the pane in-headset. Mirrors are a classic mobile-VR trap; the
  budget here is deliberately one 512² avatar-only pass, but the number must be
  measured, not reasoned.
