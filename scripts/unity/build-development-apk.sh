#!/usr/bin/env bash
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

"$UNITY_EDITOR" \
  -batchmode \
  -nographics \
  -quit \
  -buildTarget Android \
  -projectPath "$PROJECT_ROOT" \
  -executeMethod Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid \
  -blockiverseBuildOutput "$OUTPUT_PATH" \
  -logFile -
