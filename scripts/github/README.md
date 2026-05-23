# GitHub Bootstrap

Run the roadmap bootstrap after authenticating GitHub CLI:

```sh
gh auth login -h github.com
gh auth refresh -s project
scripts/github/bootstrap-roadmap.sh
```

Optional environment variables:

```sh
OWNER=your-github-user-or-org
REPO=your-github-user-or-org/blockiverse-vr
PROJECT_TITLE="Blockiverse VR Roadmap"
PROJECT_NUMBER=13
SKIP_PROJECT_FIELD_SETUP=1
ENSURE_PROJECT_LINK=0
DIRECT_PROJECT_ADD=1
SET_PROJECT_FIELDS=1
```

The script creates or configures:

- Public GitHub repository
- `main` branch remote
- Labels
- Milestones
- GitHub Project fields
- Epic, feature, and story issues from `roadmap.tsv`
- Issue parent/child references
- Best-effort `main` branch protection

By default, the script adds issues to the Project but skips per-item custom field updates to avoid exhausting GitHub's GraphQL quota. Set `SET_PROJECT_FIELDS=1` to populate every custom Project field when sufficient quota is available.

For a low-quota resume after the Project already exists, set `PROJECT_NUMBER`, `SKIP_PROJECT_FIELD_SETUP=1`, `ENSURE_PROJECT_LINK=0`, and keep `DIRECT_PROJECT_ADD=1` so the script adds issues to the Project through direct batched ProjectV2 mutations.
