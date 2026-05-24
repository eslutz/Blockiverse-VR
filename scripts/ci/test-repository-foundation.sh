#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$ROOT_DIR/scripts/ci/repository-foundation.sh"

if [ ! -x "$SCRIPT" ]; then
  echo "Missing executable script: $SCRIPT" >&2
  exit 1
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

assert_contains() {
  local file="$1"
  local expected="$2"

  if ! grep -Fq "$expected" "$file"; then
    echo "Expected output to contain: $expected" >&2
    echo "Actual output:" >&2
    cat "$file" >&2
    exit 1
  fi
}

write_file() {
  local path="$1"
  shift

  mkdir -p "$(dirname "$path")"
  printf '%s\n' "$@" > "$path"
}

remove_lines_containing() {
  local path="$1"
  local pattern="$2"
  local temp_path="$path.tmp"

  grep -Fv "$pattern" "$path" > "$temp_path"
  mv "$temp_path" "$path"
}

write_foundation_fixture() {
  local fixture="$1"

  mkdir -p \
    "$fixture/.github/ISSUE_TEMPLATE" \
    "$fixture/.github/workflows" \
    "$fixture/docs/adr" \
    "$fixture/docs/architecture" \
    "$fixture/docs/art-direction" \
    "$fixture/docs/roadmap" \
    "$fixture/docs/store-submission" \
    "$fixture/docs/testing" \
    "$fixture/scripts/ci"

  for file in README.md LICENSE.md NOTICE.md CONTRIBUTING.md CODE_OF_CONDUCT.md SECURITY.md CHANGELOG.md; do
    write_file "$fixture/$file" "$file"
  done

  write_file "$fixture/.github/pull_request_template.md" "Pull request template"
  write_file "$fixture/.github/CODEOWNERS" "* @eslutz"

  for template in epic feature story task bug tech-debt spike; do
    write_file "$fixture/.github/ISSUE_TEMPLATE/$template.yml" "name: $template"
  done

  write_file "$fixture/.gitignore" \
    "[Ll]ibrary/" \
    "[Tt]emp/" \
    "[Ll]ogs/" \
    "[Uu]ser[Ss]ettings/" \
    ".env" \
    ".env.*" \
    "*.keystore" \
    "*.jks" \
    "*.p12"

  write_file "$fixture/.gitattributes" \
    "*.png filter=lfs diff=lfs merge=lfs -text" \
    "*.psd filter=lfs diff=lfs merge=lfs -text" \
    "*.blend filter=lfs diff=lfs merge=lfs -text" \
    "*.fbx filter=lfs diff=lfs merge=lfs -text" \
    "*.wav filter=lfs diff=lfs merge=lfs -text" \
    "*.mp3 filter=lfs diff=lfs merge=lfs -text" \
    "*.apk filter=lfs diff=lfs merge=lfs -text" \
    "*.cs text eol=lf" \
    "*.asmdef text eol=lf" \
    "*.shader text eol=lf" \
    "*.mat text eol=lf" \
    "*.prefab text eol=lf" \
    "*.unity text eol=lf" \
    "*.asset text eol=lf" \
    "*.meta text eol=lf"

  write_file "$fixture/docs/architecture/branching-and-release.md" \
    "main is protected" \
    "There is no long-lived develop branch." \
    "Feature work uses short-lived feature/* branches." \
    "Release tags use v* and are cut from main."

  write_file "$fixture/.github/workflows/ci-pr.yml" \
    "on:" \
    "  pull_request:" \
    "    branches:" \
    "      - main" \
    "jobs:" \
    "  repository-checks:" \
    "    steps:" \
    "      - run: scripts/ci/test-repository-foundation.sh" \
    "      - run: scripts/ci/repository-foundation.sh" \
    "      - run: scripts/ci/forbidden-files.sh"

  write_file "$fixture/.github/workflows/build-main-dev-apk.yml" \
    "on:" \
    "  push:" \
    "    branches:" \
    "      - main" \
    "jobs:" \
    "  build-dev-apk:" \
    "    steps:" \
    "      - run: scripts/ci/forbidden-files.sh" \
    "      - run: mkdir -p .ci-artifacts/dev-apk" \
    "      - uses: actions/upload-artifact@v4" \
    "        with:" \
    "          name: development-apk" \
    "          path: .ci-artifacts/dev-apk/"

  write_file "$fixture/.github/workflows/release-apk.yml" \
    "on:" \
    "  push:" \
    "    tags:" \
    "      - \"v*\"" \
    "jobs:" \
    "  release-apk:" \
    "    steps:" \
    "      - run: git merge-base --is-ancestor \"\$GITHUB_SHA\" origin/main" \
    "      - run: mkdir -p .ci-artifacts/release-apk" \
    "      - run: echo \"\$ANDROID_KEYSTORE_BASE64\"" \
    "      - uses: actions/upload-artifact@v4" \
    "        with:" \
    "          name: signed-release-apk" \
    "          path: .ci-artifacts/release-apk/"

  write_file "$fixture/.github/workflows/store-candidate.yml" \
    "on:" \
    "  workflow_dispatch:" \
    "jobs:" \
    "  store-candidate:" \
    "    steps:" \
    "      - run: git merge-base --is-ancestor HEAD origin/main"

  cp "$ROOT_DIR/scripts/ci/forbidden-files.sh" "$fixture/scripts/ci/forbidden-files.sh"
  chmod +x "$fixture/scripts/ci/forbidden-files.sh"

  git -C "$fixture" init -q
  git -C "$fixture" add .
}

commit_foundation_fixture() {
  local fixture="$1"

  git -C "$fixture" \
    -c user.name="Blockiverse Test" \
    -c user.email="blockiverse-test@example.invalid" \
    -c commit.gpgsign=false \
    commit -q -m "fixture"
}

valid_fixture="$tmp_dir/valid"
write_foundation_fixture "$valid_fixture"

valid_output="$tmp_dir/valid.out"
"$SCRIPT" "$valid_fixture" > "$valid_output"
assert_contains "$valid_output" "Repository foundation checks passed."

missing_fixture="$tmp_dir/missing-security"
write_foundation_fixture "$missing_fixture"
rm "$missing_fixture/SECURITY.md"

missing_output="$tmp_dir/missing-security.out"
if "$SCRIPT" "$missing_fixture" > "$missing_output" 2>&1; then
  echo "Expected missing SECURITY.md fixture to fail." >&2
  exit 1
fi
assert_contains "$missing_output" "Missing required file: SECURITY.md"

forbidden_fixture="$tmp_dir/forbidden-file"
write_foundation_fixture "$forbidden_fixture"
write_file "$forbidden_fixture/.env" "SECRET=example"
git -C "$forbidden_fixture" add -f .env

forbidden_output="$tmp_dir/forbidden-file.out"
if "$SCRIPT" "$forbidden_fixture" > "$forbidden_output" 2>&1; then
  echo "Expected tracked .env fixture to fail." >&2
  exit 1
fi
assert_contains "$forbidden_output" "Forbidden generated, secret, or signing files are tracked:"

remote_develop_fixture="$tmp_dir/remote-develop"
remote_develop_origin="$tmp_dir/remote-develop.git"
write_foundation_fixture "$remote_develop_fixture"
commit_foundation_fixture "$remote_develop_fixture"
git init --bare -q "$remote_develop_origin"
git -C "$remote_develop_fixture" remote add origin "$remote_develop_origin"
git -C "$remote_develop_fixture" push -q origin HEAD:refs/heads/develop
git -C "$remote_develop_fixture" update-ref -d refs/remotes/origin/develop 2>/dev/null || true

remote_develop_output="$tmp_dir/remote-develop.out"
if "$SCRIPT" "$remote_develop_fixture" > "$remote_develop_output" 2>&1; then
  echo "Expected unfetched remote develop branch fixture to fail." >&2
  exit 1
fi
assert_contains "$remote_develop_output" "Forbidden long-lived remote branch exists: origin/develop"

missing_dev_artifact_fixture="$tmp_dir/missing-dev-artifact"
write_foundation_fixture "$missing_dev_artifact_fixture"
remove_lines_containing "$missing_dev_artifact_fixture/.github/workflows/build-main-dev-apk.yml" "actions/upload-artifact"

missing_dev_artifact_output="$tmp_dir/missing-dev-artifact.out"
if "$SCRIPT" "$missing_dev_artifact_fixture" > "$missing_dev_artifact_output" 2>&1; then
  echo "Expected missing main development APK artifact upload fixture to fail." >&2
  exit 1
fi
assert_contains "$missing_dev_artifact_output" "Main development APK artifact workflow"

missing_release_artifact_fixture="$tmp_dir/missing-release-artifact"
write_foundation_fixture "$missing_release_artifact_fixture"
remove_lines_containing "$missing_release_artifact_fixture/.github/workflows/release-apk.yml" "actions/upload-artifact"

missing_release_artifact_output="$tmp_dir/missing-release-artifact.out"
if "$SCRIPT" "$missing_release_artifact_fixture" > "$missing_release_artifact_output" 2>&1; then
  echo "Expected missing release APK artifact upload fixture to fail." >&2
  exit 1
fi
assert_contains "$missing_release_artifact_output" "Release APK artifact workflow"

echo "Repository foundation checker tests passed."
