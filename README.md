# Blockiverse VR

Blockiverse VR is a VR voxel sandbox prototype for Meta Quest 3 and Quest 3S, built with Unity 6, C#, URP, OpenXR, Meta XR SDK, and Netcode for GameObjects.

## Target

- Primary platforms: Meta Quest 3 and Meta Quest 3S
- Input: Quest controllers
- Unsupported initially: hand-tracking-only mode, non-VR desktop mode, mobile, and PC VR

## Initial Gameplay Scope

- Creative-mode voxel building
- Bounded voxel terrain
- Caves and resource deposits
- Inventory, hotbar, and crafting
- Health and survival-lite resource loop
- Save/load with world versioning
- Basic two-player co-op with private voice chat

Full survival mode, mobs, day/night, combat, and deeper progression are planned after the MVP.

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

The roadmap is managed through GitHub Projects and linked GitHub issues:

- M0 Bootstrap
- M1 VR Slice
- M2 Creative
- M3 Survival-Lite
- M4 Multiplayer
- M5 Store Candidate
- M6 Full Survival
