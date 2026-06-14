#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/blockiverse-meta-release-test.XXXXXX")"
trap 'rm -rf "$TMP_DIR"' EXIT

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

assert_contains() {
  local haystack="$1"
  local needle="$2"
  if [[ "$haystack" != *"$needle"* ]]; then
    fail "expected output to contain: $needle"
  fi
}

assert_not_contains() {
  local haystack="$1"
  local needle="$2"
  if [[ "$haystack" == *"$needle"* ]]; then
    fail "expected output to omit: $needle"
  fi
}

cd "$PROJECT_ROOT"

apk_path="$TMP_DIR/BlockiverseVR-test.apk"
notes_path="$TMP_DIR/release-notes.md"
upload_output_path="$TMP_DIR/meta-upload-output.txt"
upload_summary_path="$TMP_DIR/meta-upload-summary.json"
promote_output_path="$TMP_DIR/meta-promotion-output.txt"
promote_summary_path="$TMP_DIR/meta-promotion-summary.json"

printf 'fake apk\n' > "$apk_path"
printf 'Line one\nLine "two"\n' > "$notes_path"

export OVR_PLATFORM_UTIL="$TMP_DIR/ovr-platform-util"
export META_APP_ID="1234567890"
export META_APP_SECRET="super-secret-value"
export META_AGE_GROUP="MIXED_AGES"

printf '#!/usr/bin/env bash\nprintf "fake ovr-platform-util %%s\\n" "$*"\n' > "$OVR_PLATFORM_UTIL"
chmod +x "$OVR_PLATFORM_UTIL"

upload_dry_run="$(
  scripts/ci/meta-upload-quest-build.sh \
    --dry-run \
    --channel alpha \
    --apk "$apk_path" \
    --notes-file "$notes_path" \
    --output "$upload_output_path" \
    --summary "$upload_summary_path"
)"

assert_contains "$upload_dry_run" "upload-quest-build"
assert_contains "$upload_dry_run" "--age-group MIXED_AGES"
assert_contains "$upload_dry_run" "--app-id 1234567890"
assert_contains "$upload_dry_run" "--app-secret REDACTED"
assert_contains "$upload_dry_run" "--apk $apk_path"
assert_contains "$upload_dry_run" "--channel alpha"
assert_contains "$upload_dry_run" "--notes Line one\\nLine \\\"two\\\""
assert_not_contains "$upload_dry_run" "super-secret-value"

if env -u META_AGE_GROUP \
  OVR_PLATFORM_UTIL="$OVR_PLATFORM_UTIL" \
  META_APP_ID="$META_APP_ID" \
  META_APP_SECRET="$META_APP_SECRET" \
  scripts/ci/meta-upload-quest-build.sh \
    --dry-run \
    --channel beta \
    --apk "$apk_path" \
    --notes-file "$notes_path" \
    --output "$upload_output_path" \
    --summary "$upload_summary_path" >/dev/null 2>&1; then
  fail "meta-upload-quest-build.sh should fail without META_AGE_GROUP"
fi

cat > "$upload_output_path" <<'UPLOAD_OUTPUT'
Upload complete.
Build ID: 987654321012345
UPLOAD_OUTPUT

scripts/ci/meta-parse-build-id.sh \
  --input "$upload_output_path" \
  --summary "$upload_summary_path" \
  --channel beta \
  --version v0.1.0-beta.run42.1 \
  --source-sha abcdef123456 >/dev/null

grep -q '"meta_build_id":"987654321012345"' "$upload_summary_path" \
  || fail "parsed upload summary should contain the build id"

promote_rc_dry_run="$(
  scripts/ci/meta-promote-release-channel.sh \
    --dry-run \
    --source-channel beta \
    --destination-channel rc \
    --build-id 987654321012345 \
    --output "$promote_output_path" \
    --summary "$promote_summary_path" \
    --version v0.1.0-rc.1 \
    --source-sha abcdef123456
)"

assert_contains "$promote_rc_dry_run" "set-release-channel-build"
assert_contains "$promote_rc_dry_run" "--source-channel beta"
assert_contains "$promote_rc_dry_run" "--destination-channel rc"
assert_contains "$promote_rc_dry_run" "--build-id 987654321012345"
assert_contains "$promote_rc_dry_run" "--age-group MIXED_AGES"
assert_not_contains "$promote_rc_dry_run" "super-secret-value"

promote_store_dry_run="$(
  scripts/ci/meta-promote-release-channel.sh \
    --dry-run \
    --source-channel rc \
    --destination-channel store \
    --build-id 987654321012345 \
    --output "$promote_output_path" \
    --summary "$promote_summary_path"
)"

assert_contains "$promote_store_dry_run" "--source-channel rc"
assert_contains "$promote_store_dry_run" "--destination-channel store"

echo "Meta release helper tests passed."
