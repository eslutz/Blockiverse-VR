# Testing

Testing is split into:

- Repository safety checks for shell syntax, release policy docs, and forbidden tracked files
- Local Unity validation for tests and development APK build smoke checks
- Release APK workflow checks that publish CI artifacts
- EditMode tests for pure C# logic
- PlayMode tests for Unity-connected systems
- Multiplayer Play Mode tests for local multi-client behavior
- Manual Quest 3 and Quest 3S smoke tests
- OVR Metrics performance captures
- Store-readiness validation before submission

Performance reports belong in `docs/testing/performance/`.

Run the repository checks locally with:

```sh
bash -n scripts/ci/forbidden-files.sh scripts/unity/*.sh
scripts/ci/forbidden-files.sh
test -f docs/architecture/branching-and-release.md
```

GitHub-hosted CI validates repository checks only. Unity validation is manual and local with Unity Hub Personal.

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

Current GitHub Actions workflows do not require UNITY_LICENSE, UNITY_EMAIL, or UNITY_PASSWORD secrets. Unity Personal activation is handled by Unity Hub on the local developer machine, and the local license file is not committed, copied into CI, or uploaded as an artifact.

Record the local Unity validation commands, result summary, output APK path, and any residual risk in the pull request or linked issue. The current development APK build artifact is local only, usually `/tmp/blockiverse-vr-development.apk`, and is not uploaded by GitHub Actions.

If the project later adopts a CI-compatible Unity license, Unity Build Automation, or a self-hosted runner with an accepted local license, reintroduce hosted Unity test and build jobs in a separate issue and update this document with the new validation contract.
