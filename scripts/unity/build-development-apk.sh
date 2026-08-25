#!/usr/bin/env bash
# Purpose: build a development-signed Quest APK for smoke testing and CI build
# validation. This proves the Android target can compile and package, but it is
# not suitable for Meta release-channel upload.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${1:-${UNITY_ANDROID_BUILD_OUTPUT:-$PROJECT_ROOT/Builds/Android/BlockiverseVR-development.apk}}"
BASE_VERSION_FILE="$PROJECT_ROOT/ProjectSettings/BlockiverseVersion.txt"

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.5.8f1 with Android Build Support."
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

# A local development build does NOT stamp a version (ADR 0005). It keeps whatever
# ProjectSettings.asset already carries, and the version arguments are passed ONLY when the
# caller sets them explicitly.
#
# This script used to synthesise "${base_version}-dev.local.$(date)" unconditionally, which meant
# every build rewrote a tracked file, and -- because BlockiverseNetworkSession refuses a join when
# the peer's Application.version differs -- two development APKs built minutes apart could not
# connect to each other, breaking exactly the local LAN testing a dev build exists for.
if [ -n "${UNITY_ANDROID_VERSION_NAME:-}" ]; then
  unity_args+=(-blockiverseBuildVersionName "$UNITY_ANDROID_VERSION_NAME")
fi

if [ -n "${UNITY_ANDROID_VERSION_CODE:-}" ]; then
  unity_args+=(-blockiverseBuildVersionCode "$UNITY_ANDROID_VERSION_CODE")
fi

{
  echo "Building Blockiverse Android development APK"
  echo "  output: $OUTPUT_PATH"
  echo "  versionName: ${UNITY_ANDROID_VERSION_NAME:-<unchanged from ProjectSettings>}"
  echo "  versionCode: ${UNITY_ANDROID_VERSION_CODE:-<unchanged from ProjectSettings>}"
}

"$UNITY_EDITOR" "${unity_args[@]}"
