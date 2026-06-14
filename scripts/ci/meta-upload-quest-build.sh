#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/ci/meta-upload-quest-build.sh --channel <channel> --apk <path> --notes-file <path> --output <path> --summary <path> [--version <tag>] [--source-sha <sha>] [--debug-symbols-dir <path>] [--dry-run]

Required environment:
  OVR_PLATFORM_UTIL
  META_APP_ID
  META_APP_SECRET
  META_AGE_GROUP
USAGE
}

dry_run=false
channel=""
apk_path=""
notes_file=""
output_path=""
summary_path=""
debug_symbols_dir=""
version=""
source_sha=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --dry-run)
      dry_run=true
      shift
      ;;
    --channel)
      channel="${2:-}"
      shift 2
      ;;
    --apk)
      apk_path="${2:-}"
      shift 2
      ;;
    --notes-file)
      notes_file="${2:-}"
      shift 2
      ;;
    --output)
      output_path="${2:-}"
      shift 2
      ;;
    --summary)
      summary_path="${2:-}"
      shift 2
      ;;
    --debug-symbols-dir)
      debug_symbols_dir="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --source-sha)
      source_sha="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 64
      ;;
  esac
done

if [ -z "$channel" ] || [ -z "$apk_path" ] || [ -z "$notes_file" ] || [ -z "$output_path" ] || [ -z "$summary_path" ]; then
  usage
  exit 64
fi

required_env=(OVR_PLATFORM_UTIL META_APP_ID META_APP_SECRET META_AGE_GROUP)
for variable in "${required_env[@]}"; do
  if [ -z "${!variable:-}" ]; then
    echo "$variable must be set for Meta release-channel uploads." >&2
    exit 65
  fi
done

case "$META_AGE_GROUP" in
  TEENS_AND_ADULTS|MIXED_AGES|CHILDREN) ;;
  *)
    echo "META_AGE_GROUP must be TEENS_AND_ADULTS, MIXED_AGES, or CHILDREN." >&2
    exit 65
    ;;
esac

if [ ! -f "$apk_path" ]; then
  echo "APK not found: $apk_path" >&2
  exit 66
fi

if [ ! -f "$notes_file" ]; then
  echo "Release notes file not found: $notes_file" >&2
  exit 66
fi

if [ "$dry_run" != "true" ] && [ ! -x "$OVR_PLATFORM_UTIL" ]; then
  echo "OVR platform utility not found or not executable: $OVR_PLATFORM_UTIL" >&2
  exit 66
fi

notes_text="$(
  awk 'BEGIN { first = 1 } {
    gsub(/\\/, "\\\\")
    gsub(/"/, "\\\"")
    if (!first) {
      printf "\\n"
    }
    printf "%s", $0
    first = 0
  }' "$notes_file"
)"

cmd=(
  "$OVR_PLATFORM_UTIL"
  upload-quest-build
  --age-group "$META_AGE_GROUP"
  --app-id "$META_APP_ID"
  --app-secret "$META_APP_SECRET"
  --apk "$apk_path"
  --channel "$channel"
  --notes "$notes_text"
  --disable-progress-bar true
)

if [ -n "$debug_symbols_dir" ]; then
  if [ ! -d "$debug_symbols_dir" ]; then
    echo "Debug symbols directory not found: $debug_symbols_dir" >&2
    exit 66
  fi
  cmd+=(--debug-symbols-dir "$debug_symbols_dir" --debug-symbols-pattern "*.sym.so")
fi

print_redacted_command() {
  local redact_next=false
  local arg
  for arg in "${cmd[@]}"; do
    if [ "$redact_next" = "true" ]; then
      printf '%s ' "REDACTED"
      redact_next=false
      continue
    fi
    printf '%s ' "$arg"
    if [ "$arg" = "--app-secret" ]; then
      redact_next=true
    fi
  done
  printf '\n'
}

mkdir -p "$(dirname "$output_path")" "$(dirname "$summary_path")"

if [ "$dry_run" = "true" ]; then
  print_redacted_command
  {
    echo "Dry run only."
    echo "Build ID: dry-run-${channel}"
  } > "$output_path"
  scripts/ci/meta-parse-build-id.sh \
    --input "$output_path" \
    --summary "$summary_path" \
    --channel "$channel" \
    --version "$version" \
    --source-sha "$source_sha" \
    --action upload >/dev/null
  exit 0
fi

print_redacted_command
"${cmd[@]}" 2>&1 | tee "$output_path"
scripts/ci/meta-parse-build-id.sh \
  --input "$output_path" \
  --summary "$summary_path" \
  --channel "$channel" \
  --version "$version" \
  --source-sha "$source_sha" \
  --action upload
