# Testing

Testing is split into:

- Repository checks for markdown, shell syntax, release workflow conventions, and tracked-file policy
- GitHub-hosted Quest CI for Unity Personal activation and Android smoke APK validation
- Targeted local Unity validation for changed fixtures and subsystems
- Full local Unity validation before review or merge for Unity-impacting changes
- Development APK and Quest-device validation when Android, headset, release, or Quest performance behavior changes
- Meta XR Simulator and MCP-driven manual validation for canonical ruleset flows
- Editor-assisted validation through Unity MCP and Unity Skills for targeted diagnostics, console review, scene/project inspection, XR triage, and Unity Test Runner jobs
- Release channel workflow checks that upload alpha builds and promote selected tested Meta build IDs through beta, RC, and store channels
- EditMode tests for pure C# logic
- PlayMode tests for Unity-connected systems
- Multiplayer Play Mode tests for local multi-client behavior
- Manual Quest 3 and Quest 3S smoke tests
- OVR Metrics performance captures
- Store-readiness validation before submission

Performance reports belong in `docs/testing/performance/`.

Meta XR Simulator setup, MCP configuration, and historical smoke-script notes are documented in [Meta XR Simulator And MCP Validation](meta-xr-simulator-and-mcp.md). New smoke scripts should use the canonical world presets and rulesets in `../rulesets/`.

Editor-assisted validation can shorten investigation loops, but it does not replace the scripted gates below. Use MCP for Unity as the default live Editor bridge when the Unity Editor is open and connected; use Unity Skills for module-specific REST workflows, advisory guidance, XR diagnostics, batch/workflow operations, debug/console triage, and targeted Unity Test Runner jobs. Treat both as local developer tooling unless a separate dependency-update task explicitly adds them to `Packages/manifest.json`.

Smoke-check MCP for Unity:

```sh
codex mcp list
# Then read mcpforunity://instances and mcpforunity://project/info,
# confirm the active instance points at this checkout, and call read_console.
```

Smoke-check Unity Skills:

```sh
curl http://localhost:8090/health
curl -X POST http://localhost:8090/skill/unity_diagnose \
  -H 'Content-Type: application/json' \
  -d '{}'
curl 'http://localhost:8090/skills?category=xr'
```

If both Unity automation packages are installed locally, Unity may log a duplicate `package.json.meta` GUID warning from the package cache. Do not treat that warning by itself as a validation failure if Unity compiles, MCP for Unity can read the console, Unity Skills health is `ok`, `scripts/unity/run-tests.sh` passes, and the committed package manifests remain clean. Fix the package metadata through an upstream or forked package patch if the warning starts blocking import, test runs, or local server startup.

Unity editor domain reloads can also log `Call to StopSubsystems without an initialized manager` from `XRManagerSettings.OnDisable()` while Android OpenXR automatic loading/running is enabled but no XR loader was initialized in the macOS editor. Keep Android automatic loading/running enabled for Quest builds; treat this editor-domain-reload stack as local editor noise unless the same warning appears in Quest player logs or blocks tests/builds.

Historical multiplayer editor-network validation, simulated latency and packet-loss checks, and active block-editing bandwidth estimates are documented in [M5 Multiplayer Validation](multiplayer-m5-validation.md). New multiplayer validation should follow [Voxel Multiplayer and Networking Ruleset](../rulesets/voxel_multiplayer_networking_ruleset.md).

### Multiplayer Play Mode (two virtual players in one editor)

`com.unity.multiplayer.playmode` is a committed dependency (`Packages/manifest.json`). It runs
additional virtual players against the same project, which is the fastest way to reproduce
anything that only goes wrong with a real second peer — late join into a played world, join
refusals, avatar pose, disconnect handling.

1. Open the project and `Window > Multiplayer > Multiplayer Play Mode`.
2. Activate one virtual player. Leave the main editor as the host.
3. Enter Play Mode, host from the LAN panel in the main editor, and join from the virtual player
   (LAN discovery lists the host, or enter `127.0.0.1`).

Use it for iteration, not as an acceptance gate: `scripts/unity/run-tests.sh` and the on-device
Quest pass remain the gates. Two virtual players on one machine share a clock and a network
stack, so they cannot tell you anything about real Wi-Fi behaviour.

### LAN discovery on a real network

The host broadcasts a signed UDP beacon on port 7778 once per second while hosting; clients
listen only while the LAN panel is open. Some access points drop broadcast traffic between
clients ("client isolation" / "AP isolation"), and on those networks the session list stays
empty by design — manual address entry is the documented fallback, and the panel says so. When
validating discovery on device, confirm both the discovered-list path and the typed-address path.

Runtime diagnostics use local Unity and player logs only. Use `hzdb` for Quest player logs and other Quest-device operations whenever it exposes the needed command; use `adb` directly only as a documented fallback. On Eric's current development machine, `hzdb` resolves to `/Users/ericslutz/.nvm/versions/node/v24.16.0/bin/hzdb`, but agents should verify the live path with `command -v hzdb` because the active `nvm` Node can change. If `node` or `npm` resolves outside the `hzdb` Node prefix, put the `hzdb` prefix first on `PATH` for package-manager verification. Capture recent Quest player logs with:

```sh
hzdb log --tag Unity --level I --lines 200
hzdb log --tag Unity --level W --lines 200
```

Verbose gameplay tracing is available only in the Unity Editor and development builds. Enable it by setting `PlayerPrefs["Blockiverse.Diagnostics.VerboseTraceEnabled"]` to `1` or by creating the marker file `Diagnostics/enable-verbose-trace` under `Application.persistentDataPath`. When enabled, the game writes rolling JSONL files named `blockiverse-trace-<session>-NNN.jsonl` under `Application.persistentDataPath/Diagnostics` with timed player snapshots and sanitized interaction, audio/VFX, haptic, environment, and world-event records. Unity/player logs only receive trace start/stop/file summary lines.

Attach relevant excerpts to issues or pull requests when they are needed as validation evidence. Do not commit local device logs, screenshots, recordings, traces, APKs, or other generated validation artifacts unless a tracked artifact is explicitly required.

## GitHub Workflows

`quest-ci.yml` runs on pull requests and manual dispatch. It checks repository conventions, pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal through GameCI, builds an Android smoke APK, and uploads that APK as a validation artifact. It does not receive Meta credentials and must not publish to Meta. The Android smoke build is the GitHub-hosted Unity validation gate: it catches package import, compile, Android target, and APK packaging failures. The build does not require Meta Avatars sample preset zips while Blockiverse fallback preset avatars are disabled; if fallback presets are enabled later, the packaged Quest preset assets must be intentionally added. Full EditMode and PlayMode tests are intentionally local-only because GitHub-hosted UnityCI Android test containers are not reliable for the local LAN multiplayer PlayMode suite.

`quest-alpha.yml` runs on pushes to `main` and manual dispatch of a trusted branch, tag, or commit. It pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal with GameCI, computes Android version metadata, release signs the APK, uploads the artifact bundle to GitHub Actions, and publishes the APK directly to Meta `alpha`.

`quest-promote.yml` runs by manual dispatch only. It requires the tested Meta build ID, promotes that selected build through `alpha -> beta`, `beta -> rc`, or `rc -> store`, and uploads a promotion record artifact. It does not rebuild APKs.

Automated GitHub Actions validation is optimized for deterministic signals on GitHub-hosted runners:

- `quest-ci.yml` verifies repository checks and Android smoke APK packaging for pull requests.
- `quest-alpha.yml` builds and uploads a release-signed APK to Meta `alpha` after merge or manual trusted dispatch.
- `quest-promote.yml` promotes an existing tested Meta build ID without rebuilding.

Local validation is optimized for behavior:

- `scripts/unity/run-tests.sh` runs the full local EditMode and PlayMode test gate.
- `scripts/unity/run-local-validation.sh` runs shell syntax checks, full local Unity tests, and a development APK build.
- `scripts/unity/build-development-apk.sh` produces a development APK for smoke installation.
- Release-signed APKs are produced by `.github/workflows/quest-alpha.yml` only, using GitHub Actions secrets and the `meta-alpha` environment.

### `run-tests.sh` exits 0 when the run did not happen

**Its exit code does not tell you whether the tests passed, or whether they ran at all.** Measured
independently by two sessions on 2026-08-25, across six runs between them:

- A **compile error** runs no tests and exits 0.
- An **EditMode failure** skips PlayMode entirely and exits 0.
- A **segfault** (`EXIT=139`) is likewise reported as a clean exit.

The script also does **not** clear `TestResults/Unity/*.xml`, so the *previous* run's results are
still sitting there afterwards. A build break is therefore indistinguishable from "a completed run
with some failures" — one session read six stale failures as real and reasoned about them before the
file mtimes gave it away.

Always:

```sh
rm -f TestResults/Unity/EditMode.xml TestResults/Unity/PlayMode.xml
scripts/unity/run-tests.sh > gate.txt 2>&1
grep -o "error CS[0-9]*: .*" gate.txt | sort -u     # compile FIRST; empty means it built
```

Then read each XML **with its mtime**, and treat a missing file as "did not run" rather than
inferring anything from the exit code. Deleting first converts a silent lie into an obvious absence.

The same caveat applies to anything gating on that exit code, CI included: it would read a build
break as success.

Texture-pack compositing splits deliberately for testability: the pixel arithmetic
(`BlockiverseTexturePackAtlasBuilder`) is pure `Color32[]` and is covered by EditMode tests under
`-nographics`, while the GPU readback and PNG decode sit behind `IBlockiverseAtlasPixelSource` so
tests substitute a fake. The two failure modes that remain device-only — the sRGB round trip and
mip coverage — both present as *looking slightly wrong at distance* rather than as a test failure,
so a headset pass is still required when either is touched.


## Required GitHub Configuration

Repository secrets:

- `UNITY_LICENSE` - the Unity Personal `.ulf` license file contents.
- `UNITY_EMAIL` - the Unity account email used for Unity Personal activation.
- `UNITY_PASSWORD` - the Unity account password used for Unity Personal activation.
- `ANDROID_KEYSTORE_BASE64` - base64-encoded Android release keystore.
- `ANDROID_KEYSTORE_PASSWORD` - Android keystore password.
- `ANDROID_KEY_ALIAS` - Android key alias.
- `ANDROID_KEY_PASSWORD` - Android key password.
- `META_APP_ID` - Meta Horizon app ID for Blockiverse VR.
- `META_APP_SECRET` - Meta app secret used by OVR Platform Utility for upload and promotion.

Repository variables:

- `META_AGE_GROUP` - normally `TEENS_AND_ADULTS`.
- `OVR_PLATFORM_UTIL_LINUX_URL` - Linux OVR Platform Utility download URL.
- `OVR_PLATFORM_UTIL_LINUX_SHA256` - checksum for the downloaded utility.

Unity Personal activation follows the GameCI model: activate Unity Personal once locally, copy the generated `.ulf` file into `UNITY_LICENSE`, and provide `UNITY_EMAIL` and `UNITY_PASSWORD`. Do not commit, log, or upload the Unity license file.

## Validation Tiers

### Docs/Repo

Use this tier for documentation-only, governance-only, PR-template, issue-template, and markdown-only policy changes that do not alter Unity project behavior:

```sh
git diff --check
bash -n scripts/unity/*.sh
```

### Audio Assets

Run this whenever anything under `Assets/Blockiverse/Audio` or `scripts/audio/` changes. It
validates the committed WAVs — format, channel layout, level, loop continuity, and that every
cue traces to a source and a license — rather than the generator's output:

```sh
python3 scripts/audio/validate-audio-assets.py      # gates what actually ships
python3 scripts/audio/validate-generated-audio.py   # generator still reproducible
```

Rebuilding the cues needs the source packs staged outside the repository; see
[`docs/audio/audio-asset-manifest.md`](../audio/audio-asset-manifest.md) for the full
pipeline and `scripts/audio/build-audio-assets.py --check` to confirm sources resolve
before anything is written.

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

EditMode runs use `-nographics`. PlayMode runs use the Unity graphics device by default because URP and composition-layer tests can fail under NullGfx even when runtime behavior is valid. Set `UNITY_PLAYMODE_NOGRAPHICS=1` only for focused diagnostics that are known to be graphics-independent.

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

### Iterating: Filtered Runs, Not Full Gates

**The full gate (`scripts/unity/run-tests.sh` with no arguments) is expensive — 15-30 minutes,
scaling with machine load from concurrent sessions/worktrees — and using it as the iteration loop
is what makes development grind to a halt.** Measured 2026-08-25: a session doing normal
write-code / write-test / verify-the-test-can-fail / revert work ran the full gate roughly a dozen
times in one session, several of them purely to re-confirm green after an unrelated one-line fix.

**Use a filtered run for every iteration inside a change.** Reserve the full gate for the *last*
check before commit/PR, and for anything touching save schema, networking, or authority (see Test
Selection Rules below — those still want the full gate before review because cross-subsystem
regressions do not show up in a filter).

```sh
# Iterating on a class (or several, semicolon-separated): seconds, not minutes.
scripts/unity/run-tests.sh --platform EditMode   --filter "Blockiverse.Tests.EditMode.BlockiverseCloudDeckEditModeTests;Blockiverse.Tests.EditMode.VoxelSkyLightCanopyEditModeTests"   --results-name focus

# Only run PlayMode when the change actually touches Unity-connected systems. Most mutation/revert
# cycles on pure C# logic never need it — running it anyway roughly doubles the wait for nothing.
```

**Known gap: some EditMode tests are order- and scene-dependent and give false failures when
filtered.** Anything that resolves `Camera.main` (`BlockiverseRigPlacement.*`, at least) returns
whatever camera the *session* happens to have loaded, not the one the test just built, so the same
test passes in the full suite and fails when filtered — including tests nobody touched. Treat a
filtered failure in one of these as inconclusive, not as a regression, until confirmed against the
full suite. Fixing the underlying order-dependence (inject the camera rather than resolving
`Camera.main`) removes the exception; until then, don't chase a filtered-only failure as if it were
real.

**Never delete `Library/ScriptAssemblies` to "fix" a mismatch between what the code says and what a
test observed.** It is almost never staleness — Unity recompiles automatically on a batchmode
launch — and reaching for it is how a real bug gets waved through as an environment problem.
Confirm with `grep 'error CS' <log>` and a fresh results XML mtime before suspecting the build at
all; only after that, and only as a last resort, consider a clean reimport.

### Mutation-Verify Loop (proving an assertion can fail)

Per "Writing Tests That Can Actually Fail" below, a new assertion is not evidence until you have
watched it go red. Doing this ONE MUTATION AT A TIME against the full gate is the single biggest
avoidable source of gate runs. Batch it instead:

1. Write every planned mutation as a scripted, reversible text swap (a small Python script using
   exact-count anchored replacements, not sed) BEFORE writing any of them by hand. For each
   mutation, name the exact test(s) it must turn red. Keep the mutation script itself outside
   `Assets/` (the scratchpad, not the repo) — it is scaffolding, not shipped code.
2. Apply every mutation for the current unit of work in one pass.
3. Run ONE filtered EditMode pass over just the affected test classes (not the full gate — a
   mutation that only breaks EditMode-visible behavior does not need PlayMode to prove it).
4. Diff observed failures against the prediction. Anything predicted-but-green is a test that
   cannot fail; anything red-but-not-predicted is either a wrong prediction or a real bug the
   mutation exposed by accident — resolve before proceeding, don't wave it through.
5. Revert all mutations in one pass (the same script, `revert` instead of `apply`) and confirm
   `git diff` is byte-identical to before step 1 — the anchored-replacement approach makes this
   exact rather than "close enough."
6. Only THEN run the full gate once, as the final confirmation — not once per mutation.

This turns "N mutations -> N full gates" into "N mutations -> 1 filtered run + 1 full gate,"
independent of N.

### Unity Full Gate

Run the full local Unity gate before moving any Unity-impacting pull request to review or merge, before creating a known-good `kg/...` checkpoint for Unity work, and before release-candidate validation:

```sh
scripts/unity/run-tests.sh
```

With no arguments, the script remains the canonical full gate. It runs EditMode then PlayMode and writes `TestResults/Unity/EditMode.xml` and `TestResults/Unity/PlayMode.xml`.

#### Test Suite Discovery Check

A green run is not proof the whole suite ran. **An asmdef whose reference fails to resolve drops out
of test discovery entirely rather than failing to compile loudly** — the assembly is simply absent,
every test that did run passed, and the gate reports success against a silently smaller suite.
Totals do not reliably catch it either, because a change that adds cases can net out an assembly
that vanished.

Run this after the gate whenever a change touches assembly definitions, moves files between
assemblies, or adds a new one:

```sh
scripts/unity/check-test-suites.py
```

It parses the NUnit XML for `test-suite type="Assembly"` and asserts all nine expected assemblies
were discovered (seven EditMode, two PlayMode). Exit 1 on any missing assembly.

Three EditMode assemblies are small enough — `MetaPlatform` (8), `MetaAvatars` (16),
`SurvivalHealth` (28) — to disappear without visibly moving an ~880 total, which is why the
assembly name set is asserted rather than the total.

To also catch a single test class failing to compile while its assembly survives, compare
per-assembly counts against a recorded baseline. Counts are branch-specific, so the baseline is
local rather than committed:

```sh
scripts/unity/check-test-suites.py --record --against /tmp/suite-baseline.json   # before
scripts/unity/check-test-suites.py --against /tmp/suite-baseline.json            # after; fails if any shrank
```

Add `--exact` when the change should add no tests at all, so an unexpected growth also fails.

Run the combined local validation wrapper before moving a Unity-impacting pull request to review or merge when an APK build is also needed:

```sh
scripts/unity/run-local-validation.sh /tmp/blockiverse-vr-development.apk
```

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

`scripts/unity/build-development-apk.sh` does **not** stamp a version (ADR 0005).
A development APK keeps whatever `ProjectSettings.asset` already carries, so
every local build reports the same version and the file stops churning. Set
`UNITY_ANDROID_VERSION_NAME` and `UNITY_ANDROID_VERSION_CODE` when a test
genuinely requires specific package metadata; the script forwards them only
when they are set.

One-time transition: a headset still carrying a build from before this policy has a
timestamped `versionCode` far higher than the committed one, so Android refuses the
install as `INSTALL_FAILED_VERSION_DOWNGRADE`. Pass `--downgrade` once:

```sh
hzdb app install -r -g --downgrade Builds/Android/BlockiverseVR-development.apk
```

After that every local build shares the committed code and a plain `-r` replace works,
because Android rejects only a LOWER code, never an equal one.

This also means two locally built APKs can join each other. `Application.version`
is the join gate — `BlockiverseNetworkSession` refuses a peer whose version
differs — so the previous timestamped stamp left two dev builds made minutes
apart unable to connect, which is precisely the LAN case a dev build exists to
test.

Unity MCP builds should invoke
`Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid()`. There is no
longer a separate call to stamp a development version first: falling back to the
committed `ProjectSettings.asset` values is now the intended behaviour, not the
failure it used to be.

`hzdb` is installed under the active default `nvm` Node with `npm install -g @meta-quest/hzdb@1.2.1`; the expected current executable path is `/Users/ericslutz/.nvm/versions/node/v24.16.0/bin/hzdb`. Prefer `hzdb` for Quest device discovery, APK install and launch, log capture, screenshots, screen recordings, file transfer, and performance captures. If `hzdb device list` cannot see a connected Quest from a Codex sandboxed shell, rerun physical-device commands outside the sandbox before treating validation as blocked. Use the Meta XR Simulator or physical Quest 3/Quest 3S validation flow when a behavior cannot be proven by EditMode or PlayMode tests alone. Use OVR Metrics or equivalent captures for Quest performance work, and store summaries under `docs/testing/performance/`.

When Unity is already open to keep MCP for Unity and Unity Skills alive, prefer
the open-Editor install path instead of closing the project for batchmode. Build
through MCP or `Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid()`,
then let the running Unity editor spawn `hzdb app install --replace
--grant-permissions Builds/Android/BlockiverseVR-development.apk`. This keeps
the editor, MCP bridge, and Unity Skills server alive while still replacing the
APK on the headset. If `hzdb app launch --cold-start --wait-for-idle --verify`
reports a Quest system dialog such as `LaunchCheckControllerRequiredDialogActivity`,
treat install as successful but launch verification as blocked by headset state.

For Quest pointer/ray changes, validate the normal development APK in the real game title-menu and New World flow, not a stub diagnostic scene or diagnostic APK. Confirm both Comfort dominant-hand settings:

- Right-handed mode shows one stable interaction ray from the right controller/tool hand; the support-hand interaction ray remains hidden except while teleport owns a locomotion ray.
- Left-handed mode shows one stable interaction ray from the left controller/tool hand; the support-hand interaction ray remains hidden except while teleport owns a locomotion ray.
- Menu hover suppresses block editing for the active ray, missed menu rays use the short menu aim guide, world targeting restores normal line length after the menu is left, routed game menus are direct world-space surfaces with no shared composition Quad, and controller/ray visuals stay on the normal main-camera render path. There is no composition menu cursor to look for: the ray itself is the pointer over a world-space menu.

Remove any temporary ray diagnostic scenes or build scripts once the issue is reproduced in the real game path. Stub ray worlds are not part of the validation gate.

## Writing Tests That Can Actually Fail

A green suite only means something if each test could have gone red. Several tests in
this repo have passed for reasons unrelated to the behaviour they named, and they are
indistinguishable from real passes: same colour, same duration, same everything.

**The check: when a test passes, ask what it would have looked like had the behaviour
been broken. If the answer is "exactly the same", it is not a test yet.**

Shapes this has actually taken here:

- **Asserting a tautology.** A registry-hash test built two identical registries and
  asserted their hashes matched. It could not fail. The real test builds two registries
  that differ only in the field under test and asserts the hash separates them.
- **Measuring outside the window.** A snapshot-pacing test sampled per-frame send counts
  only after the transfer had begun, so a regression that sent everything in one burst
  would have finished before sampling started and every sample would have read zero.
  Sampling now spans the whole operation, and the test asserts it observed the work in
  flight rather than trusting that it did.
- **A fixture too small to reach the behaviour.** That same test used a world whose
  changed-block count fitted inside a single frame's batch budget, so pacing was never
  exercised. Fixtures for a threshold must cross it, and it is worth asserting the
  fixture reached the size it intended.
- **Testing a function nothing calls.** A helper can be correct, thoroughly covered, and
  wired to nothing. Unit coverage of the helper cannot detect that; a test one level up
  that drives the real entry point can.
- **Correct per unit, wrong per path.** Each snapshot batch was individually within the
  transport payload limit while the burst of them overflowed the send queue. Where a
  limit applies to an aggregate, assert against the aggregate.

The common thread is that the assertion sat at a different level from the behaviour. When
a bug is found in tested code, the useful question is not only "what was wrong" but
"what level was the test measuring, and what level does the bug live at".

## Test Selection Rules

- Docs, governance, PR templates, issue templates, and markdown-only policy changes: run the Docs/Repo tier only.
- Pure C# logic in engine-free assemblies: run the targeted EditMode fixture first, then the Unity Full Gate before review.
- Boot scene, prefabs, input, UI, VR interaction, assets, bootstrapper, or rendering: run targeted EditMode plus the relevant Boot or interaction PlayMode filter; add the APK/Quest Gate only if Android or device behavior could change.
- Save/load, schema, worldgen, survival, networking, multiplayer, or authority changes: run targeted subsystem tests plus the relevant PlayMode or networking filter; run the Unity Full Gate before review.
- Release, signing, store, Quest comfort, Quest performance, device multiplayer, or headset-only behavior: run the Unity Full Gate plus the APK/Quest Gate.

Local Unity validation requires globally installed tools on the developer machine:

- Unity Hub installed globally, preferably with Homebrew, and Unity Editor `6000.5.8f1`.
- Android Build Support, Android SDK/NDK Tools, and OpenJDK installed through Unity Hub for that Editor version.
- A Unity Personal or higher license accepted in Unity Hub before running batchmode commands.
- `UNITY_EDITOR` set when the executable is not at `/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity`.

Release-signed builds are intentionally not produced by local scripts. Use `.github/workflows/quest-alpha.yml` to build and upload release-signed APKs from `main` or a trusted manual ref so signing material stays centralized in GitHub Actions secrets.

Record the selected validation tier, exact commands, result summary, output APK path when applicable, promoted Meta build ID when applicable, intentionally deferred validation, and any residual risk in the pull request or linked issue. Local development APKs usually use `/tmp/blockiverse-vr-development.apk`; alpha channel release-signed APKs are uploaded by the alpha release workflow.

If the project later adopts a CI-compatible Unity license, Unity Build Automation, or a self-hosted runner with an accepted local license, reintroduce hosted Unity test and build jobs in a separate issue and update this document with the new validation contract.
