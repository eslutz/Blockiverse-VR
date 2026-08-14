# ADR 0006: Quest OpenXR Rendering And Asset Policy

## Status

Accepted

## Date

2026-06-16

## Decision

Blockiverse VR uses the current Unity/OpenXR/Meta stack with explicit runtime package ownership:

- `com.unity.xr.compositionlayers` is a direct dependency, not only a transitive Meta/OpenXR dependency.
- `com.unity.addressables` is a direct dependency for large or streamable content.
- Android Quest builds stay on OpenXR, Vulkan, URP, IL2CPP, ARM64, and Single Pass Instanced rendering.
- Routed game menus share one generated `Blockiverse Menu Composition Surface` Quad layer with one source canvas. Individual menu screens must not add their own `CompositionLayer`, `TexturesExtension`, `InteractableUIMirror`, or hidden `CanvasCamera`.
- Controller and ray visuals that must appear above composition-layer UI render through the generated `Blockiverse XR Visual Projection Rig`, ordered above the menu Quad layer and assigned to the `BlockiverseXrVisuals` Unity layer.
- Startup/loading, gameplay HUD, and block quick menu remain normal world-space canvases unless headset captures prove they need their own compositor treatment.

## Context

Quest text and menu artwork benefit from compositor-backed Quad layers, but a per-screen layer model consumes scarce user layers and makes controller/ray visibility brittle. Unity's XR Composition Layers package documents Projection Eye Rig as the supported path for rendering high-priority XR hands/controllers over UI composition layers.

Addressables is required before content growth because biome catalogs, texture sets, audio, VFX, avatar assets, and future additive scenes cannot safely scale through `Resources`, ad hoc `StreamingAssets`, or synchronous scene bootstrap references.

## Consequences

- Bootstrap-generated rig tests must verify the single menu composition surface, the Projection Eye Rig, and package manifest ownership.
- New routed menus go under the shared composition canvas and use presenter-controlled active state rather than canvas enablement.
- Main scene cameras cull `BlockiverseXrVisuals`; Projection Eye Rig cameras render only that layer with transparent backgrounds.
- New large assets must be Addressables candidates and must release handles after use. Runtime gameplay code must not call `WaitForCompletion()` or block the main thread on Addressables loads.
- Quest UI or rendering changes require targeted EditMode coverage plus physical Quest or Meta XR Simulator acceptance when ray/controller ordering cannot be proven in edit-mode tests.
