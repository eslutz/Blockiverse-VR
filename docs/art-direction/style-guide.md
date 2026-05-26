# Blockiverse VR Art Direction

## Direction

Use a readable, blocky, colorful voxel style with a distinct identity:

- Softer, toy-like block edges
- Brighter storybook explorer palette
- Original block names
- Original item icons
- Original UI panels
- VR-readable contrast and silhouettes

## Early Validation Visual Pass

The M4 Art and Texture Assets milestone uses committed authored texture assets as the default rendering path. The original procedural block atlas remains available only as an explicit development/test fallback so missing authored assets do not turn into magenta or invisible surfaces during local validation.

This pass is intentionally functional and VR-readable:

- 16x16 source tiles
- Point-filtered pixels
- High block-to-block contrast in VR
- Distinct color families for grass, soil, stone, wood, leaves, glass, ores, crafted blocks, and light sources
- Original names and visual motifs only

The current procedural atlas covers:

- Meadow Turf
- Loam
- Slate
- Timber
- Leafmass
- Clearstone
- Coalstone
- Copperstone
- Ironstone
- Workbench
- Storage Crate
- Torchbud

Committed authored texture assets now live under:

- `Assets/Blockiverse/Art/Textures/Blocks/Source/`
- `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png`
- `Assets/Blockiverse/Art/Textures/Items/`
- `Assets/Blockiverse/Art/Sprites/UI/`

## M4 Palette And Naming Rules

Use original Blockiverse names in filenames, issue text, UI labels, prompts, and provenance notes. Filenames are lowercase snake_case and should match the block, item, or UI sprite name without third-party references.

The M4 palette should stay bright, readable, and varied:

- Terrain: meadow greens, warm loam browns, cool slate grays
- Organic: amber timber and saturated leaf greens
- Clearstone: cyan glass/crystal tones
- Resources: dark coal contrast, orange copper accents, pale iron accents
- Crafted blocks: warm utility browns with distinct crate/workbench construction marks
- Torchbud: green stem tones with warm yellow light
- UI: dark translucent work surfaces with green, gold, red, and blue accents

## Texture Rules

Block textures:

- Source tiles are 16x16 RGBA PNG files.
- The runtime atlas is a committed 4x4, 64x64 RGBA PNG.
- Tile order follows `BlockVisualAtlas`: Meadow Turf, Loam, Slate, Timber, Leafmass, Clearstone, Coalstone, Copperstone, Ironstone, Workbench, Torchbud, Storage Crate.
- Use point filtering, clamp wrapping, and no mipmaps for the first M4 validation pass.
- Keep silhouettes and color families distinct enough to read in Quest headset validation.

Item and UI textures:

- Item icons are transparent 64x64 PNG sprites.
- UI sprites are transparent PNG sprites sized for their immediate use: hotbar frame, selected slot, health pip, inventory panel, crafting panel, and multiplayer status badge.
- Do not embed text in icon or UI sprites.

Quest import policy:

- Commit Unity `.meta` files with texture import settings.
- Android overrides stay enabled for M4 assets with max texture size matching the authored asset dimensions.
- Compression remains disabled for this first readability pass; revisit compression only with headset evidence that readability is preserved.

## Fallback Policy

Runtime rendering must select the committed authored block atlas first. `BlockVisualAtlas` procedural generation is allowed only when fallback is explicitly enabled for development or tests. Fallback use must be visible through logs or test-visible state and must not silently replace authored assets in release-candidate validation.

## Prohibited References

Do not use Minecraft textures, screenshots, sounds, music, logos, fonts, mob names, character names, or distinctive item names.

Do not prompt image or audio tools for protected Minecraft-specific assets.
