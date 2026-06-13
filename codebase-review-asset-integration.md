# Codebase Review — Asset Integration Expert

> Workflow run `wf_53b36881-009`, agent `af415e296e08ab0d8`. Raw expert output, pre-verification.

## Area Reviewed

Asset integration for the Blockiverse VR Quest project: cross-checked BlockRegistry (80 blocks) and ItemRegistry (131 registered canonical ids) against committed art (block atlas, 79 block source tiles, 142 item icons, 8 UI sprites, 8 VFX sprites, branding), the generated-asset python pipeline (scripts/art/generate-art-assets.py, scripts/audio/generate-audio.py), audio wiring (34 wavs vs bootstrapper cue table), texture/audio .meta import settings, GUID reference integrity across all scenes/prefabs/materials/settings assets, shader build inclusion, Resources usage, and the build scene list. Overall the committed runtime assets are in remarkably good shape — every reference resolves, registries and icons are in sync, the atlas is pixel-verified against its sources, and audio is fully consistent — but the documented art regeneration pipeline has drifted badly behind the committed art (running it would destroy the atlas), the custom voxel-lighting shader is referenced by nothing and will be stripped from device builds, four item icons are silently unloadable due to a wrong spriteMode, and the generated UI/VFX sprite sets are produced and test-enforced but never actually used by the game.

## Findings (8)

### 1. Art generator script is two milestones stale; regenerating destroys the committed block atlas

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `scripts/art/generate-art-assets.py`, `scripts/art/test_generate_art_assets.py`, `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png`, `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- **Impact:** CLAUDE.md instructs "never hand-author; regenerate instead" via python3 scripts/art/generate-art-assets.py, but the committed script (last touched in PR #304) predates the art shipped in PRs #305/#306/#311. Running it overwrites the committed 128x160 (8x10, 76-tile) atlas with a 128x112 (8x7, 50-tile) one — deleting tiles 50–75 (lumen_lamp, spark_flare, tended_soil, all crop/sapling growth stages, smooth_branchwood, reed_basket, tool_rack, pantry_jar, deep_locker, freshwater/brine/emberflow), rewrites blockiverse_block_atlas.png.meta with maxTextureSize 128 (committed: 256, atlas is 160 tall so Unity would downscale it), and reverts the corrected fired_brick item icon (verified pixel-different from script output). BlockVisualAtlas.IsAuthoredAtlasTexture requires exactly 8x10x16, so the world renderer would throw at runtime. The script also cannot produce 79 of the 142 committed item icons and 23 of the 79 block source tiles (tool tiers 3–7, foods, buckets, growth stages, etc.) — there is no committed generator for the current art, so the documented pipeline is unusable for any future art change.
- **Evidence:** scripts/art/generate-art-assets.py:14-15 `ATLAS_COLUMNS = 8 / ATLAS_ROWS = 7` and BLOCKS list ends at tile 49 (mend_bench), ITEMS list has 63 entries; BlockVisualAtlas.cs:11-12 `Columns = 8; Rows = 10` with TileIndexByBlockId mapping up to index 75; committed atlas PNG header is 128x160; committed atlas .meta maxTextureSize: 256 vs script write_texture_meta max_size=max(8,7)*16=128 (generate-art-assets.py:647); pixel-diff run: 'item icons script would change: 1 [fired_brick DIFFERENT]'; scripts/art/test_generate_art_assets.py:78 asserts ATLAS_ROWS == 7, codifying the stale state. git log: script last changed in #304; atlas changed in #305/#306/#311.
- **Recommended fix:** Recreate the missing generator coverage: extend BLOCKS to all 76 atlas tiles (matching BlockVisualAtlas.TileIndexByBlockId), set ATLAS_ROWS = 10, extend ITEMS to all 131 registered item ids (reuse the tier/class tool composition from ItemRegistry), write atlas meta with max_size >= 160, and update scripts/art/test_generate_art_assets.py to the new dimensions. Verify regeneration is byte/pixel-stable against the committed art (the committed sources for the 50 known blocks and 62/63 known items already match the script's pixel functions exactly, so only the new entries need porting). Until then, add a guard or README warning that the script must not be run.

### 2. Blockiverse/Voxel Lit shader is referenced by nothing and will be stripped from Quest builds, disabling all voxel lighting visuals

- **Severity:** High  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`, `ProjectSettings/GraphicsSettings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The entire voxel lighting feature (VoxelSkyLightMap sky light, cave darkness, emissive blocks like glowwick/lumen_lamp/spark_flare/campfire, per-face light baked by ChunkMeshBuilder) is rendered by multiplying vertex color in the custom shader. The shader is resolved only via Shader.Find at runtime; since no material, scene, prefab, or the Always Included Shaders list references it, Unity strips it from APK builds. On device, CreateBaseMaterial silently falls back to the chunk material's URP/Lit shader, which ignores vertex colors — so caves render fully lit, emissive blocks cast no visible light, and day/night face shading is lost. Editor and PlayMode tests are unaffected (Shader.Find works there), so the regression is invisible to the only test gate.
- **Evidence:** BlockVisualAtlas.cs:212 `Shader voxelShader = Shader.Find(VoxelLitShaderName);` with silent fallback to sourceMaterial's shader (lines 213-225); shader guid 0447044b0998a413fa33570b9cb06621 appears in zero .unity/.prefab/.mat/.asset files (repo-wide grep); GraphicsSettings.asset m_AlwaysIncludedShaders contains only built-in shaders (lines 29-36); all three committed materials use URP Lit/Unlit guids (933532a4…, 650dd952…); the bootstrapper contains no occurrence of 'VoxelLit'/'Voxel Lit'/'AlwaysIncluded'; ChunkMeshBuilder.cs:186 adds per-vertex light colors that only the custom shader's `* input.color` (BlockiverseVoxelLit.shader frag) consumes; BlockiverseBuildSmoke.cs does not touch GraphicsSettings.
- **Recommended fix:** Make the shader reachable in builds: either (a) have the bootstrapper assign Blockiverse/Voxel Lit as the shader of BlockiverseTestBlock.mat (the serialized chunk material in Boot.unity), which both includes the shader and removes the runtime swap, or (b) have the bootstrapper add the shader to GraphicsSettings m_AlwaysIncludedShaders. Also change CreateBaseMaterial to log a warning (BlockiverseLog) when the voxel shader cannot be found so device logs reveal the fallback.

### 3. Four registered items (worldroot, deepmantle, snowpack, frostglass) have unloadable icons due to spriteMode: 2 with an empty sprite sheet

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Textures/Items/worldroot.png.meta`, `Assets/Blockiverse/Art/Textures/Items/deepmantle.png.meta`, `Assets/Blockiverse/Art/Textures/Items/snowpack.png.meta`, `Assets/Blockiverse/Art/Textures/Items/frostglass.png.meta`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`, `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- **Impact:** These four blocks are harvestable/placeable survival resources, but their inventory/crafting icons never load: the textures are imported as Sprite Mode = Multiple with zero sprite rects defined, so no Sprite sub-asset exists. EnsureItemIconLibrary silently skips null sprites, so the icon library ships with 138 of 142 icons — confirmed in the committed XR rig prefab, which is missing exactly these four ids. Re-running the bootstrapper does NOT heal this (it only forces textureType, not spriteImportMode), so fresh builds also ship with blank icon slots: SurvivalInventoryPanel.SetSlotIcon disables the icon Image when the sprite is null, leaving empty squares for these items.
- **Evidence:** All four .png.meta files have `spriteMode: 2` + `sprites: []` + `internalIDToNameTable: []` (every other icon has spriteMode: 1); BlockiverseProjectBootstrapper.cs:2898-2907 only fixes `importer.textureType != TextureImporterType.Sprite` then `if (sprite == null) continue;` with no warning; parsed BlockiverseXRRig.prefab icon library block: 138 ids/138 sprite refs, and the diff against the Items folder is exactly ['deepmantle','frostglass','snowpack','worldroot']; all four ids are registered in ItemRegistry.cs (Resource items with block refs); SurvivalInventoryPanel.cs:148-149 `slotIcons[slotIndex].enabled = icon != null`. No test references BlockiverseItemIconLibrary.
- **Recommended fix:** Fix the four .meta files to `spriteMode: 1` (matching every other item icon). Harden EnsureItemIconLibrary to force `importer.spriteImportMode = SpriteImportMode.Single` when no sprite loads, and log a warning naming any Items/*.png that yields no Sprite. Add an EditMode test asserting every ItemRegistry-registered id loads a non-null Sprite from Assets/Blockiverse/Art/Textures/Items/<id>.png (M4ArtAssetValidationEditModeTests only checks file existence, which is why this passed).

### 4. Generated UI sprites (8) and VFX particle sprites (16 assets total) are produced and test-enforced but wired into nothing

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Sprites/UI/`, `Assets/Blockiverse/Art/Sprites/VFX/`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs`
- **Impact:** The art pipeline generates and the M4 test mandates hotbar_frame, selected_slot, health_pip, inventory_panel, crafting_panel, multiplayer_status_badge, settings_panel, feedback_toast plus 8 particle sprites (rain splash, snowflake, ember, dust, puff, sparks, fog wisp) — but no scene, prefab, material, or bootstrapper code references any of them. The HUD/menus are built from Unity's built-in rounded sprite + flat colors, and BlockiverseVfxPool renders every particle with a fallback `new Material(Shader.Find("Sprites/Default"))` because its serialized particleMaterial is never assigned — so all block-break/craft/weather VFX appear as untextured squares instead of the authored particles. Either the integration was never finished (visual-quality gap on device) or 16 assets plus their test entries are dead weight.
- **Evidence:** GUID search across all .unity/.prefab/.mat/.asset: all 16 sprite guids (e.g. hotbar_frame 9227a740…, rain_splash_particle 72a22845…) are UNREFERENCED; bootstrapper has zero occurrences of any UI/VFX sprite name and EnsureXrRigFeedback only does EnsureComponent<BlockiverseVfxPool>(rig) without assigning particleMaterial; BlockiverseVfxPool.cs:75-77 fallback to Sprites/Default with no texture; panel construction uses GetRoundedSprite() (bootstrapper lines 1530, 2108, 2478); M4ArtAssetValidationEditModeTests.cs:25-47 enforces the files exist.
- **Recommended fix:** Wire them in via the bootstrapper: create a particle material per VFX cue (or a single material using the appropriate particle sprite) and assign BlockiverseVfxPool.particleMaterial; use the UI sprites on the HUD sections they were drawn for (hotbar frame, selected slot, health pips, panel backgrounds, status badge, toast). If the flat-color UI is the intended final look, delete the unused sprites and their entries in the generator and M4 test instead of carrying dead assets.

### 5. Eleven orphan item icons have no registered item id (legacy ids and unimplemented ruleset foods)

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Textures/Items/`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `docs/rulesets/voxel_survival_ruleset.md`, `scripts/art/generate-art-assets.py`
- **Impact:** Assets/Blockiverse/Art/Textures/Items contains icons for ids that no longer (or do not yet) exist in ItemRegistry: 8 legacy block-named icons superseded by canonical drop items (berrybush, grain_stalk, reedgrass, niterstone, paletin_thread, staropal_geode, sunmetal_fleck, umbralite_node — drops are berry_cluster, grain_bundle, reed_fiber, etc.) and 3 food icons (berry_mash, flatbread, trail_ration) whose recipes exist in docs/rulesets/voxel_survival_ruleset.md (§ lines 637-638, 856-857) but were never registered as items. They are loaded into the icon library as dead entries and signal either missing game content (the Prep Board foods) or stale generator output that should be pruned.
- **Evidence:** Programmatic diff of 131 registered ids (ItemId.cs constants + RegisterToolTier compositions) vs 142 icon filenames: icons with no registered item = [berry_mash, berrybush, flatbread, grain_stalk, niterstone, paletin_thread, reedgrass, staropal_geode, sunmetal_fleck, trail_ration, umbralite_node]; grep for BerryMash/Flatbread/TrailRation in Assets/Blockiverse/Scripts returns nothing; the legacy 8 are still in the stale generator's ITEMS list so regeneration recreates them.
- **Recommended fix:** Either register the three ruleset foods (berry_mash, flatbread, trail_ration — their Prep Board recipes are canon) so the icons become live, and delete the 8 legacy block-named icons (also removing them from the generator's ITEMS list); or, if the foods are deferred, delete all 11 icons and regenerate them when the items land. Add the same registry-coverage test recommended above to flag future orphans.

### 6. Stray committed artifacts: batchmode InitTestScene at Assets root, Unity .meta outside Assets, dev scene in EditorBuildSettings

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`, `scripts/art/generate-first-launch-assets.py.meta`, `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `scripts/ci/forbidden-files.sh`
- **Impact:** Three hygiene issues: (1) Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity (+.meta) — the temporary scene Unity creates during -runTests batch runs — is committed at the Assets root and re-imported by every contributor; (2) scripts/art/generate-first-launch-assets.py.meta is a Unity .meta for a file outside Assets (meaningless to Unity, pure clutter); (3) MultiplayerTest.unity is deliberately enabled in EditorBuildSettings by EnsureBuildScenes, so any build made through the Unity Build window (rather than the build scripts, which pass an explicit Boot-only scene list) ships the dev test scene in the APK. scripts/ci/forbidden-files.sh catches none of these.
- **Evidence:** git ls-files confirms both stray files are tracked; EditorBuildSettings.asset lists Boot.unity and MultiplayerTest.unity both enabled:1; BlockiverseProjectBootstrapper.cs:1290-1311 (EnsureBuildScenes) intentionally adds MultiplayerTestScenePath; BlockiverseBuildSmoke.cs:29/60 builds with scenes = { BootScenePath } only; forbidden-files.sh regex only covers Library/Temp/Logs/UserSettings/.env/keystores.
- **Recommended fix:** Delete Assets/InitTestScene*.unity(.meta) and scripts/art/generate-first-launch-assets.py.meta; add `^Assets/InitTestScene` to the forbidden-files.sh regex so test-runner artifacts can't return. Either drop MultiplayerTest from EnsureBuildScenes (loading it via path in PlayMode tests doesn't require build-settings membership when using LoadSceneMode in editor tests) or mark it disabled so editor-menu builds don't ship it.

### 7. World atlas sampled with point filtering and no mipmaps — likely texture shimmer/aliasing in VR

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta`, `scripts/art/generate-art-assets.py`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- **Impact:** blockiverse_block_atlas.png imports with filterMode: 0 (point), enableMipMap: 0, aniso: 1. On Quest, world geometry seen at oblique angles and distance with an unmipped point-sampled texture produces high-frequency shimmer, which is much more noticeable (and comfort-relevant) in a head-tracked stereo display than on a flat screen. The Android override also forces textureCompression: 0 (uncompressed RGBA32) for all art — deliberate and memory-trivial at these sizes (atlas 80 KB, icons ~2.3 MB total), so only the mip/filter choice is a real concern. The 0.001 UV inset in BlockVisualAtlas.GetTileRect suggests bleed was already being fought; mipmapping a tight atlas would need padding or a texture array, so this is a design decision to verify on device rather than a clear-cut bug.
- **Evidence:** blockiverse_block_atlas.png.meta: `enableMipMap: 0`, `filterMode: 0`, Android `textureCompression: 0, overridden: 1`; generate-art-assets.py write_texture_meta emits the same settings for every texture; BlockVisualAtlas.cs:18 `const float UvInset = 0.001f` (bleed mitigation consistent with no-mip atlas).
- **Recommended fix:** Evaluate on device: if shimmer is objectionable, repack the atlas with per-tile padding (gutter duplication) and enable mipmaps + trilinear with a clamped mip bias, or move block tiles to a Texture2DArray (16x16 layers, mips per layer, no bleed) and update the voxel shader accordingly. If current look is accepted, record the decision so future reviewers don't re-flag it.

### 8. Production chunk material is named 'BlockiverseTestBlock'

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Materials/BlockiverseTestBlock.mat`, `Assets/Blockiverse/Scenes/Boot.unity`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The material serialized into Boot.unity's VoxelWorldRenderer.chunkMaterial — the material that carries the authored atlas into the build and the runtime material pipeline — is Assets/Blockiverse/Materials/BlockiverseTestBlock.mat (constant BlockiverseProject.TestBlockMaterialPath). The 'Test' name misleadingly suggests a placeholder; it is also the asset a fix for the voxel-shader finding would touch, so the naming actively invites accidental deletion or exclusion.
- **Evidence:** Boot.unity line 168: `chunkMaterial: {fileID: 2100000, guid: 4d5cd674d4ec742bfa27f01c9b1f16fe...}` resolves to BlockiverseTestBlock.mat.meta; the material's _BaseMap is the authored atlas (guid 90a6fc9b496045d7ad07d8e02954ce10); bootstrapper line 929 EnsureMaterial(BlockiverseProject.TestBlockMaterialPath, ...).
- **Recommended fix:** Rename the asset and constant to something like BlockiverseChunkAtlas.mat / ChunkMaterialPath via the bootstrapper (preserving the GUID by renaming the file+meta pair, not recreating it), ideally in the same change that binds the Voxel Lit shader to it.

## What Looks Good (7)

- Zero broken references: every guid in Boot.unity, MultiplayerTest.unity, all three prefabs, and every Blockiverse .mat/.asset resolves to a tracked .meta or a package asset; no m_Script {fileID: 0} (missing script) anywhere; no duplicate GUIDs in Assets.
- Registry/icon coverage is complete: all 131 registered item ids (including the 35 composed tool-tier ids) have an icon file at Assets/Blockiverse/Art/Textures/Items/<id>.png, and all 76 renderable-block atlas mappings in BlockVisualAtlas.cs are backed by real tiles — 14 sampled tiles (including every post-script addition and the fluid tiles) decoded pixel-identical between the committed atlas and Blocks/Source.
- The audio pipeline is fully coherent end-to-end: scripts/audio/generate-audio.py produces exactly the 34 clips the bootstrapper wires (28 cues + 2 footsteps + 4 music tracks), GUIDs are deterministic (md5 of asset path — verified f2dd699e… for music_day) so regeneration never breaks prefab references, music streams from disk (loadType 2, preloadAudioData 0) while short SFX decompress on load, and the committed XR rig prefab has all clip fields assigned.
- GUID-stability discipline in the asset generators (pinned META_GUIDS, crc32-derived texture guids, md5 audio guids) means regenerating known assets preserves all serialized references — committed metas for script-era assets match the derivation exactly.
- M4ArtAssetValidationEditModeTests is a genuine guardrail: it pins the atlas to BlockVisualAtlas.Columns/Rows with Android maxTextureSize bounds, requires a Source PNG per renderable block and an icon per registered item, and validates CreateMaterial fail-fast paths — it would catch the stale python generator's 8x7 atlas before merge (when run locally per the testing contract).
- Quest-appropriate import settings overall: Android overrides present on every art texture, point filtering matches the pixel-art style, uncompressed RGBA32 is a sound choice at these tiny sizes (no ASTC artifacts), audio is forced mono 44.1 kHz, and the app icon (512x512, referenced from ProjectSettings.asset) and launch art (referenced by the rig menu RawImage) are wired in.
- The bootstrapper's EnsureFluidAtlasTiles (BlockiverseProjectBootstrapper.cs:760-805) is carefully additive: it never overwrites committed source tiles, guards on exact atlas dimensions before painting, and keeps source PNGs and atlas tiles generated from the same pixel functions.

## Could Not Review (5)

- On-device behavior: shader stripping in the APK, the visual consequence of the URP Lit fallback, and atlas aliasing in the headset are inferred statically from serialized data and Unity's documented build rules; confirming requires building and running on a Quest (the review method forbids running builds).
- Unity import results: I could not open Unity, so conclusions about what the four spriteMode:2 metas import to rest on Unity's standard importer behavior plus the corroborating absence of those four sprites from the committed rig prefab.
- Third-party asset trees (Assets/Oculus/Avatar2*, TextMesh Pro, XR/XRI settings, Meta SDK Resources assets) were only checked for reference resolution, not audited for import-setting quality or licensing.
- Whether the newer (uncommitted) art generator that produced PRs #305-#311 assets still exists somewhere outside this repo — git history here only shows its outputs.
- Audio subjective quality/loudness and whether 1.0-1.2 s ambience 'loops' loop cleanly — file headers and import settings were verified, audible seams were not.

## Inspected (35)

- `scripts/art/generate-art-assets.py`
- `scripts/art/test_generate_art_assets.py`
- `scripts/art/generate-first-launch-assets.py`
- `scripts/audio/generate-audio.py`
- `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png (+ .meta, pixel-decoded)`
- `Assets/Blockiverse/Art/Textures/Blocks/Source/ (79 tiles, sampled pixel comparison)`
- `Assets/Blockiverse/Art/Textures/Items/ (142 icons + metas)`
- `Assets/Blockiverse/Art/Sprites/UI, VFX, Branding (+ metas)`
- `Assets/Blockiverse/Audio/ (34 wavs + metas)`
- `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseItemIconLibrary.cs`
- `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseMusicController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeCatalog.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs (BlockRegistry)`
- `Assets/Blockiverse/Scripts/Survival/ItemId.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (audio/icon/atlas/build-scene wiring)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader (+ .meta)`
- `Assets/Blockiverse/Materials/*.mat`
- `Assets/Blockiverse/Scenes/Boot.unity`
- `Assets/Blockiverse/Scenes/MultiplayerTest.unity`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`
- `Assets/Blockiverse/Prefabs/Networking/*.prefab`
- `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`
- `Assets/Resources/ (Meta SDK settings assets)`
- `Assets/Plugins/Android/BlockiverseBranding.androidlib`
- `ProjectSettings/GraphicsSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs`
- `scripts/ci/forbidden-files.sh`
