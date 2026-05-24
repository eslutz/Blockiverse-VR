#!/usr/bin/env sh
set -eu

forbidden_regex='^(Library|Temp|Logs|UserSettings)/|(^|/)(\.utmp|\.ci-artifacts)/|\.env(\.|$)|\.(jks|keystore|p12)$'

if git ls-files | grep -E "$forbidden_regex" >/dev/null; then
  echo "Forbidden generated, secret, or signing files are tracked:" >&2
  git ls-files | grep -E "$forbidden_regex" >&2
  exit 1
fi

echo "No forbidden files are tracked."
