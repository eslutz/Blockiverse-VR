#!/usr/bin/env bash
# Purpose: build the Linux x86-64 dedicated server. The resulting tree is what ships
# both as the downloadable archive and as the container image, so the two are cut
# from one artifact and cannot drift.
#
# The version matters here in a way it does not for a client build: a server
# advertises it in the connection-approval payload, and a client whose version
# differs is refused with GameVersionMismatch. Server and client must be built at
# the same version or nobody can join.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${1:-${UNITY_LINUX_SERVER_BUILD_OUTPUT:-$PROJECT_ROOT/Builds/LinuxServer/BlockiverseServer}}"
BASE_VERSION_FILE="$PROJECT_ROOT/ProjectSettings/BlockiverseVersion.txt"

if [ ! -x "$UNITY_EDITOR" ]; then
  {
    echo "Unity editor not found or not executable: $UNITY_EDITOR"
    echo "Install Unity Hub globally, preferably with Homebrew, then install Unity 6000.5.8f1 with the"
    echo "Linux Dedicated Server Build Support module (module id: linux-server)."
    echo "Set UNITY_EDITOR to the Unity executable path if it is installed elsewhere."
  } >&2
  exit 127
fi

# PlaybackEngines sits beside Unity.app, three levels up from Contents/MacOS/Unity.
playback_engines="$(dirname "$UNITY_EDITOR")/../../../PlaybackEngines"
if [ ! -d "$playback_engines/LinuxStandaloneSupport" ]; then
  {
    echo "Linux Dedicated Server Build Support is not installed for this editor."
    echo "Install it with:"
    echo "  \"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub\" -- --headless install-modules \\"
    echo "    --version 6000.5.8f1 --module linux-server --childModules"
  } >&2
  exit 69
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
BLOCKIVERSE_SERVER_VERSION="${BLOCKIVERSE_SERVER_VERSION:-${base_version}-dev.local.${build_stamp}}"

mkdir -p "$(dirname "$OUTPUT_PATH")"

unity_args=(
  -batchmode
  -nographics
  -quit
  -buildTarget Linux64
  -projectPath "$PROJECT_ROOT"
  -executeMethod Blockiverse.Editor.BlockiverseBuildSmoke.BuildLinuxServer
  -blockiverseBuildOutput "$OUTPUT_PATH"
  -blockiverseBuildVersionName "$BLOCKIVERSE_SERVER_VERSION"
  -logFile -
)

{
  echo "Building Blockiverse Linux dedicated server"
  echo "  output:  $OUTPUT_PATH"
  echo "  version: $BLOCKIVERSE_SERVER_VERSION"
  echo "  note:    clients must be built at this same version or joins are refused"
}

"$UNITY_EDITOR" "${unity_args[@]}"

# The build regenerates Server.unity, so check the scene that was actually just built rather than
# whatever was on disk beforehand.
#
# This catches a failure mode no test can: a component whose script lives in an assembly excluded
# from LinuxStandalone64 deserializes as "The referenced script on this Behaviour is missing!" on
# every server start. The build SUCCEEDS -- it is a runtime log line, not a compile error -- so
# without this gate a broken artifact ships green. The shared network manager prefab carries exactly
# such a component (SurvivalVitalsRuntime, from Blockiverse.Gameplay).
if command -v python3 >/dev/null 2>&1; then
  echo "Checking the generated server scene for components from excluded assemblies"
  if ! python3 "$PROJECT_ROOT/scripts/unity/check-prefab-assemblies.py" \
      --platform LinuxStandalone64 \
      --assets "$PROJECT_ROOT/Assets" \
      --asset "$PROJECT_ROOT/Assets/Blockiverse/Scenes/Server.unity"; then
    {
      echo
      echo "The server scene contains components the server build cannot load."
      echo "Strip them in BlockiverseProjectBootstrapper.Server.StripClientOnlyComponents."
    } >&2
    exit 65
  fi
  # Both scenes instantiate the same network manager prefab and are configured by one method, so
  # they agree by construction -- this makes that structural, instead of something to remember.
  echo "Checking client/server Netcode connection-config parity"
  if ! python3 "$PROJECT_ROOT/scripts/unity/check-netcode-config-parity.py"; then
    {
      echo
      echo "A scene overrides a NetworkConfig field that Netcode hashes into the connection config."
      echo "Clients would be dropped before approval, with no rejection reason on either side."
    } >&2
    exit 65
  fi
else
  echo "python3 not found; skipping the excluded-assembly scene check" >&2
fi
