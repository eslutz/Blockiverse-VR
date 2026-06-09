# Blockiverse VR

Blockiverse VR is a VR voxel sandbox prototype for Meta Quest 3 and Quest 3S, built with Unity 6, C#, URP, OpenXR, Meta XR SDK, and Netcode for GameObjects.

## Target

- Primary platforms: Meta Quest 3 and Meta Quest 3S
- Input: Quest controllers
- Unsupported initially: hand-tracking-only mode, non-VR desktop mode, mobile, and PC VR

## Initial Gameplay Scope

- Ruleset-defined survival and creative modes
- Canonical bounded world presets: `survival_terrain`, `flat_builder`, and `void_builder`
- Canonical voxel registries, terrain, caves, resources, structures, vegetation, and environment systems
- Inventory, hotbar, tools, crafting, stations, farming, containers, and player survival stats
- Save/load with explicit schema versioning and temporary-ID migrations
- LAN host-authoritative co-op with Meta Horizon avatars or fallback proxies

Future expansion scope, including seasons, full survival expansion, and cloud-hosted private worlds, is tracked in the execution plan's future-features section.

## Development Model

This repository uses trunk-based development:

- `main` is protected and should remain releasable.
- Feature work uses short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches.
- There is no long-lived `develop` branch.
- Releases are cut from commits on `main` only.
- Release tags use `v*` naming, such as `v0.1.0`.

## Licensing

Current licensing state: source-available / All Rights Reserved. See [LICENSE.md](LICENSE.md) and [NOTICE.md](NOTICE.md).

Third-party assets may only be committed when redistribution is allowed. Secrets, keystores, API credentials, `.env` files, and local Unity generated folders must never be committed.

## Roadmap

The canonical development/design source of truth is:

- [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md)
- [docs/rulesets/](docs/rulesets/)

GitHub issues and the `Blockiverse VR Roadmap` project are lightweight workflow aids for active bugs, blockers, reviews, and current initiatives. They are not the canonical roadmap or a required issue hierarchy.
