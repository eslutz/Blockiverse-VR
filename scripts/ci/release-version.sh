#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/ci/release-version.sh <production|rc|beta|alpha> <X.Y.Z> [sequence]

Examples:
  scripts/ci/release-version.sh production 1.2.3
  scripts/ci/release-version.sh rc 1.2.3 1
  scripts/ci/release-version.sh beta 1.2.3 run42.1
  scripts/ci/release-version.sh alpha 1.2.3 pr17.42.1
USAGE
}

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  usage
  exit 64
fi

channel="$1"
base_version="$2"
sequence="${3:-}"

base_version_pattern='^[0-9]+\.[0-9]+\.[0-9]+$'
sequence_pattern='^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$'

if [[ ! "$base_version" =~ $base_version_pattern ]]; then
  echo "Base version must use X.Y.Z SemVer syntax without a leading v: $base_version" >&2
  exit 65
fi

if [ -n "$sequence" ] && [[ ! "$sequence" =~ $sequence_pattern ]]; then
  echo "Sequence must use SemVer pre-release identifiers: $sequence" >&2
  exit 65
fi

case "$channel" in
  production)
    if [ -n "$sequence" ]; then
      echo "Production versions must not include a pre-release sequence." >&2
      exit 65
    fi
    printf 'v%s\n' "$base_version"
    ;;
  rc|beta|alpha)
    if [ -n "$sequence" ]; then
      printf 'v%s-%s.%s\n' "$base_version" "$channel" "$sequence"
    else
      printf 'v%s-%s\n' "$base_version" "$channel"
    fi
    ;;
  *)
    echo "Unknown release channel: $channel" >&2
    usage
    exit 64
    ;;
esac
