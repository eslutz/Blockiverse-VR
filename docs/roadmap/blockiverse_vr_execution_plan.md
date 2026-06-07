# Blockiverse VR Execution Plan

**Working title:** Blockiverse VR
**Target platform:** Meta Quest 3 / Quest 3S
**Input:** Quest controllers only; no hand-tracking-only mode for the initial target
**Engine:** Unity 6
**Language:** C#
**Rendering:** URP, original voxel art assets, Quest-readable atlas/material pipeline
**XR stack:** OpenXR, Unity OpenXR: Meta, Meta XR SDK, Unity Input System, XR Interaction Toolkit where it fits
**Networking stack:** Netcode for GameObjects + Unity Transport
**Primary game target:** Ruleset-defined voxel survival/creative game using the canonical Blockiverse world, registries, menus, save schema, environment, structures, vegetation, multiplayer, and audio/VFX rulesets
**World model:** Bounded canonical worlds first; world presets are `survival_terrain`, `flat_builder`, and `void_builder`
**Player representation:** Meta Horizon avatars for local and remote players; original voxel characters for NPCs/mobs
**Multiplayer:** LAN host-authoritative co-op first; cloud-hosted private worlds are a later upgrade
**Voice:** Meta Quest party chat; no in-app voice chat in the initial multiplayer scope
**Release path:** Meta release channels and Meta Horizon Store / Early Access; signed APK fallback through GitHub Releases
**Repo model:** Public GitHub repo, trunk-based development, protected `main`, short-lived branches, releases from `main` only
**Known-good rollback model:** Annotated `kg/...` checkpoint tags after stable implementation states, separate from release `v*` tags

---

## 1. Source of truth

The canonical target is defined by the Blockiverse ruleset documents. Existing code may be reused where it already matches these rules, especially VR locomotion, XRI menu interaction, block targeting, host-authoritative command patterns, save/versioning hooks, and generated original audio cues. The older temporary validation world, temporary block names, and reduced starter registries should be replaced or migrated to the canonical ruleset-defined world.

### Canonical design documents

- [voxel_survival_ruleset.md](../rulesets/voxel_survival_ruleset.md)
- [voxel_creative_ruleset.md](../rulesets/voxel_creative_ruleset.md)
- [voxel_survival_menus.md](../rulesets/voxel_survival_menus.md)
- [voxel_world_environment_effects.md](../rulesets/voxel_world_environment_effects.md)
- [voxel_structure_generation_ruleset.md](../rulesets/voxel_structure_generation_ruleset.md)
- [voxel_biome_vegetation_ruleset.md](../rulesets/voxel_biome_vegetation_ruleset.md)
- [voxel_save_versioning_schema.md](../rulesets/voxel_save_versioning_schema.md)
- [voxel_multiplayer_networking_ruleset.md](../rulesets/voxel_multiplayer_networking_ruleset.md)
- [voxel_audio_vfx_ruleset.md](../rulesets/voxel_audio_vfx_ruleset.md)
- [voxel_git_known_good_tagging_policy.md](../rulesets/voxel_git_known_good_tagging_policy.md)
- [voxel_implementation_alignment_matrix.md](../rulesets/voxel_implementation_alignment_matrix.md)

### Implementation policy

```text
The rulesets define the game.
The repo implementation conforms to the rulesets.
Temporary validation blocks, items, presets, and registries are migration inputs only.
New gameplay code should use stable string IDs from the canonical registries.
Backward compatibility is handled by save migrations, not by preserving temporary systems as permanent design.
```

---

## 2. Product target

### Platform

```text
Primary: Meta Quest 3 and Quest 3S
Input: Quest controllers
Unsupported initially: hand-tracking-only mode, non-VR desktop mode, mobile, PC VR
```

### Game modes

```text
survival
creative
```

### World presets

```text
survival_terrain
  Primary ruleset-defined survival world.
  Includes terrain, caves, resources, biome vegetation, structures, day/night, weather, and persistence.

flat_builder
  Creative-friendly flat world with canonical block/item catalog.
  Used for building, quick validation, and tutorials.

void_builder
  Empty creative construction space with safety bounds and explicit spawn platform.
  Used for large creative builds and structure template testing.
```

### Initial playable target

```text
Quest-playable VR voxel world
Ruleset-defined terrain/block/resource registry
Creative placement/removal using refined controller movement and block interaction
Survival resource mining, inventory, tools, crafting, and storage
Day/night and environment effects
Biome vegetation and generated structures
Versioned save/load with migration support
LAN host-authoritative co-op
Original audio, haptics, and lightweight VFX feedback
Quest-readable menus and menu flows
```

### Later expansion target

```text
Original voxel NPCs and mobs
Combat, armor, and deeper progression
Expanded biome and structure variety
Cloud-hosted persistent private worlds
Owner/member invite access
Cloud save, spin-down, spin-up, and reconnect flow
```

---

## 3. Engine, licensing, and platform decision

Use:

```text
Unity 6
C#
Universal Render Pipeline
OpenXR
Unity OpenXR: Meta
Meta XR Core SDK
Meta XR Platform SDK when entitlement/identity/store features are needed
Meta Avatars SDK for player representation
Unity Input System
XR Interaction Toolkit for locomotion, interaction rays, UI input, and teleport where appropriate
Netcode for GameObjects
Unity Transport
```

Before commercial release, re-check current Unity, Meta, store, SDK, and licensing terms.

---

## 4. Naming, IP, and asset policy

Use original Blockiverse names, textures, icons, UI, audio, creatures, structures, and branding.

Do not use:

```text
Minecraft name
Minecraft logo
Minecraft screenshots
Minecraft textures
Minecraft UI
Minecraft music
Minecraft sounds
Minecraft mob names
Minecraft character names
Creeper-like or Enderman-like characters
Minecraft item names as distinctive names
Minecraft fonts or branding
```

A colorful, blocky, voxel sandbox style is acceptable. The final game identity must remain original.

---

## 5. Development principles

1. **Ruleset-first implementation.** Gameplay systems must implement the canonical ruleset docs.
2. **Preserve what works.** Keep refined VR movement, XRI interaction, and block editing behavior where it supports the canonical game.
3. **Replace temporary registries.** Temporary blocks, items, and world presets are mapped through migrations into canonical IDs.
4. **Pure C# where practical.** World data, block registries, crafting, inventory, save/load, worldgen, command validation, and networking DTOs should be testable without VR hardware.
5. **Host-authoritative commands.** Block edits, inventory changes, crafting, resource harvests, damage, weather-affecting world events, and structure edits should flow through command objects.
6. **Quest performance is a feature.** Target stable 72 FPS minimum on Quest 3/3S, with 90 FPS as an optimization goal when feasible.
7. **VR comfort comes first.** Teleport, snap turn, height reset, readable UI, comfort toggles, and no forced smooth locomotion.
8. **Trunk-based development.** `main` remains releasable. Feature branches stay short-lived.
9. **Known-good tags.** After stable checkpoints, create annotated `kg/...` tags so rollback is easy.
10. **Original assets only.** Do not copy protected assets, sounds, names, or silhouettes.

---

## 6. Git and release workflow

### Branch model

```text
main          protected, always releasable
feature/*     short-lived feature branches
fix/*         short-lived bug-fix branches
chore/*       short-lived maintenance branches
spike/*       short-lived exploratory branches
hotfix/*      short-lived urgent fixes
```

Do not use a long-lived `develop` branch.

### Release tags

```text
v0.1.0
v0.2.0
v1.0.0
```

Release tags are player-facing. They must point to commits reachable from `origin/main`.

### Known-good checkpoint tags

```text
kg/20260606-before-world-ruleset-migration
kg/20260606-canonical-registry-loaded
kg/20260606-survival-terrain-generates
kg/20260606-creative-build-loop-canonical
kg/20260606-save-schema-migration-stable
kg/20260606-lan-coop-canonical-world-sync
```

Known-good tags are internal checkpoints. They should be annotated and pushed after validation.

Example:

```bash
git tag -a kg/20260606-canonical-registry-loaded \
  -m "Known good: canonical registry loads, tests pass, old temporary IDs migrate"
git push origin kg/20260606-canonical-registry-loaded
```

### Checkpoint rule

Create a `kg/...` tag after each state that is:

```text
Tested
Playable or objectively verifiable
Worth returning to
Before a risky migration, refactor, rendering change, networking change, save migration, or Quest performance change
```

---

## 7. Repository setup and structure

### Repo files

```text
README.md
LICENSE.md
NOTICE.md
CONTRIBUTING.md
CODE_OF_CONDUCT.md
SECURITY.md
CHANGELOG.md
AGENTS.md
docs/
  architecture/
  adr/
  roadmap/
  testing/
  store-submission/
  art-direction/
  rulesets/
.github/
  ISSUE_TEMPLATE/
  workflows/
  pull_request_template.md
.gitattributes
.gitignore
```

### Unity project structure

```text
Assets/
  Blockiverse/
    Art/
      Textures/
      Sprites/
      Materials/
    Audio/
      SFX/
      Ambience/
      Music/
    Prefabs/
      Gameplay/
      Networking/
      UI/
      VR/
    Scenes/
    Scripts/
      Core/
      Voxel/
      WorldGen/
      Environment/
      Structures/
      Vegetation/
      Gameplay/
      Survival/
      Creative/
      Persistence/
      Networking/
      AudioVfx/
      VR/
      UI/
      Editor/
    Settings/
    Tests/
      EditMode/
      PlayMode/
Packages/
ProjectSettings/
docs/
```

### Assembly definitions

```text
Blockiverse.Core
Blockiverse.Voxel
Blockiverse.WorldGen
Blockiverse.Environment
Blockiverse.Structures
Blockiverse.Vegetation
Blockiverse.Survival
Blockiverse.Creative
Blockiverse.Gameplay
Blockiverse.Persistence
Blockiverse.Networking
Blockiverse.AudioVfx
Blockiverse.VR
Blockiverse.UI
Blockiverse.Editor
Blockiverse.Tests.EditMode
Blockiverse.Tests.PlayMode
```

---

## 8. Roadmap overview

| Milestone | Goal | Result |
|---|---|---|
| M0 | Repo, Unity, CI, ruleset docs, and rollback policy | Project is buildable, documented, and checkpointable |
| M1 | Quest VR foundation | Player movement, controller input, UI interaction, and block targeting are comfortable |
| M2 | Canonical voxel core and registries | Block/item/tool/resource IDs match the ruleset-defined game |
| M3 | Canonical world generation | `survival_terrain`, `flat_builder`, and `void_builder` replace temporary validation worlds |
| M4 | Environment, vegetation, and structures | Day/night, weather, biome vegetation, and generated structures exist in the canonical world |
| M5 | Creative mode | Ruleset-defined creative catalog, placement/removal, world tools, menus, and save support |
| M6 | Survival systems | Mining, tools, inventory, crafting, stations, farming, containers, and player survival stats |
| M7 | Save/load, migration, and versioning | Stable save schema, migrations from temporary IDs, autosave, and recovery |
| M8 | LAN multiplayer | Host-authoritative co-op with canonical world sync, inventories, structures, and environment state |
| M9 | Audio, VFX, haptics, art polish | Original feedback, ambience, weather effects, block VFX, UI sounds, and Quest-readable assets |
| M10 | Quest performance and store candidate | Performance budget, release APK, Meta release channels, and store submission package |
| M11 | Full survival expansion | Original NPCs/mobs, combat, armor, deeper progression, and multiplayer survival interactions |
| M12 | Cloud private worlds | Owner/member cloud worlds with invite access, persistence, spin-down, and reconnect |

---

# 9. Phase-by-phase execution plan

## Phase 0 — Documentation, repo governance, and known-good policy

### Deliverable

The repo has clear source-of-truth documents, trunk-based workflow, validation commands, and `kg/...` checkpoint tag policy.

### Scope

```text
Keep roadmap in docs/roadmap/blockiverse_vr_execution_plan.md
Add ruleset docs under docs/rulesets/ or equivalent
Document canonical world replacement strategy
Document temporary ID migration strategy
Keep AGENTS.md aligned with roadmap
Keep CHANGELOG.md updated for material changes
Use source-available / All Rights Reserved until licensing posture changes
```

### Tests / validation

```text
Markdown files render cleanly.
No committed secrets, keystores, generated APKs, Unity Library, Temp, Logs, or local credentials.
Repo rules protect main.
Release tags use v*.
Known-good tags use kg/*.
A pre-migration kg tag exists before replacing old world registries.
```

### Recommended first checkpoint

```text
kg/20260606-before-world-ruleset-migration
```

---

## Phase 1 — Quest VR movement and interaction foundation

### Deliverable

A Quest-playable scene where the player can move, turn, teleport, point, use VR menus, and target blocks comfortably.

### Scope

```text
Quest 3/3S controller bindings
Unity Input System
OpenXR Meta tracking
TrackedPoseDriver with before-render tracking
XR Interaction Toolkit ray interactors
Teleport locomotion
Snap turn
Optional smooth turn with comfort setting
Height reset
Controller haptics abstraction
World-space VR menus using XRI UI input
Suppress block editing while UI is targeted
Dominant-hand block editing toggle
Void safety handling
```

### Reuse guidance

```text
Keep the refined movement implementation if it remains stable.
Keep native XRI UI interaction where it works.
Keep controller ray-based block targeting.
Do not preserve temporary debug UI as final menu design unless it is converted to the menu spec.
```

### Tests

```text
EditMode: input actions exist and required bindings load.
PlayMode: rig spawns safely and menus are reachable.
PlayMode: UI input and block editing do not conflict.
PlayMode: teleport, snap turn, height reset, and dominant-hand editing toggle work.
Manual Quest: launch APK, controllers tracked, menus interactable, movement comfortable.
```

### Validation

```text
Quest 3 and Quest 3S can launch to a playable VR scene.
Player can move and turn without forced smooth locomotion.
Menus are readable and interactable.
Block targeting works without interfering with UI.
```

---

## Phase 2 — Canonical voxel core and registries

### Deliverable

The game loads canonical block, item, tool, resource, terrain, crop, structure, environment, and audio/VFX IDs from ruleset-defined registries.

### Scope

```text
Stable string IDs for canonical content
Numeric runtime IDs generated from registry snapshots
Block categories: air, terrain, stone, resource_node, plant, crafted, fluid
Item categories: terrain block, resource, tool, food, utility, station, container, fluid container
Tool classes: HAND, DELVER, SPADE, FELLER, SICKLE, MALLET, CARVER, TILLER
Harvest tiers 0-7
Canonical stack sizes
Canonical block hardness and drop rules
Legacy temporary ID migration table
Validation for duplicate IDs and missing mappings
```

### Canonical block/item direction

```text
Use the ruleset names such as:
Air
Worldroot
Deepmantle
Graystone
Dark Slate
Warm Granite
White Limestone
Black Basalt
Meadow Turf
Loose Loam
Rootsoil
Branchwood Log
Leafmoss
Reedgrass
Embercoal Seam
Rosycopper Bloom
Paletin Thread
Rustcore Ore
Lumen Quartz Cluster
Storage Crate
Build Table
Clay Kiln
Bellows Forge
```

### Migration policy

```text
Temporary validation names such as Loam, Slate, Timber, Leafmass, Clearstone, Coalstone, Copperstone, Ironstone, Workbench, and Torchbud are migrated to canonical equivalents or marked as legacy aliases.
New saves write only canonical IDs.
Old saves load through migration hooks.
```

### Tests

```text
Unit: registry rejects duplicate stable IDs.
Unit: all recipes reference valid items.
Unit: all block drops reference valid items.
Unit: all tool definitions reference valid materials and classes.
Unit: legacy temporary IDs migrate to canonical IDs.
Unit: registry snapshot hash changes when content changes.
```

### Validation

```text
Canonical registry loads in editor and player.
Temporary validation registries are not used for new worlds.
A kg checkpoint is tagged after canonical registry load and migration tests pass.
```

### Recommended checkpoint

```text
kg/20260606-canonical-registry-loaded
```

---

## Phase 3 — Canonical world generation

### Deliverable

The game generates canonical `survival_terrain`, `flat_builder`, and `void_builder` worlds from the ruleset-defined worldgen model.

### Scope

```text
Chunk size: 16
World min/max Y from rulesets
Bounded world size presets
Seeded deterministic generation
Biome temperature/moisture selection
Terrain layering
Surface blocks by biome
Caves
Fluids
Resource placement
Spawn-safe area
Worldroot bottom layer
Resource abundance tables
Biome modifiers
```

### World presets

```text
survival_terrain
  Ruleset-defined survival world with terrain, caves, resources, vegetation, environment hooks, and structures.

flat_builder
  Flat canonical creative world with full creative catalog.

void_builder
  Empty builder world with safety floor/spawn platform and explicit bounds.
```

### Tests

```text
Unit: same seed produces same world.
Unit: spawn is safe and has headroom.
Unit: caves do not carve through protected spawn area.
Unit: resources stay within configured Y ranges and valid host blocks.
Unit: biome selection is deterministic.
Integration: generated world contains terrain, stone, resource nodes, caves, and surface vegetation hooks.
Performance: default survival_terrain generates within desktop budget.
```

### Validation

```text
New game creates a canonical survival_terrain world.
Creative can start flat_builder or void_builder.
Old temporary generation presets are not used for new worlds.
Seed can be replayed.
```

### Recommended checkpoint

```text
kg/20260606-survival-terrain-generates
```

---

## Phase 4 — Chunk rendering, authored block visuals, and visual validation

### Deliverable

Canonical worlds render efficiently with original authored voxel art.

### Scope

```text
Chunk mesh builder
Visible-face generation
Transparent/air handling
Texture atlas lookup by canonical block ID
Authored block texture atlas
Point-filtered VR-readable textures
Dirty chunk rebuild queue
Mesh/collider rebuild throttling
Material validation
Debug chunk/stats overlay in development builds only
```

### Tests

```text
Unit: solid cube mesh has only exterior faces.
Unit: adjacent solid blocks remove internal faces.
Unit: transparent and non-solid blocks follow render rules.
Unit: atlas contains all renderable canonical blocks.
EditMode: missing or unrelated atlas fails validation.
PlayMode: generated canonical terrain renders without missing materials.
```

### Validation

```text
No magenta/missing-material blocks.
Canonical terrain blocks and resources are visually distinct in Quest.
Chunk rebuilds after block mutation.
```

---

## Phase 5 — Environment effects

### Deliverable

The world has deterministic day/night, lighting rules, cloud coverage, rain, thunderstorms/lightning, fog, and snow.

### Scope

```text
World time state
Day/night cycle
Sun/moon light intensity
Ambient light rules
Block light interaction
Cloud coverage
Rain
Thunderstorms
Lightning strike validation
Fog
Snowfall
Snow accumulation
Weather transitions
Environment presets: normal, clear_builder, storm_test, winter_test
Environment save state
Audio/VFX hooks for weather and time-of-day
```

### Tests

```text
Unit: world time advances deterministically.
Unit: light level changes match time-of-day curve.
Unit: weather transitions obey configured probabilities and cooldowns.
Unit: snow only accumulates in valid cold conditions.
Unit: lightning cannot strike protected/invalid targets.
Integration: environment state saves and loads.
```

### Validation

```text
Day/night visibly changes the world.
Rain and snow are readable but not overwhelming in VR.
Storms produce audio/VFX hooks.
Environment presets are selectable from world settings or debug tools.
```

---

## Phase 6 — Biome vegetation

### Deliverable

Canonical biomes contain vegetation according to the vegetation ruleset.

### Scope

```text
Tree variants
Groundcover
Reedgrass
Berrybushes
Grain stalks
Biome vegetation density
Saplings
Regrowth
Leaf decay
Plant harvesting
Weather interactions
Structure integration
Spawn vegetation guarantees
Creative vegetation placement tools
Vegetation save hooks
```

### Tests

```text
Unit: vegetation placement is deterministic per seed.
Unit: vegetation respects valid surface/biome/water rules.
Unit: spawn area remains safe.
Unit: regrowth timers save/load correctly.
Unit: leaf decay does not remove protected/generated structure blocks incorrectly.
```

### Validation

```text
Biomes read visually different.
Plants and trees provide correct resources.
Vegetation does not block spawn or essential paths.
```

---

## Phase 7 — Structure generation

### Deliverable

The canonical world includes deterministic generated structures and structure-aware persistence.

### Scope

```text
Structure templates
Placement validation
Terrain fitting
Structure categories
Path/connector rules
Loot tables
Containers and stations
Block masks
Structure IDs and instance IDs
Persistence hooks
Environment interaction hooks
Creative structure tools
```

### Initial structure targets

```text
Ruin
Waypost
Cabin shell
Cave shrine
Ore marker
Bridge/path segment
Small campsite
```

### Tests

```text
Unit: structures place only on valid terrain.
Unit: structure placement is deterministic.
Unit: no structure overlaps protected spawn unless explicitly allowed.
Unit: loot tables reference valid canonical items.
Unit: block masks protect intended content.
Integration: structure instances save/load and do not duplicate.
```

### Validation

```text
Generated structures appear naturally.
Structures use canonical blocks and containers.
Structure loot does not break progression.
```

---

## Phase 8 — Creative mode

### Deliverable

Creative mode implements the ruleset-defined catalog, placement/removal, world tools, menus, and save behavior.

### Scope

```text
Unlimited catalog access
Creative hotbar
Block picker
Search/category filter
Placement preview
Break/place/remove
Undo/redo
World edit tools
Structure tools
Vegetation tools
Environment controls
Time/weather controls
No survival resource consumption
No durability loss
Optional creative flight only if comfort-tested and explicitly enabled
VR-safe build limits
Canonical save metadata
```

### Tests

```text
Unit: creative placement uses canonical block IDs.
Unit: cannot place outside world bounds.
Unit: cannot place into player collision volume unless no-clip/admin mode is enabled.
Unit: undo/redo restores block state.
PlayMode: raycast placement and removal work in VR.
PlayMode: creative catalog menu selects canonical items.
```

### Validation

```text
Player can build in flat_builder and void_builder.
Player can edit survival_terrain in creative mode when allowed.
Menus match the menu spec.
Creative saves use canonical schema.
```

### Recommended checkpoint

```text
kg/20260606-creative-build-loop-canonical
```

---

## Phase 9 — Survival resources, tools, inventory, and crafting

### Deliverable

Survival mode implements mining, tools, drops, inventory, crafting, containers, stations, farming, and progression according to the survival ruleset.

### Scope

```text
Mining formula
Harvest tiers
Tool classes
Tool speed
Tool durability
Drop tables
Resource abundance
Inventory slots and stack sizes
Hotbar
Containers
Crafting stations
Fuel values
Kiln and forge logic
Recipe registry
Farming/tended soil
Crop growth
Repair/mending
Food and recovery items
```

### Initial progression

```text
Gather surface pebbles, reed fiber, branchwood, and flint.
Craft poles, cord, simple tools, and build table.
Mine embercoal, rosycopper, and paletin.
Build clay kiln and bellows forge.
Create bronze, ironroot, deepsteel, and starforged progression.
```

### Tests

```text
Unit: mining time and harvest success follow block/tool rules.
Unit: durability cost applies correctly.
Unit: drops are deterministic under seeded test RNG.
Unit: inventory stack merge/split rules work.
Unit: crafting consumes correct inputs and returns containers where required.
Unit: station and fuel requirements validate correctly.
Unit: crop growth obeys light/water/soil rules.
```

### Validation

```text
Player can gather resources.
Player can craft tools.
Player can build stations.
Player can mine progressively deeper resources.
Player can store items and recover from basic damage/resource needs.
```

---

## Phase 10 — Menus and VR UI flows

### Deliverable

All major menus, actions, transitions, and state flows match the menu specification.

### Scope

```text
Main menu
New world
Load world
World settings
Mode selection
Pause menu
Inventory
Crafting
Creative catalog
Creative world tools
Environment controls
Structure/vegetation tools
LAN multiplayer menu
Reconnect/session-ended flow
Settings
Comfort
Audio
Controls
Save/delete confirmation
Error dialogs
```

### Tests

```text
EditMode: menu action handlers exist and route to expected services.
PlayMode: main menu can create and load worlds.
PlayMode: pause menu opens from VR controller input.
PlayMode: LAN host/join/stop actions update UI state.
PlayMode: UI suppresses world editing while targeted.
Manual Quest: all menus are readable and interactable.
```

### Validation

```text
User can start survival or creative mode.
User can select canonical world presets.
User can host/join LAN sessions.
User can recover from host disconnect.
User can save/load/delete worlds.
Comfort and audio settings persist.
```

---

## Phase 11 — Unified save/load, versioning, and migration

### Deliverable

Canonical worlds, players, inventories, environment state, structures, vegetation, and multiplayer host saves persist with versioning and migrations.

### Scope

```text
Save manifest
Ruleset version metadata
Registry snapshot hashes
World metadata
Dimension files
Chunk delta or chunk storage
Block states
Block entities
Inventories
Player state
Environment state
Structure state
Vegetation state
Autosaves
Manual saves
Atomic write
Backup and corruption recovery
Migration registry
Temporary ID migration to canonical IDs
LAN host-only save ownership
```

### Tests

```text
Unit: save/load reproduces canonical block changes.
Unit: registry snapshot mismatch is detected.
Unit: old temporary IDs migrate to canonical IDs.
Unit: corrupted save is detected and does not crash.
Unit: environment, structures, vegetation, and inventories persist.
Integration: terrain seed + changed chunks reconstruct world.
PlayMode: save/load returns player to expected state.
PlayMode: LAN host save persists shared edits.
```

### Validation

```text
Build a structure, save, quit, reload, and verify it persists.
Old temporary saves either migrate or fail with clear recovery messaging.
Host multiplayer saves include canonical metadata.
```

### Recommended checkpoint

```text
kg/20260606-save-schema-migration-stable
```

---

## Phase 12 — LAN multiplayer and canonical world sync

### Deliverable

Two Quest players can cooperatively play in the same canonical world over LAN.

### Networking model

```text
Host authoritative
Client requests only
Host validates commands
Host owns world generation, mutation validation, save state, environment simulation, structure generation, vegetation simulation, inventory/crafting validation, and survival resource state
Clients receive snapshots and deltas
Late joiners receive canonical world metadata and current changed state
```

### Scope

```text
LAN host
LAN join by IP
Session stop
Reconnect/session-ended UX
Meta Horizon avatars
Fallback proxy avatar
Head/controller pose sync
Block mutation requests
Chunk deltas
Late-join snapshots
Inventory snapshots
Shared containers
Crafting validation
Resource harvesting
Environment state sync
Structure/vegetation state sync
Host-only save ownership
Meta Quest party chat for voice
No in-app voice chat
No public matchmaking
```

### Tests

```text
PlayMode: host starts and client connects.
PlayMode: two simulated players spawn with unique IDs.
PlayMode: client block mutation is host-validated and broadcast.
PlayMode: stale competing edits reject deterministically.
PlayMode: late join receives current world state.
PlayMode: inventory/crafting/harvest commands are host-authoritative.
PlayMode: shared container snapshots stay synchronized.
PlayMode: environment/structure/vegetation state appears in sync payloads.
Network simulation: active block editing converges at 100ms latency.
Network simulation: ordered chunk deltas converge under packet loss.
Manual Quest: two headsets join same LAN session.
```

### Validation

```text
Two Quest players can join the same canonical world.
Both players see each other through Meta Horizon avatars or fallback proxies.
Both players can build and gather resources together.
World state stays synchronized through extended play.
Host save/load preserves shared edits.
Host disconnect produces clear client UX.
```

### Recommended checkpoint

```text
kg/20260606-lan-coop-canonical-world-sync
```

---

## Phase 13 — Audio, haptics, and VFX

### Deliverable

The game has original audio, haptics, and lightweight VR-readable VFX for interactions, UI, environment, structures, vegetation, and survival events.

### Scope

```text
Block break/place audio
Mining hits
Tool swing/contact sounds
Inventory open/close
Craft success/fail
UI select/confirm/cancel
Footsteps/landing
Weather ambience
Rain/snow/storm audio
Lightning audio/VFX
Campfire and torch audio/VFX
Block particles
Resource harvest particles
Crop/plant harvest particles
Structure placement/loot cues
Controller haptics
Audio settings
Volume categories
Object pooling for VFX
```

### Tests

```text
Unit: audio cue registry covers required events.
Unit: no missing clips for required cues.
Unit: VFX pool handles repeated block edits.
PlayMode: block mutation event triggers audio/haptics/VFX subscribers.
PlayMode: UI actions trigger UI cues.
Manual Quest: sounds are audible but not harsh.
Manual Quest: VFX are readable and do not harm frame rate.
```

### Validation

```text
Building loop has clear feedback.
Weather feels present without reducing comfort.
Important events are visible and audible.
All shipped audio is original and documented.
```

---

## Phase 14 — Art direction and asset polish

### Deliverable

The game has cohesive original block, item, UI, structure, vegetation, and environment art that is readable in Quest.

### Scope

```text
Block atlas
Item icons
Tool icons
Crafting station visuals
Container visuals
Structure block variations
Vegetation textures
Weather visual assets
UI panels
Branding assets
Store screenshots
Asset provenance
Texture import settings
Quest readability validation
```

### Asset policy

```text
16x16 source block tiles for initial voxel readability
64x64 item icons
Transparent PNG sprites for UI where appropriate
Point filtering for block texture clarity
No embedded text in icons
No protected third-party names or visual identities
```

### Tests

```text
Editor: all block textures match expected dimensions.
Editor: all registered renderable blocks map to atlas tiles.
Editor: no missing materials.
Editor: no forbidden asset names/references.
PlayMode: all registered blocks render with valid visuals.
```

### Validation

```text
World is visually readable in headset.
Block/resource families are easy to tell apart.
No placeholder magenta/missing material surfaces remain.
Art provenance is recorded.
```

---

## Phase 15 — Quest performance, diagnostics, and hardening

### Deliverable

Canonical survival/creative worlds meet Quest 3/3S comfort and performance targets.

### Performance budget

```text
Minimum target: stable 72 FPS
Optimization goal: 90 FPS where practical
No runaway mesh allocations
Bounded world sizes until profiling supports expansion
Chunk rebuilds are throttled
VFX are pooled
Development-only debug overlays hidden in release builds
```

### Scope

```text
Profiler markers
Frame statistics sampler
In-game development performance overlay
Chunk mesh pooling
Collider simplification
Texture atlas batching
Object pooling for VFX
Stress scenes
OVR Metrics captures
Thermal observation
Quest 3 validation
Quest 3S validation
Performance report templates
```

### Tests

```text
EditMode: mesh generation deterministic and bounded.
PlayMode: max configured world generates and meshes without exceptions.
PlayMode: high edit-rate scene remains stable.
Manual Quest: 10-minute survival_terrain run.
Manual Quest: 10-minute creative building run.
Manual Quest: two-player LAN run.
```

### Validation

```text
Quest 3 and 3S builds remain comfortable.
World generation and chunk rebuilds avoid extended hitches.
Multiplayer remains stable.
Performance reports are saved under docs/testing/performance/.
```

---

## Phase 16 — Release pipeline and sideload fallback

### Deliverable

Signed APKs are produced from `main`, attached to GitHub Releases, and installable on Quest.

### Scope

```text
Production Android keystore outside repo
GitHub Actions secret-based signing
Version code/version name automation
Release tags from main only
APK checksum
Symbols/log artifacts
Release notes template
Known issues
ADB/hzdb sideload validation path
```

### Release artifacts

```text
BlockiverseVR-v0.1.0-dev.apk
BlockiverseVR-v0.1.0-release.apk
BlockiverseVR-v0.1.0-symbols.zip
checksums.txt
CHANGELOG.md excerpt
test-results.zip
performance-summary.md
```

### Tests

```text
CI: release tag must be on main.
CI: signed release artifact exists.
CI: checksum is generated.
Smoke: APK installs on Quest.
Smoke: app launches to main menu.
Smoke: boot scene reaches playable state.
```

### Validation

```text
Tagging v0.x from main creates a GitHub Release.
APK can be sideloaded.
Version appears correctly in game.
Release notes include known issues.
```

---

## Phase 17 — Meta release channels and private testing

### Deliverable

The game is testable by invited users through Meta release channels.

### Scope

```text
Meta developer app
Package name
App ID
Meta Platform settings
Meta Horizon Avatar requirements
Entitlement requirements
Data Use Checkup requirements
Alpha/Beta/RC channels
Private tester invites
Family tester accounts if appropriate
Bug feedback flow
```

### Tests

```text
Store upload accepts APK.
Release channel install works.
Entitlement behavior is understood.
Avatar/platform requirements are documented.
Private tester can launch app.
Crash/log collection path documented.
```

### Validation

```text
At least one invited tester can install via release channel.
Known issues are tracked.
No GitHub-only sideload step is required for invited testing.
```

---

## Phase 18 — Meta Horizon Store / Early Access submission candidate

### Deliverable

A complete submission package for Meta review.

### Scope

```text
App metadata
Short description
Long description
Screenshots
Trailer/capture if available
Comfort rating notes
Privacy policy
Data usage declarations
Age/child-safety review
VRC checklist
Performance evidence
Content checklist
Store artwork
Support email/site
Known issues
Release notes
```

### Privacy posture for first public candidate

```text
No public chat
Voice uses Meta Quest party chat
No in-app voice chat, voice capture, or game-hosted voice transport
No user-generated text chat
No advertising SDK
No analytics beyond documented diagnostics unless explicitly added
Local saves remain local unless user shares them
LAN multiplayer exchanges local network connection data only for the active session
Meta Horizon Avatar/profile data use is disclosed when avatar integration ships
Cloud-hosted private worlds are later scope
```

### Tests

```text
Submission checklist complete.
VRC checklist complete.
Privacy policy link works.
Store images meet dimensions.
APK upload succeeds.
Release channel RC build matches submitted build.
Submitted build comes from a main release tag.
```

### Validation

```text
Submission can be sent to Meta without missing metadata.
If rejected, rejection reasons become tracked work.
If approved, release remains Early Access/Beta until save and multiplayer stability are proven.
```

---

## Phase 19 — Full survival expansion

### Deliverable

A deeper survival game with original NPCs/mobs, combat, armor, progression, and expanded multiplayer survival interactions.

### Scope

```text
Original passive creatures
Original hostile creatures
NPCs/mobs use original voxel character designs
Mob spawning
Mob pathfinding
Combat
Armor
Weapons/tools progression
Status effects
Difficulty settings
Biome expansion
Dungeon/cave points of interest
Advanced structures
Advanced crafting
Multiplayer combat sync
```

### Out of scope

```text
Minecraft mob names
Minecraft mob silhouettes
Creeper-like behavior identity
Meta Horizon avatars for NPCs/mobs
Public matchmaking
Marketplace/modding
```

### Tests

```text
Unit: spawn rules obey safe zones.
Unit: combat damage and armor formulas are deterministic.
PlayMode: hostile mob can navigate simple test arena.
PlayMode: combat damage syncs in multiplayer.
Performance: mob counts stay within Quest budget.
```

### Validation

```text
Survival mode feels distinct from creative.
Mobs and combat remain comfortable in VR.
NPCs/mobs are original.
Multiplayer remains stable.
```

---

## Phase 20 — Cloud-hosted persistent private worlds

### Deliverable

Cloud-hosted, owner-scoped private worlds persist when empty and can be joined by invited members over the internet. LAN remains available as a separate low-cost mode.

### Scope

```text
Cloud-hosted dedicated/private world runtime
Server-authoritative world simulation
Stable cloud world IDs
Owner identity
Durable member list
Owner-only invite code generation/regeneration
One active reusable invite code per world
Invite codes expire 48 hours after generation
Auto-accept invite redemption by default
Owner-approval-required invite mode
Pending member requests
Permanent membership until owner removal or self-removal
Owner management: remove, block, approve, deny, regenerate invite
Meta identity, entitlement, membership, and access-state validation on join
Cloud persistent storage
Save versioning and migration
Atomic writes
Restore validation
Corruption recovery
Idle spin-down with final save
On-demand spin-up
Session registry/routing
Lifecycle diagnostics
Cost and quota guardrails
Privacy/data-use/store documentation
```

### Out of scope

```text
Public matchmaking
Public/community world browser
Community world discovery
Public moderation/reporting surfaces
Marketplace or mod distribution
In-app voice chat
Members generating invite codes
Single-use per-recipient invite codes
Replacing LAN multiplayer
```

### Tests

```text
Unit: invite code generation prevents collisions.
Unit: expired invite codes are rejected.
Unit: membership persists after code expiration.
Unit: removed/blocked members cannot rejoin.
Integration: session registry routes authorized players.
Integration: cloud save/load restores terrain, structures, inventories, environment, vegetation, and metadata.
Integration: idle spin-down saves before teardown.
Integration: spin-up restores latest world state before admitting players.
Network simulation: edit/save/spin-down/reconnect remain stable under expected latency.
```

### Validation

```text
Owner can create a cloud private world.
Owner can share a 48-hour invite code.
Entitled signed-in redeemers become members in auto-accept mode.
Owner can approve/deny/remove/block members.
Cloud world saves when empty and spins down.
Authorized members reconnect and trigger spin-up.
Two Quest devices can resume a persisted world over the internet.
LAN mode still works without cloud dependencies.
```

---

# 10. Testing strategy

## EditMode / pure C# tests

```text
Voxel coordinates
Chunk storage
Block registry
Item registry
Tool registry
Recipe registry
World bounds
World generation determinism
Resource placement
Biome selection
Environment simulation
Structure placement
Vegetation placement/regrowth
Inventory
Crafting
Save/load serialization
Migration registry
Network command validation
Audio/VFX cue registries
Settings persistence
```

## PlayMode / Unity integration tests

```text
Boot scene
VR rig setup
World generation scene
Chunk rendering
Block break/place
Creative catalog UI
Inventory UI
Crafting UI
Save/load in scene
Environment visuals
Structure generation in scene
Vegetation in scene
Multiplayer host/client scene
Late join world sync
LAN session reconnect UX
Audio/haptics/VFX feedback
```

## Multiplayer tests

```text
Unity Multiplayer Play Mode local clients
Host/client lifecycle
Host-authoritative block mutation
Client correction/rejection
Late join
Inventory snapshots
Shared containers
Crafting validation
Resource harvesting
Environment sync
Structure/vegetation sync
100ms latency simulation
Packet loss resilience
Bandwidth measurement
Manual two-Quest LAN tests
```

## VR/device smoke tests

```text
Install APK on Quest 3
Install APK on Quest 3S
Launch app
Reach main menu
Start survival_terrain
Start flat_builder
Start void_builder
Move, turn, teleport
Open menus
Break block
Place block
Open inventory
Craft item
Save world
Reload world
Start LAN host
Join second headset
Build together for 5 minutes
Collect logs and performance data
```

## Performance tests

```text
Default survival_terrain
Large bounded survival_terrain
Flat builder edit-rate stress
Cave-heavy scene
Structure-heavy scene
Vegetation-heavy scene
Storm/weather scene
Two-player edit scene
10-minute thermal/performance run
```

## Store-readiness tests

```text
Signed release APK
Correct Android manifest
No debug keystore
No development build flag
Privacy policy present
VRC checklist complete
Comfort settings available
Performance capture archived
Known issues updated
Support contact filled
```

---

# 11. Implementation Outline

Use this as an implementation outline and planning aid. Do not file each row as a GitHub issue; create issues only for active bugs, blockers, validation gates, multi-PR initiatives, or follow-ups that need durable tracking.

## EPIC-00 — Repo, documentation, CI/CD, and checkpoint policy

```text
FEATURE: Roadmap and ruleset source of truth
  STORY: Move ruleset docs into repo docs/rulesets/
  STORY: Update roadmap to canonical ruleset-defined world
  STORY: Add implementation alignment matrix
  STORY: Add known-good git tagging policy

FEATURE: Trunk-based workflow
  STORY: Protect main
  STORY: Document release-from-main-only policy
  STORY: Document short-lived branch policy
  STORY: Add forbidden-files checks

FEATURE: Local Unity validation
  STORY: Keep scripts/unity/run-tests.sh as required local validation
  STORY: Keep development APK build script
  STORY: Document Quest hzdb validation path
```

## EPIC-01 — Quest VR foundation

```text
FEATURE: Controller input and locomotion
  STORY: Validate Quest controller input actions
  STORY: Preserve/refine teleport locomotion
  STORY: Preserve/refine snap turn and comfort menu
  STORY: Preserve/refine height reset
  STORY: Validate movement on Quest 3/3S

FEATURE: Native XRI interaction
  STORY: Use XRUIInputModule and TrackedDeviceGraphicRaycaster
  STORY: Suppress block editing over UI
  STORY: Preserve/refine controller ray block targeting
  STORY: Add dominant-hand editing toggle
```

## EPIC-02 — Canonical voxel data and registries

```text
FEATURE: Canonical block registry
  STORY: Add stable string IDs
  STORY: Add terrain/stone/resource/fluid/plant/crafted categories
  STORY: Add hardness/tool/tier/drop metadata
  STORY: Add registry snapshot hashing

FEATURE: Canonical item/tool registry
  STORY: Add resource, food, utility, station, container, tool definitions
  STORY: Add tool classes and harvest tiers
  STORY: Add stack size rules
  STORY: Add durability metadata

FEATURE: Temporary ID migration
  STORY: Map old temporary block names to canonical IDs
  STORY: Map old temporary item/tool names to canonical IDs
  STORY: Add save migration tests
```

## EPIC-03 — Canonical world generation

```text
FEATURE: World presets
  STORY: Implement survival_terrain
  STORY: Implement flat_builder
  STORY: Implement void_builder
  STORY: Remove temporary validation presets from new-world creation

FEATURE: Terrain/caves/resources
  STORY: Add deterministic height/biome generation
  STORY: Add terrain layering
  STORY: Add cave generation
  STORY: Add fluid placement
  STORY: Add resource placement table
  STORY: Add spawn safety
```

## EPIC-04 — Environment, vegetation, and structures

```text
FEATURE: Environment
  STORY: Add world time
  STORY: Add day/night lighting
  STORY: Add clouds, rain, storms, lightning, fog, snow
  STORY: Add environment save state
  STORY: Add environment audio/VFX hooks

FEATURE: Vegetation
  STORY: Add biome vegetation profiles
  STORY: Add tree/plant placement
  STORY: Add regrowth and leaf decay
  STORY: Add vegetation harvesting
  STORY: Add weather interactions

FEATURE: Structures
  STORY: Add template schema
  STORY: Add deterministic placement
  STORY: Add terrain fitting
  STORY: Add loot/container/station hooks
  STORY: Add structure save state
```

## EPIC-05 — Creative mode

```text
FEATURE: Creative catalog
  STORY: Add block/item category browser
  STORY: Add search/filter
  STORY: Add hotbar assignment
  STORY: Add unlimited placement mode

FEATURE: Creative tools
  STORY: Add placement/removal
  STORY: Add undo/redo
  STORY: Add world edit tools
  STORY: Add structure placement tools
  STORY: Add vegetation tools
  STORY: Add environment controls
```

## EPIC-06 — Survival systems

```text
FEATURE: Mining and drops
  STORY: Implement mining formula
  STORY: Implement harvest success rules
  STORY: Implement durability costs
  STORY: Implement drop tables and yield bonuses

FEATURE: Inventory and containers
  STORY: Implement inventory model
  STORY: Implement stack rules
  STORY: Implement pickup/merge/drop rules
  STORY: Implement storage containers

FEATURE: Crafting and stations
  STORY: Implement recipe registry
  STORY: Implement build table
  STORY: Implement clay kiln and fuel
  STORY: Implement bellows forge and alloys
  STORY: Implement mend bench repair
  STORY: Implement farming/crop growth
```

## EPIC-07 — Save/load and versioning

```text
FEATURE: Unified save schema
  STORY: Save manifests and metadata
  STORY: Save registry snapshot hashes
  STORY: Save chunks/block changes
  STORY: Save inventories
  STORY: Save environment, structure, and vegetation state
  STORY: Save player state and settings

FEATURE: Migration and recovery
  STORY: Add migration registry
  STORY: Add temporary ID migration
  STORY: Add atomic writes and backup recovery
  STORY: Add corrupted-save handling
```

## EPIC-08 — LAN multiplayer

```text
FEATURE: LAN session flow
  STORY: Host LAN world
  STORY: Join by IP
  STORY: Stop session
  STORY: Reconnect/session-ended UX
  STORY: Host-only save ownership

FEATURE: Player representation
  STORY: Meta Horizon avatar local/remote setup
  STORY: Fallback proxy avatar
  STORY: Head/controller pose sync

FEATURE: Host-authoritative world sync
  STORY: Block mutation requests
  STORY: Ordered chunk deltas
  STORY: Late-join snapshots
  STORY: Conflict rejection/correction
  STORY: Environment/structure/vegetation sync

FEATURE: Survival co-op sync
  STORY: Resource harvest commands
  STORY: Inventory snapshots
  STORY: Shared container snapshots
  STORY: Host-validated crafting
```

## EPIC-09 — Audio, VFX, haptics, and art polish

```text
FEATURE: Audio/haptics
  STORY: Break/place sounds
  STORY: Mining/tool sounds
  STORY: UI sounds
  STORY: Inventory/crafting sounds
  STORY: Weather ambience
  STORY: Haptic patterns

FEATURE: VFX
  STORY: Block particles
  STORY: Resource harvest particles
  STORY: Weather effects
  STORY: Lightning effects
  STORY: Campfire/torch effects
  STORY: VFX pooling

FEATURE: Art polish
  STORY: Canonical block atlas
  STORY: Item/tool icons
  STORY: UI panels
  STORY: Structure and vegetation visuals
  STORY: Quest readability validation
```

## EPIC-10 — Quest performance and store readiness

```text
FEATURE: Performance
  STORY: Profiler markers
  STORY: Frame statistics overlay
  STORY: Stress scenes
  STORY: Quest 3 capture
  STORY: Quest 3S capture
  STORY: Performance report

FEATURE: Release pipeline
  STORY: Signed APK from main v* tag
  STORY: Checksum and release artifact
  STORY: Store candidate workflow
  STORY: Release notes and known issues

FEATURE: Meta store
  STORY: Privacy policy
  STORY: Data use declarations
  STORY: Store listing
  STORY: Screenshots
  STORY: VRC checklist
  STORY: Private release channel validation
```

## EPIC-11 — Full survival expansion

```text
FEATURE: NPCs/mobs
  STORY: Original passive creature
  STORY: Original hostile creature
  STORY: AI and pathfinding
  STORY: Spawn rules

FEATURE: Combat/progression
  STORY: Weapons/tools progression
  STORY: Armor
  STORY: Damage/status effects
  STORY: Multiplayer combat sync
```

## EPIC-12 — Cloud private worlds

```text
FEATURE: Cloud architecture
  SPIKE: Choose hosting/runtime/provider model
  STORY: Define cost, quota, region, privacy guardrails
  STORY: Define server-authoritative runtime

FEATURE: Owner/member access
  STORY: Stable cloud world IDs
  STORY: Owner model
  STORY: Member list
  STORY: Invite codes
  STORY: Removal/block/approval flows

FEATURE: Cloud persistence
  STORY: Cloud save schema
  STORY: Spin-down final save
  STORY: On-demand spin-up
  STORY: Reconnect sync
```

---

# 12. Recommended build order

```text
1. Commit ruleset docs and updated roadmap.
2. Tag known-good state before canonical world migration.
3. Preserve/refine Quest VR movement, XRI UI, and block targeting.
4. Replace temporary registries with canonical registries.
5. Add migration aliases for old temporary IDs.
6. Generate survival_terrain.
7. Generate flat_builder and void_builder.
8. Render canonical blocks with authored atlas validation.
9. Add environment state and day/night/weather.
10. Add biome vegetation.
11. Add generated structures.
12. Implement creative catalog and tools.
13. Implement survival mining, inventory, tools, crafting, and farming.
14. Implement unified save/load and migrations.
15. Add LAN host-authoritative canonical world sync.
16. Add audio, haptics, and VFX.
17. Harden Quest performance.
18. Produce signed release candidate.
19. Validate through Meta release channels.
20. Prepare Store / Early Access submission.
21. Expand with mobs, combat, and deeper progression.
22. Add cloud private worlds.
```

---

# 13. Definition of done by milestone

## M0 — Repo, docs, and checkpoint policy

```text
Ruleset docs are committed.
Roadmap identifies rulesets as source of truth.
Known-good tag policy exists.
Pre-migration kg tag exists.
Validation commands are documented.
```

## M1 — Quest VR foundation

```text
Quest app launches.
Movement and turn work.
Controllers track reliably.
Menus are interactable through XRI UI.
Block targeting works.
Comfort settings persist.
```

## M2 — Canonical voxel core

```text
Canonical registries load.
Temporary IDs migrate.
New worlds write canonical IDs only.
Registry tests pass.
kg checkpoint exists.
```

## M3 — Canonical world generation

```text
survival_terrain generates.
flat_builder generates.
void_builder generates.
Resources, caves, spawn safety, and biome selection are deterministic.
Temporary validation world is not used for new worlds.
kg checkpoint exists.
```

## M4 — Environment, vegetation, structures

```text
Day/night and weather exist.
Biome vegetation exists.
Generated structures exist.
Environment, vegetation, and structure state save/load.
Audio/VFX hooks exist.
```

## M5 — Creative mode

```text
Creative catalog uses canonical IDs.
Player can build/remove/undo in VR.
World tools work.
Environment/structure/vegetation creative tools exist.
Creative saves use canonical schema.
kg checkpoint exists.
```

## M6 — Survival systems

```text
Mining/tool/durability/drop rules work.
Inventory and containers work.
Crafting stations and recipes work.
Farming/regrowth works.
Progression is playable.
```

## M7 — Save/load and migration

```text
Canonical save schema is stable.
Autosave/manual save work.
Corruption recovery works.
Temporary save data migrates or fails clearly.
LAN host save metadata is supported.
kg checkpoint exists.
```

## M8 — LAN multiplayer

```text
Two Quest devices can connect over LAN.
Players see avatars or fallback proxies.
Block edits sync.
Inventory/crafting/resource commands are host-authoritative.
Environment/structure/vegetation state syncs.
Host save/load works.
Host disconnect UX is clear.
kg checkpoint exists.
```

## M9 — Audio/VFX/art polish

```text
Required audio cues exist.
Haptics exist.
VFX are pooled and readable.
Weather ambience exists.
Art assets are original and Quest-readable.
No missing materials.
```

## M10 — Quest performance and store candidate

```text
Quest 3 and Quest 3S performance evidence exists.
Signed APK builds from main v* tag.
Meta release channel testing works.
Store metadata and privacy docs are ready.
Submission package is complete.
```

## M11 — Full survival expansion

```text
Original mobs/NPCs exist.
Combat and armor/progression exist.
Multiplayer survival remains stable.
Quest performance remains acceptable.
```

## M12 — Cloud private worlds

```text
LAN remains available.
Owner can create cloud private world.
Invite/member model works.
Cloud save/spin-down/spin-up works.
Authorized Quest users can resume persisted world over internet.
Privacy/cost/quota diagnostics are documented.
```

---

# 14. Source references to re-check before implementation

Re-check external references before implementation because pricing, SDKs, platform rules, and store requirements can change.

```text
Unity pricing and plan thresholds
Unity Personal terms
Meta Unity/OpenXR project setup
Meta XR SDK documentation
Meta Horizon Avatars documentation
Meta Platform SDK and Data Use Checkup requirements
Unity Input System documentation
XR Interaction Toolkit documentation
Netcode for GameObjects documentation
Unity Transport documentation
Meta Quest performance VRCs
Meta release channel and store submission requirements
Meta APK signing requirements
GitHub Actions release workflow guidance
GitHub protected branch/ruleset documentation
Minecraft usage guidelines
```
