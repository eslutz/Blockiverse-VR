# Blockiverse VR Execution Plan

**Working title:** Blockiverse VR
**Target platform:** Meta Quest 3 / Quest 3S
**Input:** Quest controllers only; no hand-tracking-only mode
**Engine:** Unity 6 Personal
**Language:** C#
**Primary gameplay scope:** Creative-mode voxel building + survival-lite with health, resources, crafting, inventory, terrain, and caves
**Later expansion:** Full survival mode with mobs and day/night
**Multiplayer:** Basic co-op for two players
**Player representation:** Use each player's Meta Horizon avatar for local and remote players; do not build a game-specific custom avatar creator
**NPC representation:** Use original blocky voxel characters for in-game NPCs/mobs; do not use player Meta Horizon avatars for NPCs
**World model:** Bounded test world first; no infinite terrain initially
**Primary release path:** Meta Horizon Store / Early Access / release channels
**Fallback release path:** Signed APKs through GitHub Releases for sideloading if store submission is blocked or delayed
**Repo model:** Public GitHub repo, trunk-based development, protected `main`, short-lived feature branches, releases from `main` only

---

## 1. Engine, licensing, and platform decision

Use **Unity 6 Personal + C# + URP + OpenXR + Unity OpenXR: Meta + Meta XR SDK + Netcode for GameObjects**.

Unity is the strongest fit for this project because:

- The project should use C#.
- Quest support is mature.
- Unity has first-party and widely adopted tooling for XR, Android builds, testing, and CI/CD.
- The personal/free tier is suitable for a personal project unless revenue/funding grows past Unity's threshold.

As of the sources referenced when this plan was created:

- **Unity Personal** is free for customers with up to **$200,000 USD in annual revenue and funding**.
- Above that, **Unity Pro** is required. Unity listed 2026 Pro pricing as **$2,310/year per seat prepaid yearly** or **$210/month per seat paid monthly**.
- Unity states the old Runtime Fee was canceled and does not apply to Unity 6 or any other Unity games.
- **Unreal Engine still exists** and is excellent, but it is not the best fit here because its primary development model is C++ and Blueprints rather than C#.
- For games, Unreal's current model is generally free until the product exceeds **$1M USD lifetime gross revenue**, then a **5% royalty applies only to gross revenue above $1M**.

Before any serious commercial release, re-check current Unity, Unreal, Meta, and store policy terms.

### Recommended technical stack

```text
Unity 6 Personal
C#
Universal Render Pipeline (URP)
OpenXR
Unity OpenXR: Meta
Meta XR Core SDK
Meta Avatars SDK when multiplayer player representation starts
Unity Input System
XR Interaction Toolkit, if it fits the final interaction model
Netcode for GameObjects
Unity Transport
Optional later: Unity Relay and Lobby for easier remote multiplayer
Meta XR Platform SDK when Meta Horizon Avatars, entitlement, identity, or store platform features are needed
```

### Why not Godot for this version?

Godot is attractive because it is fully free and open source. However, for this project's constraints — C#, standalone Quest support, CI/CD, test tooling, XR maturity, and store submission path — Unity is the lower-risk choice.

---

## 2. Naming, IP, and repo visibility

Use **Blockiverse VR** as the working title.

Avoid using **BlockCraft World** as the primary name. It is likely to be crowded with similar app names and could create brand/search confusion.

This project should be visually inspired by voxel sandbox games, but it must not copy Minecraft's protected identity. Do not use:

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

A blocky, colorful, voxel sandbox style is fine. The game should have original block names, item names, textures, UI, audio, creatures, and branding.

### Repo visibility recommendation

Use a **public GitHub repo** because showcasing the work matters.

Recommended licensing posture at the start:

```text
Repo visibility: public
Code license: source-available / All Rights Reserved initially
Third-party assets: only included if redistribution is allowed
Secrets/keystores/API credentials: never committed
```

A source-available public repo gives portfolio value while reducing the chance that someone can freely commercialize the code. The project can later switch to MIT, Apache-2.0, GPL, or another open-source license if desired.

---

## 3. Product target

### Platform

```text
Primary: Meta Quest 3 and Meta Quest 3S
Input: Quest controllers
Unsupported initially: hand-tracking-only mode, non-VR desktop mode, mobile, PC VR
```

### Gameplay scope

Initial playable product:

```text
Creative building
Bounded voxel terrain
Caves
Resources
Inventory
Crafting
Health
Basic survival-lite resource loop
Two-player co-op multiplayer
Save/load
Original placeholder-to-polished voxel art
```

### Player and character representation

Player identity and representation should come from Meta Horizon:

```text
Local player uses their Meta Horizon avatar
Remote multiplayer players are shown as their Meta Horizon avatars
No custom Blockiverse-only player avatar creator
No custom player profile/identity system unless required later for non-Meta platforms
Development-only fallback proxies are allowed before Meta Horizon Avatar integration is complete
```

In-world non-player characters remain part of Blockiverse's original voxel art direction:

```text
Friendly NPCs use original blocky voxel character designs
Hostile mobs use original blocky voxel creature designs
NPCs/mobs do not use Meta Horizon avatars
NPCs/mobs must not copy Minecraft-protected names, silhouettes, sounds, or behavior identities
```

Later expansion:

```text
Full survival mode
Mobs
Day/night cycle
Deeper progression
Combat
Biome expansion
More recipes
More multiplayer survival interactions
```

### Distribution path

Primary:

```text
Meta Developer Dashboard
Meta release channels
Private Alpha/Beta testing
Meta Horizon Store / Early Access submission
```

Fallback:

```text
Signed APKs through GitHub Releases
Personal sideloading
Private manual tester distribution where appropriate
```

The GitHub Release APK fallback should not be used to broadly bypass legitimate store review concerns such as safety, privacy, or policy violations.

---

## 4. Development principles

1. **Build vertical slices.** Each phase ends with a playable or verifiable deliverable.
2. **Keep core game logic in pure C#.** Voxel storage, world generation, inventory, crafting, save/load, and command validation should be testable without VR hardware.
3. **Treat VR performance as a feature.** Target stable 72 FPS minimum on Quest 3/3S, with 90 FPS as an optimization goal where feasible.
4. **Design for multiplayer early.** Even single-player block edits should use command objects that can later be synchronized over the network.
5. **Keep assets original.** The game can be blocky and voxel-based, but not a Minecraft asset or branding clone.
6. **Use platform identity for players.** Player bodies should be Meta Horizon avatars in multiplayer; reserve original voxel character design for NPCs and mobs.
7. **Use trunk-based development.** Keep `main` releasable. Use short-lived feature branches only. Cut releases from `main`.

---

# 5. GitHub repository setup

## Phase 0 — GitHub repo, governance, and roadmap foundation

### Deliverable

A public GitHub repository with repo rules, project board, labels, milestones, issue hierarchy, and first backlog.

### Create the repo

Use GitHub CLI:

```bash
gh auth login
gh auth refresh -s project

gh repo create <OWNER>/blockiverse-vr \
  --public \
  --description "VR voxel sandbox prototype for Meta Quest 3/3S built with Unity and C#." \
  --gitignore Unity \
  --clone
```

Create the GitHub Project:

```bash
cd blockiverse-vr

gh project create \
  --owner <OWNER> \
  --title "Blockiverse VR Roadmap"
```

### Repo files to add

```text
README.md
LICENSE.md
NOTICE.md
CONTRIBUTING.md
CODE_OF_CONDUCT.md
SECURITY.md
CHANGELOG.md
docs/
  architecture/
  adr/
  testing/
  store-submission/
  art-direction/
.github/
  ISSUE_TEMPLATE/
  workflows/
  pull_request_template.md
.gitattributes
.gitignore
```

### Unity-specific Git settings

Use Git LFS for binary assets.

`.gitattributes`:

```text
*.png filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.blend filter=lfs diff=lfs merge=lfs -text
*.fbx filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.apk filter=lfs diff=lfs merge=lfs -text

*.cs text eol=lf
*.asmdef text eol=lf
*.shader text eol=lf
*.mat text eol=lf
*.prefab text eol=lf
*.unity text eol=lf
*.asset text eol=lf
*.meta text eol=lf
```

### Branching and release model

Use **trunk-based development**.

Branches:

```text
main          protected, always releasable
feature/*     short-lived feature branches
fix/*         short-lived bug-fix branches
chore/*       short-lived maintenance branches
spike/*       short-lived exploratory branches
hotfix/*      short-lived urgent fixes, merged back to main quickly
```

Do **not** create or use a long-lived `develop` branch.

Do **not** use long-lived release branches.

Rules:

```text
All production releases are cut from main.
All release tags must point to commits on main.
Feature branches should stay small and short-lived.
Pull requests merge into main after CI passes.
main should always be playable or quickly repairable.
Use feature flags or disabled scenes for incomplete systems.
Use draft PRs for work in progress.
Prefer squash merge or rebase merge to keep main readable.
```

Recommended repository ruleset for `main`:

```text
Require pull request before merge
Require passing CI
Require Repository checks status
Require no unresolved review comments
Require linear history or squash merge
Disallow force pushes
Disallow branch deletion
Do not use classic branch protection for main
Do not require approving reviews or CODEOWNERS review while Eric is the only human maintainer
```

Release flow:

```text
1. Merge validated work to main.
2. Confirm main is green.
3. Create a version tag from the main commit:
   git tag v0.1.0
   git push origin v0.1.0
4. GitHub Actions verifies the tag is on main.
5. Release workflow builds, signs, tests, and uploads artifacts.
6. GitHub Release is created from the tag.
7. Optional: same signed artifact is uploaded to a Meta release channel.
```

### GitHub Project fields

Create fields:

```text
Status: Backlog, Ready, In Progress, In Review, Blocked, Done
Type: Epic, Feature, Story, Task, Bug, Tech Debt, Spike
Phase: 0-20
Priority: P0, P1, P2, P3
Area: Repo, Engine, VR, Voxel, Terrain, Creative, Survival, Multiplayer, Art, Audio, UI, CI/CD, Store, QA
Milestone: M0 Bootstrap, M1 VR Slice, M2 Creative, M3 Survival-Lite, M4 Art and Texture Assets, M5 Multiplayer, M6 Store Candidate, M7 Full Survival
Risk: Low, Medium, High
Target Release: Prototype, Alpha, Beta, RC, Store
Effort: XS, S, M, L, XL
```

### Validation

This phase is done when:

```text
Repo is public.
No develop branch exists.
main is protected.
Project exists.
Labels, milestones, fields, and issue templates exist.
At least one epic issue exists for every roadmap phase.
README explains project goals, target headset, engine, and license.
Branch policy says releases are cut from main only.
No secrets or generated keystores are present in git history.
```

---

# 6. Suggested GitHub epics

Create these as parent issues, then add feature/story sub-issues beneath them.

```text
EPIC-00 Repo, project management, CI/CD foundation
EPIC-01 Unity/Quest project bootstrap
EPIC-02 VR player controller and interaction foundation
EPIC-03 Voxel data model and block registry
EPIC-04 Bounded terrain, caves, and resources
EPIC-05 Chunk rendering and mesh optimization
EPIC-06 Creative-mode building loop
EPIC-07 Inventory, hotbar, resources, and crafting
EPIC-08 Survival-lite health and resource loop
EPIC-09 Save/load and world versioning
EPIC-10 Multiplayer co-op foundation
EPIC-11 Multiplayer world synchronization
EPIC-12 Art generation, audio, and UI polish
EPIC-13 Performance, profiling, and Quest validation
EPIC-14 Meta release pipeline and store readiness
EPIC-15 Full survival expansion: mobs, day/night, progression
```

Each issue should include:

```text
Goal
Player-facing outcome
Technical scope
Out of scope
Acceptance criteria
Test plan
Manual validation steps
Dependencies
```

---

# 7. Roadmap overview

| Milestone | Goal | Result |
|---|---|---|
| M0 Bootstrap | Repo, Unity project, CI, basic Quest build | Project is buildable and testable |
| M1 VR Slice | Player can stand in VR, move, point, select, and interact | Quest 3/3S vertical slice |
| M2 Creative | Bounded voxel world, break/place blocks, save/load | Playable creative prototype |
| M3 Survival-Lite | Terrain, caves, resources, inventory, crafting, health | Solo survival-lite loop |
| M4 Art and Texture Assets | Authored art/texture assets, renderer integration, provenance, and Quest visual validation | Visually readable world using original committed assets |
| M5 Multiplayer | Two-player join/edit foundation | Father/daughter co-op |
| M6 Store Candidate | Performance, privacy, signing, release channels, metadata | Meta submission candidate |
| M7 Full Survival | Original voxel NPCs/mobs, day/night, hostile encounters, progression | Later expansion |

---

# 8. Phase-by-phase execution plan

## Phase 1 — Unity project bootstrap

### Deliverable

A Unity 6 project that opens cleanly, targets Quest 3/3S, and has a minimal empty VR scene.

### Scope

Use:

```text
Unity 6.1+ / current Unity 6 LTS
Universal 3D / URP template
C#
OpenXR
Unity OpenXR: Meta
Meta XR Core SDK
Meta Avatars SDK later, when multiplayer player representation starts
Meta XR Platform SDK later, when Meta Horizon Avatars, entitlement, identity, or store platform features are needed
Input System
XR Interaction Toolkit, if it fits the controller interaction model
```

### Repo structure

```text
Assets/
  Blockiverse/
    Art/
    Audio/
    Materials/
    Prefabs/
    Scenes/
    Scripts/
      Core/
      Voxel/
      WorldGen/
      Gameplay/
      VR/
      Networking/
      Persistence/
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
Blockiverse.Gameplay
Blockiverse.Persistence
Blockiverse.VR
Blockiverse.Networking
Blockiverse.UI
Blockiverse.Editor
Blockiverse.Tests.EditMode
Blockiverse.Tests.PlayMode
```

### Tests

```text
EditMode: empty sanity test passes.
PlayMode: Boot scene loads without errors.
Build smoke: Android/Quest build profile can produce a development APK.
```

### Validation

```text
Unity opens without package errors.
Boot scene loads.
XR rig exists.
Quest build profile is selected.
A development APK can be built.
CI test job runs at least one EditMode and one PlayMode test.
```

---

## Phase 2 — CI/CD quality gates

### Deliverable

GitHub Actions validates pull requests and keeps release/store artifact workflow contracts in place. Development APK smoke builds are produced locally while the project uses Unity Personal.

### Preferred approach

Use **GitHub-hosted runners** for CI by default. Local developer machines can keep globally installed Unity tooling for convenience, but GitHub Actions should not depend on Eric's workstation or a self-hosted runner.

Use a hybrid validation model while the project is on Unity Personal:

- GitHub-hosted runners validate repository policy, shell syntax, forbidden files, and release workflow contracts.
- Unity tests and development APK build smoke checks run locally with Unity Hub Personal and globally installed developer tooling.
- Pull requests that touch Unity behavior must include local validation evidence before review or merge.

Hosted Unity test/build jobs can be added later through a separate issue if the project adopts a CI-compatible Unity license, Unity Build Automation, or a self-hosted runner with an accepted local license.

### Workflows

```text
.github/workflows/ci-pr.yml
  Runs on pull requests targeting main
  Validates repository shell script syntax
  Runs forbidden tracked-file checks
  Verifies release policy docs exist

.github/workflows/release-apk.yml
  Runs on tags v*
  Verifies the tag commit is contained in main
  Builds signed APK
  Creates GitHub Release
  Uploads APK, symbols, changelog, checksum

.github/workflows/store-candidate.yml
  Manual only
  Requires selected commit/tag from main
  Builds signed release APK using production keystore
  Runs store-readiness checklist
```

### Secrets

```text
ANDROID_KEYSTORE_BASE64
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_ALIAS
ANDROID_KEY_PASSWORD
META_APP_ID
UNITY_CLOUD_PROJECT_ID
```

Current GitHub Actions workflows do not use `UNITY_LICENSE`, `UNITY_EMAIL`, or `UNITY_PASSWORD`. Local Unity validation uses the Unity Personal license accepted in Unity Hub on the developer machine.

### Tests

```text
CI fails if release tag is not on main.
CI fails if forbidden files are committed: keystore, .env, credentials, local Unity Library, Temp, Logs.
CI fails if tracked shell scripts have syntax errors.
Local validation fails if Unity tests fail.
Local validation fails if the project cannot build a development APK.
```

### Validation

```text
Opening a PR to main runs GitHub-hosted repository checks.
Before review or merge, run scripts/unity/run-tests.sh locally and post the result.
Before review or merge, run scripts/unity/build-development-apk.sh /tmp/blockiverse-vr-development.apk locally and post the result.
Tagging v0.1.0 from main creates a GitHub Release with a signed APK.
Tagging from a non-main commit fails.
No production signing occurs on PRs from forks.
```

---

## Phase 3 — VR controller locomotion and interaction foundation

### Deliverable

A Quest-playable scene where the player can move, rotate, point at blocks/placeholders, and use controller buttons.

### Scope

```text
Quest 3/3S controller bindings
Standing/seated calibration
Snap turn
Smooth turn option, disabled by default
Teleport locomotion for comfort
Optional smooth locomotion later
Dominant-hand ray pointer
Non-dominant-hand quick menu
Controller haptics abstraction
Comfort vignette option if smooth locomotion is enabled
```

### Tests

```text
EditMode: input action asset loads and required actions exist.
PlayMode: fake input can trigger select, grab, menu, jump/teleport.
PlayMode: player rig spawns at safe origin.
Manual Quest smoke: launch APK, controllers tracked, buttons respond, no hand tracking required.
```

### Validation

```text
On Quest 3/3S, player can move and turn.
Both controllers are tracked.
Ray pointer can highlight a test cube.
Menu button opens/closes debug menu.
No hand tracking dependency exists.
```

---

## Phase 4 — Pure C# voxel core

### Deliverable

A testable voxel world model independent of Unity scenes.

### Scope

```text
BlockId value type
BlockDefinition registry
Block categories: air, terrain, organic, crafted, resource
Chunk coordinate system
Bounded world dimensions
Voxel storage
Block read/write API
Block mutation commands
Deterministic random seed handling
Basic event stream for block changes
```

### Initial block set

Use original names:

```text
Air
Meadow Turf
Loam
Slate
Timber
Leafmass
Clearstone
Coalstone
Copperstone
Ironstone
Workbench
Torchbud
Storage Crate
```

### Tests

```text
Unit: block registry rejects duplicate IDs.
Unit: chunk coordinates map correctly for positive/negative local positions.
Unit: setting/getting blocks works across chunk boundaries.
Unit: world bounds reject invalid coordinates.
Unit: mutation commands are reversible where needed.
Unit: same seed produces same initial world metadata.
```

### Validation

```text
All voxel logic tests run without Unity scene loading.
A debug console command can create a bounded empty world.
World dimensions are visible in debug UI.
```

---

## Phase 5 — Bounded terrain and cave generation

### Deliverable

A bounded world with terrain, caves, trees, and resource deposits.

### Scope

```text
Default test world: 128 x 64 x 128 blocks
Chunk size: 16 x 16 x 16 or 16 x 64 x 16 after profiling
Seeded heightmap
Basic biome bands
Caves using noise/carving
Resource veins
Spawn-safe area
Flat test world preset
Debug terrain regeneration menu
```

### Tests

```text
Unit: same seed generates identical terrain.
Unit: spawn area is always non-lethal and has walkable space.
Unit: caves do not exceed world bounds.
Unit: resource distribution stays within configured ranges.
Integration: generated world contains air, surface, underground, and resource blocks.
Performance: generation for default world completes under target time on desktop.
```

### Validation

```text
Player spawns in a generated bounded world.
Terrain has visible height variation.
Caves exist and are reachable.
Resources are present.
A deterministic seed can be shared/replayed.
```

---

## Phase 6 — Chunk mesh rendering

### Deliverable

The generated voxel world renders efficiently as chunk meshes.

### Scope

```text
Chunk mesh builder
Face culling for hidden faces
Texture atlas lookup
Simple material set
Chunk dirty flag system
Mesh regeneration queue
Debug wireframe/chunk boundary toggle
LOD excluded for now
Greedy meshing as optimization if needed
```

### Tests

```text
Unit: solid cube mesh has only exterior faces.
Unit: adjacent solid blocks remove internal faces.
Unit: transparent/air blocks do not render as solid faces.
Integration: mutating one block marks only affected chunks dirty.
PlayMode: generated terrain scene renders without exceptions.
```

### Validation

```text
Bounded world renders on desktop and Quest.
Chunk rebuild happens after block mutation.
Debug overlay shows chunk count, triangle count, and rebuild queue.
```

---

## Phase 7 — Creative-mode block interaction loop

### Deliverable

Playable creative mode: select block type, break block, place block, undo last action.

### Scope

```text
Raycast block targeting
Face-normal placement
Break/place controller bindings
Creative hotbar
Block picker menu
Placement preview ghost
Range limit
Undo/redo command history for local play
Haptic feedback
Basic sound placeholders
```

### Tests

```text
Unit: placement computes correct adjacent coordinate from hit face.
Unit: cannot place outside world bounds.
Unit: cannot place into player collision volume.
Unit: undo restores previous block state.
PlayMode: fake raycast can break/place expected block.
PlayMode: hotbar selection changes active block.
Smoke: Quest player can build a small structure.
```

### Validation

```text
Player can build, remove, and change block types in VR.
Placement preview matches final block location.
No accidental self-trapping without a warning/escape.
Creative mode can be tested in under two minutes.
```

---

## Phase 8 — Save/load and world versioning

### Deliverable

Worlds can be saved, loaded, deleted, and versioned.

### Scope

```text
World metadata
Seed
Changed block delta storage
Inventory state
Player spawn transform
Versioned save schema
Migration hooks
Autosave
Manual save
Corruption-safe write: temp file then atomic replace
Debug save browser
```

### Tests

```text
Unit: save then load reproduces block changes.
Unit: corrupted save is detected and does not crash boot.
Unit: old schema migrates to current schema.
Integration: terrain seed + deltas reconstruct world.
PlayMode: save/load returns player to expected world state.
```

### Validation

```text
Build a small structure.
Save.
Quit.
Reload.
Structure persists.
World version is displayed in debug menu.
```

---

## Phase 9 — Inventory, hotbar, resources, and crafting

### Deliverable

Survival-lite resource loop: collect resources, store items, craft basic blocks/tools.

### Scope

```text
Inventory slots
Stacking rules
Hotbar
Item definitions
Resource drops from blocks
Crafting recipes
Workbench UI
Storage crate
Basic tools: hand, chipper, mallet, pick
Durability optional
Creative inventory remains separate
```

### Tests

```text
Unit: item stacks merge/split correctly.
Unit: inventory rejects over-capacity items.
Unit: crafting consumes correct inputs and produces expected outputs.
Unit: invalid recipes fail safely.
Integration: breaking block grants configured drop.
PlayMode: controller menu can select and craft item.
```

### Validation

```text
Player can collect Timber/Slate/etc.
Player can craft a Workbench.
Player can craft and place a Storage Crate.
Player can switch between creative and survival-lite test modes.
```

---

## Phase 10 — Survival-lite health and resource loop

### Deliverable

A non-combat survival-lite mode with health, stamina/energy optional, fall damage optional, and basic recovery.

### Scope

```text
Health
Damage events
Fall damage, optional after comfort testing
Food/energy, optional
Healing item
Safe respawn
Resource scarcity tuning
No hostile mobs yet
No day/night yet
```

### Tests

```text
Unit: damage cannot reduce health below zero.
Unit: healing respects max health.
Unit: respawn restores player to safe spawn.
Integration: fall/damage event updates health UI.
PlayMode: death/respawn flow works without soft-lock.
```

### Validation

```text
Player can lose health.
Player can recover health.
Player can respawn safely.
No full survival mobs or day/night are included yet.
```

---

## Phase 10.5 — Early visual differentiation and generated creative terrain

### Deliverable

The current headset validation build renders generated terrain with distinct original block visuals before multiplayer work begins, then the M4 Art and Texture Assets milestone replaces validation-only runtime visuals with committed authored texture assets and fail-fast atlas validation.

### Scope

This is a moved-up art subset of EPIC-12 so in-headset validation is not blocked by flat terrain or indistinguishable placeholder blocks.

```text
Generated survival-lite terrain remains the default validation world.
Creative editing still works against generated terrain.
Renderable block types use distinct original visual treatments.
Committed authored texture assets become the default rendering source once created.
Missing or unrelated atlas textures fail validation instead of falling back to generated runtime visuals.
The first block visual pass is documented in the art direction and provenance logs.
The implementation must not use copied Minecraft textures, names, prompts, or references.
Audio and haptics remain later EPIC-12 art/audio/UI polish work.
Store screenshots and final store polish remain EPIC-14/M6 Store Candidate work.
```

### Tests

```text
EditMode: creative validation world uses survival-lite terrain settings.
EditMode: renderable blocks have distinct atlas tiles.
EditMode: chunk mesh UVs use block-specific atlas coordinates.
PlayMode: Boot scene still loads with the XR rig and rendered world.
```

### Validation

```text
In headset, the player should not spawn onto a flat-only world.
Terrain height variation should be visible around spawn.
Meadow Turf, Loam, Slate, Timber, Leafmass, ores, Workbench, Storage Crate, and Torchbud should be easier to tell apart.
Creative break/place validation still works on the generated world.
```

---

## Phase 11 — Multiplayer architecture foundation

### Deliverable

Two players can join the same test scene, see each other, and move around.

### Networking choice

Use **Netcode for GameObjects** with Unity Transport.

### Cost strategy

Start with:

```text
LAN host/client for home play: lowest cost
Relay/Lobby later for remote play or easier join codes
```

### Scope

```text
Host-authoritative model
One player hosts
Second player joins by LAN/IP first
Join code via Relay later
Networked Meta Horizon player avatar
Head/controller transform sync
Player display name/identity from Meta platform data where allowed
Development-only fallback proxy while Meta Horizon Avatar integration is unavailable
Disconnect handling
No public matchmaking initially
Private voice chat for basic co-op
No open text chat initially
No game-specific custom avatar creator
```

### Tests

```text
EditMode: network message DTOs serialize/deserialize.
PlayMode: host starts successfully.
PlayMode: client connects to host.
PlayMode: two simulated players spawn with unique IDs.
PlayMode: disconnect cleans up player object.
PlayMode: avatar sync layer tolerates missing Meta Horizon Avatar data by using a development fallback proxy.
Manual: two Quest devices join same LAN session.
Manual: each Quest user sees the other player as that user's Meta Horizon avatar.
```

### Validation

```text
You and your daughter can stand in the same bounded test scene.
Each player sees the other player's Meta Horizon avatar.
Disconnect/rejoin does not crash the host.
```

---

## Phase 12 — Multiplayer voxel synchronization

### Deliverable

Two players can cooperatively break/place blocks, and the world stays synchronized.

### Scope

```text
Networked block mutation commands
Server/host validates world bounds and placement rules
Client prediction for local placement preview
Authoritative correction
Chunk delta broadcast
Late-join world state sync
Simple conflict handling
Multiplayer save belongs to host
```

### Tests

```text
Unit: block mutation command is deterministic.
Unit: invalid remote placement is rejected.
Integration: command log replay reconstructs world.
PlayMode: client places block, host and second client observe same block.
PlayMode: late joiner receives current world deltas.
PlayMode: simultaneous edits resolve predictably.
```

### Validation

```text
Two players can build one shared structure.
Breaking/placing remains synchronized after 100+ edits.
Late join shows current world state.
Host save/load preserves multiplayer edits.
```

---

## Phase 13 — Multiplayer inventory and crafting

### Deliverable

Survival-lite co-op works with basic inventory/resource/crafting synchronization.

### Scope

```text
Per-player inventory
Host-authoritative item grants
Shared world resources
Optional shared storage crate
Crafting validation on host
No item trading UI initially
No economy
```

### Tests

```text
Unit: item grant command validates source block/resource.
Unit: crafting cannot duplicate items.
Integration: shared crate state syncs to both clients.
PlayMode: player A mines resource; only A receives item unless using shared crate.
PlayMode: crafting result appears consistently.
```

### Validation

```text
Two players can gather resources.
Each has a separate inventory.
Shared storage crate works.
Crafting does not duplicate or lose items under normal latency.
```

---

## Phase 14 — Full art asset generation pipeline

### Deliverable

The moved-up block readability pass expands into a coherent voxel art, item, UI, audio, and haptics style.

The first block visual pass now begins in the M4 Art and Texture Assets milestone for validation readability. That milestone is responsible for committed original block texture assets, renderer integration, provenance, asset validation, and Quest headset visual validation. Phase 14 remains responsible for broader production-ready item icons, UI panels, audio, haptics, store-facing polish, and any later replacement or refinement of reviewed assets.

### Art direction

Keep the feel readable and blocky, but establish a distinct identity:

```text
Softer, toy-like block edges
Brighter "storybook explorer" palette
Original block names
Original icons
Original UI panels
No Minecraft textures, sounds, mobs, screenshots, music, logos, font, or item names
```

### Asset pipeline

```text
docs/art-direction/style-guide.md
docs/art-direction/prompt-log.md
Assets/Blockiverse/Art/Textures/Blocks/
Assets/Blockiverse/Art/Textures/Items/
Assets/Blockiverse/Art/Sprites/UI/
Assets/Blockiverse/Audio/SFX/
Assets/Blockiverse/Materials/
```

`BlockVisualAtlas` owns atlas tile layout and validation only. Production-facing development builds must use committed authored texture assets, and missing or unrelated atlas textures should fail fast before store-candidate validation.

### Generated asset rules

For AI-generated assets:

```text
Store the prompt.
Store the generation date/tool.
Store post-processing steps.
Never prompt for "Minecraft texture," "Creeper," "Steve," "Enderman," etc.
Prefer "original voxel sandbox texture, 16x16, colorful, readable, VR-friendly."
Keep source images layered when possible.
```

### First asset set

```text
Blocks:
  Meadow Turf
  Loam
  Slate
  Clearstone
  Timber
  Leafmass
  Coalstone
  Copperstone
  Ironstone
  Workbench
  Storage Crate
  Torchbud

Items:
  Timber Chunk
  Slate Shard
  Copper Nugget
  Iron Nugget
  Workbench Kit
  Crate Kit
  Chipper Tool
  Pick Tool

UI:
  Hotbar frame
  Selected slot
  Health pips
  Inventory panel
  Crafting panel
  Multiplayer status badge
```

### Tests

```text
Editor test: all block textures are expected dimensions.
Editor test: all BlockDefinitions reference valid texture atlas entries.
Editor test: no missing materials.
Editor test: no forbidden asset names or Minecraft references.
Editor test: authored atlas texture name and dimensions are validated.
Editor test: missing or unrelated atlas textures are rejected.
PlayMode: all registered blocks render with non-placeholder textures.
```

### Validation

```text
Game looks cohesive.
All placeholder magenta/missing materials are gone.
Art remains original and documented.
```

---

## Phase 15 — Audio and comfort polish

### Deliverable

The game has basic feedback, comfort settings, and readable VR UI.

### Scope

```text
Block break/place sounds
Footstep placeholders
Inventory/crafting UI sounds
Controller haptics
Comfort menu
Height reset
Dominant-hand setting
Snap turn angle setting
Subtitles/captions for important sounds if needed
Large VR-readable UI
```

### Tests

```text
Unit: settings persist.
PlayMode: comfort settings apply to locomotion provider.
PlayMode: audio events fire for break/place/craft.
Manual Quest: UI readable at headset resolution.
Manual Quest: no forced smooth locomotion.
```

### Validation

```text
Player can adjust comfort settings.
UI can be read in headset.
Building loop has clear feedback.
No required audio is missing.
```

---

## Phase 16 — Performance and Quest hardening

### Deliverable

A Quest 3/3S build that meets the internal performance budget.

### Performance budget

```text
Internal minimum: stable 72 FPS
Store/VRC floor: must not drop below required Meta thresholds
Memory: no runaway chunk mesh allocations
World size: bounded until profiling proves larger worlds are safe
Chunk rebuilds: throttled
Draw calls/materials: monitored
```

### Scope

```text
Chunk mesh pooling
Mesh rebuild throttling
Texture atlas batching
Physics collider simplification
Object pooling for UI/effects
Profiler markers
In-game debug stats panel
OVR Metrics manual test pass
Thermal throttling observation
```

The local diagnostics logging foundation is pulled forward into M4/Alpha as issue #225. Full profiler markers, in-game stats panels, OVR Metrics captures, stress scenes, and formal performance reports remain in M6.

### Tests

```text
EditMode: mesh builder allocation regression checks where practical.
PlayMode: stress scene generates max test world without exceptions.
Performance smoke: 10 minutes in generated world, no memory growth spike.
Manual Quest: OVR Metrics capture for creative, cave, and multiplayer scenes.
```

### Validation

```text
Quest 3 and Quest 3S builds remain comfortable in normal play.
World generation and chunk rebuilds do not cause extended hitches.
Two-player session remains stable.
Performance report is saved under docs/testing/performance/.
```

---

## Phase 17 — Release pipeline and sideload fallback

### Deliverable

Signed APKs are produced consistently from `main`, attached to GitHub Releases, and installable on Quest.

### Scope

```text
Production Android keystore
Keystore stored outside repo
GitHub Actions secret-based signing
Version code/version name automation
Changelog generation
APK checksum
Symbols/logs artifact
ADB install instructions for personal sideload testing
Release notes template
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

### Release-from-main requirements

```text
Release tags must match v*.
Release tags must point to a commit reachable from origin/main.
Release workflow must fail if tag is not on main.
Release workflow must fetch full history so ancestry checks work.
Production signing only happens for approved release workflow contexts.
```

Example ancestry check:

```bash
git fetch origin main --depth=1
git merge-base --is-ancestor "$GITHUB_SHA" origin/main
```

For a tag workflow, use a full fetch or fetch enough history to verify ancestry reliably.

### Tests

```text
CI: release build signs successfully.
CI: APK artifact exists and checksum is generated.
CI: release tag on non-main commit is rejected.
Smoke: APK installs on Quest via ADB.
Smoke: app launches to main menu.
Smoke: boot scene reaches playable state.
```

### Validation

```text
Tagging v0.x from main creates a GitHub Release.
APK can be sideloaded.
APK version appears correctly in game.
Release notes include known issues.
```

---

## Phase 18 — Meta release channels and private testing

### Deliverable

The game is testable by invited users through Meta release channels before public submission.

### Scope

```text
Create Meta developer app
Configure package name
Configure app ID
Configure Meta Platform settings required for Meta Horizon Avatars
Complete or explicitly defer Data Use Checkup requirements for any Platform SDK APIs used by avatars, identity, entitlement, voice, or multiplayer platform features
Upload signed build from a main release tag
Create Alpha/Beta/RC channels
Invite private testers
Add family tester accounts if appropriate
Collect feedback
Track bugs in GitHub
```

### Tests

```text
Store upload accepts APK.
Alpha channel installs on Quest.
Entitlement behavior is understood.
Meta Horizon Avatar and Platform SDK requirements are understood and documented.
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

## Phase 19 — Meta Horizon Store / Early Access submission candidate

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

### Privacy posture

For the first public candidate:

```text
No public chat
Private voice chat only for invited/join-code multiplayer
No user-generated text
No analytics beyond essential crash/performance diagnostics unless explicitly documented
No account system beyond Meta/Unity services required for multiplayer
Meta Horizon Avatar/profile data use is disclosed when avatar integration ships
No custom player avatar/profile data is collected for a Blockiverse-only avatar system
Private invite/join-code multiplayer
Clear privacy policy
```

### Tests

```text
Submission checklist complete.
VRC checklist complete.
Privacy policy link works.
Store images meet required dimensions.
APK upload succeeds.
Release channel RC build matches submitted build.
Submitted build comes from a main release tag.
```

### Validation

```text
Submission can be sent to Meta without missing metadata.
If rejected, rejection reasons become GitHub issues under EPIC-14.
If approved, release remains Early Access/Beta until multiplayer and save stability are proven.
```

---

## Phase 20 — Full survival mode, later expansion

### Deliverable

A separate post-MVP survival expansion with day/night, mobs, and deeper progression.

### Scope

```text
Day/night cycle
Lighting changes
Friendly creatures
Hostile mobs
Original blocky voxel NPC/creature visual language
Mob pathfinding
Combat
Armor/tools progression
More recipes
Biome expansion
Dungeon/cave points of interest
Difficulty settings
Multiplayer combat sync
```

### Out of scope until this phase

```text
Creeper-like mobs
Minecraft mob names
Minecraft item names
Minecraft sounds/music
Meta Horizon avatars for NPCs or mobs
Public matchmaking
Infinite terrain
Marketplace/modding support
```

### Tests

```text
Unit: time-of-day system deterministic.
Unit: mob spawn rules obey safe zones.
PlayMode: hostile mob can find player in simple test arena.
PlayMode: combat damage syncs in multiplayer.
Performance: mob counts stay within Quest budget.
```

### Validation

```text
Survival mode feels distinct from creative.
Mobs and day/night do not break VR comfort.
Multiplayer remains stable.
No Minecraft-protected characters or names are introduced.
NPCs/mobs are original voxel characters and do not use player Meta Horizon avatars.
```

---

# 9. Testing strategy

## Unit tests

Use EditMode tests for pure C#:

```text
Voxel coordinates
Chunk storage
Block registry
World bounds
Generation determinism
Crafting recipes
Inventory
Save/load serialization
Network command validation
Settings persistence
```

## Integration tests

Use PlayMode tests for Unity-connected systems:

```text
Boot scene
World generation scene
Chunk rendering
Block break/place
Inventory UI
Crafting UI
Save/load in scene
Multiplayer host/client scene
Late join world sync
```

## Multiplayer tests

Use:

```text
Unity Multiplayer Play Mode for local editor multi-client tests
Host/client PlayMode tests
Manual two-Quest LAN tests
Relay smoke tests later
```

## VR/device smoke tests

Manual or self-hosted-runner-assisted:

```text
Install APK on Quest 3
Install APK on Quest 3S
Launch app
Reach main menu
Start creative world
Break block
Place block
Open inventory
Save world
Reload world
Start host session
Join second headset
Build together for 5 minutes
Collect logs
```

## Performance tests

```text
Small world scene
Max bounded world scene
Cave-heavy scene
High edit-rate scene
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
```

---

# 10. Backlog seed

Use these as initial GitHub issues/sub-issues.

## EPIC-00 — Repo, project management, CI/CD foundation

```text
FEATURE: Create public repo and baseline docs
  STORY: Add README with project goals and target platform
  STORY: Add source-available license
  STORY: Add CONTRIBUTING, SECURITY, CODE_OF_CONDUCT
  STORY: Add Unity .gitignore and .gitattributes with LFS
  STORY: Add docs/adr folder and first architecture decision records

FEATURE: Configure GitHub Project
  STORY: Create Roadmap project
  STORY: Add fields: Phase, Priority, Area, Risk, Target Release, Effort
  STORY: Add milestones M0-M7
  STORY: Add labels for area/type/priority
  STORY: Add issue templates

FEATURE: Configure trunk-based development
  STORY: Protect main
  STORY: Document no develop branch policy
  STORY: Document short-lived feature branch policy
  STORY: Document release-from-main-only policy
  STORY: Add CI check that release tags are on main

FEATURE: Configure CI
  STORY: Add PR test workflow targeting main
  STORY: Add main development APK artifact workflow
  STORY: Add release APK workflow triggered by v* tags
  STORY: Add forbidden-secrets check
```

## EPIC-01 — Unity/Quest project bootstrap

```text
FEATURE: Create Unity 6 URP project
  STORY: Pin Unity version
  STORY: Configure visible meta files and text serialization
  STORY: Add assembly definitions
  STORY: Add boot scene
  STORY: Configure Quest/OpenXR build profile

FEATURE: Add Meta/XR dependencies
  STORY: Install OpenXR
  STORY: Install Unity OpenXR: Meta
  STORY: Install Meta XR Core SDK
  STORY: Add XR rig prefab
```

## EPIC-02 — VR player controller and interaction foundation

```text
FEATURE: Controller input
  STORY: Create input action map
  STORY: Bind Quest controller buttons
  STORY: Add controller haptics abstraction
  STORY: Add input smoke tests

FEATURE: Locomotion
  STORY: Add teleport locomotion
  STORY: Add snap turn
  STORY: Add height reset
  STORY: Add comfort settings menu

FEATURE: Interaction
  STORY: Add ray pointer
  STORY: Add block highlight
  STORY: Add radial/block menu placeholder
```

## EPIC-03 — Voxel data model and block registry

```text
FEATURE: Block registry
  STORY: Add BlockId and BlockDefinition
  STORY: Add initial original block list
  STORY: Add registry validation tests

FEATURE: Chunk/world storage
  STORY: Add chunk coordinate math
  STORY: Add bounded world storage
  STORY: Add block mutation commands
  STORY: Add world bounds tests
```

## EPIC-04 — Bounded terrain, caves, and resources

```text
FEATURE: Terrain generation
  STORY: Add seeded heightmap
  STORY: Add flat test preset
  STORY: Add spawn-safe clearing
  STORY: Add deterministic generation tests

FEATURE: Caves/resources
  STORY: Add cave carving
  STORY: Add resource veins
  STORY: Add distribution tests
```

## EPIC-05 — Chunk rendering and mesh optimization

```text
FEATURE: Chunk meshing
  STORY: Generate visible faces only
  STORY: Add UV atlas mapping
  STORY: Add dirty chunk rebuild queue
  STORY: Add mesh tests

FEATURE: Quest rendering budget
  STORY: Add debug stats panel
  STORY: Add triangle/chunk counter
  STORY: Add stress scene
```

## EPIC-06 — Creative-mode building loop

```text
FEATURE: Break/place blocks
  STORY: Add raycast targeting
  STORY: Add face-normal placement
  STORY: Add placement preview
  STORY: Add undo command

FEATURE: Creative inventory
  STORY: Add hotbar
  STORY: Add block picker
  STORY: Add selected block UI
```

## EPIC-07 — Inventory, resources, and crafting

```text
FEATURE: Inventory
  STORY: Add inventory model
  STORY: Add stacking/splitting
  STORY: Add inventory UI
  STORY: Add unit tests

FEATURE: Crafting
  STORY: Add recipe definitions
  STORY: Add workbench
  STORY: Add crafting validation
  STORY: Add crafting UI
```

## EPIC-08 — Survival-lite health/resource loop

```text
FEATURE: Health
  STORY: Add health model
  STORY: Add damage/healing events
  STORY: Add respawn
  STORY: Add health UI

FEATURE: Resource loop
  STORY: Add block drops
  STORY: Add tool effectiveness
  STORY: Add simple scarcity tuning
```

## EPIC-09 — Save/load and world versioning

```text
FEATURE: Save system
  STORY: Save world metadata
  STORY: Save changed block deltas
  STORY: Save player inventory
  STORY: Add atomic save writes

FEATURE: Load/migration
  STORY: Load current schema
  STORY: Add schema version
  STORY: Add migration hook
  STORY: Add corrupted-save handling
```

## EPIC-10 — Multiplayer co-op foundation

```text
FEATURE: Host/client setup
  STORY: Add NetworkManager prefab
  STORY: Add LAN host flow
  STORY: Add LAN join flow
  STORY: Add Meta Horizon Avatar sync

FEATURE: Multiplayer tests
  STORY: Add Multiplayer Play Mode test scene
  STORY: Add host/client connection test
  STORY: Add disconnect/rejoin test
```

## EPIC-11 — Multiplayer world synchronization

```text
FEATURE: Networked voxel edits
  STORY: Add authoritative block mutation RPC
  STORY: Add chunk delta sync
  STORY: Add late-join sync
  STORY: Add conflict handling

FEATURE: Networked survival-lite
  STORY: Sync resource drops
  STORY: Sync shared crate
  STORY: Validate crafting on host
```

## EPIC-12 — Art/audio/UI polish

```text
FEATURE: Art style guide
  STORY: Define palette (M4)
  STORY: Define block texture rules (M4)
  STORY: Define naming rules (M4)
  STORY: Add AI/procedural asset provenance log (M4)

FEATURE: First art pass
  STORY: Generate original block textures (M4)
  STORY: Generate original item icons (M4)
  STORY: Generate UI panels (M4)
  STORY: Add asset validation tests (M4)
  STORY: Integrate authored block texture atlas (M4)
  STORY: Configure Quest texture import settings (M4)
  STORY: Validate authored art assets in headset (M4)

FEATURE: Audio/haptics
  STORY: Add break/place sounds
  STORY: Add UI sounds
  STORY: Add haptic patterns
```

## EPIC-13 — Performance, profiling, and Quest validation

```text
FEATURE: Performance instrumentation
  STORY: Add profiler markers
  STORY: Add in-game stats
  STORY: Add stress scenes

FEATURE: Quest validation
  STORY: Run Quest 3 smoke test
  STORY: Run Quest 3S smoke test
  STORY: Capture OVR Metrics
  STORY: Document performance results
```

## EPIC-14 — Meta release pipeline and store readiness

```text
FEATURE: Signed release builds
  STORY: Create production keystore
  STORY: Store keystore in secrets
  STORY: Build signed APK from main tag
  STORY: Create GitHub Release artifact

FEATURE: Meta release channels
  STORY: Create Meta app
  STORY: Upload Alpha build from main tag
  STORY: Invite private testers
  STORY: Validate install/update flow

FEATURE: Store submission
  STORY: Draft privacy policy
  STORY: Prepare screenshots
  STORY: Prepare descriptions
  STORY: Complete VRC checklist
  STORY: Submit candidate build
```

## EPIC-15 — Full survival expansion

```text
FEATURE: Day/night
  STORY: Add world time
  STORY: Add lighting cycle
  STORY: Add sleep/skip mechanic, optional

FEATURE: Mobs
  STORY: Add original passive creature
  STORY: Add original hostile creature
  STORY: Add basic AI
  STORY: Add multiplayer combat sync
```

---

# 11. Recommended build order

Build in this order:

```text
1. Repo + GitHub Project + Unity bootstrap
2. CI tests + main development APK build
3. VR controller locomotion
4. Pure C# voxel model
5. Render bounded flat world
6. Break/place blocks in creative mode
7. Save/load
8. Procedural bounded terrain
9. Inventory/crafting/resources
10. Survival-lite health loop
11. M4 Art and Texture Assets, authored texture integration, atlas validation, and generated creative validation terrain
12. Multiplayer host/client movement
13. Multiplayer block synchronization
14. Multiplayer inventory/crafting sync
15. Full art/audio/UI pass
16. Quest performance hardening
17. GitHub Release signed APK fallback from main tags
18. Meta release channels
19. Meta Store/Early Access submission
20. Full survival expansion later
```

The key architectural decision is to make block edits, inventory changes, crafting, and damage all flow through command objects early. That keeps single-player, multiplayer, save/load, undo, and tests aligned instead of requiring a networking rewrite later.

---

# 12. Definition of done by milestone

## M0 Bootstrap

```text
Public repo exists.
main is protected.
No develop branch exists.
GitHub Project exists.
Unity project opens.
CI runs tests.
main can build a dev APK artifact.
```

## M1 VR Slice

```text
Quest 3/3S app launches.
Player can move and turn.
Controllers work.
Pointer can highlight objects.
Comfort settings exist.
```

## M2 Creative

```text
Bounded world renders.
Player can place and break blocks.
Hotbar works.
Save/load works.
World can be validated with automated tests.
```

## M3 Survival-Lite

```text
Terrain, caves, and resources generate.
Inventory works.
Crafting works.
Health loop works.
No mobs/day-night yet.
```

## M4 Art and Texture Assets

```text
Generated terrain is visible in the default validation world.
Committed original block texture assets exist with Unity .meta files.
Renderable blocks use authored texture assets by default.
Missing or unrelated atlas textures fail validation instead of falling back to generated runtime visuals.
Texture import settings are appropriate for Quest 3/3S validation.
Asset provenance is recorded.
Asset validation tests cover dimensions, mappings, missing materials, and unrelated atlas rejection.
Local diagnostics logging foundation supports Alpha validation for renderer, persistence, and authored asset bootstrap failures.
Quest headset validation confirms block readability and no magenta/missing-material surfaces.
```

## M5 Multiplayer

```text
Two Quest devices can connect.
Players can see each other's Meta Horizon avatars.
Players can edit the same world.
World edits stay synchronized.
Basic multiplayer save/load works.
```

## M6 Store Candidate

```text
Signed release APK is built from a main tag.
Meta release channel testing works.
Performance evidence is captured.
Privacy/store metadata is ready.
Submission package is complete.
```

## M7 Full Survival

```text
Day/night exists.
Original mobs exist.
Combat/progression exists.
Multiplayer survival remains stable.
Quest performance remains acceptable.
```

---

# 13. Source references to re-check before implementation

These references were used to form the plan. Re-check them before implementation because pricing, store policy, SDK guidance, and platform requirements can change.

- Unity pricing and plan thresholds: <https://unity.com/products/pricing-updates>
- Unity Personal: <https://unity.com/products/unity-personal>
- Unreal Engine licensing: <https://www.unrealengine.com/license>
- Meta Unity project setup: <https://developers.meta.com/horizon/documentation/unity/unity-project-setup/>
- Unity Quest/OpenXR documentation: <https://docs.unity3d.com/6000.0/Documentation/Manual/xr-meta-quest-develop.html>
- Unity Test Framework: <https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/index.html>
- Unity command-line tests: <https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html>
- Netcode for GameObjects: <https://github.com/Unity-Technologies/com.unity.netcode.gameobjects>
- Unity Multiplayer Play Mode / sessions quickstart: <https://docs.unity.com/en-us/mps-sdk/build-your-first-session>
- Unity Relay: <https://docs.unity.com/en-us/relay/get-started>
- Unity Lobby cost information: <https://support.unity.com/hc/en-us/articles/4410130452628-What-are-the-costs-associated-with-using-the-Lobby-service>
- GitHub CLI repo creation: <https://cli.github.com/manual/gh_repo_create>
- GitHub CLI project creation: <https://cli.github.com/manual/gh_project>
- GitHub Projects and Issues: <https://github.com/features/issues>
- GitHub sub-issues: <https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/adding-sub-issues>
- GitHub issue types: <https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/managing-issue-types-in-an-organization>
- Meta performance VRC: <https://developers.meta.com/horizon/resources/vrc-quest-performance-1/>
- Meta app submission overview: <https://developers.meta.com/horizon/resources/publish-submit/>
- Meta release channels: <https://developers.meta.com/horizon/blog/end-to-end-testing-with-release-channels/>
- Meta APK signing: <https://developers.meta.com/horizon/documentation/native/android/mobile-application-signing-quest/>
- Meta store ecosystem / App Lab changes: <https://developers.meta.com/horizon/blog/a-more-open-ecosystem-for-developers/>
- Minecraft usage guidelines: <https://www.minecraft.net/en-us/usage-guidelines>
