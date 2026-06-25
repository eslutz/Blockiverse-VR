#!/usr/bin/env bash
# Purpose: local Unity test gate for developer machines. This script runs the
# Unity EditMode and PlayMode suites in the local editor, where LAN/multiplayer
# PlayMode tests are meaningful and stable enough to diagnose product behavior.
# Do not use this as the GitHub-hosted PR gate; Quest CI uses an Android smoke
# build because the Linux Android Unity test container is not reliable for the
# full PlayMode suite.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity}"
RESULTS_DIR="${UNITY_TEST_RESULTS_DIR:-$PROJECT_ROOT/TestResults/Unity}"
TEST_PLATFORM="all"
TEST_FILTER=""
RESULTS_NAME=""

usage() {
  cat <<'USAGE'
Usage: scripts/unity/run-tests.sh [options]

Runs Unity tests for Blockiverse VR. With no options, runs the full EditMode
suite followed by the full PlayMode suite and writes:
  TestResults/Unity/EditMode.xml
  TestResults/Unity/PlayMode.xml

Options:
  --platform EditMode|PlayMode|all  Test platform to run. Default: all.
  --filter <test-filter>            Unity test filter, such as a fixture or test fullname.
  --results-name <slug>             Result XML name without extension.
                                    Single platform: <slug>.xml
                                    all: <slug>-EditMode.xml and <slug>-PlayMode.xml
  --results-dir <path>              Result directory. Default: TestResults/Unity or UNITY_TEST_RESULTS_DIR.
  -h, --help                        Show this help.
USAGE
}

die() {
  echo "run-tests.sh: $*" >&2
  echo >&2
  usage >&2
  exit 2
}

require_value() {
  local option="$1"
  local value="${2:-}"

  if [ -z "$value" ] || [[ "$value" == --* ]]; then
    die "$option requires a value."
  fi
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --platform)
      require_value "$1" "${2:-}"
      TEST_PLATFORM="$2"
      shift 2
      ;;
    --platform=*)
      TEST_PLATFORM="${1#*=}"
      shift
      ;;
    --filter)
      require_value "$1" "${2:-}"
      TEST_FILTER="$2"
      shift 2
      ;;
    --filter=*)
      TEST_FILTER="${1#*=}"
      shift
      ;;
    --results-name)
      require_value "$1" "${2:-}"
      RESULTS_NAME="$2"
      shift 2
      ;;
    --results-name=*)
      RESULTS_NAME="${1#*=}"
      shift
      ;;
    --results-dir)
      require_value "$1" "${2:-}"
      RESULTS_DIR="$2"
      shift 2
      ;;
    --results-dir=*)
      RESULTS_DIR="${1#*=}"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "Unknown option: $1"
      ;;
  esac
done

case "$TEST_PLATFORM" in
  EditMode|PlayMode|all)
    ;;
  *)
    die "--platform must be EditMode, PlayMode, or all."
    ;;
esac

if [ -n "$RESULTS_NAME" ] && [[ "$RESULTS_NAME" == *"/"* ]]; then
  die "--results-name must be a filename slug, not a path."
fi

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.3.18f1 with Android Build Support."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

mkdir -p "$RESULTS_DIR"

run_test_platform() {
  local platform="$1"
  local results_file="$RESULTS_DIR/${platform}.xml"

  if [ -n "$RESULTS_NAME" ]; then
    if [ "$TEST_PLATFORM" = "all" ]; then
      results_file="$RESULTS_DIR/${RESULTS_NAME}-${platform}.xml"
    else
      results_file="$RESULTS_DIR/${RESULTS_NAME}.xml"
    fi
  fi

  local unity_args=(
    "$UNITY_EDITOR"
    -batchmode
    -projectPath "$PROJECT_ROOT"
    -runTests
    -testPlatform "$platform"
    -testResults "$results_file"
    -logFile -
  )

  if [ "$platform" = "EditMode" ] || [ "${UNITY_PLAYMODE_NOGRAPHICS:-0}" = "1" ]; then
    unity_args+=(-nographics)
  fi

  if [ -n "$TEST_FILTER" ]; then
    unity_args+=(-testFilter "$TEST_FILTER")
  fi

  "${unity_args[@]}"
}

case "$TEST_PLATFORM" in
  EditMode|PlayMode)
    run_test_platform "$TEST_PLATFORM"
    ;;
  all)
    run_test_platform EditMode
    run_test_platform PlayMode
    ;;
esac
