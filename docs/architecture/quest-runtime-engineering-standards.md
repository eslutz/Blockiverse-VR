# Quest Runtime Engineering Standards

This page captures the repo rules for Unity/C# code that runs in Quest gameplay, UI, networking, generation, and asset-loading paths.

## C# And Allocation Policy

- No recurring managed allocations in `Update`, `LateUpdate`, `FixedUpdate`, XR input polling, ray interaction, menu routing, chunk meshing, or network tick paths. Reuse collections, pre-size buffers, and keep temporary data owned by the system that performs the work.
- Avoid LINQ, closure captures, string interpolation, boxing, `foreach` over allocation-prone enumerables, and repeated `GetComponent`/`Find` calls in hot paths. Cache references during bootstrap, `Awake`, `Start`, or explicit configure methods.
- Logging in hot paths must be sampled, state-change based, or gated behind diagnostics flags such as `BlockiverseTrace`; do not format high-volume log strings every frame.
- Prefer small pure-C# methods for voxel, inventory, save, rules, and networking logic so EditMode tests can exercise behavior without headset hardware.

## Async And Lifecycle Policy

- Unity object access stays on the main thread. Background work may prepare pure data only; it must marshal results back to the main thread before touching Unity APIs.
- Every coroutine, `Task`, or async load that can outlive its owner must have an owner-tied cancellation or release path in `OnDisable`/`OnDestroy`. No fire-and-forget gameplay async.
- Do not block gameplay with task `.Result` or `.Wait()` before completion, `Thread.Sleep`, synchronous scene loads, or Addressables `WaitForCompletion()`. Loading flows must surface progress through the startup/loading presenter or an explicit in-world loading state; reading a task result is acceptable only after an explicit completion gate.

## Addressables Policy

- Use Addressables for large or variant-heavy textures, biome/structure catalogs, audio banks, VFX sets, avatar assets, and future additive scenes.
- Keep bootstrap-critical assets that must always exist as direct serialized references only when their size and churn are small. Otherwise store an Addressables key or label in the registry/config object.
- Release every Addressables handle owned by a runtime system. Shared caches need explicit reference ownership and a teardown path.
- Addressables labels should describe runtime intent, such as `quest-common`, `biome:<id>`, `texture-set:<id>`, `audio-bank:<id>`, or `avatar`, instead of editor folder layout.

## XR UI And Rendering Policy

- Routed game menus, modals, and the gameplay HUD are direct world-space surfaces; composition layers are retained only for the startup splash. The shared-Quad composition menu model was reversed after on-headset failures ([ADR 0006](../adr/0006-quest-openxr-rendering-and-asset-policy.md), Amendment 2026-08-13), and screens are migrating to UI Toolkit ([ADR 0010](../adr/0010-ui-toolkit-runtime-ui.md)). Menus must not add their own `CompositionLayer`, RenderTexture compositor, hidden camera, or custom pointer projection.
- Controller and ray visuals stay on the normal main-camera render path. Do not add projection-layer cameras for controller rays.
- UI panels must sit on a layer inside `VrUiRaycastLayerMask` — a panel on Unity's default UI layer (5) renders perfectly and cannot be pointed at.

## Meta Avatars Policy

- Keep Meta Avatars as the preferred player representation while retaining the fallback proxy avatar as a first-class path for editor, offline, child-account, unavailable-platform, and failed-avatar cases.
- Do not enable Meta sample preset avatars or package sample preset assets unless the release intentionally ships and discloses them.
- Child-account or unknown-age paths must not request Meta profile/avatar data unless current Meta policy review explicitly permits it; fallback identity/avatar behavior remains available.

## Deterministic Input Wiring

Folded from retired ADR "Deterministic XRI Input Reference Wiring" (2026-06-15).

- The Unity Input System catalog is generated deterministically by the bootstrapper: map, action, and binding IDs derive from stable project-owned names, never Unity-generated random IDs. New input actions are added to the bootstrapper catalog, not hand-added in the editor.
- One generated `InputActionReference` asset per project-owned action lives under `Assets/Blockiverse/Settings/InputActionReferences/`. Generated scenes and prefabs reference those asset-owned references for XRI pose drivers, ray interactors, UI input modules, and locomotion readers — never scene-local `InputActionReference` objects or serialized inline `InputAction` instances. (Unity rewrites inline input serialization during import/build/batchmode, producing large phantom diffs.)
- Runtime repair code may create transient fallback references only when repairing live objects; persisted generated assets always use tracked generated references. EditMode tests guard deterministic IDs, reference-asset coverage, and the absence of scene-local references in generated scenes.

## XRI Locomotion And Gravity

Engine-level traps from the swim work (design rules live in `voxel_survival_ruleset.md` §5.6). Each one failed silently before it was pinned by a test; do not relearn them.

- Suppressing gravity needs `TryLockGravity(GravityOverride.ForcedOff)` **and** `gravityProvider.gravityControllers.Add(this)`. The controller list auto-populates exactly once from components already present, so a provider added later is never consulted; `gravityPaused` alone is ignored while no forced lock is held. Every path that stops the suppression must release the lock — leaving the water, locomotion suppression, creative flight, world teardown, `OnDisable` — or the player hangs in mid-air on dry land.
- `TryStartLocomotionImmediately()` returns false once the provider is already Moving. Gating motion on its return value queues motion on the entry frame only, which reads as the player drifting a few millimetres and stopping.
- Mode-gated inputs must be read as **actions**, not through their providers: `jumpProvider` is disabled in Teleport mode, so code reading the provider instead of the jump action can strand a Teleport-mode player (able to descend underwater but not ascend).
- Fluid queries are engine-free voxel reads (`FluidSubmersion`, `FluidLedge`), never physics casts: casts contend with the throttled collider recook queue and disagree with the GPU wave, which is presentation-only. A null world reads as dry and releases every lock — the title screen and world swaps are real states.

## Performance Instrumentation

Folded from retired ADR "Performance Instrumentation and Interaction Feedback" (2026-06-07); the audio/VFX/feedback content it once carried is owned by `voxel_audio_vfx_ruleset.md`.

- Hot CPU paths (world generation, chunk meshing, renderer rebuilds, save/load, session transitions, UI routing, host-authoritative networking) keep named `ProfilerMarker`s; a governance test pins the watch list.
- The performance stats overlay and other debug-only UI never render in release builds (a VRC requirement). On-headset frame evidence goes under `docs/testing/performance/` using the report template; editor-side stress tests are a CPU proxy, not acceptance.
- Presentation reacts to gameplay through events (e.g. `BlockMutationApplied`), keeping audio/haptics/VFX responders decoupled from edit logic.
