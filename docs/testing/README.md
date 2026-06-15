# Testing

Testing is split into:

- Repository checks for shell syntax and release workflow conventions
- GitHub-hosted Quest CI for Unity Personal activation and Android smoke APK validation
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
bash -n scripts/unity/*.sh
```

## GitHub Workflows

`quest-ci.yml` runs on pull requests and manual dispatch. It checks repository conventions, pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal through GameCI, builds an Android smoke APK, and uploads that APK as a validation artifact. It does not receive Meta credentials and must not publish to Meta. The Android smoke build is the GitHub-hosted Unity validation gate: it catches package import, compile, Android target, and APK packaging failures. The build does not require Meta Avatars sample preset zips while Blockiverse fallback preset avatars are disabled; if fallback presets are enabled later, the packaged Quest preset assets must be intentionally added. Full EditMode and PlayMode tests are intentionally local-only because GitHub-hosted UnityCI Android test containers are not reliable for the local LAN multiplayer PlayMode suite.

`quest-alpha.yml` runs on pushes to `main` and manual dispatch of a trusted branch, tag, or commit. It pulls Git LFS assets, restores the Unity `Library` cache, activates Unity Personal with GameCI, computes Android version metadata, release signs the APK, uploads the artifact bundle to GitHub Actions, and publishes the APK directly to Meta `alpha`.

`quest-promote.yml` runs by manual dispatch only. It requires the tested Meta build ID, promotes that selected build through `alpha -> beta`, `beta -> rc`, or `rc -> store`, and uploads a promotion record artifact. It does not rebuild APKs.

## Automated Versus Local Validation

Automated GitHub Actions validation is optimized for deterministic signals on GitHub-hosted runners:

- `quest-ci.yml` verifies repository checks and Android smoke APK packaging for pull requests.
- `quest-alpha.yml` builds and uploads a release-signed APK to Meta `alpha` after merge or manual trusted dispatch.
- `quest-promote.yml` promotes an existing tested Meta build ID without rebuilding.

Local validation is optimized for behavior:

- `scripts/unity/run-tests.sh` runs the full local EditMode and PlayMode test gate.
- `scripts/unity/run-local-validation.sh` runs shell syntax checks, full local Unity tests, and a development APK build.
- `scripts/unity/build-development-apk.sh` produces a development APK for smoke installation.
- Release-signed APKs are produced by `.github/workflows/quest-alpha.yml` only, using GitHub Actions secrets and the `meta-alpha` environment.

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

Run the combined local validation wrapper before moving a Unity-impacting pull request to review or merge:

```sh
scripts/unity/run-local-validation.sh /tmp/blockiverse-vr-development.apk
```

Run only the Unity test gate when an APK build is not needed:

```sh
scripts/unity/run-tests.sh
```

Local Unity validation requires globally installed tools on the developer machine:

- Unity Hub installed globally, preferably with Homebrew, and Unity Editor `6000.3.16f1`.
- Android Build Support, Android SDK/NDK Tools, and OpenJDK installed through Unity Hub for that Editor version.
- A Unity Personal or higher license accepted in Unity Hub before running batchmode commands.
- `UNITY_EDITOR` set when the executable is not at `/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity`.

Release-signed builds are intentionally not produced by local scripts. Use
`.github/workflows/quest-alpha.yml` to build and upload release-signed APKs from
`main` or a trusted manual ref so signing material stays centralized in GitHub
Actions secrets.

Record the local Unity validation commands, result summary, output APK path when applicable, promoted Meta build ID when applicable, and any residual risk in the pull request or linked issue.
