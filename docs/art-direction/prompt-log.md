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

## 2026-05-26 — M4 committed block, item, and UI asset pass

- Asset path: `Assets/Blockiverse/Art/Textures/Blocks/Source/*.png`
- Asset path: `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png`
- Asset path: `Assets/Blockiverse/Art/Textures/Items/*.png`
- Asset path: `Assets/Blockiverse/Art/Sprites/UI/*.png`
- Generation date: 2026-05-26
- Tool/model: Codex-assisted visual direction and deterministic Python raster generation in `scripts/art/generate-m4-assets.py`; no external image model was used for committed pixels
- Prompt: `original VR-friendly voxel sandbox asset, colorful storybook explorer style, readable silhouette, soft toy-like block edges, no text, no logos, no third-party references`
- Negative prompt or exclusions: No Minecraft, Creeper, Steve, Enderman, Mojang, copied game texture, copied logo, copyrighted character, protected font, third-party screenshots, or protected item identity
- Post-processing steps: Pixel assets were normalized into RGBA PNG files, block source tiles were packed into a committed 4x4 atlas, Unity `.meta` import settings were written with point filtering, clamp wrapping, no mipmaps, Android max-size overrides, and sprite import settings for item/UI assets
- Reviewer notes: This is the first committed M4 art pass for validation readability. It covers twelve renderable blocks, eight survival item icons, and six UI sprites. The authored atlas is now the default renderer path; the earlier procedural atlas remains a logged development/test fallback.
