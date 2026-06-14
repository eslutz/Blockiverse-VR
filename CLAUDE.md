# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Read and follow AGENTS.md. It owns all workflow policy: issues, PRs, branching, approval rules, project guardrails, tooling (Unity MCP, `hzdb` for Quest devices), and the Unity licensing-stall recovery recipe. Canonical game design lives in [docs/rulesets/](docs/rulesets/) and the roadmap in [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md); decisions go in [docs/adr/](docs/adr/); the testing contract is [docs/testing/README.md](docs/testing/README.md). This file only adds what those do not cover: commands and the code architecture map.

## Commands

Unity 6000.3.16f1 (Apple Silicon path is the default; override with `UNITY_EDITOR`).

```sh
# Required full Unity gate — runs EditMode then PlayMode, NUnit XML to TestResults/Unity/
scripts/unity/run-tests.sh

# Targeted iteration while coding
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.SomeClass.SomeTest \
  --results-name Single

# Builds (entry points in Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs)
scripts/unity/build-development-apk.sh            # dev APK; runs the bootstrapper first
scripts/unity/build-release-apk.sh                # signed; needs ANDROID_KEYSTORE_PATH/PASSWORD, ANDROID_KEY_ALIAS/PASSWORD

# Generated original assets (never hand-author; regenerate instead)
python3 scripts/art/generate-art-assets.py        # block/item/UI/VFX textures + atlas
python3 scripts/audio/generate-audio.py           # all SFX

scripts/ci/forbidden-files.sh                     # what PR CI actually runs
```

**PR CI does not compile or test Unity code** (it only checks forbidden files and shell syntax). Use targeted `scripts/unity/run-tests.sh` filters while iterating, but the no-arg script remains the full local compile/test gate before Unity-impacting PR review or merge. Release channel workflows build and upload alpha/beta APKs, then promote Beta to RC and RC to Meta `store` without rebuilding.

## Architecture

VR voxel sandbox for Meta Quest 3/3S. Unity 6, URP, OpenXR + Meta XR SDK, XRI, Netcode for GameObjects 2.11.2. LAN host-authoritative co-op. No scene switching: `Assets/Blockiverse/Scenes/Boot.unity` is the whole game.

### Assembly layering (Assets/Blockiverse/Scripts/)

Bottom → top; an assembly may only reference those below it:

- **Core** (logging facade `BlockiverseLog`, canonical paths/constants in `BlockiverseProject`) and **Networking** (thin LAN session over NetworkManager/UnityTransport — no gameplay knowledge)
- **Voxel** — the data model: `VoxelWorld` (flat `BlockId[]`, `BlockChanged` event, changed-block delta set), `BlockRegistry`, `BlockMutationAuthority` (the single validation gate for world edits), `ChunkDeltaLog`, `DeterministicHash`
- **Survival.Health** (vitals/hazards; note its rootNamespace is `Blockiverse.Survival`) and **WorldGen** (terrain presets, seed-pure `SurvivalBiomeResolver`, structures/vegetation, Markov `WeatherService`, `WorldConstants`: ChunkSize 16, WorldMaxY 255, SeaLevel 96, 20 ticks/s, 24000-tick day)
- **Survival** — items/inventory/crafting/stations/harvest/farming; `ItemRegistry`, `ContainerInventoryStore`
- **Persistence** (`WorldSaveService` — see save format below) and **MetaAvatars** (Meta Avatars streaming over Netcode at 15 Hz)
- **Gameplay** — the integration hub: `CreativeWorldManager` (central world owner for both modes; `Awake()` generates a default world), `MultiplayerChunkAuthoritySync` (block mutations + late-join world distribution), `MultiplayerSurvivalSync` (the entire survival economy command channel), rendering/lighting (`VoxelWorldRenderer`, `ChunkMeshBuilder`, `VoxelSkyLightMap`, `WorldTimeClock`)
- **VR** (XR rig, input, comfort) → **UI** (menu router/panels + `BlockiverseWorldSessionController`) → **Editor** (the bootstrapper; editor-only)

EditMode tests live per-area under `Assets/Blockiverse/Tests/EditMode/`, PlayMode (incl. real Netcode host/client sessions) under `Tests/PlayMode/`.

### Cross-cutting invariants

- **No `InternalsVisibleTo` anywhere.** `internal` members are invisible across assemblies — cross-assembly APIs must be `public`. This has shipped compile breaks before; the asmdef boundary, not the namespace, is what matters (Survival.Health shares the `Blockiverse.Survival` namespace but not the assembly).
- **Engine-free simulation core.** Voxel, Survival, Survival.Health, and WorldGen have no `UnityEngine` dependency. This is what allows world generation on background threads (`Task.Run` in late-join sync) and plain NUnit EditMode tests. Do not introduce `UnityEngine` into these assemblies.
- **Host authority.** The host owns chunk generation, mutation validation/commit (`ChunkAuthorityBoundary` flags), delta broadcast, late-join sync, all survival economy resolution (inventories, crafting, stations, drop rolls, shared crate), and multiplayer saves. Clients only send requests and mirror snapshots. Exception: each peer simulates its own vitals locally.
- **Determinism.** Everything seed-derived goes through `DeterministicHash.Hash/UnitRoll` (distinct salts per system) or the seed-pure biome resolver; simulation advances only on `WorldTimeClock` ticks; weather RNG state and tick counts travel in sync snapshots so late-joiners stay in lockstep. Wall-clock randomness is allowed only where host-authoritative (harvest drop rolls). Never put live sim state on background threads — only pure generation.
- **Canonical string IDs** (from the rulesets, e.g. `meadow_turf`) are the persistence and wire vocabulary; saves store canonical strings and registry hashes. Int `BlockId` values are in-memory only. New code, UI labels, and saves must use canonical IDs.
- **Scenes and prefabs are generated, not hand-edited.** `BlockiverseProjectBootstrapper.Run()` (menu: Blockiverse → Bootstrap Unity Quest Project, 4K lines, idempotent) produces the Boot scene, XR rig prefab, network prefabs, player settings, materials, and input actions. To change scene/prefab wiring, change the bootstrapper and rerun it. Input actions and XRI action references are deterministic generated assets; do not serialize scene-local `InputActionReference` objects or inline project-owned XRI actions in generated scenes/prefabs.

### Runtime flow

Boot scene carries the XR rig (with all world-space menus and both controllers' input), a world root (`CreativeWorldManager` + renderer + interaction), and the full network/survival stack — so single-player survival and LAN host/join work without scene loads. `BlockiverseMenuController` only routes screens and emits `ActionRequested(actionId)` (constants in `MenuActions`); `BlockiverseWorldSessionController` implements the session verbs (new world from seed text, save/load/continue). Menu flows are specified in `docs/rulesets/voxel_survival_menus.md`.

### Save format

`<name>.vxlworld/` directory (schema v4): `manifest.json` (pretty-printed, registry hashes), `dimensions/main/` (dimension, environment, containers, `regions/r.<rx>.<rz>.vxlr`), `players/local_player.json`. Regions store **only changed blocks** (delta vs. terrain regenerated from seed) as 16-block sections with string palettes. All writes are atomic (`.tmp` → move/replace; regions dir swap keeps a `.bak` recovery window). Legacy v1–v3 saves fail fast — no migrations pre-release. Single-player saves live under `Application.persistentDataPath/Saves`; the multiplayer host world is `multiplayer-world.vxlworld`.

## Documentation currency

Two companion projects must stay current alongside this repo:

- **Wiki** (`../Blockiverse-VR.wiki`) — the primary source for all user-facing documentation (gameplay mechanics, controls, crafting/survival rules, save format, multiplayer setup, store descriptions, release notes, known issues). The wiki is what players and store reviewers read; it must reflect the shipped state of the game, not aspirational plans.
- **Website** (`../Blockiverse-VR.website`) — the public-facing project site; keep store metadata, feature lists, screenshots, and versioning consistent with what is actually in the game.

**When to update:** any change that affects a user-observable behaviour, a publicly documented feature, or a store-submitted artefact warrants a corresponding wiki and/or website update in the same PR or immediately following commit. This includes (but is not limited to):

- New or changed gameplay mechanics, survival rules, crafting recipes, or block/item behaviour
- Save-format version bumps or migration behaviour changes
- Multiplayer session flow changes (hosting, joining, disconnect handling)
- VR comfort, control binding, or locomotion changes
- New store-ready features, screenshots, or release notes
- Changes to the privacy policy or data-use declarations

Changes that are purely internal (refactors, test additions, CI fixes, performance work with no observable behaviour change) do not require wiki or website updates, but use judgement — if a performance fix removes a known limitation that appears in the known-issues page, update it.
