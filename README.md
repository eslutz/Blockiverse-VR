# Blockiverse VR

Blockiverse VR is a VR voxel sandbox prototype for Meta Quest 3 and Quest 3S, built with Unity 6, C#, URP, OpenXR, Meta XR SDK, and Netcode for GameObjects.

## Target

- Primary platforms: Meta Quest 3 and Meta Quest 3S
- Input: Quest controllers
- Unsupported initially: hand-tracking-only mode, non-VR desktop mode, mobile, and PC VR

## Initial Gameplay Scope

- Ruleset-defined survival and creative modes
- Canonical bounded world presets: `survival_terrain` and `flat_builder`
- Canonical voxel registries, terrain, caves, resources, structures, vegetation, and environment systems
- Inventory, hotbar, tools, crafting, stations, farming, containers, and player survival stats
- Save/load with explicit schema versioning and temporary-ID migrations
- LAN host-authoritative co-op with Meta Horizon avatars or fallback proxies
- A self-hosted Linux dedicated server, so a world can stay up with nobody wearing a headset

Future expansion scope, including seasons, full survival expansion, and paid cloud hosting, is tracked in the execution plan's future-features section.

## Self-Hosted Dedicated Server

A world normally lives on one player's headset, which means it ends when they take the headset off and only reaches their LAN. The dedicated server is the same authoritative simulation with no local player, no renderer, and no XR, so it stays up on its own and can be reached from outside your network.

Two ways to run one, both cut from the same build:

```sh
# Container
docker run -d -p 7777:7777/udp -v "$PWD/world:/data" ghcr.io/eslutz/blockiverse-server:latest

# Linux archive, installed as a systemd service
tar -xzf blockiverse-server-<version>-linux-x86_64.tar.gz && cd blockiverse-server && sudo ./install.sh
```

Operator documentation lives in [docs/server/](docs/server/):

- [Setup and running](docs/server/README.md) — install, upgrade, connect, back up, administer
- [Configuration reference](docs/server/configuration.md) — every setting, and the ones that deliberately do not exist
- [Security posture](docs/server/security-posture.md) — read this before forwarding a port

**The server and its clients must be the same version**, or every join is refused. Four players is the supported ceiling; higher is honoured but unmeasured. Design rationale is in [ADR 0007](docs/adr/0007-self-hosted-dedicated-server.md).

## Development Model

This repository uses trunk-based development:

- `main` is protected and should remain releasable.
- Feature work uses short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches.
- There is no long-lived `develop` branch.
- Releases are cut from commits on `main` only.
- Release tags use `v*` naming, such as `v0.1.0`.

## Licensing

Current licensing state: source-available / All Rights Reserved. See [LICENSE.md](LICENSE.md) and [NOTICE.md](NOTICE.md).

The compiled dedicated server is the one exception: it carries a binary-only distribution grant so operators may run and redistribute the official artifact, with use additionally governed by [SERVER-EULA.md](SERVER-EULA.md). The source remains All Rights Reserved.

Third-party assets may only be committed when redistribution is allowed. Secrets, keystores, API credentials, `.env` files, and local Unity generated folders must never be committed.

## Roadmap

The canonical development/design source of truth is:

- [docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md)
- [docs/rulesets/](docs/rulesets/)

GitHub issues and the `Blockiverse VR Roadmap` project are lightweight workflow aids for active bugs, blockers, reviews, and current initiatives. They are not the canonical roadmap or a required issue hierarchy.
