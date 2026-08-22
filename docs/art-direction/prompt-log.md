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
- Asset path: `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/enhanced/blockiverse_block_atlas.png`
- Asset path: `Assets/Blockiverse/Art/Textures/Items/*.png`
- Asset path: `Assets/Blockiverse/Art/Sprites/UI/*.png`
- Generation date: 2026-05-26
- Tool/model: Codex-assisted visual direction and deterministic Python raster generation in `scripts/art/generate-art-assets.py`; no external image model was used for committed pixels
- Prompt: `original VR-friendly voxel sandbox asset, colorful storybook explorer style, readable silhouette, soft toy-like block edges, no text, no logos, no third-party references`
- Negative prompt or exclusions: No Minecraft, Creeper, Steve, Enderman, Mojang, copied game texture, copied logo, copyrighted character, protected font, third-party screenshots, or protected item identity
- Post-processing steps: Pixel assets were normalized into RGBA PNG files, block source tiles were packed into a committed 4x4 atlas, Unity `.meta` import settings were written with point filtering, clamp wrapping, no mipmaps, Android max-size overrides, and sprite import settings for item/UI assets
- Reviewer notes: This is the first committed M4 art pass for validation readability. It covers twelve renderable blocks, eight survival item icons, and six UI sprites. The authored atlas is now the required renderer path; missing, unrelated, or incorrectly sized atlas textures fail validation instead of using a procedural fallback.

## 2026-05-28 — First-launch branding assets

- Asset path: `Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape.png`
- Asset path: `Assets/Blockiverse/Art/Sprites/Branding/blockiverse_app_icon.png`
- Asset path: `Assets/Plugins/Android/res/mipmap-*/app_icon.png`
- Generation date: 2026-05-28
- Tool/model: Codex built-in image generation for the launch landscape; deterministic Python raster generation in `scripts/art/generate-first-launch-assets.py` for the app icon and Android icon density variants
- Prompt: `stylized voxel landscape splash artwork for an original Meta Quest voxel survival VR game named Blockiverse VR; bright readable low-poly voxel valley at sunrise with blocky meadow terrain, stepped hills, cubic trees, a simple workbench silhouette, clear sky, no text`
- Negative prompt or exclusions: No text, logos, UI, watermarks, dark horror mood, copyrighted characters, brand references, photorealism, Minecraft textures, screenshots, mobs, characters, logos, fonts, or protected item identity
- Post-processing steps: The launch landscape was copied into the Unity branding sprite folder and imported as a texture. The startup overlay renders the exact `Blockiverse VR` title as Unity text for readability. The app icon was generated as a simple original block-font `B` mark over Blockiverse terrain color blocks, with Android mipmap density variants and Unity `.meta` import settings written by script.
- Reviewer notes: The app icon favors readability over scene detail. The launch art is used behind the in-game startup/loading overlay, while the Unity splash remains in place unless the current Unity license/settings permit removing it later.

## 2026-06-07 — Phase 13/14 feedback and asset review pass

- Asset path: `Assets/Blockiverse/Audio/*.wav`
- Asset path: `Assets/Blockiverse/Art/Textures/Blocks/Source/*.png`
- Asset path: `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/enhanced/blockiverse_block_atlas.png`
- Asset path: `Assets/Blockiverse/Art/Textures/Items/*.png`
- Asset path: `Assets/Blockiverse/Art/Sprites/UI/*.png`
- Asset path: `Assets/Blockiverse/Art/Sprites/VFX/*.png`
- Generation date: 2026-06-07
- Tool/model: Deterministic Python raster/audio generation in `scripts/art/generate-art-assets.py` and `scripts/audio/generate-audio.py`; no external image or audio model was used for committed pixels or WAV files
- Prompt: `original Blockiverse VR feedback and art assets using canonical ruleset IDs, Quest-readable silhouettes, bright varied palette, short comfort-safe audio transients and loops, no text, no logos, no third-party references`
- Negative prompt or exclusions: No Minecraft, Creeper, Steve, Enderman, Mojang, copied game texture, copied sound, copied logo, copyrighted character, protected font, third-party screenshots, or protected item identity
- Post-processing steps: Block source tiles were normalized into RGBA PNG files and packed into an 8x7 committed atlas; item/UI/VFX sprites were written as transparent PNGs; WAV cues were normalized with headroom; Unity `.meta` import settings were generated for texture and audio assets
- Reviewer notes: This pass expands the committed generated set to the current canonical block and item registries, adds feedback UI/VFX sprites, and makes the generator scripts reusable across roadmap phases. Quest headset readability and audio-comfort review remains a manual validation step.
- **Superseded for audio (2026-08-21):** the "no external audio model was used" statement above describes this pass only and remains accurate for it. The committed sound effects were replaced in the 2026-08-21 production audio pass below; the music tracks from this pass are still what ships. Art is unaffected.

## 2026-08-21 — Production audio pass

- Asset path: `Assets/Blockiverse/Audio/*.wav` (all sound effects)
- Asset path: `Assets/Blockiverse/Audio/music_*.wav` (unchanged — original generated music, retained deliberately)
- Asset path: `Assets/Blockiverse/Audio/classic_block_*.wav` (original generated block cues, retained behind the Classic Block Sounds setting)
- Generation date: 2026-08-21
- Tool/model: **No generative model was used.** Sound effects are edited from licensed third-party field recordings and foley libraries (Sonniss #GameAudioGDC bundles under the Sonniss GDC Bundle Licensing Agreement; Kenney and OpenGameArt packs under CC0 1.0), processed deterministically by `scripts/audio/build-audio-assets.py` driven by `scripts/audio/audio-manifest.json`. Music and the classic block cues remain synthesized by `scripts/audio/generate-audio.py`.
- Prompt: Not applicable — source material is recorded audio, selected by hand against the ruleset §5 cue catalog and §13 block-material table.
- Negative prompt or exclusions: No Minecraft or other protected voxel-game sounds, music, UI cues, mob cues, or branding. No source whose license restricts commercial use, modification, or distribution inside a commercial game. No AI/ML training use of any Sonniss material, which its license expressly prohibits.
- Post-processing steps: Sources trimmed to cue length, resampled to 44.1 kHz, loop beds crossfaded at the loop point, one-shots peak-normalized to 0.82 (approximately -1.7 dBFS) and folded to mono for spatialization, ambience/weather beds kept stereo with Unity import normalization disabled so the source mastering survives. Unity `.meta` files are regenerated with path-derived GUIDs so prefab wiring is preserved.
- Reviewer notes: Every committed cue has a row in `docs/audio/audio-asset-manifest.md` naming its source pack, source file, and license — a cue without a manifest row fails `scripts/audio/validate-audio-assets.py`. Raw bundles are staged outside the repository and never committed. Licenses were verified at each original provider, not from search results or mirrors. Quest headset audio-comfort review remains a manual validation step.
