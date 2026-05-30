# Changelog

All notable changes to Blockiverse VR will be documented here.

The format is based on Keep a Changelog, and releases use tags cut from `main`.

## Unreleased

- Fixed Quest movement tracking by migrating the rig to Unity Input System/XRI pose
  and locomotion providers for before-render HMD tracking, continuous movement,
  snap turn, and teleport.
- Completed the native XRI migration for tracking and interaction: controllers are
  driven by `TrackedPoseDriver` (Update + BeforeRender) like the headset, the comfort
  menu's Smooth Turn toggle switches between snap and continuous turn, and teleport is
  target-based (`TeleportationArea` on the voxel terrain with an arc reticle) instead of
  a fixed forward dash.
- Reworked every VR menu onto the native UI stack (`XRUIInputModule`,
  `TrackedDeviceGraphicRaycaster`, controller ray interactor) so buttons, toggles,
  sliders, scrolling, and the LAN address field (via the system keyboard) are fully
  usable; block break/place now uses the native ray interactor and is suppressed while
  the ray is over UI. Removed the custom ray pointer and UI pointer.
- Added the M6 signed-release APK pipeline path, including a Unity release build entry point,
  secret-based Android signing script, checksum generation, GitHub Release artifact publishing,
  store-document validation, and a Quest screenshot/capture plan.
- Expanded and refined the M6 audio generator to cover eleven original WAV cues:
  block break/place, select/confirm/cancel, footstep alternates, inventory open/close,
  and craft success/fail, with layered synthesis and peak-headroom regression checks.
- Added M6 performance instrumentation: ProfilerMarkers around survival-lite world generation, chunk meshing, and renderer rebuild paths; an engine-free `FrameStatisticsSampler` with EditMode coverage; a local-only in-game performance overlay (FPS, frame time, chunk/triangle counts, rebuild queue) that stays hidden in release builds; and a max-world generation/meshing stress test plus a performance report template.
- Added M6 store readiness documentation: drafted privacy policy, store listing/metadata, VRC working checklist, data-use and safety declarations, known-issues/support notes, and a release-notes template, with hardware/account-dependent items marked as external follow-ups.
- Added M6 audio and haptics feedback: a block-mutation event on the creative interaction controller drives an audio cue player (break/place plus UI cues) and dominant-hand haptic patterns, with a generator script for original synthesized sound effects (Git LFS).
- Fixed a codebase analysis pass: corrected the survival-lite spawn headroom clearing so the reserved air column matches the validated headroom, made the world save fallback path keep a recoverable backup if an atomic replace is unavailable, removed the duplicate `ItemId.Air` alias of `ItemId.None`, and deleted the unused multiplayer host delta tracking helper.
- Added M5 multiplayer simulator validation for active block edits under 100ms latency and packet loss, plus recorded bandwidth estimates for host-authoritative chunk mutation messages.
- Added host-authoritative multiplayer survival-lite sync for resource harvesting, per-player inventory snapshots, shared crate transfers, and crafting validation across two clients.
- Added deterministic multiplayer conflict handling so stale competing client block mutations are rejected with host-authoritative correction.
- Fixed Quest first-launch comfort by auto-starting Android OpenXR, opening VR menus headset-relative, adding controller mapping and branded startup overlays, wiring survival HUD buttons for VR ray input, and adding Blockiverse VR app identity assets.
- Added Meta Horizon Avatar runtime wiring for single-player first-person avatar visibility and multiplayer avatar stream relay with fallback proxy coverage.
- Added late-join multiplayer validation that proves joined clients receive current host state and remain synchronized with subsequent authoritative chunk deltas.
- Added sequenced chunk delta records and observer-client delta sync coverage for authoritative multiplayer block edits.
- Added request IDs and pending-response tracking to authoritative multiplayer block mutation RPCs so accepted deltas and host rejections are correlated deterministically.
- Added host-owned multiplayer chunk authority boundaries with client block-edit requests, host validation, delta broadcast, late-join changed-block snapshots, save ownership checks, and client-side direct mutation rejection.
- Added a fallback proxy avatar rig to the multiplayer network player prefab so local editor multiplayer remains usable when Meta Horizon Avatar data is unavailable.
- Added multiplayer host world persistence hooks so graceful LAN host shutdown saves the host world before disconnecting clients and reloads saved edits before the next hosted session.
- Added LAN-scoped session-ended and reconnect UX when clients lose the host, with local editor coverage for host restart and client rejoin.
- Added an M5 LAN multiplayer session menu for hosting, joining by IP address, stopping sessions, and validating the MultiplayerTest scene flow through Unity UI controls.
- Added the M8 Cloud Private Worlds roadmap milestone for post-release cloud-hosted persistent private worlds while preserving local LAN multiplayer as a separate mode.
- Added the M5 multiplayer networking foundation with Netcode for GameObjects, Unity Transport, a host/client session bootstrap, multiplayer test scene, and local lifecycle coverage.
- Updated the M5 multiplayer roadmap to clarify host-authoritative LAN co-op, host disconnect/save behavior, fallback avatars, chunk authority, network resilience checks, and Meta Quest party chat instead of in-app voice chat.
- Added a local-only diagnostics logging foundation for Alpha validation, including categorized Unity/player log routing, sanitized save/render diagnostics, and Quest log capture documentation.
- Fixed external review findings in voxel renderer mesh lifecycle, placement preview material overrides, world-load change events, save validation, atlas coverage checks, and survival inventory UI fallbacks.
- Added a Boot-scene survival HUD that binds inventory, crafting, and health panels to runtime M3 survival state for simulator/headset validation.
- Added the matching Meta XR Interaction SDK package and removed committed Unity MCP/AI Assistant editor packages from the clean validation baseline to eliminate non-gameplay Unity package warnings.
- Added committed M4 block textures, item icons, UI sprites, texture import metadata, authored-atlas renderer integration, and asset validation coverage.
- Removed the runtime procedural block-atlas fallback so missing or unrelated block textures fail validation instead of silently rendering incorrect visuals.
- Created the M4 Art and Texture Assets milestone in the roadmap for authored block textures, renderer integration, provenance, and Quest visual validation.
- Moved early block art/readability work into M4 Art and Texture Assets and documented generated terrain as the default creative validation world.
- Added an original procedural block texture atlas for renderable blocks so headset validation can visually distinguish terrain, resource, crafting, storage, and lighting blocks.
- Replaced the flat default creative validation world with generated survival-lite terrain while preserving creative break/place editing.
- Added M3 player inventory persistence to the world save schema with version migration and validation.
- Added M3 survival resource harvesting rules for block drops, basic tool effectiveness, and explicit resource scarcity tuning.
- Added M3 survival UI binders for inventory slots, crafting recipes/actions, and player health state.
- Documented repository tooling guidance for Unity MCP, global `hzdb` CLI Quest-device validation, unstable MCP server isolation, and generated validation artifacts.
- Added the M3 survival crafting model with default recipe definitions, workbench-gated validation, and inventory-backed crafting tests.
- Added the M3 Meta XR Simulator, Horizon Debug Bridge, Unity MCP, and Meta XR Unity MCP Extension validation workflow documentation.
- Documented the requirement to use Meta Horizon avatars for multiplayer players while keeping NPCs and mobs as original blocky voxel characters.
- Added the M2 flat creative prototype foundation with a bounded voxel world, original block registry, chunk mesh rendering, creative block placement and breaking, hotbar selection, undo, and schema-versioned world save/load.
- Added the M1 VR interaction foundation with a dominant-hand ray pointer, highlightable test block, and left-hand block menu placeholder.
- Added M1 VR comfort locomotion with teleport movement, snap turning, height reset, comfort settings, and a VR comfort settings menu used by the XR rig.
- Added the M1 VR controller input action map, Quest controller anchor bindings, haptics abstraction, and input smoke tests.
- Updated GitHub Actions workflow pins to current stable major versions and documented dependency-currency guidance for agents.
- Bootstrapped the Unity 6 Quest project with URP, OpenXR Meta Quest settings, a boot scene, an XR rig prefab, assembly boundaries, EditMode and PlayMode bootstrap tests, and a development APK smoke build.
- Configured hybrid CI for Unity Personal: GitHub-hosted PR checks validate repository policy, while Unity tests and development APK smoke builds run locally through Unity Hub.
- Documented the manual Unity validation workflow and removed hosted Unity license, test, and development APK jobs from the current Actions contract.
- Removed one-off repository foundation and roadmap bootstrap scripts from the tracked tree while keeping reusable CI safety and Unity validation scripts.
- Migrated `main` protection from classic branch protection to a repository ruleset requiring `Repository checks`, linear history, conversation resolution, and force-push/deletion protection.
- Enabled repository auto-delete of head branches after pull requests merge.
- Loosened agent issue-completion guidance so simple, objectively verified non-PR work can be moved to `Done` and closed with evidence.
- Updated agent workflow guidance for GitHub Project lane updates, issue/PR linking, and solo-maintainer review rules.
- Added reusable CI checks for script syntax, release policy docs, and forbidden tracked files.
- Added repository bootstrap, governance files, GitHub templates, and CI scaffolding.
- Added agent workflow guidance requiring completed work to be recorded in this changelog.
