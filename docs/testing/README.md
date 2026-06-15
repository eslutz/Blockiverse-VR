# Testing

Testing is split into:

- Repository safety checks for shell syntax, release policy docs, and forbidden tracked files
- Targeted local Unity validation for changed fixtures and subsystems
- Full local Unity validation before review or merge for Unity-impacting changes
- Development APK and Quest-device validation when Android, headset, release, or Quest performance behavior changes
- Meta XR Simulator and MCP-driven manual validation for canonical ruleset flows
- Release channel workflow checks that upload alpha/beta builds and promote beta-to-RC and RC-to-store builds
- EditMode tests for pure C# logic
- PlayMode tests for Unity-connected systems
- Multiplayer Play Mode tests for local multi-client behavior
- Manual Quest 3 and Quest 3S smoke tests
- OVR Metrics performance captures
- Store-readiness validation before submission

Performance reports belong in `docs/testing/performance/`.

Meta XR Simulator setup, MCP configuration, and historical smoke-script notes are documented in [Meta XR Simulator And MCP Validation](meta-xr-simulator-and-mcp.md). New smoke scripts should use the canonical world presets and rulesets in `../rulesets/`.

Historical multiplayer editor-network validation, simulated latency and packet-loss checks, and active block-editing bandwidth estimates are documented in [M5 Multiplayer Validation](multiplayer-m5-validation.md). New multiplayer validation should follow [Voxel Multiplayer and Networking Ruleset](../rulesets/voxel_multiplayer_networking_ruleset.md).

Runtime diagnostics use local Unity and player logs only. Use `hzdb` for Quest player logs and other Quest-device operations whenever it exposes the needed command; use `adb` directly only as a documented fallback. On Eric's current development machine, `hzdb` resolves to `/Users/ericslutz/.nvm/versions/node/v24.16.0/bin/hzdb`, but agents should verify the live path with `command -v hzdb` because the active `nvm` Node can change. If `node` or `npm` resolves outside the `hzdb` Node prefix, put the `hzdb` prefix first on `PATH` for package-manager verification. Capture recent Quest player logs with:

```sh
hzdb log --tag Unity --level I --lines 200
hzdb log --tag Unity --level W --lines 200
```

Verbose gameplay tracing is available only in the Unity Editor and development builds. Enable it by setting `PlayerPrefs["Blockiverse.Diagnostics.VerboseTraceEnabled"]` to `1` or by creating the marker file `Diagnostics/enable-verbose-trace` under `Application.persistentDataPath`. When enabled, the game writes rolling JSONL files named `blockiverse-trace-<session>-NNN.jsonl` under `Application.persistentDataPath/Diagnostics` with timed player snapshots and sanitized interaction, audio/VFX, haptic, environment, and world-event records. Unity/player logs only receive trace start/stop/file summary lines.

Attach relevant excerpts to issues or pull requests when they are needed as validation evidence. Do not commit local device logs, screenshots, recordings, traces, APKs, or other generated validation artifacts unless a tracked artifact is explicitly required.

## Validation tiers

### Docs/Repo

Use this tier for documentation-only, governance-only, PR-template, issue-template, and markdown-only policy changes that do not alter Unity project behavior:

```sh
git diff --check
bash -n scripts/ci/forbidden-files.sh scripts/unity/*.sh
scripts/ci/forbidden-files.sh
test -f docs/architecture/branching-and-release.md
```

GitHub-hosted CI validates repository checks only. Unity validation is manual and local with Unity Hub Personal.

### Targeted Unity

Use targeted Unity validation while iterating on Unity-impacting changes. Prefer the smallest fixture, test fullname, or subsystem filter that covers the changed behavior:

```sh
scripts/unity/run-tests.sh \
  --platform EditMode \
  --filter Blockiverse.Tests.EditMode.BlockiverseInputActionAssetTests \
  --results-name validation-editmode-smoke

scripts/unity/run-tests.sh \
  --platform PlayMode \
  --filter Blockiverse.Tests.PlayMode.BootScenePlayModeTests.BootSceneLoadsWithXrRigAndCamera \
  --results-name validation-playmode-smoke
```

`scripts/unity/run-tests.sh` supports `--platform EditMode|PlayMode|all`, `--filter <test-filter>`, `--results-name <slug>`, and `--results-dir <path>`. Named single-platform runs write `TestResults/Unity/<slug>.xml`; named `--platform all` runs write `TestResults/Unity/<slug>-EditMode.xml` and `TestResults/Unity/<slug>-PlayMode.xml`.

#### Generated Input Wiring

The bootstrapper owns the Unity Input System action catalog and generated XR wiring. `Assets/Blockiverse/Settings/BlockiverseInputActions.inputactions` uses deterministic map, action, and binding IDs, and every action has a tracked `InputActionReference` asset under `Assets/Blockiverse/Settings/InputActionReferences/`.

When adding or changing input actions, update the bootstrapper catalog, run `Blockiverse.Editor.BlockiverseProjectBootstrapper.Run`, and keep the regenerated input-action asset plus generated reference assets. Generated scenes and prefabs should reference those assets; they should not store scene-local `InputActionReference` objects or serialized inline `InputAction` instances for project-owned XRI actions.

Use the focused input guards while iterating:

```sh
scripts/unity/run-tests.sh \
  --platform EditMode \
  --filter Blockiverse.Tests.EditMode.BlockiverseInputActionAssetTests \
  --results-name input-action-determinism

scripts/unity/run-tests.sh \
  --platform EditMode \
  --filter Blockiverse.Tests.EditMode.BlockiverseRigPrefabTests \
  --results-name input-reference-wiring
```

### Unity Full Gate

Run the full local Unity gate before moving any Unity-impacting pull request to review or merge, before creating a known-good `kg/...` checkpoint for Unity work, and before release-candidate validation:

```sh
scripts/unity/run-tests.sh
```

With no arguments, the script remains the canonical full gate. It runs EditMode then PlayMode and writes `TestResults/Unity/EditMode.xml` and `TestResults/Unity/PlayMode.xml`.

### APK/Quest Gate

Add this tier when the change affects VR comfort, Android or Quest behavior, headset-only behavior, networking on devices, release signing, store submission, or Quest performance:

```sh
scripts/unity/build-development-apk.sh /tmp/blockiverse-vr-development.apk
HZDB_BIN="$(command -v hzdb)"
HZDB_NODE_PREFIX="$(cd "$(dirname "$HZDB_BIN")/.." && pwd)"
"$HZDB_NODE_PREFIX/bin/node" --version
PATH="$HZDB_NODE_PREFIX/bin:$PATH" npm list -g --depth=0 @meta-quest/hzdb
hzdb --version
hzdb device list
```

`hzdb` is installed under the active default `nvm` Node with `npm install -g @meta-quest/hzdb@1.2.1`; the expected current executable path is `/Users/ericslutz/.nvm/versions/node/v24.16.0/bin/hzdb`. Prefer `hzdb` for Quest device discovery, APK install and launch, log capture, screenshots, screen recordings, file transfer, and performance captures. If `hzdb device list` cannot see a connected Quest from a Codex sandboxed shell, rerun physical-device commands outside the sandbox before treating validation as blocked. Use the Meta XR Simulator or physical Quest 3/Quest 3S validation flow when a behavior cannot be proven by EditMode or PlayMode tests alone. Use OVR Metrics or equivalent captures for Quest performance work, and store summaries under `docs/testing/performance/`.

For Quest pointer/ray changes, validate the normal development APK in the real game title-menu and New World flow, not a stub diagnostic scene or diagnostic APK. Confirm both Comfort dominant-hand settings:

- Right-handed mode shows one stable interaction ray from the right controller/tool hand; the support-hand interaction ray remains hidden except while teleport owns a locomotion ray.
- Left-handed mode shows one stable interaction ray from the left controller/tool hand; the support-hand interaction ray remains hidden except while teleport owns a locomotion ray.
- Menu hover suppresses block editing for the active ray, missed menu rays remain short, world targeting restores normal reach after the menu is left, and controller/ray visuals render through the normal scene camera while menu/HUD surfaces may use Quad composition layers.

Remove any temporary ray diagnostic scenes or build scripts once the issue is reproduced in the real game path. Stub ray worlds are not part of the validation gate.

## Test selection rules

- Docs, governance, PR templates, issue templates, and markdown-only policy changes: run the Docs/Repo tier only.
- Pure C# logic in engine-free assemblies: run the targeted EditMode fixture first, then the Unity Full Gate before review.
- Boot scene, prefabs, input, UI, VR interaction, assets, bootstrapper, or rendering: run targeted EditMode plus the relevant Boot or interaction PlayMode filter; add the APK/Quest Gate only if Android or device behavior could change.
- Save/load, schema, worldgen, survival, networking, multiplayer, or authority changes: run targeted subsystem tests plus the relevant PlayMode or networking filter; run the Unity Full Gate before review.
- Release, signing, store, Quest comfort, Quest performance, device multiplayer, or headset-only behavior: run the Unity Full Gate plus the APK/Quest Gate.

Local Unity validation requires globally installed tools on the developer machine:

- Unity Hub installed globally, preferably with Homebrew, and Unity Editor `6000.3.16f1`.
- Android Build Support, Android SDK/NDK Tools, and OpenJDK installed through Unity Hub for that Editor version.
- A Unity Personal or higher license accepted in Unity Hub before running batchmode commands.
- `UNITY_EDITOR` set when the executable is not at `/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity`.

Current GitHub Actions release workflows use the UnityCI editor container for alpha and beta APK builds and do not require UNITY_LICENSE, UNITY_EMAIL, or UNITY_PASSWORD secrets. RC and production workflows promote already-uploaded Meta build IDs and do not rebuild APKs. Unity Personal activation remains local for developer-run validation, and the local license file is not committed, copied into CI, or uploaded as an artifact.

Record the selected validation tier, exact commands, result summary, output APK path when applicable, promoted Meta build ID when applicable, intentionally deferred validation, and any residual risk in the pull request or linked issue. Local development APKs usually use `/tmp/blockiverse-vr-development.apk`; alpha channel development APKs are uploaded by the alpha release workflow for same-repository pull request commits.

If the project later adopts a CI-compatible Unity license, Unity Build Automation, or a self-hosted runner with an accepted local license, reintroduce hosted Unity test and build jobs in a separate issue and update this document with the new validation contract.
