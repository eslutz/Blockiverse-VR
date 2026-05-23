#!/usr/bin/env bash
set -euo pipefail

PROJECT_TITLE="${PROJECT_TITLE:-Blockiverse VR Roadmap}"
SKIP_PROJECT="${SKIP_PROJECT:-0}"
REPO_DESCRIPTION="VR voxel sandbox prototype for Meta Quest 3/3S built with Unity and C#."
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ROADMAP_FILE="$ROOT_DIR/scripts/github/roadmap.tsv"

cd "$ROOT_DIR"

require_tool() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required tool: $1" >&2
    exit 1
  fi
}

require_tool git
require_tool gh
require_tool jq

if ! gh auth status >/dev/null 2>&1; then
  echo "GitHub CLI is not authenticated with a valid token." >&2
  echo "Run: gh auth login -h github.com" >&2
  echo "Then: gh auth refresh -s project" >&2
  exit 1
fi

OWNER="${OWNER:-$(gh api user --jq '.login')}"
REPO="${REPO:-$OWNER/blockiverse-vr}"
REPO_NAME="${REPO#*/}"

echo "Configuring $REPO"

git branch -M main

if ! gh repo view "$REPO" >/dev/null 2>&1; then
  gh repo create "$REPO" \
    --public \
    --description "$REPO_DESCRIPTION" \
    --source . \
    --remote origin \
    --push
else
  if ! git remote get-url origin >/dev/null 2>&1; then
    git remote add origin "https://github.com/$REPO.git"
  fi
  git push -u origin main
fi

create_label() {
  local name="$1"
  local color="$2"
  local description="$3"
  gh label create "$name" -R "$REPO" --color "$color" --description "$description" --force >/dev/null
}

echo "Creating labels"
create_label "type: epic" "5319E7" "Major roadmap phase or cross-cutting initiative"
create_label "type: feature" "1D76DB" "Feature under an epic"
create_label "type: story" "0E8A16" "Implementation story under a feature"
create_label "type: task" "C2E0C6" "Concrete implementation task"
create_label "type: bug" "D73A4A" "Broken behavior"
create_label "type: tech debt" "FBCA04" "Maintainability work"
create_label "type: spike" "BFDADC" "Bounded research or feasibility work"

for priority in P0 P1 P2 P3; do
  create_label "priority: $priority" "FBCA04" "Priority $priority"
done

for risk in Low Medium High; do
  create_label "risk: $risk" "F9D0C4" "Risk level $risk"
done

for phase in $(seq 0 20); do
  create_label "phase: $phase" "D4C5F9" "Roadmap phase $phase"
done

for area in Repo Engine VR Voxel Terrain Creative Survival Multiplayer Art Audio UI CI/CD Store QA; do
  create_label "area: $area" "C5DEF5" "Area $area"
done

for target in Prototype Alpha Beta RC Store; do
  create_label "target: $target" "BFDADC" "Target release $target"
done

create_milestone() {
  local title="$1"
  local existing
  existing="$(gh api "repos/$REPO/milestones?state=all&per_page=100" --jq ".[] | select(.title == \"$title\") | .number" | head -n 1)"
  if [ -z "$existing" ]; then
    gh api "repos/$REPO/milestones" -X POST -f title="$title" >/dev/null
  fi
}

echo "Creating milestones"
create_milestone "M0 Bootstrap"
create_milestone "M1 VR Slice"
create_milestone "M2 Creative"
create_milestone "M3 Survival-Lite"
create_milestone "M4 Multiplayer"
create_milestone "M5 Store Candidate"
create_milestone "M6 Full Survival"

project_number=""
project_id=""

if [ "$SKIP_PROJECT" != "1" ]; then
  project_number="$(gh project list --owner "$OWNER" --limit 100 --format json --jq ".projects[]? | select(.title == \"$PROJECT_TITLE\") | .number" | head -n 1)"
  if [ -z "$project_number" ]; then
    project_number="$(gh project create --owner "$OWNER" --title "$PROJECT_TITLE" --format json --jq '.number')"
  fi

  project_id="$(gh project view "$project_number" --owner "$OWNER" --format json --jq '.id')"
  gh project link "$project_number" --owner "$OWNER" --repo "$REPO_NAME" >/dev/null || true
fi

single_select_options_graphql() {
  local options="$1"
  local result=""
  local option
  local sep=""

  IFS=',' read -ra option_names <<< "$options"
  for option in "${option_names[@]}"; do
    result="${result}${sep}{name:\"${option}\",color:GRAY,description:\"${option}\"}"
    sep=","
  done

  printf '%s' "$result"
}

update_select_field_options() {
  local field_id="$1"
  local options="$2"
  local options_graphql
  options_graphql="$(single_select_options_graphql "$options")"

  gh api graphql -f query="mutation { updateProjectV2Field(input:{fieldId:\"$field_id\",singleSelectOptions:[$options_graphql]}) { projectV2Field { ... on ProjectV2SingleSelectField { id } } } }" >/dev/null
}

ensure_select_field() {
  local field="$1"
  local options="$2"
  local existing existing_type
  existing="$(gh project field-list "$project_number" --owner "$OWNER" --format json --jq ".fields[]? | select(.name == \"$field\") | .id" | head -n 1)"
  existing_type="$(gh project field-list "$project_number" --owner "$OWNER" --format json --jq ".fields[]? | select(.name == \"$field\") | .type" | head -n 1)"
  if [ -z "$existing" ]; then
    gh project field-create "$project_number" \
      --owner "$OWNER" \
      --name "$field" \
      --data-type SINGLE_SELECT \
      --single-select-options "$options" >/dev/null
  elif [ "$existing_type" != "ProjectV2SingleSelectField" ]; then
    echo "Using GitHub's issue-derived $field field; requested single-select values are stored in Roadmap $field."
  elif ! gh project field-list "$project_number" --owner "$OWNER" --format json \
    --jq ".fields[]? | select(.name == \"$field\") | .options[]?.name" | grep -qx "$(printf '%s' "$options" | cut -d, -f1)"; then
    update_select_field_options "$existing" "$options"
  fi
}

if [ "$SKIP_PROJECT" != "1" ]; then
  echo "Creating project fields"
  ensure_select_field "Status" "Backlog,Ready,In Progress,In Review,Blocked,Done"
  ensure_select_field "Type" "Epic,Feature,Story,Task,Bug,Tech Debt,Spike"
  ensure_select_field "Phase" "0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20"
  ensure_select_field "Priority" "P0,P1,P2,P3"
  ensure_select_field "Area" "Repo,Engine,VR,Voxel,Terrain,Creative,Survival,Multiplayer,Art,Audio,UI,CI/CD,Store,QA"
  ensure_select_field "Milestone" "M0 Bootstrap,M1 VR Slice,M2 Creative,M3 Survival-Lite,M4 Multiplayer,M5 Store Candidate,M6 Full Survival"
  ensure_select_field "Roadmap Milestone" "M0 Bootstrap,M1 VR Slice,M2 Creative,M3 Survival-Lite,M4 Multiplayer,M5 Store Candidate,M6 Full Survival"
  ensure_select_field "Risk" "Low,Medium,High"
  ensure_select_field "Target Release" "Prototype,Alpha,Beta,RC,Store"
  ensure_select_field "Effort" "XS,S,M,L,XL"
else
  echo "Skipping GitHub Project setup because SKIP_PROJECT=1."
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
map_file="$tmp_dir/issues.tsv"
fields_file="$tmp_dir/project-fields.json"
existing_issues_file="$tmp_dir/existing-issues.tsv"
existing_items_file="$tmp_dir/existing-project-items.tsv"
milestones_file="$tmp_dir/milestones.tsv"
touch "$map_file"

refresh_fields() {
  gh project field-list "$project_number" --owner "$OWNER" --format json > "$fields_file"
}

refresh_existing_issues() {
  gh api --paginate "repos/$REPO/issues?state=all&per_page=100" \
    --jq '.[] | select(.pull_request | not) | [.title, .number, .html_url] | @tsv' > "$existing_issues_file"
}

refresh_existing_project_items() {
  gh project item-list "$project_number" --owner "$OWNER" --limit 300 --format json \
    --jq '.items[]? | select(.content.url != null) | [.content.url, .id] | @tsv' > "$existing_items_file"
}

refresh_milestones() {
  gh api --paginate "repos/$REPO/milestones?state=all&per_page=100" \
    --jq '.[] | [.title, .number] | @tsv' > "$milestones_file"
}

milestone_number() {
  local title="$1"
  awk -F '\t' -v title="$title" '$1 == title { print $2; exit }' "$milestones_file"
}

field_id() {
  local field="$1"
  jq -r --arg field "$field" '.fields[] | select(.name == $field) | .id' "$fields_file" | head -n 1
}

option_id() {
  local field="$1"
  local value="$2"
  jq -r --arg field "$field" --arg value "$value" '.fields[] | select(.name == $field) | .options[]? | select(.name == $value) | .id' "$fields_file" | head -n 1
}

add_project_field_mutation() {
  local mutation_file="$1"
  local alias="$2"
  local item_id="$3"
  local field="$4"
  local value="$5"
  [ -z "$value" ] && return 0

  local fid oid
  fid="$(field_id "$field")"
  oid="$(option_id "$field" "$value")"
  if [ -n "$fid" ] && [ -n "$oid" ]; then
    printf '%s:updateProjectV2ItemFieldValue(input:{projectId:"%s",itemId:"%s",fieldId:"%s",value:{singleSelectOptionId:"%s"}}){projectV2Item{id}}\n' \
      "$alias" "$project_id" "$item_id" "$fid" "$oid" >> "$mutation_file"
  fi
}

set_project_values() {
  local item_id="$1"
  local type="$2"
  local phase="$3"
  local priority="$4"
  local area="$5"
  local milestone="$6"
  local risk="$7"
  local target="$8"
  local effort="$9"

  local mutation_file="$tmp_dir/project-values-$item_id.graphql"
  {
    echo "mutation {"
  } > "$mutation_file"

  add_project_field_mutation "$mutation_file" "status" "$item_id" "Status" "Backlog"
  add_project_field_mutation "$mutation_file" "type" "$item_id" "Type" "$type"
  add_project_field_mutation "$mutation_file" "phase" "$item_id" "Phase" "$phase"
  add_project_field_mutation "$mutation_file" "priority" "$item_id" "Priority" "$priority"
  add_project_field_mutation "$mutation_file" "area" "$item_id" "Area" "$area"
  add_project_field_mutation "$mutation_file" "roadmapMilestone" "$item_id" "Roadmap Milestone" "$milestone"
  add_project_field_mutation "$mutation_file" "risk" "$item_id" "Risk" "$risk"
  add_project_field_mutation "$mutation_file" "target" "$item_id" "Target Release" "$target"
  add_project_field_mutation "$mutation_file" "effort" "$item_id" "Effort" "$effort"

  echo "}" >> "$mutation_file"

  if [ "$(wc -l < "$mutation_file")" -gt 2 ]; then
    gh api graphql -f "query=$(cat "$mutation_file")" >/dev/null || true
  fi
}

lookup_issue_number() {
  local key="$1"
  awk -F '\t' -v key="$key" '$1 == key { print $2 }' "$map_file" | head -n 1
}

lookup_issue_url() {
  local key="$1"
  awk -F '\t' -v key="$key" '$1 == key { print $3 }' "$map_file" | head -n 1
}

find_issue_by_title() {
  local title="$1"
  awk -F '\t' -v title="$title" '$1 == title { print $2 "\t" $3; exit }' "$existing_issues_file"
}

write_issue_body() {
  local path="$1"
  local type="$2"
  local title="$3"
  local parent_number="$4"
  local phase="$5"
  local milestone="$6"
  local area="$7"
  local priority="$8"
  local risk="$9"
  local target="${10}"
  local effort="${11}"

  {
    echo "## Goal"
    echo
    echo "Deliver: $title."
    echo
    echo "## Player-facing outcome"
    echo
    echo "See the roadmap phase and acceptance criteria for this item."
    echo
    echo "## Technical scope"
    echo
    echo "Implement the scope described by this backlog item and its linked parent/children."
    echo
    echo "## Out of scope"
    echo
    echo "Unrelated refactors, unapproved licensing changes, and Minecraft-protected names/assets."
    echo
    echo "## Acceptance criteria"
    echo
    echo "- The item is implemented or explicitly verified as complete."
    echo "- Tests and manual validation steps are recorded before closing."
    echo "- Any follow-up work is captured in linked issues."
    echo
    echo "## Test plan"
    echo
    echo "- Add automated tests where practical."
    echo "- Run relevant EditMode, PlayMode, CI, Quest, multiplayer, or store-readiness checks."
    echo
    echo "## Manual validation steps"
    echo
    echo "- Validate the behavior in the relevant editor, build, Quest headset, or GitHub workflow."
    echo
    echo "## Dependencies"
    echo
    if [ -n "$parent_number" ]; then
      echo "- Parent: #$parent_number"
    else
      echo "- None recorded."
    fi
    echo
    echo "## Roadmap metadata"
    echo
    echo "- Type: $type"
    echo "- Phase: $phase"
    echo "- Milestone: $milestone"
    echo "- Area: $area"
    echo "- Priority: $priority"
    echo "- Risk: $risk"
    echo "- Target Release: $target"
    echo "- Effort: $effort"
  } > "$path"
}

write_issue_payload() {
  local path="$1"
  local title="$2"
  local body_file="$3"
  local milestone="$4"
  local labels_csv="$5"
  local milestone_num
  milestone_num="$(milestone_number "$milestone")"

  jq -n \
    --arg title "$title" \
    --rawfile body "$body_file" \
    --arg labels "$labels_csv" \
    --arg milestone "$milestone_num" \
    '{title: $title, body: $body, labels: ($labels | split(","))}
      + (if $milestone == "" then {} else {milestone: ($milestone | tonumber)} end)' > "$path"
}

create_issue_rest() {
  local title="$1"
  local body_file="$2"
  local milestone="$3"
  local labels_csv="$4"
  local payload_file="$tmp_dir/create-issue.json"

  write_issue_payload "$payload_file" "$title" "$body_file" "$milestone" "$labels_csv"
  gh api "repos/$REPO/issues" -X POST --input "$payload_file"
}

update_issue_rest() {
  local number="$1"
  local title="$2"
  local body_file="$3"
  local milestone="$4"
  local labels_csv="$5"
  local payload_file="$tmp_dir/update-issue-$number.json"

  write_issue_payload "$payload_file" "$title" "$body_file" "$milestone" "$labels_csv"
  gh api "repos/$REPO/issues/$number" -X PATCH --input "$payload_file" >/dev/null
}

add_issue_to_project() {
  local url="$1"
  local item_id
  item_id="$(awk -F '\t' -v url="$url" '$1 == url { print $2; exit }' "$existing_items_file")"
  if [ -z "$item_id" ]; then
    if item_id="$(gh project item-add "$project_number" --owner "$OWNER" --url "$url" --format json --jq '.id' 2>/dev/null)"; then
      printf '%s\t%s\n' "$url" "$item_id" >> "$existing_items_file"
    else
      refresh_existing_project_items
      item_id="$(awk -F '\t' -v url="$url" '$1 == url { print $2; exit }' "$existing_items_file")"
    fi
  fi
  echo "$item_id"
}

create_or_update_issue() {
  local key="$1"
  local type="$2"
  local title="$3"
  local parent="$4"
  local phase="$5"
  local milestone="$6"
  local area="$7"
  local priority="$8"
  local risk="$9"
  local target="${10}"
  local effort="${11}"

  local parent_number=""
  if [ -n "$parent" ]; then
    parent_number="$(lookup_issue_number "$parent")"
  fi

  local body_file="$tmp_dir/$key.md"
  write_issue_body "$body_file" "$type" "$title" "$parent_number" "$phase" "$milestone" "$area" "$priority" "$risk" "$target" "$effort"

  local type_lower
  type_lower="$(printf '%s' "$type" | tr '[:upper:]' '[:lower:]')"

  local labels=(
    "type: $type_lower"
    "phase: $phase"
    "area: $area"
    "priority: $priority"
    "risk: $risk"
    "target: $target"
  )

  local labels_csv
  labels_csv="$(IFS=,; echo "${labels[*]}")"

  local number url existing
  existing="$(find_issue_by_title "$title")"
  if [ -n "$existing" ]; then
    number="$(printf '%s' "$existing" | cut -f1)"
    url="$(printf '%s' "$existing" | cut -f2)"
    update_issue_rest "$number" "$title" "$body_file" "$milestone" "$labels_csv"
  else
    local response
    response="$(create_issue_rest "$title" "$body_file" "$milestone" "$labels_csv")"
    number="$(printf '%s' "$response" | jq -r '.number')"
    url="$(printf '%s' "$response" | jq -r '.html_url')"
    printf '%s\t%s\t%s\n' "$title" "$number" "$url" >> "$existing_issues_file"
  fi

  printf '%s\t%s\t%s\t%s\n' "$key" "$number" "$url" "$title" >> "$map_file"

  if [ -n "$parent" ] && [ -n "$parent_number" ]; then
    printf -- "- [ ] #%s %s\n" "$number" "$title" >> "$tmp_dir/children-$parent.md"
  fi

  if [ "$SKIP_PROJECT" != "1" ]; then
    local item_id
    item_id="$(add_issue_to_project "$url")"
    if [ -n "$item_id" ]; then
      set_project_values "$item_id" "$type" "$phase" "$priority" "$area" "$milestone" "$risk" "$target" "$effort"
    fi
  fi
}

refresh_milestones
refresh_existing_issues
if [ "$SKIP_PROJECT" != "1" ]; then
  refresh_fields
  refresh_existing_project_items
else
  touch "$fields_file" "$existing_items_file"
fi

echo "Creating roadmap issues"
tail -n +2 "$ROADMAP_FILE" | while IFS='|' read -r key type title parent phase milestone area priority risk target effort; do
  [ -z "$key" ] && continue
  create_or_update_issue "$key" "$type" "$title" "$parent" "$phase" "$milestone" "$area" "$priority" "$risk" "$target" "$effort"
done

echo "Linking parent issue bodies"
while IFS=$'\t' read -r key number url title; do
  children_file="$tmp_dir/children-$key.md"
  [ -f "$children_file" ] || continue
  current_body="$tmp_dir/current-$key.md"
  base_body="$tmp_dir/base-$key.md"
  updated_body="$tmp_dir/updated-$key.md"
  gh api "repos/$REPO/issues/$number" --jq '.body // ""' > "$current_body"
  awk 'BEGIN { skip = 0 } /^## Children$/ { skip = 1; next } skip == 0 { print }' "$current_body" > "$base_body"
  {
    cat "$base_body"
    echo
    echo "## Children"
    cat "$children_file"
  } > "$updated_body"
  jq -n --rawfile body "$updated_body" '{body: $body}' > "$tmp_dir/parent-link-$key.json"
  gh api "repos/$REPO/issues/$number" -X PATCH --input "$tmp_dir/parent-link-$key.json" >/dev/null
done < "$map_file"

echo "Applying best-effort main branch protection"
cat > "$tmp_dir/branch-protection.json" <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Repository checks",
      "Unity tests"
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": true,
    "required_approving_review_count": 1,
    "require_last_push_approval": false
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": true,
  "lock_branch": false,
  "allow_fork_syncing": true
}
JSON

if ! gh api "repos/$REPO/branches/main/protection" -X PUT --input "$tmp_dir/branch-protection.json" >/dev/null; then
  echo "Warning: branch protection could not be applied. Check repository admin permissions." >&2
fi

echo "GitHub bootstrap complete for $REPO"
