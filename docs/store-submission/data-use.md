# Data Use Inventory

This inventory supports the privacy policy draft and Meta data-use review. It describes the first store candidate only.

| Data area | Data processed | Purpose | Stored by app | Sent off-device by Blockiverse VR | Notes |
| --- | --- | --- | --- | --- | --- |
| Local world saves | Terrain edits, placed blocks, inventory, crafting, health, settings | Restore local gameplay state | Yes, on device | No | Player can remove by clearing app data or uninstalling. |
| LAN co-op session | Host/client state, block edits, survival sync, avatar sync messages | Synchronize nearby players on the same LAN | Runtime only unless reflected in host save | LAN peers only | No public matchmaking or cloud relay in first candidate. |
| Meta Horizon Avatar/profile | Platform avatar state and profile-dependent avatar loading where enabled | Show local and remote players as Meta Horizon avatars | Runtime only in app code | Meta platform services handle platform data | Disclose only for builds that ship avatar integration. |
| Diagnostics | Unity/player logs, warning/error categories, performance observations | Debug validation, support, and store-candidate readiness | Local logs; excerpts may be attached to review artifacts | No automatic app analytics | Large logs and captures stay outside the repo. |
| Device/runtime metadata | Quest model, runtime, build version, package identity | App launch, compatibility, validation | Runtime/platform-managed | Platform-managed | Required by Unity, Android, and Meta runtime. |
| Voice | None in app | No in-app voice feature | No | No | Players may use Meta Quest party chat outside the app. |
| Text chat | None | No in-app text chat feature | No | No | No public user-generated text in first candidate. |
| Ads/analytics | None planned | Not used in first candidate | No | No | Adding analytics or ads requires privacy and data-use updates first. |

## Data Use Defaults

- Use only the minimum data needed to run, support, and validate the game.
- Do not add analytics, advertising identifiers, public chat, cloud persistence, or external telemetry without updating this inventory, the privacy policy, and the store checklist.
- Do not commit local logs, Quest captures, APKs, secrets, keystores, or signing material.
- If platform API access changes, re-check Meta's current Data Use Checkup and privacy policy requirements before submission.
