# CLAUDE.md

This file is the single source of truth for agent instructions in this repository.
`AGENTS.md` intentionally points here.

Canonical game design lives in [docs/rulesets/](docs/rulesets/) and the roadmap in
[docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md).
Architecture decisions go in [docs/adr/](docs/adr/), and the testing contract is
[docs/testing/README.md](docs/testing/README.md).

## Agent Workflow Policy

- The project owner is Eric Slutz; the GitHub username for assignment and review is `eslutz`.
- Eric must provide final approval for complex, high-risk, product-facing, or PR-backed work before merge.
- Eric is currently the only human on the project. Do not configure required approving reviews or required CODEOWNERS review unless another human reviewer is added; otherwise Eric cannot approve his own PR.
- Keep `main` protected with a repository ruleset requiring status checks, linear history, conversation resolution, and force-push protection.
- Use trunk-based development. Do not create a long-lived `develop` branch or long-lived release branches.
- Use short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches.
- Prefer pull requests into `main` after CI passes. Direct pushes to `main` should be rare and explicit.
- Link pull requests to an issue when active issue tracking exists. Otherwise link the relevant execution-plan section, ruleset, or ADR.
- Use GitHub issues and the `Blockiverse VR Roadmap` project only for active workflow state: bugs, blockers, review work, multi-PR initiatives, and durable follow-ups. The roadmap and rulesets remain the canonical product sources.
- When work begins on an existing issue, assign it to `eslutz` unless Eric explicitly says otherwise.
- Keep PR descriptions useful: scope, linked issue or source doc, validation commands, manual validation, risk notes, and known follow-ups.
- Do not merge a PR, close a PR-backed issue, or move it to Done until Eric has approved the work or explicitly asked for completion.
- Before adding or changing GitHub Actions, packages, SDKs, CLIs, Unity packages, build images, or other third-party dependencies, verify the current stable version from official upstream sources. Prefer latest stable majors unless the repo has a documented compatibility constraint.
- Update documentation when behavior, workflow, architecture, project policy, release process, store submission, or user-visible scope changes.

### Release Policy

- Production releases are cut from `main`.
- Release versioning follows [ADR 0005](docs/adr/0005-release-versioning.md), with the root `VERSION` file as the SemVer base version source.
- Pull requests use `.github/workflows/quest-ci.yml` for validation only. PR workflows must not receive Meta credentials or publish to Meta release channels.
- Meta channel CD is split across:
  - `.github/workflows/quest-alpha.yml`, which builds a release-signed Quest APK from `main` pushes or manual trusted refs and uploads it to Meta `alpha`;
  - `.github/workflows/quest-promote.yml`, which manually promotes an existing tested Meta build through `alpha -> beta`, `beta -> rc`, or `rc -> store` without rebuilding.
- Promotion to `beta`, `rc`, and `store` must preserve the exact tested Meta build artifact. Store promotion requires the `meta-store` environment approval gate.
- Known-good engineering checkpoint tags use the `kg/...` family and follow [docs/rulesets/voxel_git_known_good_tagging_policy.md](docs/rulesets/voxel_git_known_good_tagging_policy.md). They are recovery checkpoints, not release tags.

### Project Guardrails

- Treat Meta Quest 3 and Meta Quest 3S as the primary target platforms.
- Initial multiplayer uses Meta Quest party chat for voice. Do not add in-app voice chat unless the rulesets and roadmap explicitly change.
- Use original names and original assets; do not copy protected third-party identity.
- Gameplay code, UI labels, registries, save data, and tests should use canonical IDs from the rulesets. Legacy IDs must be handled through explicit migration code or marked as historical validation artifacts.
- Never commit secrets, keystores, signing credentials, API keys, `.env` files, Unity `Library`, `Temp`, `Logs`, local generated folders, device logs, screenshots, recordings, Perfetto traces, APKs, or other generated validation artifacts unless a tracked artifact is explicitly required.
- Keystores and production signing material stay outside the repo and in GitHub Actions secrets.
- Current licensing state is source-available / All Rights Reserved. Keep `LICENSE.md`, `NOTICE.md`, and related docs aligned with that posture.

### Tooling Policy

- Prefer reproducible command-line tooling over GUI-only actions when command output is useful validation evidence.
- Use the Unity MCP server for interactive Unity Editor inspection, simulator-oriented editor workflows, scene/object checks, and Unity-specific automation exposed through MCP.
- Use the committed local scripts as the repeatable Unity validation source of truth. `scripts/unity/run-tests.sh` remains the required EditMode and PlayMode validation command.
- Use the globally installed Horizon Debug Bridge CLI, `hzdb`, for Meta Quest device work instead of enabling the hzdb MCP server in the base Codex config.
- Verify Quest-device tooling before device work with `hzdb --version` and `hzdb device list`.
- Use `adb` directly only when `hzdb` does not expose the needed operation or when comparing behavior against lower-level Android tooling; document why the fallback was needed.
- Use GitHub CLI for best-effort GitHub Project updates and cleanup because connector tools may not expose all project mutations.

### Unity Licensing Recovery

If Unity batchmode logs `ResponseCode: 505`, `Unsupported protocol version '1.18.1'`,
or waits on `LicenseClient-ericslutz-6000.3.16`, reset the local Unity/Hub process state:

```sh
osascript -e 'tell application "Unity Hub" to quit'
pkill -f 'Unity.Licensing.Client|Unity Hub Helper|Unity Hub.app' || true
pgrep -afil 'Unity|Licensing|UnityPackageManager'
scripts/unity/run-tests.sh
```

The `pgrep` command should return no Unity editor, Unity Hub, UnityPackageManager,
or Unity licensing processes before the retry. Do not leave stuck Unity batchmode
processes running.

## Commands

Unity 6000.3.16f1 (Apple Silicon path is the default; override with `UNITY_EDITOR`).

```sh
# Required validation — runs EditMode then PlayMode, NUnit XML to TestResults/Unity/
scripts/unity/run-tests.sh

# Single test / one platform (the script takes no args; invoke Unity directly)
"${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}" \
  -batchmode -nographics -projectPath . -runTests -testPlatform EditMode \
  -testFilter "Blockiverse.Tests.EditMode.SomeClass.SomeTest" \
  -testResults TestResults/Unity/Single.xml -logFile -

# Builds (entry points in Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs)
scripts/unity/build-development-apk.sh            # dev APK; runs the bootstrapper first
scripts/unity/build-release-apk.sh                # signed; needs ANDROID_KEYSTORE_PATH, ANDROID_KEYSTORE_PASSWORD, ANDROID_KEY_ALIAS, ANDROID_KEY_PASSWORD

# Generated original assets (never hand-author; regenerate instead)
python3 scripts/art/generate-art-assets.py        # block/item/UI/VFX textures + atlas
python3 scripts/audio/generate-audio.py           # all SFX
```

`.github/workflows/quest-ci.yml` validates pull requests with repository checks, Unity Personal activation through GameCI, Unity tests, and an Android smoke APK. `.github/workflows/quest-alpha.yml` builds the release-signed APK that goes to Meta `alpha`. `.github/workflows/quest-promote.yml` promotes already-uploaded Meta build IDs to `beta`, `rc`, and eventually `store` without rebuilding.

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
- **Scenes and prefabs are generated, not hand-edited.** `BlockiverseProjectBootstrapper.Run()` (menu: Blockiverse → Bootstrap Unity Quest Project, 4K lines, idempotent) produces the Boot scene, XR rig prefab, network prefabs, player settings, materials, and input actions. To change scene/prefab wiring, change the bootstrapper and rerun it.

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
