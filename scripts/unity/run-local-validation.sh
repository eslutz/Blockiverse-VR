#!/usr/bin/env bash
# Purpose: local pre-review validation wrapper. This script runs repository
# shell syntax checks, the full local Unity test gate, and a development APK
# build. Use it before pushing Unity-impacting changes when local Unity
# licensing and Android build support are healthy.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APK_PATH="${1:-/tmp/blockiverse-vr-development.apk}"

cd "$PROJECT_ROOT"

bash -n scripts/unity/*.sh
scripts/unity/run-tests.sh
scripts/unity/build-development-apk.sh "$APK_PATH"

printf 'Local validation completed. Development APK: %s\n' "$APK_PATH"
