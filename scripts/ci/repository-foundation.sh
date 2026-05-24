#!/usr/bin/env sh
set -eu

if [ "$#" -gt 1 ]; then
  echo "Usage: $0 [repository-root]" >&2
  exit 2
fi

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
root_dir="${1:-}"

if [ -z "$root_dir" ]; then
  root_dir="$(CDPATH= cd -- "$script_dir/../.." && pwd)"
else
  root_dir="$(CDPATH= cd -- "$root_dir" && pwd)"
fi

failures_file="$(mktemp)"
trap 'rm -f "$failures_file"' EXIT

record_failure() {
  printf '%s\n' "$1" >> "$failures_file"
}

require_file() {
  local_path="$1"

  if [ ! -f "$root_dir/$local_path" ]; then
    record_failure "Missing required file: $local_path"
  fi
}

require_executable() {
  local_path="$1"

  if [ ! -x "$root_dir/$local_path" ]; then
    record_failure "Missing executable script: $local_path"
  fi
}

require_dir() {
  local_path="$1"

  if [ ! -d "$root_dir/$local_path" ]; then
    record_failure "Missing required directory: $local_path"
  fi
}

require_text() {
  local_path="$1"
  expected="$2"
  description="$3"

  if [ ! -f "$root_dir/$local_path" ]; then
    record_failure "Missing required file: $local_path"
    return
  fi

  if ! grep -Fq -- "$expected" "$root_dir/$local_path"; then
    record_failure "$description: $local_path must contain '$expected'"
  fi
}

require_required_files() {
  for file in \
    README.md \
    LICENSE.md \
    NOTICE.md \
    CONTRIBUTING.md \
    CODE_OF_CONDUCT.md \
    SECURITY.md \
    CHANGELOG.md \
    .gitattributes \
    .gitignore \
    .github/CODEOWNERS \
    .github/pull_request_template.md \
    .github/workflows/ci-pr.yml \
    .github/workflows/build-main-dev-apk.yml \
    .github/workflows/release-apk.yml \
    .github/workflows/store-candidate.yml \
    docs/architecture/branching-and-release.md \
    scripts/ci/forbidden-files.sh
  do
    require_file "$file"
  done

  require_executable scripts/ci/forbidden-files.sh
}

require_required_directories() {
  for dir in \
    .github/ISSUE_TEMPLATE \
    docs/adr \
    docs/architecture \
    docs/art-direction \
    docs/roadmap \
    docs/store-submission \
    docs/testing
  do
    require_dir "$dir"
  done
}

require_issue_templates() {
  for template in epic feature story task bug tech-debt spike; do
    require_file ".github/ISSUE_TEMPLATE/$template.yml"
  done
}

require_unity_git_settings() {
  for pattern in \
    "[Ll]ibrary/" \
    "[Tt]emp/" \
    "[Ll]ogs/" \
    "[Uu]ser[Ss]ettings/" \
    ".env" \
    ".env.*" \
    "*.keystore" \
    "*.jks" \
    "*.p12"
  do
    require_text .gitignore "$pattern" "Unity, secret, and signing ignore policy"
  done

  for attribute in \
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
  do
    require_text .gitattributes "$attribute" "Unity and asset Git attribute policy"
  done
}

require_branching_release_policy() {
  require_text docs/architecture/branching-and-release.md "main" "Branch policy"
  require_text docs/architecture/branching-and-release.md "develop" "Branch policy"
  require_text docs/architecture/branching-and-release.md "feature/*" "Branch policy"
  require_text docs/architecture/branching-and-release.md "v*" "Release policy"
}

require_workflow_contracts() {
  require_text .github/workflows/ci-pr.yml "pull_request" "PR CI trigger"
  require_text .github/workflows/ci-pr.yml "- main" "PR CI target branch"
  require_text .github/workflows/ci-pr.yml "scripts/ci/test-repository-foundation.sh" "PR CI repository foundation test"
  require_text .github/workflows/ci-pr.yml "scripts/ci/repository-foundation.sh" "PR CI foundation check"
  require_text .github/workflows/ci-pr.yml "scripts/ci/forbidden-files.sh" "PR CI forbidden file check"
  require_text .github/workflows/build-main-dev-apk.yml "push" "Main development APK trigger"
  require_text .github/workflows/build-main-dev-apk.yml "- main" "Main development APK branch"
  require_text .github/workflows/build-main-dev-apk.yml "scripts/ci/forbidden-files.sh" "Main development APK repository safety check"
  require_text .github/workflows/build-main-dev-apk.yml ".ci-artifacts/dev-apk" "Main development APK artifact workflow"
  require_text .github/workflows/build-main-dev-apk.yml "actions/upload-artifact" "Main development APK artifact workflow"
  require_text .github/workflows/build-main-dev-apk.yml "development-apk" "Main development APK artifact workflow"
  require_text .github/workflows/release-apk.yml "tags:" "Release APK tag trigger"
  require_text .github/workflows/release-apk.yml '"v*"' "Release APK tag pattern"
  require_text .github/workflows/release-apk.yml "git merge-base --is-ancestor" "Release tag ancestry check"
  require_text .github/workflows/release-apk.yml ".ci-artifacts/release-apk" "Release APK artifact workflow"
  require_text .github/workflows/release-apk.yml "actions/upload-artifact" "Release APK artifact workflow"
  require_text .github/workflows/release-apk.yml "signed-release-apk" "Release APK artifact workflow"
  require_text .github/workflows/release-apk.yml "ANDROID_KEYSTORE_BASE64" "Release APK signing secret wiring"
  require_text .github/workflows/store-candidate.yml "workflow_dispatch" "Store candidate manual trigger"
  require_text .github/workflows/store-candidate.yml "git merge-base --is-ancestor" "Store candidate ancestry check"
}

require_no_develop_branch() {
  if ! git -C "$root_dir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    record_failure "Repository root is not a Git work tree."
    return
  fi

  if git -C "$root_dir" show-ref --verify --quiet refs/heads/develop; then
    record_failure "Forbidden long-lived branch exists: develop"
  fi

  if git -C "$root_dir" remote get-url origin >/dev/null 2>&1; then
    if git -C "$root_dir" ls-remote --exit-code --heads origin develop >/dev/null 2>&1; then
      record_failure "Forbidden long-lived remote branch exists: origin/develop"
    else
      remote_status="$?"
      if [ "$remote_status" -ne 2 ]; then
        record_failure "Unable to query origin for forbidden develop branch."
      fi
    fi
  fi
}

run_forbidden_file_check() {
  if [ ! -x "$root_dir/scripts/ci/forbidden-files.sh" ]; then
    return
  fi

  output_file="$(mktemp)"

  if ! (cd "$root_dir" && scripts/ci/forbidden-files.sh) > "$output_file" 2>&1; then
    cat "$output_file" >&2
    rm -f "$output_file"
    record_failure "Forbidden file check failed."
    return
  fi

  rm -f "$output_file"
}

require_required_files
require_required_directories
require_issue_templates
require_unity_git_settings
require_branching_release_policy
require_workflow_contracts
require_no_develop_branch
run_forbidden_file_check

if [ -s "$failures_file" ]; then
  echo "Repository foundation checks failed:" >&2
  cat "$failures_file" >&2
  exit 1
fi

echo "Repository foundation checks passed."
