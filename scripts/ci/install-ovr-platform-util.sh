#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/ci/install-ovr-platform-util.sh [--output <path>]

Required environment:
  OVR_PLATFORM_UTIL_LINUX_URL
  OVR_PLATFORM_UTIL_LINUX_SHA256
USAGE
}

output_path="${RUNNER_TEMP:-/tmp}/ovr-platform-util/ovr-platform-util"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --output)
      output_path="${2:-}"
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

if [ -z "$output_path" ]; then
  echo "--output must not be empty." >&2
  exit 64
fi

if [ -z "${OVR_PLATFORM_UTIL_LINUX_URL:-}" ]; then
  echo "OVR_PLATFORM_UTIL_LINUX_URL must be set." >&2
  exit 65
fi

if [ -z "${OVR_PLATFORM_UTIL_LINUX_SHA256:-}" ]; then
  echo "OVR_PLATFORM_UTIL_LINUX_SHA256 must be set." >&2
  exit 65
fi

install_dir="$(dirname "$output_path")"
download_path="${output_path}.download"

mkdir -p "$install_dir"
curl -fsSL "$OVR_PLATFORM_UTIL_LINUX_URL" -o "$download_path"

if command -v sha256sum >/dev/null 2>&1; then
  printf '%s  %s\n' "$OVR_PLATFORM_UTIL_LINUX_SHA256" "$download_path" | sha256sum --check --status
else
  actual_sha="$(shasum -a 256 "$download_path" | awk '{print $1}')"
  if [ "$actual_sha" != "$OVR_PLATFORM_UTIL_LINUX_SHA256" ]; then
    echo "SHA256 mismatch for $download_path" >&2
    echo "expected: $OVR_PLATFORM_UTIL_LINUX_SHA256" >&2
    echo "actual:   $actual_sha" >&2
    exit 66
  fi
fi

mv "$download_path" "$output_path"
chmod +x "$output_path"
"$output_path" version

if [ -n "${GITHUB_ENV:-}" ]; then
  echo "OVR_PLATFORM_UTIL=$output_path" >> "$GITHUB_ENV"
fi

printf '%s\n' "$output_path"
