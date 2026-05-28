#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [ -z "${UNITY_EDITOR:-}" ] && [ -n "${UNITY_PATH:-}" ]; then
  UNITY_EDITOR="$UNITY_PATH/Editor/Unity"
fi
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${1:-${UNITY_ANDROID_BUILD_OUTPUT:-$PROJECT_ROOT/Builds/Android/BlockiverseVR-release.apk}}"

required_env=(
  ANDROID_KEYSTORE_PATH
  ANDROID_KEYSTORE_PASSWORD
  ANDROID_KEY_ALIAS
  ANDROID_KEY_PASSWORD
)

for variable in "${required_env[@]}"; do
  if [ -z "${!variable:-}" ]; then
    echo "$variable must be set for Android release signing." >&2
    exit 64
  fi
done

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.3.16f1 with Android Build Support."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

if [ ! -f "$ANDROID_KEYSTORE_PATH" ]; then
  echo "Android keystore not found: $ANDROID_KEYSTORE_PATH" >&2
  exit 66
fi

mkdir -p "$(dirname "$OUTPUT_PATH")"

unity_args=(
  -batchmode
  -nographics
  -quit
  -buildTarget Android
  -projectPath "$PROJECT_ROOT"
  -executeMethod Blockiverse.Editor.BlockiverseBuildSmoke.BuildReleaseAndroid
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
