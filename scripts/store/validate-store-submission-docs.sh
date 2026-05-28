#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"

required_docs=(
  "docs/store-submission/checklist.md"
  "docs/store-submission/metadata.md"
  "docs/store-submission/privacy-policy.md"
  "docs/store-submission/data-use.md"
  "docs/store-submission/screenshots.md"
  "docs/store-submission/vrc-checklist.md"
  "docs/store-submission/known-issues.md"
  "docs/store-submission/release-notes.md"
  "docs/store-submission/support.md"
)

for path in "${required_docs[@]}"; do
  if [[ ! -s "$path" ]]; then
    echo "Missing or empty required store-submission document: $path" >&2
    exit 1
  fi
done

if grep -RInE 'TODO|TBD|FIXME|PLACEHOLDER|example\.com|your-app|your app|your support|your privacy' docs/store-submission; then
  echo "Store-submission docs contain placeholder text." >&2
  exit 1
fi

required_terms=(
  "Blockiverse VR"
  "dev.ericslutz.blockiversevr"
  "Quest 3"
  "Quest 3S"
  "privacy policy"
  "LAN co-op"
  "no in-app voice"
  "out of scope"
)

for term in "${required_terms[@]}"; do
  if ! grep -Riq -- "$term" docs/store-submission; then
    echo "Store-submission docs are missing required term: $term" >&2
    exit 1
  fi
done

links=(
  "docs/store-submission/metadata.md"
  "docs/store-submission/privacy-policy.md"
  "docs/store-submission/data-use.md"
  "docs/store-submission/screenshots.md"
  "docs/store-submission/vrc-checklist.md"
  "docs/store-submission/known-issues.md"
  "docs/store-submission/release-notes.md"
  "docs/store-submission/support.md"
)

for target in "${links[@]}"; do
  if ! grep -q "(${target#docs/store-submission/})" docs/store-submission/checklist.md; then
    echo "Checklist does not link required document: $target" >&2
    exit 1
  fi
done

echo "Store submission docs validation passed."
