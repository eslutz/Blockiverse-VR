#!/usr/bin/env sh
set -eu

forbidden_regex='^(Library|Temp|Logs|UserSettings)/|(^|/)UserSettings/|(^|/)(\.utmp|\.ci-artifacts)/|\.env(\.|$)|\.(jks|keystore|p12)$'
tracked_forbidden="$(git ls-files | grep -E "$forbidden_regex" || true)"
existing_forbidden=""

if [ -n "$tracked_forbidden" ]; then
  existing_forbidden="$(
    printf '%s\n' "$tracked_forbidden" | while IFS= read -r path; do
      [ -e "$path" ] && printf '%s\n' "$path"
    done || true
  )"
fi

if [ -n "$existing_forbidden" ]; then
  echo "Forbidden generated, secret, or signing files are tracked:" >&2
  printf '%s\n' "$existing_forbidden" >&2
  exit 1
fi

echo "No forbidden files are tracked."
