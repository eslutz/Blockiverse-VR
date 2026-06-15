#!/usr/bin/env bash
# Purpose: build a development-signed Quest APK for smoke testing and CI build
# validation. This proves the Android target can compile and package, but it is
# not suitable for Meta release-channel upload.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${1:-${UNITY_ANDROID_BUILD_OUTPUT:-$PROJECT_ROOT/Builds/Android/BlockiverseVR-development.apk}}"

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.3.16f1 with Android Build Support."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

mkdir -p "$(dirname "$OUTPUT_PATH")"

unity_args=(
  -batchmode
  -nographics
  -quit
  -buildTarget Android
  -projectPath "$PROJECT_ROOT"
  -executeMethod Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid
  -blockiverseBuildOutput "$OUTPUT_PATH"
  -logFile -
)

if [ -n "${UNITY_ANDROID_VERSION_NAME:-}" ]; then
  unity_args+=(-blockiverseBuildVersionName "$UNITY_ANDROID_VERSION_NAME")
fi

if [ -n "${UNITY_ANDROID_VERSION_CODE:-}" ]; then
  unity_args+=(-blockiverseBuildVersionCode "$UNITY_ANDROID_VERSION_CODE")
fi

"$UNITY_EDITOR" "${unity_args[@]}"
