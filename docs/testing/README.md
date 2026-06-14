# Testing

Testing is split into:

- Repository checks for shell syntax and release workflow conventions
- GitHub-hosted Quest CI for Unity Personal activation, Unity tests, and Android smoke APK validation
- Local Unity validation for focused developer checks before review
- Meta XR Simulator and MCP-driven manual validation for canonical ruleset flows
- EditMode tests for pure C# logic
- PlayMode tests for Unity-connected systems
- Multiplayer Play Mode tests for local multi-client behavior
- Manual Quest 3 and Quest 3S smoke tests
- OVR Metrics performance captures
- Store-readiness validation before submission

Performance reports belong in `docs/testing/performance/`.

Meta XR Simulator setup, MCP configuration, and historical smoke-script notes are documented in [Meta XR Simulator And MCP Validation](meta-xr-simulator-and-mcp.md). New smoke scripts should use the canonical world presets and rulesets in `../rulesets/`.

Historical multiplayer editor-network validation, simulated latency and packet-loss checks, and active block-editing bandwidth estimates are documented in [M5 Multiplayer Validation](multiplayer-m5-validation.md). New multiplayer validation should follow [Voxel Multiplayer and Networking Ruleset](../rulesets/voxel_multiplayer_networking_ruleset.md).

Runtime diagnostics use local Unity and player logs only. Capture recent Quest player logs with:

```sh
hzdb log --tag Unity --level I --lines 200
hzdb log --tag Unity --level W --lines 200
```

Attach relevant excerpts to issues or pull requests when they are needed as validation evidence. Do not commit local device logs, screenshots, recordings, traces, APKs, or other generated validation artifacts unless a tracked artifact is explicitly required.

Run the repository checks locally with:

```sh
bash -n scripts/store/*.sh scripts/unity/*.sh
```

## GitHub Workflows

`quest-ci.yml` runs on pull requests and manual dispatch. It checks repository conventions, pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal with GameCI, runs Unity tests, builds an Android smoke APK, and uploads validation artifacts. It does not receive Meta credentials and must not publish to Meta.

`quest-alpha.yml` runs on pushes to `main` and manual dispatch of a trusted branch, tag, or commit. It pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal with GameCI, computes Android version metadata, release signs the APK, uploads the artifact bundle to GitHub Actions, and publishes the APK directly to Meta `alpha`.

`quest-promote.yml` runs by manual dispatch only. It promotes an existing Meta build through `alpha -> beta`, `beta -> rc`, or `rc -> store` and uploads a promotion record artifact. It does not rebuild APKs.

## Required GitHub Configuration

Repository secrets:

- `UNITY_LICENSE` — the Unity Personal `.ulf` license file contents.
- `UNITY_EMAIL` — the Unity account email used for Unity Personal activation.
- `UNITY_PASSWORD` — the Unity account password used for Unity Personal activation.
- `ANDROID_KEYSTORE_BASE64` — base64-encoded Android release keystore.
- `ANDROID_KEYSTORE_PASSWORD` — Android keystore password.
- `ANDROID_KEY_ALIAS` — Android key alias.
- `ANDROID_KEY_PASSWORD` — Android key password.
- `META_APP_ID` — Meta Horizon app ID for Blockiverse VR.
- `META_APP_SECRET` — Meta app secret used by OVR Platform Utility for upload and promotion.

Repository variables:

- `META_AGE_GROUP` — normally `TEENS_AND_ADULTS`.
- `OVR_PLATFORM_UTIL_LINUX_URL` — Linux OVR Platform Utility download URL.
- `OVR_PLATFORM_UTIL_LINUX_SHA256` — checksum for the downloaded utility.

Unity Personal activation follows the GameCI model: activate Unity Personal once locally, copy the generated `.ulf` file into `UNITY_LICENSE`, and provide `UNITY_EMAIL` and `UNITY_PASSWORD`. Do not commit, log, or upload the Unity license file.

## Local Unity Validation

Run Unity validation locally before moving a Unity-impacting pull request to review or merge:

```sh
scripts/unity/run-tests.sh
scripts/unity/build-development-apk.sh /tmp/blockiverse-vr-development.apk
```

Local Unity validation requires globally installed tools on the developer machine:

- Unity Hub installed globally, preferably with Homebrew, and Unity Editor `6000.3.16f1`.
- Android Build Support, Android SDK/NDK Tools, and OpenJDK installed through Unity Hub for that Editor version.
- A Unity Personal or higher license accepted in Unity Hub before running batchmode commands.
- `UNITY_EDITOR` set when the executable is not at `/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity`.

For local release signing, use:

```sh
export ANDROID_KEYSTORE_PATH=/path/to/blockiverse-release.keystore
export ANDROID_KEYSTORE_PASSWORD=...
export ANDROID_KEY_ALIAS=...
export ANDROID_KEY_PASSWORD=...
scripts/unity/build-release-apk.sh /tmp/blockiverse-vr-release.apk
```

Record the local Unity validation commands, result summary, output APK path when applicable, promoted Meta build ID when applicable, and any residual risk in the pull request or linked issue.
