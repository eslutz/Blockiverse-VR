# Changelog

All notable changes to Blockiverse VR will be documented here.

The format is based on Keep a Changelog, and releases use tags cut from `main`.

## Unreleased

- Added M3 player inventory persistence to the world save schema with version migration and validation.
- Added M3 survival resource harvesting rules for block drops, basic tool effectiveness, and explicit resource scarcity tuning.
- Added M3 survival UI binders for inventory slots, crafting recipes/actions, and player health state.
- Documented repository tooling guidance for Unity MCP, global `hzdb` CLI Quest-device validation, unstable MCP server isolation, and generated validation artifacts.
- Added the M3 survival crafting model with default recipe definitions, workbench-gated validation, and inventory-backed crafting tests.
- Added the M3 Meta XR Simulator, Horizon Debug Bridge, Unity MCP, and Meta XR Unity MCP Extension validation workflow documentation.
- Documented the requirement to use Meta Horizon avatars for multiplayer players while keeping NPCs and mobs as original blocky voxel characters.
- Added the M2 flat creative prototype foundation with a bounded voxel world, original block registry, chunk mesh rendering, creative block placement and breaking, hotbar selection, undo, and schema-versioned world save/load.
- Added the M1 VR interaction foundation with a dominant-hand ray pointer, highlightable test block, and left-hand block menu placeholder.
- Added M1 VR comfort locomotion with teleport movement, snap turning, height reset, comfort settings, and a VR comfort settings menu used by the XR rig.
- Added the M1 VR controller input action map, Quest controller anchor bindings, haptics abstraction, and input smoke tests.
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
