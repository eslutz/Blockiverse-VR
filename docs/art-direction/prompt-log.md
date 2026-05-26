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

## 2026-05-26 — Superseded procedural block validation atlas

- Asset path: `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs` (superseded by committed authored atlas validation)
- Generation date: 2026-05-26
- Tool/model: Hand-authored procedural C# texture generation; no AI image model used
- Prompt: Not applicable
- Negative prompt or exclusions: No Minecraft names, textures, screenshots, logos, fonts, mobs, characters, item names, or protected visual references
- Post-processing steps: Texture was generated at runtime as a point-filtered 4x4 atlas of 16x16 pixel tiles and assigned to the chunk rendering material before committed assets existed.
- Reviewer notes: Superseded by the committed M4 authored atlas. Runtime procedural texture generation is no longer retained as a fallback because it can hide broken art wiring in production/release validation.

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
- Reviewer notes: This is the first committed M4 art pass for validation readability. It covers twelve renderable blocks, eight survival item icons, and six UI sprites. The authored atlas is now the required renderer path; missing, unrelated, or incorrectly sized atlas textures fail validation instead of using a procedural fallback.
