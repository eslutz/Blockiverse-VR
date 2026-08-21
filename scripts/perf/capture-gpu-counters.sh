#!/usr/bin/env bash
# Purpose: capture Adreno GPU counters from the Quest while the game is running, for
# before/after comparisons of rendering changes. Writes one timestamped log per run under
# TestResults/Performance/ so two captures can be diffed directly.
#
# The headset must be WORN and the app must be in the foreground. With the headset off, the
# system shell owns the compositor and the counters describe the dashboard rather than the
# game -- which is why this script refuses to capture unless the foreground package matches.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGE="${BLOCKIVERSE_PACKAGE:-dev.ericslutz.blockiversevr}"
LABEL="${1:-capture}"
SECONDS_TO_SAMPLE="${2:-10}"
OUTPUT_DIR="$PROJECT_ROOT/TestResults/Performance"

if ! command -v hzdb >/dev/null 2>&1; then
  echo "hzdb not found on PATH. Install the Horizon Debug Bridge CLI first." >&2
  exit 127
fi

if ! hzdb device list 2>/dev/null | grep -q "device"; then
  echo "No Quest device attached. Connect the headset and enable developer mode." >&2
  exit 1
fi

foreground="$(hzdb app foreground 2>/dev/null | tail -1 || true)"

if [[ "$foreground" != *"$PACKAGE"* ]]; then
  {
    echo "The foreground app is not $PACKAGE:"
    echo "  $foreground"
    echo
    echo "Put the headset on, launch the game, and stand where you want to measure."
    echo "With the headset off these counters describe the system dashboard, not the game."
  } >&2
  exit 2
fi

mkdir -p "$OUTPUT_DIR"
output="$OUTPUT_DIR/gpu-$LABEL-$(date -u +%Y%m%dT%H%M%SZ).log"

{
  echo "# Blockiverse GPU counter capture"
  echo "# label:    $LABEL"
  echo "# package:  $PACKAGE"
  echo "# build:    $(hzdb shell "dumpsys package $PACKAGE | grep versionName" 2>/dev/null | tr -d '[:space:]')"
  echo "# device:   $(hzdb device list --format plain 2>/dev/null | tail -1 || echo unknown)"
  echo "# sampled:  ${SECONDS_TO_SAMPLE}s"
  echo
} > "$output"

echo "Capturing ${SECONDS_TO_SAMPLE}s of GPU counters -> $output"
echo "Hold still and keep looking at the same thing until this finishes."

# Cleared first so the frame stats below cover this window and not the whole session.
hzdb shell "logcat -c" >/dev/null 2>&1 || true

# timeout runs ON THE DEVICE: macOS has no timeout binary.
hzdb shell "timeout $SECONDS_TO_SAMPLE ovrgpuprofiler -r" >> "$output" 2>&1 || true

# PerformanceStatsOverlay draws through IMGUI, which never reaches a VR eye buffer, so its
# periodic log line is the only place frame stats actually surface on device. queuedRebuilds is
# the one to watch for anything that rewrites blocks -- settling snow, fluid spread -- because
# chunk remeshing costs CPU that GPU counters cannot see.
{
  echo
  echo "# --- frame stats logged during this window (development builds only) ---"
  hzdb shell "logcat -d" 2>/dev/null | grep "Performance sample" | tail -20 || true
} >> "$output"

echo
echo "Wrote $output"
echo "Headline GPU counters:"
grep -E "% Shaders Busy|% Time ALUs Working|GPU % Bus Busy|% Texture Fetch Stall|Write Total" "$output" | tail -5 || true
echo "Frame stats:"
grep "Performance sample" "$output" | tail -3 || echo "  (none logged -- is this a development build?)"
