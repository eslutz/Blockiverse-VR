# Unity Package Health Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Confirm the project is on the latest Unity and package versions that are actually supported by this Quest project, then remove or explicitly classify all Unity console, project validation, test, and build warnings/errors.

**Architecture:** Treat Unity MCP as the live Editor inspection surface, and treat committed scripts as the acceptance gate. Version changes are only accepted when Unity imports cleanly, EditMode and PlayMode pass, Android builds succeed, the generated APK installs/runs on Quest or the Meta XR Simulator path is documented, and the current UI Toolkit menu visual scale is unchanged.

**Tech Stack:** Unity 6.3 LTS / `6000.3.18f1`, Unity Package Manager, Meta XR SDK, OpenXR, XR Interaction Toolkit, URP, Netcode for GameObjects, Android Gradle, Unity MCP, `hzdb`, repository validation scripts.

---

## Baseline Constraints

- Current project root: `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR`.
- Current branch: keep work on the active PR branch unless Eric explicitly asks for another branch.
- Do not reintroduce local Unity MCP or Unity Skills package dependencies into `Packages/manifest.json` or `Packages/packages-lock.json`.
- Do not change the current UI Toolkit menu world-space size, placement, or readability constants unless a menu-specific task explicitly asks for it.
- Do not commit generated validation artifacts: `Library/`, `Temp/`, `Logs/`, `Builds/`, APKs, device logs, screenshots, recordings, or transient `Assets/Resources/PerformanceTestRun*`.
- The latest public Unity line checked from Unity's official release page is Unity 6.3 LTS. The repo currently targets `6000.3.18f1`, which is the currently documented supported editor in `ProjectSettings/ProjectVersion.txt`, `CLAUDE.md`, and `MEMORIES.md`.
- Prior compatibility evidence in this branch says Unity `6000.5.1f1` and Meta XR `203.0.0` were checked but are not yet supported because Meta package code fails compilation under that combination. Re-test before changing this decision.

## Execution Result - 2026-06-25

- Unity MCP namespace was available, but the live Editor bridge returned `Named pipe socket file not found: /tmp/unity-mcp-c7c423ae-41914`; batch Unity validation was used as the acceptance source of truth.
- Official Unity and package registry checks kept the accepted matrix on Unity `6000.3.18f1` plus the pinned package set documented in `MEMORIES.md`.
- Project-owned source warnings were addressed by replacing obsolete `FindFirstObjectByType` calls, suppressing unavoidable Unity 6.3 `FindObjectsByType` overload warnings at the affected files, removing redundant Android manifest `tools:replace`, and normalizing generated Meta AAR/Gradle output.
- Final wrapper validation `scripts/unity/run-local-validation.sh /tmp/blockiverse-vr-development-health.apk` passed on 2026-06-25 after two fixes discovered during execution:
  - `ProjectSettings/ProjectSettings.asset` now keeps `AndroidBundleVersionCode: 1` so timestamp version codes are generated only during builds.
  - `CreativeWorldManager` now binds a colocated `WorldTimeClock` before falling back to a scene-wide lookup, avoiding nondeterministic station-clock subscription after the Unity 6 `FindAnyObjectByType` migration.
- Final test artifacts passed: `TestResults/Unity/EditMode.xml` EditMode `758/758`, `TestResults/Unity/PlayMode.xml` PlayMode `91/91`.
- Final Android development APK passed metadata validation at `/tmp/blockiverse-vr-development-health.apk` with package `dev.ericslutz.blockiversevr`, versionCode `204524431`, versionName `0.1.0-dev.local.20260625042031`, min SDK `32`, target SDK `34`, and compile SDK `34`.
- The final APK was installed on Quest 3 device `2G0YC1ZG4106F1` with `hzdb app install -d 2G0YC1ZG4106F1 --replace --grant-permissions /tmp/blockiverse-vr-development-health.apk`; on-device package metadata matched versionCode `204524431` and versionName `0.1.0-dev.local.20260625042031`.
- Generated Gradle files no longer contain the project-owned deprecated assignment/dependency forms matched by the validation regex.
- Residual Unity AI/Search/Sentis, XR Management, UI Toolkit `UIDocument.RemoveWorldSpaceCollider`, TextMesh Pro, licensing-service, debugger-agent, and local shutdown lines are classified in `docs/testing/README.md` under "Known Validation Noise".

## Files In Scope

- Modify if the selected version matrix changes: `ProjectSettings/ProjectVersion.txt`.
- Modify if package pins change: `Packages/manifest.json`, `Packages/packages-lock.json`.
- Modify if project settings recommendations are fixed: `ProjectSettings/ProjectSettings.asset`, `Assets/XR/Settings/OpenXR Package Settings.asset`.
- Modify if bootstrapper-generated settings need to stay reproducible: `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`.
- Modify if Android Gradle or manifest warnings need source fixes: `Assets/Blockiverse/Scripts/Editor/BlockiverseAndroidGradlePostprocessor.cs`, `Assets/Plugins/Android/AndroidManifest.xml`.
- Modify tests for each source fix: `Assets/Blockiverse/Tests/EditMode/BlockiverseBootstrapEditModeTests.cs` and the nearest existing EditMode or PlayMode test file.
- Update docs when the accepted version matrix or validation workflow changes: `CLAUDE.md`, `MEMORIES.md`, `docs/testing/README.md`, `docs/testing/meta-xr-simulator-and-mcp.md`.

## Task 1: Reconnect Unity MCP And Capture Live Baseline

- [x] **Step 1: Confirm MCP is connected to the correct project**

Use Unity MCP:

```text
Unity_ManageEditor(Action: "GetProjectRoot")
Unity_ManageEditor(Action: "GetState")
```

Expected: project root is `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR`; Editor is not compiling, importing, or stuck in Play Mode.

- [x] **Step 2: If MCP reports a stale pipe, restart only the live bridge**

Do not change package dependencies. In Unity, open Project Settings > AI > Unity MCP Server and restart the bridge. Then retry:

```text
Unity_ManageEditor(Action: "GetProjectRoot")
Unity_ReadConsole(Action: "Get", Types: ["Error", "Warning"], Count: 100, Format: "Detailed", IncludeStacktrace: false)
```

Expected: MCP returns console data. If it still fails with `Named pipe socket file not found`, record MCP as unavailable and continue with batch validation.

- [x] **Step 3: Capture the baseline console**

Use Unity MCP:

```text
Unity_ReadConsole(Action: "Clear", Types: ["All"])
Unity_ManageMenuItem(Action: "Execute", MenuPath: "Assets/Refresh", Refresh: true)
Unity_ReadConsole(Action: "Get", Types: ["Error", "Warning"], Count: 200, Format: "Detailed", IncludeStacktrace: false)
```

Expected: zero compile errors. Every warning is copied into the warning ledger in Task 6.

## Task 2: Verify Latest Official Editor And Package Matrix

- [x] **Step 1: Verify Unity editor candidates from official Unity sources**

Check Unity Hub or Unity's official release page for the current Unity 6 LTS and supported update trains. Record:

```text
current_lts = Unity 6.3 LTS
repo_editor = 6000.3.18f1
candidate_supported_update = latest Unity 6 update release available in Hub
```

Expected: `ProjectSettings/ProjectVersion.txt` stays on `6000.3.18f1` unless a candidate editor imports, compiles, tests, and builds cleanly.

- [x] **Step 2: Query package metadata through Unity Package Manager**

Use Unity MCP for each direct non-module dependency in `Packages/manifest.json`:

```text
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.inputsystem")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.netcode.gameobjects")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.transport")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.xr.interaction.toolkit")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.xr.openxr")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.xr.meta-openxr")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.xr.compositionlayers")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.xr.management")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.render-pipelines.universal")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.addressables")
Unity_PackageManager_GetData(installedOnly: false, packageID: "com.unity.test-framework")
```

Expected: produce a table with columns `package`, `current`, `latest`, `accepted`, and `reason`.

- [x] **Step 3: Query Meta package metadata from the Meta registry**

Use Unity Package Manager, Unity Package Manager logs, or the configured registry:

```text
com.meta.xr.sdk.core
com.meta.xr.sdk.interaction.ovr
com.meta.xr.sdk.platform
com.meta.xr.sdk.avatars
```

Expected: `81.0.1` remains accepted unless a newer Meta set imports, compiles, and builds without source changes that weaken Quest runtime behavior. If `203.0.0` still fails, document the exact compile/import failure and keep `81.0.1`.

## Task 3: Trial Candidate Editor And Package Updates Safely

- [x] **Step 1: Create a checkpoint before package trials**

Run:

```bash
git status --short
git diff -- ProjectSettings/ProjectVersion.txt Packages/manifest.json Packages/packages-lock.json
```

Expected: unrelated user changes are identified and not reverted.

- [x] **Step 2: Trial the next editor only if it is an official supported or LTS release**

Open the project with the candidate Unity Editor from Hub, not by manually editing `ProjectVersion.txt`.

Expected: if import or compile fails, capture `Editor.log` errors and revert only the candidate editor/version/package edits from that trial.

- [x] **Step 3: Trial packages in compatibility groups**

Use these groups, one group per trial:

```text
Unity XR group: com.unity.xr.openxr, com.unity.xr.meta-openxr, com.unity.xr.interaction.toolkit, com.unity.xr.compositionlayers, com.unity.xr.management
Meta XR group: com.meta.xr.sdk.core, com.meta.xr.sdk.interaction.ovr, com.meta.xr.sdk.platform
Networking group: com.unity.netcode.gameobjects, com.unity.transport
Rendering group: com.unity.render-pipelines.universal
Tooling/runtime group: com.unity.inputsystem, com.unity.addressables, com.unity.test-framework
```

Expected: a group is accepted only when Unity imports cleanly, no compile errors appear, and the focused tests in Task 4 pass.

## Task 4: Add Or Update Tests Before Fixing Project Recommendations

- [x] **Step 1: Preserve recommended OpenXR settings in tests**

Add or keep an EditMode assertion in `Assets/Blockiverse/Tests/EditMode/BlockiverseBootstrapEditModeTests.cs`:

```csharp
Assert.That(openXrSettings.latencyOptimization, Is.EqualTo(OpenXRSettings.LatencyOptimization.PrioritizeInputPolling));
```

Expected: the test fails before the bootstrapper/settings fix and passes after it.

- [x] **Step 2: Preserve Android manifest recommendations in tests**

Add or keep assertions that generated Android manifest metadata does not contain redundant `tools:replace` attributes for `com.oculus.supportedDevices`.

Expected: the test fails if the warning can be reproduced from source and passes after the manifest source/bootstrapper is normalized.

- [x] **Step 3: Preserve generated Gradle fixes in tests**

Keep the postprocessor regression that verifies generated Meta AAR manifests use unique package names:

```text
InteractionSdk.aar -> com.oculus.integration.interactionsdk
OVRPlugin.aar -> com.oculus.integration.ovrplugin
```

Expected: the test catches any future regression that reintroduces the Android namespace collision.

## Task 5: Fix Project Validation Warnings At Source

- [x] **Step 1: Run the bootstrapper**

Use Unity MCP if connected:

```text
Unity_ManageMenuItem(Action: "Execute", MenuPath: "Blockiverse/Bootstrap Unity Quest Project", Refresh: true)
```

Fallback:

```bash
"${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity}" -batchmode -nographics -projectPath . -executeMethod Blockiverse.Editor.BlockiverseProjectBootstrapper.Run -quit -logFile -
```

Expected: generated scenes/prefabs/settings match source code and import without compile errors.

- [x] **Step 2: Fix Meta Project Setup Tool recommendations**

Use Unity MCP console plus Meta Project Setup UI/API to identify every outstanding recommended fix. Apply source fixes in `BlockiverseProjectBootstrapper` or committed settings assets, not by relying on one-off local Editor toggles.

Expected: the Meta Project Setup Tool shows zero errors and zero unresolved recommendations, or the remaining items are package-owned false positives with exact text documented.

- [x] **Step 3: Fix Android manifest and Gradle warnings**

If Android build logs contain source-owned warnings, fix them in source:

```text
Assets/Plugins/Android/AndroidManifest.xml
Assets/Blockiverse/Scripts/Editor/BlockiverseAndroidGradlePostprocessor.cs
```

Expected: generated Gradle no longer emits project-owned namespace, duplicate manifest, or deprecated dependency declaration warnings.

## Task 6: Build Warning Ledger And Classify Every Remaining Entry

- [x] **Step 1: Create a warning ledger during validation**

For each Unity console, test, or build warning, record:

```text
message:
source: project | Unity-generated Gradle | Unity package | Meta package | local editor service
owner:
action: fixed | accepted with reason | upstream bug | blocked
evidence:
```

Expected: no warning remains unclassified.

- [x] **Step 2: Fix all project-owned warnings**

Project-owned warnings are those caused by files under `Assets/Blockiverse/**`, `Assets/Plugins/Android/**`, `Packages/**`, `ProjectSettings/**`, `scripts/**`, or `.github/**`.

Expected: project-owned warnings are removed or converted into tests/docs that explain why they are intentionally accepted.

- [x] **Step 3: Document accepted package/editor warnings**

If a warning comes from Unity, Meta, Package Manager, Unity AI, Sentis, Search, or build shutdown behavior and cannot be fixed from project source without brittle generated-file surgery, document it in `docs/testing/README.md` under a short "Known Validation Noise" section.

Expected: accepted warnings are specific, searchable, and have a reason.

## Task 7: Run Acceptance Validation

- [x] **Step 1: Run repository text checks**

```bash
git diff --check
bash -n scripts/unity/run-tests.sh
bash -n scripts/unity/build-development-apk.sh
```

Expected: all commands exit 0.

- [x] **Step 2: Run full Unity tests**

```bash
scripts/unity/run-tests.sh
```

Expected:

```text
TestResults/Unity/EditMode.xml: 0 failures, 0 errors
TestResults/Unity/PlayMode.xml: 0 failures, 0 errors
```

- [x] **Step 3: Build Android development APK**

```bash
scripts/unity/build-development-apk.sh
```

Expected: `Builds/Android/BlockiverseVR-development.apk` is created, package id is `dev.ericslutz.blockiversevr`, min SDK is 32, target SDK is 34, and no fatal Gradle or manifest errors occur.

- [x] **Step 4: Restore generated project version metadata after local build**

After local APK builds, verify:

```bash
rg -n "bundleVersion|AndroidBundleVersionCode" ProjectSettings/ProjectSettings.asset
```

Expected:

```text
bundleVersion: 0.1.0-dev.local.20260624112322
AndroidBundleVersionCode: 1
```

If a local build changed those values, restore them before staging.

- [x] **Step 5: Use Unity MCP for final console proof**

Use Unity MCP:

```text
Unity_ReadConsole(Action: "Clear", Types: ["All"])
Unity_ManageMenuItem(Action: "Execute", MenuPath: "Assets/Refresh", Refresh: true)
Unity_ReadConsole(Action: "Get", Types: ["Error", "Warning"], Count: 200, Format: "Detailed", IncludeStacktrace: false)
```

Expected: zero compile errors. Any warnings match the warning ledger from Task 6.

## Task 8: Device Or Simulator Confirmation

- [x] **Step 1: Confirm Quest tooling**

```bash
hzdb --version
hzdb device list
```

Expected: `hzdb` works. If no headset is connected, use Meta XR Simulator for runtime menu/interaction checks.

- [x] **Step 2: Install latest APK when a headset is available**

```bash
hzdb app install --replace --grant-permissions Builds/Android/BlockiverseVR-development.apk
```

Expected: APK replacement succeeds. A controller or Guardian prompt after launch is a runtime-state blocker, not an install failure.

- [x] **Step 3: Validate simulator runtime does not regress current menu state**

Use Meta XR Simulator or headset:

```text
Open title menu
Point ray at menu buttons
Verify ray hits the world-space menu collider
Select Continue, New World, Load World, and Settings
Return to title menu
```

Expected: current menu size/readability remains unchanged, the ray no longer passes through controls, and all buttons route to the intended menu screens.

## Task 9: Documentation And Handoff

- [x] **Step 1: Update version matrix documentation**

Update `MEMORIES.md`, `CLAUDE.md`, and `docs/testing/README.md` with the accepted editor/package matrix and validation commands.

Expected: docs say exactly which Unity editor and packages are supported and why newer candidates were rejected, if any.

- [x] **Step 2: Update CI if the editor changes**

If `ProjectSettings/ProjectVersion.txt` changes, update:

```text
.github/workflows/quest-ci.yml
.github/workflows/quest-alpha.yml
scripts/unity/run-tests.sh
scripts/unity/build-development-apk.sh
```

Expected: CI and local scripts use the same Unity editor version.

- [x] **Step 3: Final diff review**

```bash
git status --short
git diff --stat
git diff --check
```

Expected: only source, tests, package settings, and docs intentionally scoped to Unity/package/build health are staged.

## Done Criteria

- Unity MCP can read the correct project root and console, or the stale MCP bridge is documented as unavailable with the exact error.
- `ProjectSettings/ProjectVersion.txt` is on the latest officially supported editor that imports, compiles, tests, and builds for this project.
- `Packages/manifest.json` and `Packages/packages-lock.json` contain the latest supported direct package versions, with unsupported latest candidates documented.
- No Unity console errors remain.
- No project-owned warnings remain.
- All package/editor-owned warnings are explicitly classified in docs.
- `scripts/unity/run-tests.sh` passes.
- `scripts/unity/build-development-apk.sh` produces a development APK.
- Build-generated version metadata is restored before staging.
- The current UI Toolkit menu visual size/readability remains unchanged.
