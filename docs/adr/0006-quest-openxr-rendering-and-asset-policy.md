# ADR 0006: Quest OpenXR Rendering And Asset Policy

## Status

Accepted; menu-surface decision amended 2026-08-13 (see Amendment below)

## Date

2026-06-16 (amended 2026-08-13)

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
