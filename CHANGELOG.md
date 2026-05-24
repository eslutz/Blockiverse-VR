# Changelog

All notable changes to Blockiverse VR will be documented here.

The format is based on Keep a Changelog, and releases use tags cut from `main`.

## Unreleased

- Fixed the repository foundation check to query `origin` before asserting no remote `develop` branch exists.
- Enabled repository auto-delete of head branches after pull requests merge.
- Loosened agent issue-completion guidance so simple, objectively verified non-PR work can be moved to `Done` and closed with evidence.
- Updated agent workflow guidance for GitHub Project lane updates, issue/PR linking, and solo-maintainer review rules.
- Added repository foundation CI checks and tests for EPIC-00 governance, Unity Git settings, release policy, APK artifact workflow contracts, and forbidden tracked files.
- Added repository bootstrap, governance files, GitHub templates, roadmap automation, and CI scaffolding.
- Added agent workflow guidance requiring completed work to be recorded in this changelog.
