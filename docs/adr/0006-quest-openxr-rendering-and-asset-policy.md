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
