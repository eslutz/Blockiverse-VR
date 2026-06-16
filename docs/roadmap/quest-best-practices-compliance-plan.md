# Quest Best Practices Compliance Plan

Source: attached `Final Best Practices for Meta Quest VR Game Development with Unity, C#, and OpenXR.pdf`, reviewed on 2026-06-16. This plan maps each prioritized task from the report to repo evidence and remaining validation.

## Status Legend

- Implemented: source changes and EditMode tests now cover the item.
- Verify on Quest: source is prepared, but the PDF requires native headset proof that cannot be replaced by editor or Link behavior.
- Planned: the item needs a dedicated compatibility or profiling pass outside this branch.

## Highest Priority

| Task | Status | Repo action | Validation |
| --- | --- | --- | --- |
| Replace the blanket anti-composition-layer menu rule with one reusable live menu surface. | Implemented, verify on Quest. | `BlockiverseProjectBootstrapper` now generates `Blockiverse Menu Composition Surface` with one routed menu canvas. Routed menus move under that canvas; startup/loading, HUD, and block quick menu remain normal world-space UI. | `CompositionLayerUiEditModeTests.GeneratedRigUsesSingleCompositionLayerMenuSurface`; native Quest pass must confirm readability and input ordering. |
| Add Projection Eye Rig and correct user-layer assignment for XRI controllers/rays. | Implemented, verify on Quest. | Generated `Blockiverse XR Visual Projection Rig` renders the `BlockiverseXrVisuals` layer above the menu Quad layer. Controller visuals are assigned to that layer and the main camera culls it. | `CompositionLayerUiEditModeTests.GeneratedRigUsesProjectionEyeRigForControllerAndRayVisuals`; native Quest pass must confirm controller models and rays render over menu UI. |
| Make `com.unity.xr.compositionlayers` explicit. | Implemented. | `Packages/manifest.json` declares `com.unity.xr.compositionlayers`; lockfile records it at depth 0. | `QuestBestPracticesGovernanceEditModeTests.QuestRuntimePackagesAreDirectDependencies`. |
| Validate menus and comfort-critical systems on device. | Verify on Quest. | `docs/testing/README.md` now requires shared composition-surface checks in the Quest gate. | Must be completed with a native Quest build or Meta XR Simulator plus real-device follow-up for compositor ordering. |

## High Priority

| Task | Status | Repo action | Validation |
| --- | --- | --- | --- |
| Review OpenXR feature budget, including Meta XR Feature, Meta XR Foveation, and Meta XR Subsampled Layout. | Implemented as source audit, verify on Quest. | Android OpenXR settings show Meta XR Feature enabled, Meta XR Foveation enabled, Composition Layers enabled, and Meta XR Subsampled Layout present but the OpenXR feature's own `enableSubsampledLayout` flag remains disabled until headset UI-quality profiling justifies it. Meta's current setup report separately marks the OculusSettings Subsampled Layout rule done. | Source audit of `Assets/XR/Settings/OpenXR Package Settings.asset`; follow-up should capture before/after Quest profiles if enabling the OpenXR feature flag. |
| Adopt a measured Quest rendering baseline. | Implemented, verify on Quest. | Android URP asset uses Vulkan-side mobile defaults: depth texture off, opaque texture off, HDR off, 2x MSAA, render scale 1.0, shadows/additional lights off, adaptive performance on. | `BlockiverseBootstrapEditModeTests.AndroidUrpAssetUsesQuestMobileRenderDefaults`; Quest profiling must prove refresh stability on representative content. |
| Upgrade or rationalize package versions as one compatibility move. | Planned. | This branch avoids piecemeal package churn beyond explicit dependencies required by the PDF. | Create a separate package-matrix branch with one regression pass for input, locomotion, UI, networking, and Android build stability. |
| Replace negative composition-layer tests with positive architectural tests. | Implemented. | Tests now assert the shared menu composition layer, Projection Eye Rig, world-space exceptions, and Android composition splash path. | `CompositionLayerUiEditModeTests` and `QuestBestPracticesGovernanceEditModeTests`. |

## Medium Priority

| Task | Status | Repo action | Validation |
| --- | --- | --- | --- |
| Add or confirm Addressables for scene/content streaming and explicit load/release. | Implemented as dependency and policy; content migration planned. | `com.unity.addressables` is explicit and the runtime standards require Addressables ownership/release for large or variant-heavy content. No heavy runtime catalog migration was forced in this branch. | Governance test verifies package ownership and docs; future content PRs must add labels, load ownership, and release paths. |
| Add profiler instrumentation and repeatable performance budget process. | Implemented, verify on Quest. | Voxel rendering/meshing, save/load, world-session transitions, UI routing, and host-authoritative networking hot paths now retain named `ProfilerMarker`s; performance docs name the Quest profiling loop and marker watch list. | `QuestBestPracticesGovernanceEditModeTests.QuestPerformanceHotPathsKeepProfilerMarkers`; Quest captures still need real frame-time and allocation evidence. |
| Harden hot paths against allocation churn. | Planned with source audit guardrails. | Runtime standards ban recurring allocations in frame-loop, XR input, UI routing, meshing, and network tick paths; new profiler markers make the likely allocation sources easier to isolate. | Follow-up Quest profiling must track `GC.Alloc`, frame time, and CPU/GPU bottlenecks over representative play loops. |
| Audit async usage and main-thread boundaries. | Implemented as source audit, planned lifecycle hardening. | Current `.Result` reads are behind `IsCompleted` gates; no `.Wait()` or Addressables `WaitForCompletion()` calls were found in runtime scripts. Runtime standards prohibit blocking gameplay async. | Source audit command: `rg -n "\.Result|\.Wait\(|WaitForCompletion" Assets/Blockiverse/Scripts Assets/Blockiverse/Tests docs`. |

## Lower Priority

| Task | Status | Repo action | Validation |
| --- | --- | --- | --- |
| Adopt modern C# safety defaults where practical. | Planned. | Runtime standards document file-scoped namespaces, nullable intent, and readonly structs as preferred when the project language version and Unity serialization constraints allow them. Current assemblies compile with C# 9, so file-scoped namespaces require a language-version move first. | Track as a compatibility change before converting code; existing Unity scripts should convert opportunistically only after the language version supports it. |
| Review long-term Meta Avatars commitment. | Implemented as policy, planned product decision. | Runtime standards keep Meta Avatars shallow, preserve fallback proxy avatars, and forbid enabling sample presets unless intentionally shipped and disclosed. | Future avatar work must cite the policy and keep editor/offline/child-account fallback behavior intact. |

## Current Compliance Notes

- The codebase now resolves the confirmed architectural mismatch from the PDF: routed menus no longer rely on a blanket no-live-composition-layer rule.
- The remaining non-implemented items are intentionally hardware-gated, account/dashboard-gated, or migration-branch work: native Quest compositor validation, Meta Platform app ID/Data Use Checkup decisions, package-version rationalization, Meta setup-tool target-SDK/MSAA policy reconciliation, and measured allocation-budget cleanup.
- The current Meta XR Project Setup report (`/private/tmp/blockiverse-ovr-setup-current.json`, regenerated locally after bootstrap on 2026-06-16) has no unfinished required items. Its unfinished recommended items are: Data Use Checkup, application ID/package-name setup, target API 32 recommendation, and recommended Android MSAA. This branch pins ASTC in the bootstrapper and Meta now marks texture compression done; the target API/MSAA items remain deliberate policy-vs-tooling reconciliation items because the current project baseline uses target SDK 34 and 2x MSAA pending headset profiling.
- EditMode tests can prove generated structure and package/settings policy, but they do not replace the native Quest acceptance criteria called out by the PDF.
