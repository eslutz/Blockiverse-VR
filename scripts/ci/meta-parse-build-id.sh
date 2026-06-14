#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/ci/meta-parse-build-id.sh --input <path> --summary <path> [--channel <name>] [--version <version>] [--source-sha <sha>] [--action <name>]
USAGE
}

input_path=""
summary_path=""
channel=""
version=""
source_sha=""
action="upload"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --input)
      input_path="${2:-}"
      shift 2
      ;;
    --summary)
      summary_path="${2:-}"
      shift 2
      ;;
    --channel)
      channel="${2:-}"
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
    --action)
      action="${2:-}"
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

if [ -z "$input_path" ] || [ -z "$summary_path" ]; then
  usage
  exit 64
fi

if [ ! -f "$input_path" ]; then
  echo "Meta CLI output file not found: $input_path" >&2
  exit 66
fi

meta_build_id="$(
  sed -nE 's/.*[Bb]uild[ _-]?[Ii][Dd][^0-9A-Za-z_-]*([0-9A-Za-z_-]+).*/\1/p' "$input_path" | head -n 1
)"

if [ -z "$meta_build_id" ]; then
  meta_build_id="$(
    sed -nE 's/.*"build[_-]?id"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/ip' "$input_path" | head -n 1
  )"
fi

if [ -z "$meta_build_id" ]; then
  echo "Could not find a Meta build ID in $input_path." >&2
  exit 67
fi

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

mkdir -p "$(dirname "$summary_path")"
printf '{"meta_build_id":"%s","channel":"%s","version":"%s","source_sha":"%s","action":"%s"}\n' \
  "$(json_escape "$meta_build_id")" \
  "$(json_escape "$channel")" \
  "$(json_escape "$version")" \
  "$(json_escape "$source_sha")" \
  "$(json_escape "$action")" > "$summary_path"

printf '%s\n' "$meta_build_id"
