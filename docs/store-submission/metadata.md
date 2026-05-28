# Store Metadata Draft

This file is the in-repository source for the Meta Horizon Store metadata draft. The public dashboard entry is intentionally out of scope for this prep pass.

## App Identity

| Field | Value |
| --- | --- |
| App name | Blockiverse VR |
| Publisher | Eric Slutz |
| Android package | `dev.ericslutz.blockiversevr` |
| Supported devices | Meta Quest 3, Meta Quest 3S |
| App type | Immersive VR game |
| Category | Games |
| Genres | Sandbox, Creative, Survival |
| Supported controllers | Meta Quest Touch controllers |
| Play area | Standing, roomscale |
| Supported player modes | Single player, LAN co-op |
| Internet connection | Not required for single-player or LAN co-op; Meta platform features may require sign-in where enabled. |
| Voice | No in-app voice chat. Players may use Meta Quest party chat outside the app. |
| Store release state | Draft metadata only. No external submission action has been performed. |

## Short Description

Build, explore, and survive in an original voxel sandbox made for Meta Quest 3 and Quest 3S.

## Long Description

Blockiverse VR is an original voxel sandbox for Meta Quest. Step into a block-based world, gather resources, shape terrain, build structures, craft survival-lite tools, and keep your world state across play sessions.

The first store candidate focuses on comfortable VR play with Quest controllers, creative block editing, survival-lite resource loops, inventory and crafting panels, original block art, and local LAN co-op. Multiplayer uses a host-authoritative LAN session model for nearby players. Voice chat is not built into the app; players can use Meta Quest party chat outside Blockiverse VR.

The world, textures, item names, UI, and store assets are original to Blockiverse VR. The game does not include public matchmaking, community worlds, in-app text chat, in-app voice capture, cloud-hosted worlds, ads, or a custom Blockiverse-only avatar creator in the first store candidate.

## Search Keywords

voxel, sandbox, VR, Quest, building, survival, crafting, creative, co-op, blocks

## Content Notes

- Original voxel terrain, resources, crafting, storage, lighting, and UI assets.
- No protected third-party textures, screenshots, names, logos, fonts, characters, or distinctive visual references.
- No in-app purchases, ads, public chat, user-generated text, or public multiplayer discovery in the first store candidate.
- Meta Horizon Avatar/profile behavior should be disclosed when avatar integration ships in the candidate build.

## Comfort Notes

- Designed for Quest controllers.
- Comfort locomotion uses teleport and snap-turning where available.
- No hand-tracking-only mode is required.
- The store candidate should be validated on Quest 3 and Quest 3S before submission.

## Current App Metadata Evidence

- Unity product name: `Blockiverse VR`
- Unity company name: `Eric Slutz`
- Android application identifier: `dev.ericslutz.blockiversevr`
- Android game category enabled.
- Custom Android manifest marks the app as VR and supports `quest3|quest3s`.
- Branded Android resource string sets `app_name` to `Blockiverse VR`.

## External Submission Blockers

- Public privacy policy URL must be hosted over HTTPS before real submission.
- Public support URL or support contact must be selected before real submission.
- Store dashboard pricing, release channel, build upload, IARC/content rating, and final Submit for Review action are out of scope for this in-repo prep pass.
