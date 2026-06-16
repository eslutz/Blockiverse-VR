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

- Routed game menus use the generated shared Quad composition surface. Do not add per-menu composition layers.
- Controller and ray visuals use the generated Projection Eye Rig layer when they must appear over compositor UI. Do not restore the removed ad hoc pointer projection object.
- Keep UI raycast masks focused on terrain interaction plus composition UI collider layers. Visual projection layers are for rendering, not raycast targeting.

## Meta Avatars Policy

- Keep Meta Avatars as the preferred player representation while retaining the fallback proxy avatar as a first-class path for editor, offline, child-account, unavailable-platform, and failed-avatar cases.
- Do not enable Meta sample preset avatars or package sample preset assets unless the release intentionally ships and discloses them.
- Child-account or unknown-age paths must not request Meta profile/avatar data unless current Meta policy review explicitly permits it; fallback identity/avatar behavior remains available.
