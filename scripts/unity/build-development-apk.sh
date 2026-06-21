#!/usr/bin/env bash
# Purpose: build a development-signed Quest APK for smoke testing and CI build
# validation. This proves the Android target can compile and package, but it is
# not suitable for Meta release-channel upload.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${1:-${UNITY_ANDROID_BUILD_OUTPUT:-$PROJECT_ROOT/Builds/Android/BlockiverseVR-development.apk}}"
BASE_VERSION_FILE="$PROJECT_ROOT/ProjectSettings/BlockiverseVersion.txt"
ANDROID_VERSION_CODE_EPOCH=1577836800

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.3.16f1 with Android Build Support."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

if [ ! -f "$BASE_VERSION_FILE" ]; then
  echo "Blockiverse version file not found: $BASE_VERSION_FILE" >&2
  exit 66
fi

base_version="$(tr -d '[:space:]' < "$BASE_VERSION_FILE")"
if ! printf '%s' "$base_version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "ProjectSettings/BlockiverseVersion.txt must contain MAJOR.MINOR.PATCH without a leading v." >&2
  exit 64
fi

build_stamp="$(date -u +%Y%m%d%H%M%S)"
UNITY_ANDROID_VERSION_NAME="${UNITY_ANDROID_VERSION_NAME:-${base_version}-dev.local.${build_stamp}}"
UNITY_ANDROID_VERSION_CODE="${UNITY_ANDROID_VERSION_CODE:-$(( $(date -u +%s) - ANDROID_VERSION_CODE_EPOCH ))}"

mkdir -p "$(dirname "$OUTPUT_PATH")"

unity_args=(
  -batchmode
  -nographics
  -quit
  -buildTarget Android
  -projectPath "$PROJECT_ROOT"
  -executeMethod Blockiverse.Editor.BlockiverseBuildSmoke.BuildDevelopmentAndroid
  -blockiverseBuildOutput "$OUTPUT_PATH"
  -blockiverseBuildVersionName "$UNITY_ANDROID_VERSION_NAME"
  -blockiverseBuildVersionCode "$UNITY_ANDROID_VERSION_CODE"
  -logFile -
)

{
  echo "Building Blockiverse Android development APK"
  echo "  output: $OUTPUT_PATH"
  echo "  versionName: $UNITY_ANDROID_VERSION_NAME"
  echo "  versionCode: $UNITY_ANDROID_VERSION_CODE"
}

"$UNITY_EDITOR" "${unity_args[@]}"
