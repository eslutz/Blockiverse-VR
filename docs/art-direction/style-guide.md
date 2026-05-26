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

The M4 Art and Texture Assets milestone uses an original procedural block atlas as the temporary fallback so headset testing can distinguish terrain, resource, crafting, storage, and lighting blocks before committed authored texture assets replace it as the default rendering path.

This pass is intentionally functional:

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

Final authored textures should replace the procedural atlas as the default rendering path. The procedural atlas should remain available only as an explicit development/test fallback, and authored textures must preserve VR readability and the original visual identity established here.

## Prohibited References

Do not use Minecraft textures, screenshots, sounds, music, logos, fonts, mob names, character names, or distinctive item names.

Do not prompt image or audio tools for protected Minecraft-specific assets.
