#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
RESULTS_DIR="${UNITY_TEST_RESULTS_DIR:-$PROJECT_ROOT/TestResults/Unity}"

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.3.16f1 with Android Build Support."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

mkdir -p "$RESULTS_DIR"

run_test_platform() {
  local platform="$1"
  local results_file="$RESULTS_DIR/${platform}.xml"

  "$UNITY_EDITOR" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_ROOT" \
    -runTests \
    -testPlatform "$platform" \
    -testResults "$results_file" \
    -logFile -
}

run_test_platform EditMode
run_test_platform PlayMode
