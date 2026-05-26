# Asset Prompt Log

Record generated asset provenance here.

Each generated asset entry should include:

- Asset path
- Generation date
- Tool/model
- Prompt
- Negative prompt or exclusions
- Post-processing steps
- Reviewer notes

## 2026-05-26 — Procedural block validation atlas

- Asset path: `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- Generation date: 2026-05-26
- Tool/model: Hand-authored procedural C# texture generation; no AI image model used
- Prompt: Not applicable
- Negative prompt or exclusions: No Minecraft names, textures, screenshots, logos, fonts, mobs, characters, item names, or protected visual references
- Post-processing steps: Texture is generated at runtime as a point-filtered 4x4 atlas of 16x16 pixel tiles and assigned to the chunk rendering material
- Reviewer notes: Moved up for M4 Art and Texture Assets headset validation readability. Covers Meadow Turf, Loam, Slate, Timber, Leafmass, Clearstone, Coalstone, Copperstone, Ironstone, Workbench, Storage Crate, and Torchbud. This is a functional original validation pass; committed authored texture assets should become the default rendering path, with this runtime atlas retained only as an explicit development/test fallback.
