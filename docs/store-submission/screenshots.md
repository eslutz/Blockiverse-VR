# Store Screenshot And Capture Plan

This file defines the screenshot plan only. It does not upload assets to the Meta Developer Dashboard.

## Capture Rules

- Use actual Blockiverse VR gameplay, UI, and original assets.
- Capture from a current store-candidate build on Quest 3 or Quest 3S when practical.
- Avoid debug overlays unless the screenshot is explicitly for internal validation.
- Do not include protected third-party marks, copied textures, copied UI, copied fonts, or unrelated store assets.
- Do not show private user identifiers, local machine paths, network addresses, GitHub tokens, logs, or device serials.
- Keep raw captures and large source files outside the repository unless a tracked store asset is explicitly approved.

## Required Shot List

| Shot | Purpose | Source state | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| Key art / hero | Store identity | Original branded scene or approved key art | Clear app identity, no copied references, readable at store scale | Planned |
| Creative building | Core gameplay | Player places and removes blocks in authored terrain | Distinct block materials, readable controller interaction | Planned |
| Survival-lite resource loop | Gameplay progression | Resource harvesting, health/inventory/crafting state visible | UI is legible and does not obscure comfort-critical view | Planned |
| Crafting/inventory | Systems clarity | Inventory and crafting panels open in headset-friendly layout | Text and controls are readable in Quest capture | Planned |
| LAN co-op | Multiplayer scope | Two-player LAN session with Meta Horizon avatar/proxy representation | Shows co-op without implying public matchmaking | Planned |
| World readability | Art validation | Terrain, resources, storage, and lighting blocks visible | No missing-material or magenta surfaces | Planned |

## Optional Capture

- Short trailer or gameplay capture may be prepared after performance validation.
- Trailer/capture is not required for this in-repo prep pass and must not be uploaded externally as part of this work.

## External Submission Blockers

- Final store dimensions and localized asset requirements must be checked in the Meta Developer Dashboard before upload.
- Final images must be reviewed for content, privacy, quality, and VRC asset compliance before real submission.
