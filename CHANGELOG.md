# Changelog

All notable changes to Blockiverse VR will be documented here.

The format is based on Keep a Changelog, and releases use tags cut from `main`.

## Unreleased

- Updated GitHub Actions workflow pins to current stable major versions and documented dependency-currency guidance for agents.
- Bootstrapped the Unity 6 Quest project with URP, OpenXR Meta Quest settings, a boot scene, an XR rig prefab, assembly boundaries, EditMode and PlayMode bootstrap tests, and a development APK smoke build.
- Configured hybrid CI for Unity Personal: GitHub-hosted PR checks validate repository policy, while Unity tests and development APK smoke builds run locally through Unity Hub.
- Documented the manual Unity validation workflow and removed hosted Unity license, test, and development APK jobs from the current Actions contract.
- Removed one-off repository foundation and roadmap bootstrap scripts from the tracked tree while keeping reusable CI safety and Unity validation scripts.
- Migrated `main` protection from classic branch protection to a repository ruleset requiring `Repository checks`, linear history, conversation resolution, and force-push/deletion protection.
- Enabled repository auto-delete of head branches after pull requests merge.
- Loosened agent issue-completion guidance so simple, objectively verified non-PR work can be moved to `Done` and closed with evidence.
- Updated agent workflow guidance for GitHub Project lane updates, issue/PR linking, and solo-maintainer review rules.
- Added reusable CI checks for script syntax, release policy docs, and forbidden tracked files.
- Added repository bootstrap, governance files, GitHub templates, and CI scaffolding.
- Added agent workflow guidance requiring completed work to be recorded in this changelog.
