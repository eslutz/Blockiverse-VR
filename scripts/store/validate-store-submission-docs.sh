#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$PROJECT_ROOT"

required_files=(
  docs/store-submission/checklist.md
  docs/store-submission/data-and-safety.md
  docs/store-submission/known-issues-and-support.md
  docs/store-submission/privacy-policy.md
  docs/store-submission/release-notes-template.md
  docs/store-submission/screenshots.md
  docs/store-submission/store-listing.md
  docs/store-submission/vrc-checklist.md
  docs/testing/performance/report-template.md
)

for file in "${required_files[@]}"; do
  test -s "$file"
done

grep -q "Meta Quest 3" docs/store-submission/store-listing.md
grep -q "Meta Quest 3S" docs/store-submission/store-listing.md
grep -q "local LAN multiplayer" docs/store-submission/store-listing.md
grep -q "https://blockiversevr.com/privacy/" docs/store-submission/privacy-policy.md
grep -q "No in-app voice chat" docs/store-submission/data-and-safety.md
grep -q "PerformanceStatsOverlay" docs/testing/performance/report-template.md
grep -q "No Meta Developer Dashboard action" docs/store-submission/checklist.md

if grep -R "PR #281\\|validate-store-submission-docs.sh.*not present" docs/store-submission docs/testing/performance; then
  echo "Store submission docs contain stale validation notes from prior PR state." >&2
  exit 1
fi

echo "Store submission docs validation passed."
