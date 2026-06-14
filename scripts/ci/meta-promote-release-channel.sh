#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/ci/meta-promote-release-channel.sh --source-channel <channel> --destination-channel <channel> --build-id <id> --output <path> --summary <path> [--version <tag>] [--source-sha <sha>] [--dry-run]

Required environment:
  OVR_PLATFORM_UTIL
  META_APP_ID
  META_APP_SECRET
  META_AGE_GROUP
USAGE
}

dry_run=false
source_channel=""
destination_channel=""
meta_build_id=""
output_path=""
summary_path=""
version=""
source_sha=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --dry-run)
      dry_run=true
      shift
      ;;
    --source-channel)
      source_channel="${2:-}"
      shift 2
      ;;
    --destination-channel)
      destination_channel="${2:-}"
      shift 2
      ;;
    --build-id)
      meta_build_id="${2:-}"
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

if [ -z "$source_channel" ] || [ -z "$destination_channel" ] || [ -z "$meta_build_id" ] || [ -z "$output_path" ] || [ -z "$summary_path" ]; then
  usage
  exit 64
fi

required_env=(OVR_PLATFORM_UTIL META_APP_ID META_APP_SECRET META_AGE_GROUP)
for variable in "${required_env[@]}"; do
  if [ -z "${!variable:-}" ]; then
    echo "$variable must be set for Meta release-channel promotion." >&2
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

if [ "$dry_run" != "true" ] && [ ! -x "$OVR_PLATFORM_UTIL" ]; then
  echo "OVR platform utility not found or not executable: $OVR_PLATFORM_UTIL" >&2
  exit 66
fi

cmd=(
  "$OVR_PLATFORM_UTIL"
  set-release-channel-build
  --age-group "$META_AGE_GROUP"
  --app-id "$META_APP_ID"
  --app-secret "$META_APP_SECRET"
  --source-channel "$source_channel"
  --destination-channel "$destination_channel"
  --build-id "$meta_build_id"
  --disable-progress-bar true
)

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

write_summary() {
  local action="$1"
  mkdir -p "$(dirname "$summary_path")"
  printf '{"meta_build_id":"%s","source_channel":"%s","destination_channel":"%s","version":"%s","source_sha":"%s","action":"%s"}\n' \
    "$meta_build_id" \
    "$source_channel" \
    "$destination_channel" \
    "$version" \
    "$source_sha" \
    "$action" > "$summary_path"
}

mkdir -p "$(dirname "$output_path")" "$(dirname "$summary_path")"

if [ "$dry_run" = "true" ]; then
  print_redacted_command
  {
    echo "Dry run only."
    echo "Build ID: $meta_build_id"
  } > "$output_path"
  write_summary promote
  exit 0
fi

print_redacted_command
"${cmd[@]}" 2>&1 | tee "$output_path"
write_summary promote
